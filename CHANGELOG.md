# Changelog

All notable changes to GARBATI will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

GARBATI does not yet have versioned releases — this file starts tracking from the first one. For a full, dated history of everything built before that point (every merged feature across all five services, with the reasoning behind each decision), see [`LEARNING_LOG.md`](LEARNING_LOG.md) — this changelog deliberately doesn't try to reconstruct that history retroactively with the same confidence a real-time entry would have.

## [Unreleased]

### Added

- Chart of accounts (decision #48, T-061): a new `GlAccount` entity (`Asset`/`Liability`/`Equity`/`Income`/`Expense`, with a self-referencing parent for a header/detail hierarchy), an optional `GlAccountId` on `Account`, a `GlAccountsController` (`POST`/`GET /api/admin/gl-accounts`), an optional `GlAccountId` on `SystemAccountsController`'s create endpoint, and a new `GET /api/reports/trial-balance` report that rolls up every account's existing derived balance by GL account and currency. Existing `accounts` rows are backfilled by migration into the right GL category based on which prior seed migration created them.
