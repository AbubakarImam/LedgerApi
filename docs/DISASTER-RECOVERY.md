# Disaster recovery: LedgerApi's database

garbati-platform T-039 (G9): the ledger's entries are the one dataset in the whole platform that must
never be silently lost (`README.md`'s own line: "the ledger's entries are the only source of truth for
balances"). This document is the actual restore procedure, a stated recovery target, and the record of a
real restore drill — not just the one-sentence intent the README already stated.

## What's already true, today, by default

Production (`README.md`'s own "Azure (production)" section) runs on **Azure Database for PostgreSQL
Flexible Server** (`ledgerapi-db-san`, resource group `ledger-rg`, South Africa North). Flexible Server
takes automated backups and supports point-in-time restore (PITR) by default — this is not something
GARBATI configured specially, it's the baseline behavior of the managed service `ARCHITECTURE.md` already
chose. Confirmed directly against the real resource (22 Oct 2026, `az postgres flexible-server show`):

| Setting | Value |
|---|---|
| Backup retention | **35 days** |
| Geo-redundant backup | **Enabled** |
| SKU | `Standard_B1ms` (Burstable) |
| Storage | 32 GiB, Premium_LRS |
| High availability | Disabled |

Geo-redundant backup means the backup data itself is also copied to Azure's paired region, not only kept
in South Africa North — so a full regional outage, not just a single bad deploy or a bad migration, still
has a recovery path (restoring into a different region from the geo-redundant copy), though that specific
cross-region restore has not itself been drilled — see "What this drill did not cover" below.

## Recovery targets (RPO/RTO)

- **RPO (Recovery Point Objective): a few minutes.** Flexible Server's PITR is continuous (based on
  write-ahead log shipping, not periodic snapshots), so in practice the most data a restore can lose is
  whatever wasn't yet flushed to the backup stream — Azure does not publish an exact number, and this
  project has no SLA with Azure to tighten it further. State this as "minutes, not hours" rather than a
  false-precision exact figure.
- **RTO (Recovery Time Objective): under 15 minutes to a usable restored database**, based on the real
  drill below (the actual restore completed in roughly 90 seconds on this SKU; 15 minutes is a deliberate
  safety margin covering firewall/connection-string reconfiguration and verification, not just the restore
  call itself). This is the time to get a *queryable database* back, not the time to repoint `LedgerApi`'s
  App Service at it and have the service healthy again — see "What this drill did not cover."

## The restore procedure

This is the exact procedure used in the drill below, generalized for a real incident.

1. **Identify the restore point.** Pick a UTC timestamp before whatever caused the loss (a bad migration,
   a bad deploy, accidental data corruption). Must be within the last 35 days and after the server's
   `earliestRestoreDate` (`az postgres flexible-server show --name ledgerapi-db-san --resource-group
   ledger-rg` reports both).
2. **Restore to a NEW server — never in place.** Azure's PITR restore always creates a new server; it
   cannot overwrite the source. This is a safety property, not a limitation: the original server (and its
   own backup chain) is untouched no matter what happens during the restore or its verification.
   ```bash
   az postgres flexible-server restore \
     --name ledgerapi-db-san-restored \
     --resource-group ledger-rg \
     --source-server ledgerapi-db-san \
     --restore-time "<UTC timestamp>"
   ```
3. **Open a firewall rule and set a password on the restored server** (its own, independent credential —
   this never touches the source server's credential):
   ```bash
   az postgres flexible-server firewall-rule create \
     --resource-group ledger-rg --server-name ledgerapi-db-san-restored \
     --name allow-verification --start-ip-address <your IP> --end-ip-address <your IP>
   az postgres flexible-server update \
     --resource-group ledger-rg --name ledgerapi-db-san-restored --admin-password "<new password>"
   ```
4. **Verify the data** before trusting it — row counts and a few real rows, not just "the server started":
   ```bash
   psql "host=ledgerapi-db-san-restored.postgres.database.azure.com port=5432 dbname=ledgerapi \
     user=ledgerapidb sslmode=require" -c "SELECT count(*) FROM accounts;" \
     -c "SELECT count(*) FROM ledger_entries;" \
     -c "SELECT reference, status, created_at FROM transactions ORDER BY created_at DESC LIMIT 5;"
   ```
5. **Cut over.** Update the App Service's `LedgerDb` connection string
   (`az webapp config connection-string set --name ledgerapi --resource-group ledger-rg ...`) to point at
   the restored server's hostname, restart the App Service, confirm `/health/ready` returns 200.
6. **Decide what to do with the old (incident-causing) server** — keep it for forensics, or delete it once
   root cause is understood. Don't delete it immediately after an incident purely out of tidiness.
7. **Rename or re-tag the restored server** so its name doesn't carry "-restored" forever, and update this
   document's procedure if anything about this process changed by the time it's used for real.

## The drill actually performed (22 Oct 2026)

A real restore was executed against production's actual backup chain, not simulated and not assumed to
work:

1. Restored `ledgerapi-db-san` to a new server `ledgerapi-db-san-drill`, restore point `2026-10-22T12:47:49Z`
   (~10 minutes before the drill started, comfortably inside the 35-day retention window).
2. **Measured:** the restore request was issued at approximately `12:57:51Z`; the new server reported
   `createdAt: 2026-10-22T12:59:19Z` and state `Ready` by the time the command returned — **roughly 90
   seconds**, on the same `Standard_B1ms`/32 GiB SKU production runs on.
3. Opened a firewall rule for the operator's IP and set a fresh admin password **on the drill server
   only** — production's own credential was never touched, reused, or exposed as part of this drill.
4. Connected and confirmed real, non-empty data: `\dt` showed all 7 expected tables
   (`accounts`, `ledger_entries`, `transactions`, `reversals`, `transaction_chains`, `audit_logs`,
   `__EFMigrationsHistory`); row counts were `66` accounts, `20` ledger entries, `11` transactions; the 5
   most recent transactions (real references like `TXN-7M3KGV4S7EAVSDZ`, status `Success`, real timestamps
   through 19 Oct 2026) matched this platform's own known demo/E2E history — not empty tables, not garbage.
5. **Cleaned up immediately**: deleted `ledgerapi-db-san-drill` once verification was complete (confirmed
   gone via `az postgres flexible-server list`), so the drill left no ongoing cost and no extra attack
   surface behind. Total extra Azure spend from this drill: roughly the per-minute cost of one
   `Standard_B1ms` instance for under 15 minutes.

This satisfies G9's actual requirement — "a restore has actually been exercised at least once rather than
assumed to work" — with a real, dated, measured result, not a procedure written and never tried.

## What this drill did not cover

Said plainly, so this isn't mistaken for more coverage than it has:

- **No cutover was performed.** The drill restored and verified data; it did not repoint the real
  `ledgerapi` App Service at the restored server, so step 5 of the procedure above is written but not
  itself drilled. Doing that against production for real, even briefly, is a bigger, riskier exercise than
  this task's "restore and verify" scope — worth a follow-up drill specifically for that step, on a
  deliberately chosen low-traffic window, with the owner's explicit sign-off beforehand.
- **No cross-region restore was performed.** Geo-redundant backup being enabled means a cross-region
  restore should be possible, but that specific path (restoring into a different Azure region from the
  geo-redundant copy, for a full South Africa North regional outage) was not tested here — only a
  same-region PITR restore was.
- **No application-level verification.** The drill confirmed the data exists and looks real; it did not
  boot a LedgerApi instance against the restored database and confirm the application itself behaves
  correctly against it (balance calculations, EF Core migrations history compatibility, etc.).
- **This is a single data point, not a schedule.** One successful drill is real evidence the mechanism
  works today; it is not a recurring verification. If this matters enough to re-drill periodically (e.g.
  before major migrations, or on some calendar cadence), that's a decision for the owner, not assumed here.

## Who should read this

Anyone operating LedgerApi in production, before an incident happens — not written as the first thing to
read *during* one. During a real incident, follow "The restore procedure" above directly; this document's
narrative sections exist to explain why those steps are trusted, not to be read step-by-step under pressure.
