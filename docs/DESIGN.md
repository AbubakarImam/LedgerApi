# Ledger API — Design Document

**Author:** Abubakar Abdulsalam
**Status:** Design phase complete (v3). This document pairs each design question with the answer and reasoning, developed through iterative adversarial review. Decisions are recorded in the decisions log (Section 9).

---

## 1. What is this system?

**Q: In two or three sentences: what does this ledger do, and who calls it?**

This is a ledger that records accounting information. It keeps a record of entries. The system will be called by a payment API and other external clients.

**Q: What is explicitly OUT of scope for v1?**

Reconciliation, notifications, containerization, multiple account identifier types, and cross-currency transfers (a transfer between accounts of different currencies is rejected).

v1 supports currencies with two decimal places only; account creation rejects others.

## 2. Core concepts and entities

**Q: What are the core entities? For each entity: what uniquely identifies it, and what state can it be in?**

- **Account:** id, account number (unique), name, account type, currency, status.
- **Transaction (envelope):** identified by id, reference (unique), and idempotency key (unique). States: pending / success / failed. It encompasses all the details for a transaction — reference, status, narration, who initiated it — but does not hold account numbers or amounts.
- **Entry:** an individual account movement. Each entry belongs to a transaction and records one account, an amount, and a direction (debit or credit). Entries are immutable — they have no state changes after insert.
- **Reversal:** identified by id; unique per original transaction. References the original transaction and is written together with new forward entries.
- **Mandate:** the ledger does not hold identity information (email, BVN, NIN). It only saves references that the mandate system can use to query. Identity data has different retention, access, and regulatory rules and lives outside the ledger.

**Q: What is the difference between a Transaction and an Entry in a double-entry system? Why does that difference exist?**

The transaction encompasses the whole operation — both accounts involved, narration, status, and other information. An entry records one individual account movement with a direction. The difference exists so that the operation (the envelope) and the account-level facts (the entries) each live in exactly one place.

**Q: Can an Account's balance be a stored column, or must it be derived from entries? What breaks under the other choice?**

Balance is never a stored column. Balance is always derived from the entries written. A stored column would not show the history of transactions, and derivation is what makes reversals work cleanly — a reversal is just more entries, and the balance stays correct by construction.

## 3. Double-entry rules

**Q: State the invariant that must NEVER be violated in this system.**

All transactions must always even out — every debit must have a credit equivalent. Two entries must be appended for any transaction to be successful.

**Q: Walk through a transfer of NGN 500 from Account A to Account B: exactly which rows get written, in what order, inside what boundary?**

Account A is debited and Account B is credited. The idempotency key (on the transaction row), the transaction envelope, and both entries are written inside a single database transaction. A transfer never "updates a balance" — it only appends entries; the balance is what the entries sum to.

**Q: What happens if the process crashes halfway through that write? What must be true afterward?**

All actions done before commit are rolled back and discarded as if nothing happened. The invariant cannot be broken by a crash because the database physically refuses to store half a transfer.

**Q: How do you represent a failed/reversed transaction? Do you delete rows, update rows, or write new rows? Why?**

Reversals write a new row in the reversals table and new forward entries, like a normal transaction — nothing is deleted or updated. History is preserved. For failed transactions, only those that fail business logic checks have their responses saved (as a committed transaction row with a failed status), so the client can narrow down retries.

## 4. Concurrency

**Q: Two transfers hit Account A at the same instant, and A only has enough balance for one of them. Describe, step by step, what your system does.**

The chosen flow, in order:

1. Lock the source account row (SELECT ... FOR UPDATE) for the account involved in the transaction.
2. Derive the balance by summing committed entries.
3. Run business logic checks — sufficient balance, same currency, and other rules.
4. Append the entries and the transaction envelope.
5. Commit — the lock is released.

The second concurrent transfer blocks at step 1 and waits. When the first transfer commits, the second wakes, derives a fresh balance that includes the first transfer's entries, and fails its check on correct data. Overdraft is impossible.

**Q: Which strategy are you choosing: pessimistic locking, optimistic concurrency, serializable transactions, or a queue that serializes writes per account?**

Pessimistic per-account row locking (SELECT ... FOR UPDATE), at Read Committed isolation — because it happens in the database, the one component shared by all servers.

**Q: For the strategy you chose: what is its failure mode? What load pattern makes it hurt?**

Deadlock is one failure mode: when more than one account row must be locked, accounts are always locked in a consistent order (lowest account_id first). This prevents the circular wait where transfer A→B holds account A and wants B while transfer B→A holds B and wants A, each waiting on the other forever. With consistent ordering, both transfers try to lock the same account first, so one queues at the first door and never holds the second hostage.

The hotspot account load pattern makes this hurt: a very high number of transfer requests targeting a single account must lock the same row and therefore happen one at a time, creating a high number of pending transactions on that account while other accounts have low or no activity. This is accepted for v1; the mitigation path (e.g. a durable queue such as Kafka in front of hot accounts) is noted for later.

**Q: For ONE strategy you rejected: why did you reject it?**

Serializable was the closest second option. With Serializable, the balance derivation is work that gets performed and then discarded when the engine aborts the losing transaction, and the whole transfer must be retried — on a hotspot account this becomes an abort-retry storm where total work far exceeds the number of transfers. With the row lock, the second request waits without performing any action until the first commits — blocks postpone unstarted work; aborts waste completed work.

The per-account in-memory queue was also rejected as a correctness mechanism: a queue lives in memory in one process. It fails when there is more than one server, and it vanishes on restart. Correctness must live in the database. The queue idea survives only as a possible throughput optimization.

## 5. Idempotency

**Q: A client sends a transfer request. The network drops the response. The client retries. What stops the money from moving twice?**

The unique constraint on the idempotency key, enforced by the database engine. The key is inserted first, inside the database transaction, before any money logic. There is no check-then-insert: the insert is the check, enforced atomically by the engine. Check-then-insert has a gap in which two simultaneous requests both pass the check; the constraint has no gap.

If two simultaneous requests carry the same key, the engine serializes them at the unique index: the second waits on the first's pending entry, then fails with a duplicate-key violation if the first commits (the stored result is returned), or succeeds as a normal insert if the first rolls back. Row locking and versioning cannot guard this: they govern existing rows, and this race is about the creation of a row that does not exist yet.

**Q: Where does the idempotency key live, who generates it, and how long is it remembered?**

The key lives on the transactions table itself (see Section 8) with a unique constraint — the key and the transaction it produced are structurally inseparable. A key is generated by the client only on a fresh transaction; all retries must inherit the idempotency key from the original request. It is remembered for the life of the transaction row.

**Q: What should the API return on a retried request — an error, or the original result? Why?**

The stored result of the original request, so the consumer knows the true outcome. The full behavior is a three-outcome transaction boundary:

- **Success:** key, envelope, and entries commit together; a retry gets the stored success result.
- **Business-logic failure (e.g. insufficient funds):** the transaction row commits with a failed status and the stored failure response — no entries. A retry with the same key gets the stored response back.
- **System failure / crash:** nothing commits, including the key, so a legitimate retry starts fresh and is not blocked by a ghost key.

Client retry contract: on a 500 or timeout, the client retries with the identical key — never a fresh one. A retry with a new key is, to the system, a brand-new transfer.

## 6. API surface

**Q: List the endpoints for v1.**

Transfer, reversal, balance enquiry, account enquiry.

**Q: For the transfer endpoint specifically: write out the request body and the success response body.**

Request body: debit account, credit account, amount, narration, idempotency key. Time is not accepted from the client — the server assigns created_at; a ledger's ordering of events is its truth. Success response: status 200 with the transaction reference.

**Q: What does the API return when a transfer fails for insufficient funds? What HTTP status, and why that one?**

422 — the request did not fail because of network or system error; it failed a business rule, and the status should say so.

**Q: Which endpoints need authorization rules (mandates), and what does a mandate check look like in the request flow?**

All endpoints, reads included, require authorization — the mandate flow exists for this. Authorization and locking are different things and both exist: the lock happens in the database and controls access to the account row during a transaction; authorization happens in the application code before the action and checks permission to perform it. What read endpoints skip is the lock, not auth.

Mandate flow: get account references, derive balance, use identifiers to get account info.

Enquiry balances are informational — computed from committed entries without acquiring the account lock, and may be stale the moment they are returned. The only authoritative balance read is the locked one inside the transfer path. Locking enquiries would serialize the highest-volume read path through the most contended chokepoint for zero correctness gain.

## 7. Failure and truth

**Q: "The API returned 500 but did the money move?" — how does a caller find out the truth?**

Retry with the same idempotency key. Because the key and the entries commit together or not at all, the retry either finds the committed result (money moved — here it is) or finds nothing and processes fresh (money did not move). There is no window where money moved but the retry guard does not exist.

**Q: What gets logged/audited, and what must NEVER appear in logs?**

Logs: user, timestamp, idempotency key, endpoint, request_id, response status. Identity documents (BVN, NIN, email) must never appear in logs.

Audit: account number, amount, transaction_id, status, actor, action, entity, IP address, user agent, created_at.

Notifications (SMS/email) happen outside the database transaction. A flaky side-effect must never hold the money-truth hostage; the ledger is the source of truth regardless. v1 accepts that a notification can be lost after a successful commit; the known fix is an outbox pattern.

## 8. Data and storage

**Q: Sketch the tables (names + key columns).**

**accounts**
| column | notes |
|---|---|
| id | PK |
| account_number | **UNIQUE** |
| account_name | |
| account_type | |
| currency_code | |
| status | |
| user_id | |
| created_at | server-assigned |

**transactions**
| column | notes |
|---|---|
| id | PK |
| reference | **UNIQUE** |
| idempotency_key | **UNIQUE** — the idempotency store is collapsed into the envelope |
| initiated_by | |
| status | pending / success / failed |
| narration | |
| created_at | server-assigned |
| completed_at | |

**ledger_entries**
| column | notes |
|---|---|
| id | PK |
| transaction_id | FK → transactions |
| account_id | FK → accounts |
| entry_type | CHECK: 'DEBIT' or 'CREDIT' only |
| amount | CHECK: amount > 0 — direction lives in entry_type; balance = SUM(credits) − SUM(debits) |
| currency | must equal the account's currency, validated inside the transaction (Section 4, step 3) |
| created_at | server-assigned |

**reversals**
| column | notes |
|---|---|
| id | PK |
| original_transaction_id | **UNIQUE** — a transaction can be reversed at most once. A row lock cannot guard this: it only serializes simultaneous contenders and evaporates at commit; the contested thing is the creation of a new row, which is constraint territory. |
| reversal_transaction_id | FK → transactions |
| reason | |
| reversed_by | |
| created_at | server-assigned |

**audit_logs**
| column | notes |
|---|---|
| id | PK |
| actor_id | |
| action | |
| entity_type | |
| entity_id | |
| ip_address | |
| user_agent | |
| created_at | server-assigned |

**Q: Which columns are immutable after insert? Why does immutability matter in a ledger?**

All columns in ledger_entries are immutable after insert — the ledger is append-only. Account numbers, ids, and timestamps are immutable too. Immutability matters because the entries are the source of truth for every balance; if history can be edited, the ledger can lie.

**Q: What indexes will the hot paths need?**

ledger_entries(account_id, created_at) — balance derivation (enquiry) is the hot path with the most traffic.

## 9. Decisions log

| # | Decision | Options considered | Chosen | Why |
|---|---|---|---|---|
| 1 | Balance storage | Stored column vs derived from entries | Derived | A stored column loses history; derivation makes reversals correct by construction |
| 2 | Money-moving API | Separate credit/debit endpoints vs single transfer | Single transfer only | Separate endpoints let a caller violate the double-entry invariant at the front door |
| 3 | Schema shape | Transactions + Debit + Credit tables vs envelope + entries | Envelope + entries | Three tables duplicated the same fact in two places; duplicated truth is where ledgers rot |
| 4 | Idempotency mechanism | Check-then-insert in code vs insert-first with unique constraint | Insert-first + unique constraint | Check-then-insert has a race gap; the constraint is enforced by the engine across all servers |
| 5 | Idempotency storage | Separate idempotency table vs key on the transaction envelope | Key on envelope | One table fewer; key and its transaction are structurally inseparable, no linkage drift |
| 6 | Failed-transaction responses | Roll back everything vs commit key with failure result | Commit with failure result (business failures only) | Lets clients narrow retries; system failures still roll back fully so retries start fresh |
| 7 | Concurrency control | Serializable vs per-account row lock vs in-memory queue | Per-account FOR UPDATE lock at Read Committed | Serializable wastes completed work in abort-retry storms on hotspots; an in-memory queue dies with >1 server or a restart; the lock lives in the shared database |
| 8 | Enquiry reads | Locked vs unlocked | Unlocked | Enquiries are informational; locking them serializes the highest-volume path for zero correctness gain |
| 9 | Currency on entries | Derive from account vs copy with validation | Copy with validation | A mismatch with the account's currency is rejected inside the transaction; cross-currency transfers are out of scope for v1 |
| 10 | Client timestamps | Accept from request vs server-assigned | Server-assigned | A ledger's ordering of events is its truth; the client cannot be allowed to set it |
| 11 | Notifications | Inside vs outside the db transaction | Outside | A flaky side-effect must not hold money-truth hostage; outbox pattern noted as the future fix |
| 12 | Identity data (BVN/NIN) | Store in ledger vs references only | References only | Identity documents have different retention, access, and regulatory rules; they never appear in the ledger or its logs |
| 13 | Amount storage type | decimal(18,2) vs minor-unit integers | decimal(18,2) | v1 is NGN-scoped; minor units noted as the migration path if multi-exponent currencies arrive |

## 10. Open questions / next steps

**Q: List everything you couldn't answer confidently.**

- Detailed EF Core mapping of the schema (types, enum handling for entry_type and status).
- Hotspot mitigation beyond v1 (durable queue such as Kafka in front of hot accounts).
- Outbox pattern for reliable notifications.
- There are certainly things I am not yet aware of; this document is expected to evolve during implementation, with changes recorded in the decisions log.
