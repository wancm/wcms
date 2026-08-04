# CLAUDE.md — WCMS Content Import Demo (Sitecore interview task)

## Context and prime directive

This is a take-home technical task for a Sitecore job interview. I (the user) must
**present and defend this solution live**, so the primary goal is NOT finished code —
it is that I understand every line and every design decision well enough to explain
it and answer probing follow-up questions.

Therefore: optimize for my learning, not for your speed. Never dump the whole
application at once.

## The task (from the interviewer's email)

> "For one of our Web Content Management Systems (WCMS) we need to provide a facility
> to customers to allow them to import their content from their current WCMS. Once
> content has been imported, upstream systems must be notified that the new content
> is available."

Deliverable: a small solution that demonstrates the concepts (not a production system).

## What is being evaluated (from the email — treat as requirements)

- Async parallel programming
- Memory allocations and management
- .NET types: IEnumerable / IQueryable / IAsyncEnumerable, thread-safe collections
- Code design best practices and extensible architecture
- Unit testing
- Ability to explain concepts/patterns to others
- Nice to have: CI/CD, SQL and object/document databases, event-driven solutions,
  app monitoring and troubleshooting

## Agreed assumptions and constraints

- .NET 8 (LTS), C# latest, nullable enabled, implicit usings.
- Console demo app + class library + xUnit test project. No hosted service/API.
- Source formats: streaming JSON and streaming XML adapters (to demonstrate the
  extension point). Exports may be arbitrarily large — never load a whole export
  into memory.
- Upstream notification: event-driven via an in-process publisher **abstraction**
  (interface + event records), designed so a real broker (Azure Service Bus /
  Kafka / RabbitMQ) implementation could be slotted in later. No real broker.
- No external NuGet dependencies except xunit + test SDK. No mocking framework —
  hand-rolled fakes.
- Include a GitHub Actions workflow (restore, build, test).

## Target architecture (already decided — rebuild this, don't reinvent)

Producer/consumer pipeline on System.Threading.Channels:

```
IContentSource (Strategy; JSON, XML adapters)
    │  IAsyncEnumerable<ContentItem>   (streaming, one item at a time)
    ▼
bounded Channel<ContentItem>           (FullMode=Wait → backpressure, O(capacity) memory)
    ▼
N parallel consumer workers            (per-item error isolation; cancellation propagates)
    ├─▶ IContentRepository             (Repository; ConcurrentDictionary upsert by SourceId = idempotent)
    └─▶ IUpstreamNotifier              (Observer/pub-sub; ContentImported per item, ImportCompleted per run)
```

- `ContentItem` and all events are immutable records; shared mutable state limited
  to Interlocked counter + ConcurrentBag<ImportError>.
- Options object: MaxDegreeOfParallelism, ChannelCapacity.
- Result object: imported count, failed count, errors, duration.
- Per-item failure never aborts the run; OperationCanceledException always propagates.

## Working agreement (how we build — follow strictly)

1. **Plan first.** Before any code, produce a short implementation plan (component
   list + build order). I will run `/grill-me` against the plan; refine it based on
   that session before implementing.
2. **Small steps.** Implement ONE component per step, in this order unless the plan
   says otherwise: model → abstractions + events → in-memory repository → notifier →
   pipeline (the big one — may itself be split: skeleton → producer → consumers →
   error handling) → JSON source → XML source → console app → CI workflow.
3. **Explain → code → test → check.** For each step: first explain the concept and
   the "why" (2–3 short paragraphs max), then the code, then its unit tests, then
   STOP and ask me one comprehension question about what we just wrote. Do not
   proceed to the next step until I answer and confirm I'm ready.
4. **One question at a time.** In design discussions, ask me a single question and
   wait — never a batch of questions.
5. **Anticipate the interview.** At the end of each step, list 1–2 likely
   interviewer follow-up questions about that component (e.g. "why a bounded
   channel over Parallel.ForEachAsync?") with brief model answers.
6. Run `dotnet build` and `dotnet test` after every step; a step is not done until
   both pass.

## Coding standards

- Library code uses ConfigureAwait(false); CancellationToken on every async seam.
- XML doc comments on public types explaining the design intent, not restating names.
- Prefer sealed classes, records, init-only properties. No locks — thread-safe
  collections and immutability only.
- Tests: behavior-focused names (e.g. `Failures_are_recorded_but_do_not_stop_the_import`),
  exact assertions on counts, include at least: happy path at high parallelism,
  partial failure, idempotent re-import, cancellation, failing source mid-stream,
  concurrent repository writes, JSON/XML round-trip via temp files.

## Definition of done

- `dotnet test` green; `dotnet run` demos an import end-to-end with visible events.
- README explaining architecture, memory/async/extensibility rationale,
  IEnumerable vs IQueryable vs IAsyncEnumerable talking points, and
  "what I'd add for production" (outbox, retries, OpenTelemetry, checkpointing).
- I can explain every file without help.
