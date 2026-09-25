# Ledger API

A double-entry ledger service built with ASP.NET Core 8, EF Core and PostgreSQL. It records money movements as immutable debit/credit entries, derives balances from those entries, and guarantees each request moves money at most once.

The design, including every trade-off, is in [docs/DESIGN.md](docs/DESIGN.md). Start there for the *why*. This README covers the *how*.

## Core guarantees

- **Double entry.** Every successful transaction writes exactly one debit and one matching credit, in one database transaction.
- **Derived balances.** There is no balance column. A balance is `SUM(credits) − SUM(debits)` over an account's entries.
- **Append-only.** Entries are never updated or deleted. A reversal writes new opposite entries.
- **Idempotency.** Money-moving requests carry a client idempotency key, enforced by a unique constraint. A retry with the same key returns the stored result.
- **No overdraft under concurrency.** The debited account is locked (`SELECT … FOR UPDATE`) while its balance is checked.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker](https://www.docker.com/) (for the local database and for the tests)

## Getting started

**1. Start PostgreSQL.** The Development connection string expects it on port `5433`:

```bash
docker run -d --name ledger-postgres -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=ledgerapi -p 5433:5432 postgres:16-alpine
```

**2. Apply the migrations.** `dotnet-ef` is pinned in the repo's tool manifest:

```bash
dotnet tool restore
dotnet ef database update --project src/LedgerApi
```

This also seeds the funding system accounts `NGN100000001`, `USD100000001`, `GBP100000001` and `EUR100000001` that deposits and withdrawals use.

**3. Run the API:**

```bash
dotnet run --project src/LedgerApi --launch-profile http
```

Swagger UI is at http://localhost:5293/swagger. [src/LedgerApi/LedgerApi.http](src/LedgerApi/LedgerApi.http) has a ready-made request for every endpoint (VS Code REST Client, Rider or Visual Studio).

## Running the tests

```bash
dotnet test
```

Service tests run against a real PostgreSQL container started by [Testcontainers](https://dotnet.testcontainers.org/), so Docker must be running. No local database is needed for tests.

## Endpoints

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/api/accounts` | Create a customer account (10-digit random number) |
| `GET` | `/api/accounts/{accountNumber}` | Account enquiry |
| `GET` | `/api/accounts/{accountNumber}/balance` | Balance enquiry (derived, unlocked read) |
| `POST` | `/api/transfers` | Move money between two accounts |
| `POST` | `/api/deposits` | Mock funding: funding account → customer |
| `POST` | `/api/withdrawals` | Mock withdrawal: customer → funding account |
| `POST` | `/api/reversals` | Reverse a successful transaction |
| `POST` | `/api/admin/system-accounts` | Create a system account (`NGN` + 9 digits) |

### Status codes

| Status | Meaning |
|---|---|
| `200` | Success |
| `400` | Request failed validation |
| `404` | Account or transaction not found |
| `409` | Transaction already reversed |
| `422` | A business rule failed: insufficient funds, account status, currency mismatch, or a transaction that can't be reversed. For transfers, deposits and withdrawals the failure is committed and the body carries `reference`, `status: "Failed"` and `failureReason`. A retry with the same idempotency key returns the same 422. |
| `500` | Unexpected error. Retry with the **same** idempotency key to learn whether money moved. |

## Project layout

```
src/LedgerApi/
  Controllers/      HTTP endpoints (Admin/ holds the system-account route)
  Services/         Business logic: Transfer, Deposit, Withdrawal, Reversal, Account, AdminAccount
  Entities/         Account, Transaction, LedgerEntry, Reversal, AuditLog
  Data/             LedgerDbContext and model configuration
  Migrations/       EF Core migrations
  Validation/       Request validators
  Middleware/       Maps known exceptions to HTTP status codes
tests/LedgerApi.Tests/
  Services/         Integration tests against PostgreSQL (Testcontainers)
  Controllers/      Status-code mapping tests with stub services
  Validation/       Validator unit tests
docs/DESIGN.md      Design document and decisions log
```

## Not implemented yet

- **Authentication and mandates.** No endpoint is authorized yet, including the admin system-account route. See DESIGN.md Section 6.
- **Audit logging.** `AuditLogger` is a stub.
- **Request logging** with request id, actor and idempotency key (DESIGN.md Section 7).
