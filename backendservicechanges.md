# Turning the one-shot importer into a long-running service

## Context

The importer today runs one import and exits. This note records what would change if the trigger
became a watched folder or a Kafka subscription and the process stayed up.

The headline: **the architecture survives; the lifecycle does not.** Every port stays as it is.
What breaks is everything that assumes a run has a beginning and an end.

---

## What already works, unchanged

- **`IContentSource` returns `IAsyncEnumerable<SourceContentItem>`.** An infinite stream is just one
  that never completes. A Kafka consumer loop and a folder watcher both fit the existing interface
  with no change at all — `await foreach` over a sequence that never ends is what the type is for.
- **`IContentRepository` and `IUpstreamNotifier`** are untouched. Swapping SQLite for Postgres, or
  the console notifier for a Kafka publisher, is still one line in the composition root.
- **Bounded channel with N consumers** is already the right shape for a service. Nothing about it is
  one-shot.
- **Upsert by `Id`** stops being a nicety and becomes load-bearing: redelivery is normal with both
  triggers, so idempotency is what keeps duplicates out of the store.

---

## The three obvious changes

1. **Publishing moves inline into `ChannelConsumer`.** `ImportPipeline` currently waits for the
   channel to drain before publishing. There is no drain in a service, so that pass can never run.
2. **The run-scoped tail is deleted** — building `ImportResult` and raising `ImportCompleted` both
   describe a *run*, and there is no longer one.
3. **The channel becomes a singleton.** It was per-run state; now it is genuinely long-lived shared
   infrastructure, created once and never completed until shutdown.

---

## What else has to change

### 1. `ChannelProducer` must never complete the writer

`ChannelProducer` calls `writer.TryComplete()` in a `finally`. That is the one-shot contract, and in
a service it is a latent kill switch: the first time the source loop exits for any reason, the
channel closes permanently and every consumer stops. Nothing throws. Throughput simply goes to zero.

`TryComplete()` becomes exclusively a **shutdown** operation, called by the host, not the producer.

### 2. Hosting model

- Consumers become a `BackgroundService` started once at startup, rather than tasks created per call.
- The producer becomes a second `BackgroundService` — the watcher loop or the Kafka poll loop.
- `Program.cs` calls `host.RunAsync()`. Today it deliberately does not, and the Ctrl+C handler is
  hand-wired precisely because no hosted service exists; both of those comments become wrong, and
  `IHostApplicationLifetime` becomes the right source of the cancellation token.
- `ImportPipeline.RunAsync(...) → Task<ImportResult>` becomes `ExecuteAsync(CancellationToken) → Task`.

### 3. Graceful shutdown — the subtle part

Order matters, and getting it wrong silently drops buffered work on every deploy:

```
1. stop the producer accepting new work
2. writer.Complete()                  <- now, and only now
3. await the consumers draining, with a timeout
4. dispose the repository and notifier
```

Skip step 3 and everything sitting in the channel is lost. Expect the question: *"what happens to
in-flight items when you redeploy?"*

### 4. `ImportResult`, `ImportCounters`, `ImportCompleted`, `eventId`

All four are run-scoped, and all four need rethinking:

| Today | In a service |
|---|---|
| `ImportResult` returned per run | nothing to return — delete, or report per batch |
| `ImportCounters` per run | process-lifetime metrics (queue depth, throughput, failure rate) |
| `ImportCompleted` event | no run to complete — delete, or redefine per file / per poll batch |
| `eventId` per run | per file, or per Kafka message |

Correlation is the one that matters operationally: without a per-batch id you cannot answer "what
happened to the file I dropped at 14:02".

### 5. `ContentPublisher._importedContents` becomes a memory leak

`ContentPublisher` holds an unbounded `ConcurrentDictionary<string, ContentItem>` for
de-duplication. In a one-shot run it dies with the process. In a service it grows forever, holding
every `ContentItem` ever published **including its `Body`** — customer HTML, unbounded.

Delete it. The repository's upsert already de-duplicates durably, which is the right place for it:
an in-memory dictionary cannot survive a restart, so it was never real de-duplication anyway.

### 6. A demonstration is lost, and it is worth saying so

Publishing inline means there is no collection to iterate, so `Parallel.ForEachAsync` and the
`ConcurrentDictionary` de-dup disappear with it. The consumer loop becomes the only concurrency
shape in the solution. That is the right engineering call and a small presentational loss — better
named than discovered.

### 7. Worker resilience

If a consumer's `await foreach` throws, that worker's task ends and nothing replaces it. The other
workers keep going, throughput drops by a fraction, and nothing reports it. A one-shot run cannot
expose this, because the process exits anyway.

Each worker needs a supervising loop: catch at the loop level, log, restart with a backoff.

### 8. Trigger-specific traps

**Folder watch** — `FileSystemWatcher` is far less reliable than it looks:

- It fires on file *creation*, not on the writer closing the handle, so a naive read opens a
  half-written file. Wait until an exclusive open succeeds.
- Its internal buffer overflows under bursts and **silently drops events**. A periodic reconciliation
  sweep is not optional — the watcher is an optimisation over polling, not a replacement for it.
- Duplicate and multiple events per file are normal.
- Processed files must be moved to `processed/` or `failed/`, or a restart reprocesses everything.

**Kafka** — one decision dominates:

- **Commit the offset only after the item is durably stored.** The channel holds items that have been
  *received* but not *processed*; committing at enqueue time is exactly the bug — a crash loses
  everything buffered while Kafka believes it was handled.
- Backpressure means **pausing the partition**, not blocking the poll loop. Block the loop and
  heartbeats stop, the broker assumes the consumer is dead, and a rebalance happens mid-import.
- At-least-once delivery plus rebalances makes duplicates routine, which is why the idempotent upsert
  carries the design.

### 9. Storage and observability

In-memory SQLite dies with the process and grows without bound — a service needs durable storage.
And a long-running process needs what a one-shot run does not: health checks, queue-depth and
throughput metrics, and structured logs keyed by correlation id.

---

## The short version

> The ports do not change — `IContentSource` already returns `IAsyncEnumerable`, so a Kafka consumer
> or a folder watcher is just a source that never ends. What changes is lifecycle. `Channel<T>`
> completion is terminal, so the producer must stop completing the writer, and completion becomes a
> shutdown-only step ordered so that buffered items drain before the process exits. Everything
> run-scoped — the result, the counters, the completion event — either disappears or moves to
> per-batch. Publishing moves inline, because there is no drain to publish after. And two things that
> are harmless in a one-shot run become bugs: the publisher's de-dup dictionary grows forever, and a
> consumer that dies is never replaced.

---

## Evidence

Each claim above is checkable against the current code rather than asserted:

1. **Channel completion is terminal.** Running the pipeline twice against one channel produced
   `RUN 2: imported=0 failed=1 — The channel has been closed.` A service would hit that permanently.
2. **The leak** is visible in `ContentPublisher.cs`: `_importedContents` has no eviction and holds the
   whole `ContentItem`, `Body` included, keyed by id.
3. **The `IAsyncEnumerable` claim** is visible in `IContentSource.cs` — nothing in the signature says
   the sequence is finite, which is why a Kafka source needs no interface change.

## If this is ever built

Add a separate Worker project alongside the console app, sharing Application and Infrastructure. The
one-shot demo keeps working; the worker shows the service shape. Nothing existing gets broken.
