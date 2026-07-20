# Cross-API Data Sync with RabbitMQ

Event-driven synchronization between two independent .NET services. Service A owns the data
and exposes full CRUD over a `Product` entity. Service B keeps a read-only replica that is kept
in sync purely through messages. There is no direct call path between the two services — every
change flows through RabbitMQ.

```
Service A (CRUD API)
   -> DB Table (Products) + Outbox (single transaction)
   -> Outbox Publisher (background worker, publisher confirms)
   -> Exchange data.sync (topic)  [product.created | product.updated | product.deleted]
        -> Queues sync.created / sync.updated / sync.deleted
             -> Service B consumer (manual ack)
                  -> idempotency check (EventId) + version ordering
                  -> commit replica + processed-event + sync status
                  -> on failure: retry (TTL tiers) -> dead-letter queue
Service B (GET-only API) reads the replica and the sync status table
```

## Projects

| Project | Description |
| --- | --- |
| `src/Shared.Contracts` | Message contract (`ProductSyncMessage`), routing keys and topology names |
| `src/ServiceA.Api` | Producer: CRUD controller, outbox, publisher worker |
| `src/ServiceB.Api` | Consumer: background worker, retry/DLQ handling, GET-only controllers |
| `tests/Sync.UnitTests` | Idempotency, ordering, outbox and status-transition tests (SQLite in-memory) |
| `tests/Sync.IntegrationTests` | End-to-end, duplicate-delivery and DLQ tests (Testcontainers) |

## Running with Docker

The provided MySQL server is used for both databases. RabbitMQ runs in the compose stack.

```
docker compose up --build
```

- Service A: http://localhost:5001/swagger
- Service B: http://localhost:5002/swagger
- RabbitMQ management: http://localhost:15672 (user/password from `.env`)

Service A creates the `TaskDb` schema on startup; Service B creates `TaskDb_ServiceB`. Both are
created automatically on first run. The MySQL host in `.env` must be reachable from the machine
running the stack.

## Running locally

Start a RabbitMQ instance (for example `docker run -p 5672:5672 -p 15672:15672 rabbitmq:3.13-management`),
then run each service. The default local settings use `guest/guest` against `localhost`.

```
dotnet run --project src/ServiceA.Api
dotnet run --project src/ServiceB.Api
```

## API

Service A (`/api/products`):

- `GET /api/products`
- `GET /api/products/{id}`
- `POST /api/products`
- `PUT /api/products/{id}`
- `DELETE /api/products/{id}`

Service B (read-only):

- `GET /api/products`
- `GET /api/products/{id}`
- `GET /api/sync-status`
- `GET /api/sync-status/{aggregateId}`
- `GET /health`

## Message flow and guarantees

### Outbox (Service A)

Each create/update/delete writes the entity change and an `OutboxMessage` row inside the same
database transaction. A background worker polls unpublished rows and publishes them to the
`data.sync` topic exchange using publisher confirms. A row is only marked as published once the
broker confirms it, so a crash or a broker outage never loses a change — the worker retries on
the next cycle. Every message carries a unique `EventId`, a `Version` and an `OccurredAt`
timestamp.

### Consumer (Service B)

The consumer uses the RabbitMQ client with manual acknowledgement (`autoAck = false`). For each
message it:

1. Deserializes and validates the payload.
2. Rejects duplicates by checking `EventId` against the processed-events table.
3. Resolves ordering by `Version`: a change is applied only when its version is newer than the
   stored replica version. An update that arrives before its create simply inserts the row, and
   the later, lower-version create is discarded as stale.
4. Writes the replica change, the processed-event record and the sync status in a single
   transaction, and only then acknowledges the message.

## Failure handling

| Scenario | Handling |
| --- | --- |
| Processing exception | Message is republished to a retry tier (TTL) and the original is acknowledged; it is not requeued in place. |
| Fails after N retries | Routed to the dead-letter queue `sync.dlq`. |
| Duplicate delivery | Rejected via the `EventId` processed-events table (idempotent consumer). |
| Out-of-order (update before create) | Version comparison; stale changes are discarded. |
| Broker unreachable from Service A | Outbox rows stay unpublished and are retried on the polling interval. |
| Service B database write fails | Message is nacked with requeue and redelivered; it is never acknowledged before commit. |
| Poison / malformed payload | Dead-lettered immediately with no retry; the full payload is logged. |

### Retry policy

Retries use dead-letter TTL queues rather than blocking the consumer thread. On a retryable
failure the message is published to the next retry tier, where it waits for the tier's TTL and is
then dead-lettered back to `data.sync` with its original routing key, returning to the correct
main queue. The retry count travels in the `x-retry-count` header.

| Attempt | Tier queue | Delay |
| --- | --- | --- |
| 1 | `sync.retry.5s.queue` | 5s |
| 2 | `sync.retry.30s.queue` | 30s |
| 3+ | `sync.retry.5m.queue` | 5m |

After `Consumer:MaxRetryCount` (default 5) attempts the message is sent to `sync.dlx` and lands
in `sync.dlq` for manual inspection.

## Sync status

Every replicated aggregate has a row in the sync status table so operators can see sync health
without inspecting RabbitMQ. States: `Pending`, `Processed`, `Failed`, `DeadLettered`. See
[docs/status-transitions.md](docs/status-transitions.md).

## Tests

```
dotnet test tests/Sync.UnitTests
```

Unit tests cover the outbox write, idempotency, version ordering, retry-tier selection and status
transitions. They use SQLite in-memory and need no external services.

```
dotnet test tests/Sync.IntegrationTests
```

Integration tests start RabbitMQ and MySQL with Testcontainers and cover the end-to-end sync,
duplicate delivery and poison-message dead-lettering. They require a running Docker engine.

## Configuration

Settings come from `appsettings.json` and can be overridden by environment variables
(double-underscore syntax, e.g. `RabbitMq__HostName`, `ConnectionStrings__ProductDb`).

| Key | Purpose |
| --- | --- |
| `ConnectionStrings:ProductDb` | Service A database |
| `ConnectionStrings:SyncDb` | Service B database |
| `RabbitMq:*` | Broker host, port and credentials |
| `Outbox:PollingIntervalSeconds` | Publisher poll interval |
| `Consumer:MaxRetryCount` | Retries before dead-lettering |
