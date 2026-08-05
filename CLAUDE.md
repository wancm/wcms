# CLAUDE.md — WCMS Content Import Demo (Sitecore interview task)

## Status

**v1 is done.** `dotnet build` and `dotnet test` are green (65 tests), and `dotnet run`
demonstrates an import end to end. What was built, and how it differs from the original design,
is recorded in [PLAN.md](./PLAN.md). Anything further is v2 — see the backlog there.

## Context and prime directive

This is a take-home technical task for a Sitecore job interview. I (the user) must
**present and defend this solution live**, so the primary goal is NOT finished code —
it is that I understand every line and every design decision well enough to explain
it and answer probing follow-up questions.

Therefore: optimize for my learning, not for your speed. Never dump the whole
application at once.

## Simplicity rule (overrides everything below when they conflict)

This is a **demo**. Demonstrating the idea clearly beats implementing it fully.

- Write the **simplest code that shows the concept**. Prefer the obvious API over
  the optimal one — `File.OpenRead` + `JsonDocument` over a hand-rolled
  `Utf8JsonReader` buffer loop, a plain `List<T>` over `ArrayPool<T>`.
- Do not add production hardening I did not ask for: no buffer growth, no pooling,
  no partial-chunk state machines, no extra constructor parameters "for testability",
  no defensive branches for inputs the sample data never produces.
- **Comments: short and few.** One line, only where a reader would otherwise wonder
  why. No multi-paragraph `<remarks>` essays justifying a design.
- Where the simple version knowingly breaks one of the constraints below (e.g.
  loading a whole file into memory), say so in a **one-line comment** and move on.
  The trade-off belongs in the README and in what I say out loud at the interview —
  not in the code.
- If you think the simple version is genuinely wrong, say it in one sentence, then
  write the simple version anyway.

This rule won several arguments during v1, and the losers are listed in PLAN.md under
"Where v1 departs from the original plan". `JsonDocument` over `Utf8JsonReader` is the clearest
case: the plan called streaming non-negotiable, and the demo loads one export whole with a
comment naming the shortcut.

## The task (from the interviewer's email)

> "For one of our Web Content Management Systems (WCMS) we need to provide a facility
> to customers to allow them to import their content from their current WCMS. Once
> content has been imported, upstream systems must be notified that the new content
> is available."

Deliverable: a small solution that demonstrates the concepts (not a production system).

## What is being evaluated (from the email — treat as requirements)

| | Where it shows up in v1 |
|---|---|
| Async parallel programming | Bounded `Channel<T>`, N consumers, `Parallel.ForEachAsync`, cancellation |
| Memory allocations and management | Channel capacity as the memory ceiling; claim-check events; immutable records |
| .NET types: IEnumerable / IQueryable / IAsyncEnumerable, thread-safe collections | `IAsyncEnumerable` source; `ConcurrentBag`/`Dictionary`/`Queue`; `Interlocked` |
| Code design best practices and extensible architecture | Ports and adapters, four projects, the provider factory |
| Unit testing | 65 xunit tests, hand-rolled fakes |
| Ability to explain concepts/patterns to others | The reason this repo was built in small steps |
| Nice to have: CI/CD, SQL and document DBs, event-driven, monitoring | SQLite repository, event-driven notification. **CI is still missing** |

## Agreed assumptions and constraints

- .NET 8 (LTS), C# latest, nullable enabled, implicit usings, warnings as errors.
- Console demo app + class libraries + xUnit test project. No hosted service/API.
- Upstream notification: event-driven via an in-process **abstraction** (`IUpstreamNotifier`
  plus event records), designed so a real broker (Azure Service Bus / Kafka / RabbitMQ) could be
  slotted in later. No real broker.
- No mocking framework — hand-rolled fakes.

**Constraints that were revised during v1** — the earlier text is kept so the change is visible:

- ~~No external NuGet dependencies except xunit + test SDK.~~ Now also
  `Microsoft.Extensions.Hosting` (DI, `ILogger`, the standard host) and `Microsoft.Data.Sqlite`
  (SQL was a listed nice-to-have). Both are deliberate, both are defensible out loud.
- ~~No DI container; the composition root wires by hand.~~ Reversed for the same reason.
  `Program.cs` is still the only place a concrete Infrastructure type is named.
- ~~Source formats: JSON and XML adapters.~~ Only JSON/WordPress was built. The extension point
  is demonstrated by `PipelineExecutorFactory` and the ports rather than by a second provider.
- "Exports may be arbitrarily large" remains the *story*; v1 reads a sample export whole with a
  one-line comment naming the shortcut. The **pipeline** is a genuine bounded-channel
  producer/consumer — that part was never compromised.

## Architecture (as built)

```
IContentSource (WordPressJsonContentSource)
    │  IAsyncEnumerable<SourceContentItem>
    ▼
bounded Channel<SourceContentItem>        (capacity 12, FullMode=Wait → backpressure)
    ▼
N consumers                               (per-item error isolation; cancellation propagates)
    │  deserialize → validate → map → upsert
    ▼
ContentPublisher                          (separate pass, Parallel.ForEachAsync, dedupe by Id)
    └─▶ IUpstreamNotifier                 (ContentImported per item, ImportCompleted per run)
```

- `ContentItem` and all events are immutable records; shared mutable state is limited to
  `Interlocked` counters and concurrent collections.
- Per-item failure never aborts the run; `OperationCanceledException` always propagates.

## Working agreement (how we build — follow strictly)

v1 was built this way and v2 should be too.

1. **Small steps.** One component per step. Never dump the whole application at once.
2. **Explain → code → test → check.** For each step: first explain the concept and the "why"
   (2–3 short paragraphs max), then the code, then its unit tests, then STOP and ask me one
   comprehension question. Do not proceed until I answer.
3. **One question at a time.** In design discussions, ask a single question and wait — never a
   batch.
4. **Anticipate the interview.** At the end of each step, list 1–2 likely interviewer follow-up
   questions about that component, with brief model answers.
5. Run `dotnet build` and `dotnet test` after every step; a step is not done until both pass.
6. **Tell me when I am wrong.** If a decision I have made is worse than the alternative, say so
   in one sentence before implementing it. Several entries in PLAN.md's departures table came
   out of exactly that.

## Coding standards

- Library code uses `ConfigureAwait(false)`; `CancellationToken` on every async seam.
- Do not mark a method `async` when it has nothing to await — `Task.FromResult` instead.
  Warnings are errors, and CS1998 will fail the build.
- One-line XML `<summary>` on public types. No `<remarks>` unless I ask.
- Prefer sealed classes, records, init-only properties. No locks — thread-safe collections and
  immutability only, except where a non-thread-safe resource forces one (`SemaphoreSlim` guards
  the SQLite connection).
- Console output: compose one string and write once. Colour with ANSI escapes inside that string,
  never `Console.ForegroundColor` — parallel writers would bleed into each other's lines.
  Suppress escapes when output is redirected or `NO_COLOR` is set.
- Tests: behavior-focused names (e.g. `Failures_are_recorded_but_do_not_stop_the_import`), exact
  assertions on counts.

## Definition of done — v1

- [x] `dotnet test` green
- [x] `dotnet run` demos an import end to end with visible events
- [x] README explaining how to run it, where the ingest data lives, and how to add to it
- [x] ADRs for the two decisions worth recording
- [ ] **GitHub Actions workflow (restore, build, test)** — still outstanding
- [ ] Test coverage for cancellation, a failing source mid-stream, and high parallelism
- [x] I can explain every file without help
