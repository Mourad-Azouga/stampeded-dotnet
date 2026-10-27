# Upstream 0.4.0 compatibility review

Reviewed on 2026-10-25 against `seuros/stampeded` commit
`d84c19ff302688c3d0a997b7be9a0b4f25e4ce8a`, following the 0.4.0 release and its Cargo.lock fix.
The previous binding pin was `53bc32de1c5fc4cb84f8123c1871d6738f48c62d` (core 0.3.2).

## Binding impact

- The generated C header and exported FFI source are byte-for-byte unchanged. C ABI version remains
  0.1.0; no C# interop declaration or native struct layout needed changing.
- The FFI crate now enables `tls-aws-lc` by default. The binding builds that default and verifies
  `sslmode=verify-full` with a private CA against PostgreSQL 18, including untrusted-CA rejection.
- Fresh databases record schema version 3. The new upstream `schema::migrate` supports repeat and
  concurrent starts, upgrades existing 0.3 installs, and rejects unsupported/newer schemas.
  Migration is not exported by the C ABI: use the matching upstream daemon's `--migrate`.
- The deduplication expression now includes the period and uses byte separators. The .NET tests
  cover the documented hourly/daily collision and colon-scope forgery fixes.
- The saturated-slot wakeup change is in the full upstream `Worker`; the C# hosted worker uses
  the FFI `Puller` path. It is not a new C# worker feature.

## Verified on Linux x64

- Built the real FFI from the new pin with Rust 1.99.0 and the upstream Cargo.lock.
- Ran all 29 .NET tests against PostgreSQL 18.6 and Mailpit 1.32.0: no failures or skips.
- Ran upstream's five schema migration tests against the same disposable PostgreSQL instance:
  all passed with `CI=1` and the database URL configured, preventing silent skips.
- Generated and parsed real PDFs and captured MIME messages, including a checkpoint/resume retry.
- A fresh-cache NuGet consumer loaded the packaged library, used private-CA TLS and completed a
  Unicode job with a persisted named step; PostgreSQL confirmed `completed|1|1`.
- NuGet restore audit passed after pinning the mail stack's test-only PKCS dependency to 10.0.13.

The updated Windows/macOS native jobs and Docker-based CI bootstrap have not run as part of this
local review. No NuGet publication or production rollout is implied by these results.

## Reproduced upstream issue: distinct byte-escape scopes alias

`dedup_scope::bytea` interprets PostgreSQL bytea input syntax. The distinct text scopes `a` and
`\x61` therefore both become byte `0x61`. In one queue, enqueueing both leaves only the first job;
the second is refused as a duplicate. This was reproduced on both the previous 0.3 schema and
the current 0.4 schema, so it is a pre-existing upstream behavior rather than a .NET regression.

Run the rollback-only reproducer against a disposable PostgreSQL 18 database:

```sh
psql "$STAMPEDED_DATABASE_URL" -f docs/repros/dedup-bytea-scope.sql
```

Observed output:

```text
actual_jobs | expected_distinct_jobs
------------+-----------------------
          1 |                     2
```

The schema's comment that no text can decode to a zero byte also does not hold for this cast:
`encode($$\x00$$::text::bytea, 'hex')` returns `00`. This report establishes the scope alias above;
it does not assert that every older periodic-key forgery remains possible.

Upstream should preserve raw text bytes in the generated key rather than interpreting bytea escape
syntax, with a regression test for these distinct scopes. The .NET binding does not rewrite scope
values or maintain a separate SQL fix.
