# Data migration — Neon Postgres → Azure SQLite

How the original ReadLog's real reading history was migrated from the Next.js app's
**Neon PostgreSQL** database into the .NET port's **SQLite** database running on Azure
App Service. This is both a record of the one-off migration and a worked example of a
**cross-engine, cross-ORM, cross-identity-system** data move — the interesting part of a
port that a schema diagram doesn't show.

> **No secrets here.** This document contains no connection strings, API keys, tokens, or
> real personal emails. The Neon URL, the Google Books key, and the short-lived Azure
> token were handled only outside the repository (an ephemeral Azure Cloud Shell and an
> out-of-tree scratch directory) and were never committed. Third-party reader identities
> were anonymized (see [Identity](#3-identity--the-load-bearing-decision)).

## Why this was non-trivial

It is not a `pg_dump | restore`. Four things differ between source and target:

| | Original (source) | .NET port (target) |
| --- | --- | --- |
| Engine | Neon **PostgreSQL** (shared, multi-project) | **SQLite** file on Azure's `/home` share |
| ORM / schema | Prisma, `readlog_`-prefixed tables, `cuid()` string PKs | EF Core, default table names, `int` autoincrement PKs |
| Auth | NextAuth (Google OAuth, DB sessions) | ASP.NET Core Identity (cookie auth, `AspNet*` tables) |
| Types | Postgres `enum`, `timestamptz` | enum-as-string, `DateOnly`, EF's TEXT datetime format |

So the move is an **ETL**: read from Postgres, transform every representation, load into
SQLite. The source Neon database is shared across several projects, so the read touches
**only** the `readlog_`-prefixed tables (`readlog_user`, `readlog_book`, `readlog_entry`)
and is strictly **read-only** on the Postgres side.

## Schema mapping

| Neon table | Target | Disposition |
| --- | --- | --- |
| `readlog_user` | `AspNetUsers` | migrate (see Identity) |
| `readlog_book` | `Books` | migrate (PK remap) |
| `readlog_entry` | `ReadEntries` | migrate (PK + FK remap, enum, date) |
| `readlog_format` (PG enum) | `ReadEntries.Format` (TEXT) | value translation |
| `readlog_account` / `readlog_session` / `readlog_verification_token` | — | dropped — Identity uses a cookie ticket + `AspNetUserLogins`, not DB sessions/tokens |

## The transforms (and why each is needed)

### 1. cuid string PKs → int autoincrement
`Book`/`ReadEntry` use `int` identity keys; the source uses `cuid()` strings. The source
ids can't be inserted, so:
- Books are **find-or-created keyed by `OpenLibraryId`** (unique in both schemas); SQLite
  assigns the new `int` id, and an in-run map (`old key → new id`) lets each entry's
  `bookId` be rewritten to the new int FK.
- `ReadEntry` ids are simply discarded (nothing references them).

### 2. Postgres enum → C# enum name
The column stores the **enum name** the EF `HasConversion<string>()` expects, so
`BOOK / AUDIOBOOK / EBOOK` are translated to `Book / Audiobook / Ebook` (a literal copy
would fail to round-trip).

### 3. `DateTime` → `DateOnly`, and the unique index
`finishedAt` (a Postgres timestamp) becomes a `DateOnly` (the **UTC date**, matching the
original's "parsed as UTC-midnight" semantics). Because the target has a unique index on
`(UserId, BookId, FinishedAt)`, rows are **de-duplicated on the post-conversion triple**
before insert so a same-day collision can't abort the load. `CreatedAt` is carried over
from the source verbatim (preserving public-feed ordering), not stamped at import time.

### 4. Identity — the load-bearing decision
The source users are **OAuth-only (no passwords)**. Mapping:
- The **owner** is matched to the existing `AspNetUsers` row **by email**, so the imported
  reads attach to the real account and stay editable. That account's **Google login link
  is preserved**, so signing in with Google still resolves to the same user.
- Every **other reader is anonymized**: a placeholder account (`reader-1@imported.local`,
  `reader-2@imported.local`) with no password and no real email. Their reads become
  **feed-only** content. This is a deliberate privacy choice — and it's lossless to the
  UI, because the public feed (`PublicReadDto`) carries **no user fields**: the feed never
  shows *who* read a book, only the book.

### 5. Constraints honored
- **Rating** `null` (unrated) is preserved; `0` is a real value; any value outside `0..5`
  is skipped (the target adds a `CHECK (Rating IS NULL OR 0..5)` the original lacked).
- The dead **`Notes`** column (dropped from the .NET schema) has no destination; the
  feature was never ported, so source notes are discarded.

## Procedure (the safe swap)

SQLite lives at `/home/data/readlog.db` on Azure's SMB file share — single-writer, no
reliable file locking — so the file is replaced **offline**, with a backup taken first.

1. **Auth** — a short-lived AAD/ARM access token (from Azure Cloud Shell). Never stored.
2. **Recon (read-only)** — confirm the app, its settings, and the DB path via the ARM and
   Kudu (SCM) REST APIs.
3. **Back up + base** — download the live `readlog.db` over the **Kudu VFS** API. This copy
   is both the backup and the transform base; verify `PRAGMA integrity_check`.
4. **Build locally** — on a copy: clear the demo-seed rows → run the ETL import
   (anonymized) → `wal_checkpoint(TRUNCATE)` → **verify** (row counts, owner reads
   attached, Google login intact, and `__EFMigrationsHistory` still at head so startup
   migration is a no-op).
5. **Swap** — `stop` the app (quiesce the single writer) → **upload** the new DB via Kudu
   (force-overwrite) → delete any stale `-wal`/`-shm` sidecars → `start`.
6. **Google Books** — set the `GoogleBooks__ApiKey` app setting via ARM (read-merge-write,
   preserving all other settings; saving restarts the app).
7. **Verify** — `/health` 200, the feed renders the imported reads, and a public
   `/Book` detail page returns Google-enriched data (description/publisher) — the
   end-to-end proof the key is active.

## Result (verified live)

- **9 reads, 9 books, 3 users** (1 real owner + 2 anonymized readers).
- The owner's **5** reads attach to their real account (editable in Library); the other
  **4** are anonymized feed entries. All covers, ratings, formats, and dates carried over.
- **Google Books enrichment is live** (merged search results + rich detail pages).
- Startup `Database.Migrate()` is a no-op — the uploaded DB is already at head
  (`InitialCreate` + `RemoveReadEntryNotes`).

## Rollback

Re-upload the pre-swap backup (`readlog.db` downloaded in step 3) via Kudu VFS and
restart. The import is also safely repeatable: books find-or-create by `OpenLibraryId`
and entries de-dup on `(UserId, BookId, FinishedAt)`, so a re-run doesn't double-insert.

## Caveats / production upgrade path

- **SQLite on the `/home` SMB share** is single-writer and officially unsupported for
  file-DB providers; it works only because this is a single F1 instance, and it's why the
  swap requires stopping the app. A managed Postgres/SQL Server (the EF layer is
  provider-swappable) would remove the offline-swap dance entirely.
- **Anonymized readers can't sign in** (no password, no external-login link) by design —
  their reads exist purely as feed content.
