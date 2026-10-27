# Local integration tests

The suite runs against the real native library and PostgreSQL 18. Email/PDF cases create a real
one-page invoice PDF, send it as a MIME attachment through MailKit to Mailpit, then retrieve and
parse the captured message and PDF. No external email provider or real recipient is used.

## Start the test services

Install the build prerequisites from the README, Docker Compose and OpenSSL. From the repository:

```sh
bash scripts/build-native.sh
docker compose up -d --wait
bash scripts/configure-test-postgres-tls.sh "$(docker compose ps -q postgres)" "$PWD/artifacts/tls"
```

The TLS helper is for this disposable Compose PostgreSQL container. It creates a two-day private
CA and localhost certificate, enables server TLS, and reloads the configuration. The CI integration
job runs the same helper. It does not require the .NET library to implement schema migrations.

Compose initializes a database only when its volume is new. Tests create their own isolated schemas
using SQL from the resolved published crate. Existing application schemas are not rewritten by these tests.

## Run every test without skips

```sh
export STAMPEDED_TEST_CONNECTION_STRING='Host=localhost;Port=55432;Database=stampeded;Username=stampeded;Password=stampeded'
export STAMPEDED_REQUIRE_DATABASE_TESTS=1
export STAMPEDED_TEST_MAILPIT_URL=http://localhost:8025
export STAMPEDED_TEST_SMTP_HOST=localhost
export STAMPEDED_TEST_SMTP_PORT=1025
export STAMPEDED_REQUIRE_WORKLOAD_TESTS=1
export STAMPEDED_TEST_TLS_ROOT_CERT="$PWD/artifacts/tls/ca.crt"
export STAMPEDED_REQUIRE_TLS_TESTS=1
export STAMPEDED_TEST_ARTIFACTS="$PWD/artifacts/workloads"

dotnet test --solution Stampeded.slnx -c Release
```

The required flags make missing configuration fail. Without them, unconfigured database, TLS or
workload cases skip so native ABI tests can still run on the platform build jobs. A stopped or
misconfigured service fails a configured test. On Windows, use Git Bash and Windows-compatible
absolute paths for certificate and artifact environment variables.

## Inspect the results

- Mailpit UI: http://localhost:8025. Each test uses an `example.test` recipient and a subject containing
  its job ID. Mailpit is an inbox sink with no relay configured.
- `artifacts/workloads/invoice-*/` contains the generated PDF and captured `message.eml`. Without
  `STAMPEDED_TEST_ARTIFACTS`, test files use temporary directories and are deleted when the test ends.
- The tests check the recipient, Unicode email body, one PDF attachment, exact attachment bytes,
  page count and invoice text. They also check the persisted job state and attempt count.
- The retry case deliberately fails after the PDF checkpoint and before SMTP delivery. It verifies
  that the next attempt reuses the saved step result and sends one captured message. This is not
  evidence of exactly-once delivery across a process kill or an ambiguous SMTP acknowledgement.
- The TLS case checks `pg_stat_ssl` for the native pool and rejects a different, untrusted CA.

`SchemaCompatibilityTests` also checks schema version 3, distinct hourly/daily deduplication windows,
and the upstream colon-scope forgery fix. The separate [upstream review](upstream-0.4-review.md)
records the byte-escape scope collision that remains outside those passing cases.

## Stop the test services

```sh
docker compose down
```

This preserves the PostgreSQL volume. The certificate helper and Docker service setup are covered
by the workflow configuration; execution of that Docker-based bootstrap still needs a fresh CI run.
