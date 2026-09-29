# Order Desk: reliable order submission

A small order-submission application that stays correct when requests are retried, raced or
duplicated, and when the notification service fails.

- **ASP.NET Core 10** minimal API, **EF Core 10** (SQLite by default, SQL Server supported and tested)
- A **background worker** that delivers order confirmations through a transactional outbox
- An **Angular 22** client (standalone components, signals, zoneless) that reuses its
  `Idempotency-Key` on every retry
- **84 automated tests**: 76 .NET (domain, application, and HTTP-level integration against a real
  database) plus 8 client unit tests

Architecture and design decisions are in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

---

## Quick start

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download) and Node.js 22+.

### Option 1: one process (API, worker and UI together)

```bash
cd client && npm ci && npm run build:embedded && cd ..   # builds the UI into the API's wwwroot
dotnet run --project src/OrderSubmission.Api             # http://localhost:5080
```

The SQLite database is created and migrated on start-up (`src/OrderSubmission.Api/App_Data/orders.db`).
API reference (Scalar/OpenAPI) is at <http://localhost:5080/scalar> in Development.

### Option 2: API and Angular dev server

```bash
dotnet run --project src/OrderSubmission.Api     # terminal 1, http://localhost:5080
cd client && npm ci && npm start                 # terminal 2, http://localhost:4200 (proxies /api)
```

### Option 3: Docker

```bash
docker build -t order-submission . && docker run -p 8080:8080 order-submission   # SQLite
docker compose up --build                                                         # SQL Server
```

Both serve the whole application at <http://localhost:8080>.

### Tests

```bash
dotnet test                       # 76 tests; integration tests use a real SQLite database
cd client && npm test             # 8 client tests (Vitest)
```

The same integration suite runs against SQL Server when given a connection string. Each test class
creates and drops its own database:

```bash
docker run -d --name sql -e ACCEPT_EULA=Y -e "MSSQL_SA_PASSWORD=OrderDesk!2026" -p 14333:1433 mcr.microsoft.com/mssql/server:2022-latest
ORDER_TESTS_SQLSERVER="Server=localhost,14333;User Id=sa;Password=OrderDesk!2026;TrustServerCertificate=True" dotnet test
```

---

## A two-minute tour

1. Open the app, press **Fill with an example** (or pick products from the list, which fills in
   their prices), then **Place order**. You land on the confirmation page: order number,
   **server-calculated total**, and the order confirmation going from *Sending* to *Sent*.
2. Open **Test tools** in the header. Under *Duplicate-request checks*, run the three checks
   against the live API:
   - **Resend the same request**: `200 OK`, `Idempotent-Replayed: true`, same order
   - **Reuse the key with a different quantity**: `409 Conflict`
   - **Send 5 identical requests at once**: `1 × 201` and `4 × 200`, exactly one new order
3. In Test tools, set *Messaging service* to **Down** and place another order. The header shows
   *Messaging: Down*, and the confirmation card says *Confirmation delayed* while attempts are
   retried after 1s, 2s, 4s and so on (the development schedule). Set it back to **Working**: the
   next attempt delivers with no user action. If all 5 attempts fail first, the card offers
   **Send it again**, which re-queues the confirmation.
4. Stop the API and press **Place order**: the form says *We could not confirm your order* and
   offers **Try again**. Restart the API and reload the page: the unconfirmed order is restored, and
   **Try again** re-sends it with the **same key** (shown under *Technical details*).

The same through `curl`:

```bash
KEY=$(uuidgen)
BODY='{"customerReference":"ACME-4471","items":[{"productCode":"CHR-ERGO-BLK","quantity":2,"unitPrice":189}]}'
curl -i -X POST localhost:5080/api/orders -H "Idempotency-Key: $KEY" -H "Content-Type: application/json" -d "$BODY"   # 201
curl -i -X POST localhost:5080/api/orders -H "Idempotency-Key: $KEY" -H "Content-Type: application/json" -d "$BODY"   # 200, Idempotent-Replayed: true
curl -X PUT localhost:5080/api/simulation/notification-service -H "Content-Type: application/json" -d '{"mode":"Outage"}'
```

---

## API

| Method & path | Purpose | Responses |
|---|---|---|
| `POST /api/orders` + `Idempotency-Key` header | Place an order | `201` created · `200` replay (`Idempotent-Replayed: true`) · `400` validation · `409` key reused with a different payload · `429` rate limited |
| `GET /api/orders/{id}` | Order, server total and notification delivery status with attempt history | `200` · `404` |
| `POST /api/orders/{id}/notification/retry` | Re-queue a Failed notification, or skip the back-off of a Pending one | `202` · `404` · `409` already delivered |
| `GET`/`PUT /api/simulation/notification-service` | Switch the fake notification service between `Healthy`, `Flaky` (with a `failureRate`) and `Outage`, with `latencyMs` | `200` · `400` · `403` when disabled |
| `GET /health/live`, `GET /health/ready` | Liveness; readiness includes the database | `200` · `503` |

Errors are RFC 9457 problem details with a machine-readable `code` (for example `idempotency.key_reused`)
and a `traceId`.

Request body:

```json
{ "customerReference": "ACME-4471", "items": [ { "productCode": "CHR-ERGO-BLK", "quantity": 2, "unitPrice": 189.00 } ] }
```

The body has no total field. The server calculates line totals and the order total.

---

## How each requirement is met

| Requirement | Mechanism | Verified by |
|---|---|---|
| `POST /api/orders` accepts `Idempotency-Key` | Header bound in the endpoint. It is required, at most 128 visible ASCII characters, and case-sensitive. | `A_request_without_an_idempotency_key_is_rejected` |
| Same key and payload returns the original order, no new one | `IdempotencyRecords` table (key as primary key, SHA-256 request fingerprint, order ID). A hit with a matching fingerprint replays the stored order. | `Repeating_the_same_key_and_payload_returns_the_original_order_without_creating_another`, `A_retry_that_differs_only_in_formatting_is_still_the_same_request` |
| Same key, different payload returns `409` | Fingerprint mismatch returns `Conflict` / `idempotency.key_reused` | `Reusing_a_key_with_a_different_payload_returns_409_and_changes_nothing` |
| Concurrent same-key requests never duplicate | The **database primary key is the arbiter**. The loser's whole transaction is rejected by the unique constraint, and it replays the winner. There are no in-memory or distributed locks, so this holds across any number of API instances. | `Concurrent_requests_with_the_same_key_create_exactly_one_order` (32 parallel), `Concurrent_requests_racing_with_different_payloads_…`, `Losing_a_race_to_a_concurrent_duplicate_replays_the_winner` |
| `GET /api/orders/{id}` returns order and delivery status | No-tracking projection: order, lines, and notification status with its attempt log | `Get_returns_the_order_with_its_server_calculated_total_and_delivery_status` |
| Order and pending notification saved atomically | One `SaveChanges` writes order, lines, notification and idempotency record in one transaction (transactional outbox) | `Order_and_pending_notification_are_saved_atomically`: the notification `INSERT` executes, then fails, and nothing is persisted |
| Background worker sends pending notifications through a fake service | `NotificationDispatchWorker` → leased batches → `NotificationDeliveryService` → `INotificationSender` (fake) | `The_background_worker_delivers_new_orders_without_any_manual_step` |
| Configurable temporary failure | `FaultInjectingNotificationSender` decorator: `Healthy`, `Flaky` (rate) or `Outage`, plus latency. Set via configuration (`Notifications:FakeService`), the API, or the UI switch. | All delivery tests |
| Failed delivery remains retryable | Transient failures are rescheduled with exponential back-off and jitter. After `MaxAttempts` the notification is `Failed` but kept, and can be re-queued through the API or UI. | `A_failed_delivery_stays_retryable_and_recovers_once_the_service_is_back`, `Exhausted_retries_mark_the_notification_failed_and_a_manual_retry_requeues_it`, `The_background_worker_keeps_retrying_through_an_outage_and_delivers_after_recovery` |
| Angular: submit and show result and delivery status | Order form, then a confirmation page with the order summary and a live confirmation status (adaptive polling) | Manually and in a headless-browser run (see *Verification*) |
| Angular: retrying reuses its key | `OrderSubmissionStore`: one key per logical submission, persisted until the server gives a definitive answer. Automatic transient retries, manual retries and retries after a page reload all reuse it; editing the order issues a new key. | `order-submission-store.spec.ts` (8 tests) |
| No duplicate delivery from competing workers | Lease (conditional `UPDATE`) plus lease-token concurrency check on completion | `Competing_dispatchers_deliver_every_notification_exactly_once`, `A_worker_that_lost_its_lease_cannot_overwrite_the_new_owners_outcome` |

---

## Solution layout

```
src/
  OrderSubmission.Domain          Order and Notification aggregates, invariants, delivery state machine. No dependencies.
  OrderSubmission.Application     Use cases (CQRS handlers), ports, validation, idempotency, retry policy, delivery logic.
  OrderSubmission.Infrastructure  EF Core (per-provider contexts and migrations), outbox leasing, fake notification
                                  service with fault injection, dispatcher and background worker.
  OrderSubmission.Api             Minimal API endpoints, problem details, rate limiting, health, OpenAPI; hosts the SPA.
tests/
  OrderSubmission.Domain.UnitTests
  OrderSubmission.Application.UnitTests
  OrderSubmission.Api.IntegrationTests   WebApplicationFactory against a real database, fake clock.
client/                           Angular 22 app: core/, features/orders/{data-access,ui,pages}, features/simulation, shared/ui
```

Dependencies point inward (Api → Infrastructure → Application → Domain). Patterns used where they
earn their place: **Transactional Outbox**, **Competing Consumers with leases**, **Repository +
Unit of Work**, **CQRS** (command handlers on aggregates, query side projecting DTOs),
**Decorator** (validation and logging pipeline, fault injection), **Strategy** (retry policy,
database provider), **Result** (expected failures without exceptions), **Options** (validated at
start-up).

---

## Assumptions

- **Money**: one implicit currency. Unit price is greater than 0, at most 1,000,000, with at most 2
  decimal places. Quantity is 1 to 10,000. An order has 1 to 100 lines. Totals are exact decimals.
- **Text**: customer reference is at most 64 characters and product code at most 50. Surrounding
  whitespace is trimmed; case is preserved and significant. The same product may appear on several lines.
- **"Same payload"** means the same order after normalisation: JSON formatting, property order,
  whitespace and trailing zeros (`10.5` vs `10.50`) are ignored. Line order is significant.
- **Idempotency keys** are global (there is no authentication or tenancy in scope), case-sensitive,
  and kept indefinitely. A request rejected with `400` does not consume its key.
- **Replay response**: `200` with `Idempotent-Replayed: true` and the order's *current* state,
  including live delivery status, rather than a byte copy of the first response. The order itself
  never changes.
- **Notification**: one order confirmation per order, addressed to the customer reference (the
  brief has no email or phone). Delivery is **at least once**. The notification ID is passed to the
  sender so a real provider can de-duplicate.
- **Retry schedule** (production defaults): 2s initial delay doubling up to 5 min, ±20% jitter, 8
  attempts, then `Failed` and re-queueable. Development uses 1s → 15s and 5 attempts so the full
  cycle can be watched.
- **Out of scope**: authentication and authorisation, payments, real messaging. The simulation
  endpoint is a demo tool and can be switched off with `Notifications:FakeService:AllowRuntimeChanges=false`.

---

## Scaling to millions of users

The short version (details in [ARCHITECTURE.md § Scaling](docs/ARCHITECTURE.md#scaling)):

- **Stateless API; the database arbitrates idempotency.** Instances scale horizontally behind a load
  balancer with no sticky sessions and no distributed lock. The write path is one primary-key lookup
  plus one short transaction, with no external call inside it.
- **Outbox workers scale independently.** Leasing makes workers competing consumers, so throughput
  is instances × `MaxConcurrency`. `Notifications:Dispatcher:Enabled=false` turns a node into an
  API-only node for separate API and worker tiers.
- **Storage**: SQL Server (or any relational store with the same two properties: unique keys and
  atomic conditional updates). Time-ordered UUIDv7 keys, a filtered outbox index, DbContext pooling,
  and connection resiliency. SQLite is for development only (single writer).
- **Next steps at higher volume**: relay the outbox to a broker (Kafka / Service Bus) instead of
  polling, server-sent events instead of client polling, per-tenant key scoping and sharding by
  customer, an idempotency-record TTL job, and OpenTelemetry metrics on outbox lag and failed deliveries.

---

## Verification performed

- `dotnet test`: 76/76 passing on SQLite. The integration suite (21) also passes against SQL Server
  2022 in Docker.
- `npm test`: 8/8 passing. `ng build` production bundle: ~77 kB initial transfer, pages lazy-loaded.
- A headless-browser run of the real UI: place order → receipt → outage retries → the three
  idempotency checks pass in the UI → recovery to Delivered. The layout was checked at 1440px and at
  a 390px phone viewport with no horizontal overflow.
- Docker: the Dockerfile's client stage (`npm ci` and the production build on `node:24-alpine`) builds,
  and the .NET stage's `dotnet publish -c Release` succeeds locally. The complete image and
  `docker compose up` were **not** run end to end.

## Not done / next steps

- Authentication, and scoping idempotency keys per client or tenant.
- A retention job for idempotency records (indexed on `CreatedAt`, not yet scheduled).
- OpenTelemetry traces and metrics, and alerting on `Failed` notifications and outbox age.
- Browser end-to-end tests in CI (Playwright); the UI flow above was verified with a local script
  that is not committed.
- Load tests (k6) to put numbers behind the scaling section.
- A CI workflow is included (`.github/workflows/ci.yml`) but has not run on a hosted runner yet.
