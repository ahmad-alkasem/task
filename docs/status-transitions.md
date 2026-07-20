# Sync status transitions

Service B keeps one status row per aggregate (`SyncStatusRecord`, keyed by `AggregateId`). The row
records the current state, the last event applied, the attempt count and the last error. It is the
operator-facing view of sync health and is exposed through `GET /api/sync-status`.

## States

- **Pending** — a change has been received but not yet successfully applied.
- **Processed** — the latest change was applied to the replica and committed.
- **Failed** — processing failed and the message was scheduled for retry.
- **DeadLettered** — the message exhausted its retries or was malformed and was sent to the DLQ.

## Diagram

```
                 apply + commit succeeds
        ┌────────────────────────────────────────┐
        │                                         v
   (message in)  ---->  Pending  ----> processing ----> Processed
                            │              │  ^
             transient DB   │              │  │ retry succeeds
             failure /      │              │  │
             redeliver      │              v  │
                            └────────>   Failed
                                           │
                          retries exhausted / poison
                                           │
                                           v
                                      DeadLettered
```

## How transitions happen

- **Pending → Processed**: `SyncProcessor.ProcessAsync` applies the replica change, records the
  processed event and sets the status to `Processed`, all in one transaction. Duplicate and stale
  messages also settle here because the aggregate is already at or beyond that version.
- **Pending/Processing → Failed**: a processing exception routes the message to a retry tier and
  `SyncStatusWriter.MarkFailedAsync` records `Failed` with the incremented attempt count.
- **Failed → Processed**: a retried message that later succeeds moves back to `Processed`.
- **Failed → DeadLettered**: once the retry count reaches `Consumer:MaxRetryCount`, the message is
  published to the dead-letter exchange and `SyncStatusWriter.MarkDeadLetteredAsync` records
  `DeadLettered`.
- **→ DeadLettered (poison)**: a payload that cannot be deserialized or fails validation skips
  retries and goes straight to the DLQ; the full payload is logged for inspection.

A transient database failure during processing is deliberately not a status transition to a
terminal state: the message is nacked with requeue and redelivered, so the record stays in its
current state until the write commits.
