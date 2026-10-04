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

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (pinned by `global.json`)
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

This also seeds a funding system account for every supported currency (`NGN100000001`, `USD100000001`, …) that deposits and withdrawals use.

**3. Run the API:**

```bash
dotnet run --project src/LedgerApi --launch-profile http
```

Swagger UI is at http://localhost:5293/swagger (click **Authorize** and paste a development key from [Authentication](#authentication)). [src/LedgerApi/LedgerApi.http](src/LedgerApi/LedgerApi.http) has a ready-made request for every endpoint (VS Code REST Client, Rider or Visual Studio).

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
| `401` | Missing or unknown API key |
| `403` | The API key lacks the endpoint's scope |
| `404` | Account or transaction not found |
| `409` | Transaction already reversed |
| `422` | A business rule failed: insufficient funds, account status, currency mismatch, or a transaction that can't be reversed. For transfers, deposits and withdrawals the failure is committed and the body carries `reference`, `status: "Failed"` and `failureReason`. A retry with the same idempotency key returns the same 422. |
| `500` | Unexpected error. Retry with the **same** idempotency key to learn whether money moved. |

## Supported currencies

| Decimal places | Currencies |
|---|---|
| 2 | USD, EUR, GBP, NGN, CNY, GHS, SAR, QAR, AED, CHF, CAD, AUD, ZAR, KES, EGP, MAD, INR |
| 0 | XOF, XAF (CFA francs), JPY |

Codes are matched exactly (uppercase). Account creation rejects other codes with 400, and a transfer, deposit or withdrawal whose amount has more decimal places than its currency allows (e.g. `1500.50` XOF) fails with 422. Balances are returned with the currency's decimal places: `1500` for XOF or JPY, `10.50` or `0.00` for NGN. Three-decimal currencies such as OMR are not supported yet (DESIGN.md decision #37).

## Authentication

The ledger authenticates **calling services**, not customers (DESIGN.md decision #34). Every request needs an `X-Api-Key` header, and each key is granted **scopes**:

| Scope | Endpoints |
|---|---|
| `ledger.read` | `GET /api/accounts/{n}`, `GET /api/accounts/{n}/balance` |
| `ledger.accounts` | `POST /api/accounts` |
| `ledger.transfer` | `POST /api/transfers` |
| `ledger.funding` | `POST /api/deposits`, `POST /api/withdrawals` |
| `ledger.reverse` | `POST /api/reversals` |
| `ledger.admin` | `POST /api/admin/system-accounts` |

A missing or unknown key gets **401**; a valid key without the endpoint's scope gets **403**.

### Development keys

`appsettings.Development.json` configures two local-only clients. Never use these outside your machine.

| Client | Key | Scopes |
|---|---|---|
| `dev-payment-api` | `lk_dev_payment_wj6C4L8LnpS03vUDi_UV6CZJClsr4SBc` | read, accounts, transfer, funding |
| `dev-ops` | `lk_dev_ops_1pGPvWKNhflS5oblWCbjHC6a0f6buOri` | read, reverse, admin |

In Swagger UI, click **Authorize** and paste a key.

### Issuing a key

Only the SHA-256 hash of a key is configured, never the key itself. Generate a key and its hash:

```bash
KEY="lk_$(openssl rand -base64 32 | tr '+/' '-_' | tr -d '=')"; echo "$KEY"; printf %s "$KEY" | openssl dgst -sha256 -binary | base64
```

Give the key to the calling service and add the hash to configuration, e.g. through environment variables:

```bash
export ApiKeys__Clients__0__ClientId=payment-api
export ApiKeys__Clients__0__KeyHash=<hash>
export ApiKeys__Clients__0__Scopes__0=ledger.read
export ApiKeys__Clients__0__Scopes__1=ledger.transfer
```

Serve the API over **HTTPS only** in production (at your proxy/ingress or Kestrel), with no plain-HTTP port exposed: the app does not redirect HTTP to HTTPS, because by then the key has already been sent in plain text (DESIGN.md decision #36).

`appsettings.json` ships with no clients, so an environment without keys rejects every request. The app refuses to start if a configured hash or scope is invalid. To rotate a key, add a second entry for the same `ClientId` with the new hash, move the caller over, then remove the old entry.

## Logging and tracing

Every request gets a **correlation id**. Send your own in an `X-Correlation-ID` header (1-64 letters, digits, `-`, `_` or `.`), or let the API generate one. It comes back in the `X-Correlation-ID` response header, and error responses include it as `correlationId`.

- **Request logs** (Serilog, console): one line per request with method, path, status code, duration, correlation id, user, client IP and, for money-moving requests, the idempotency key. Request bodies are never logged. Configure it in the `Serilog` section of `appsettings.json`.
- **Audit trail** (`audit_logs` table): one row per account creation, transfer, deposit, withdrawal and reversal, committed in the same database transaction as the change. Each row carries the correlation id.

To investigate a request, search the logs for its correlation id, then query `audit_logs` by `correlation_id`.

## Deployment

The `Dockerfile` builds two images:

```bash
docker build -t ledgerapi .                                   # the API
docker build --target migrator -t ledgerapi-migrator .        # applies pending migrations, then exits
```

**Each release:** run the migrator against the production database first, then start the new API image. Never migrate from a developer machine, and do not migrate at API startup (two instances would race).

```bash
docker run --rm -e ConnectionStrings__LedgerDb="<connection string>" ledgerapi-migrator
docker run -p 8080:8080 \
  -e ConnectionStrings__LedgerDb="<connection string>" \
  -e ApiKeys__Clients__0__ClientId=payment-api \
  -e ApiKeys__Clients__0__KeyHash=<hash> \
  -e ApiKeys__Clients__0__Scopes__0=ledger.read \
  ledgerapi
```

- The API listens on port **8080** as a non-root user. Terminate HTTPS at the platform or a proxy (DESIGN.md decision #36).
- `ASPNETCORE_ENVIRONMENT` defaults to `Production`: Swagger is off, `appsettings.Development.json` is not in the image, and with no `ApiKeys` configured every request gets 401.
- Health checks (no API key needed): **`/health/live`** answers while the process runs (use it for restarts); **`/health/ready`** also checks the database (use it for routing traffic and gating deploys). Platforms with a single health URL should use `/health/ready`.
- Use a managed PostgreSQL with point-in-time recovery: the ledger's entries are the only source of truth for balances.

GitHub Actions (`.github/workflows/ci.yml`) runs the tests and builds both images on every pull request and push to `main`.

### Azure (production)

Production runs on **Azure App Service** (`ledgerapi`, Linux, .NET 8) with **PostgreSQL Flexible Server** (`ledgerapi-db-san`), both in South Africa North, in resource group `ledger-rg`. Every push to `main` that passes the tests is deployed by the `deploy` job in `ci.yml` (or press **Run workflow** to redeploy):

1. Publishes `src/LedgerApi` only (never the solution) without `appsettings.Development.json`.
2. Opens the database firewall to the runner's IP, applies the migration bundle, then removes the rule.
3. Zip-deploys with `--clean`, so no files from older deployments remain.
4. Smoke-tests `/health/ready` (200) and a request without a key (401).

Configuration lives in the App Service: the `LedgerDb` connection string (type **Custom**: .NET 8 ignores the PostgreSQL type) and the `ApiKeys__Clients__…` settings. The deploy job reads the connection string from there; it is not stored in GitHub. Always call the API over **https**.

## Project layout

```
src/LedgerApi/
  Controllers/      HTTP endpoints (Admin/ holds the system-account route)
  Services/         Business logic: Transfer, Deposit, Withdrawal, Reversal, Account, AdminAccount
  Entities/         Account, Transaction, LedgerEntry, Reversal, AuditLog
  Data/             LedgerDbContext and model configuration
  Migrations/       EF Core migrations
  Validation/       Request validators
  Middleware/       Correlation id, exception-to-status mapping, idempotency-key log filter
  Auditing/         Audit trail (IAuditLogger stages rows on the request's DbContext)
tests/LedgerApi.Tests/
  Services/         Integration tests against PostgreSQL (Testcontainers)
  Controllers/      Status-code mapping tests with stub services
  Validation/       Validator unit tests
docs/DESIGN.md      Design document and decisions log
```

## Out of scope

Customer-level authorization (mandates) is out of scope by design: the calling service checks that its customer may act on an account, and the ledger authorizes only the service (DESIGN.md decision #34).
