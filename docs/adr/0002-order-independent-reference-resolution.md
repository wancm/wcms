# Cross-reference resolution is deliberately out of scope

Content in an Export can reference other content in the same Export. The obvious approach is to
require dependencies arrive first, but arrival order is unreliable at three independent levels:
the Source Provider exports in its own storage order, partitioned transports only order within a
partition, and our own N parallel workers reorder the processing regardless. Any design that
takes arrival order as an input to correctness is therefore non-deterministic — green at
`MaxDegreeOfParallelism = 1`, red at 32.

We are not solving it here. An Unresolved Reference is recorded as an `ImportError` like any
other per-item failure, and the run continues. The hazard and its candidate responses are
documented in a comment at the exact point in the code where the reference check happens, so the
reader meets the analysis where the problem lives rather than in a document they may never open.

Retry is out of scope for the same reason: the in-memory repository and the fake enrichment
lookups have no transient faults, so a retry path could only ever be exercised by a test faking a
failure this demo cannot produce.

## Considered options, for the discussion this is meant to provoke

- **Deferral rounds.** An unresolved reference isn't a failure; the item is set aside and retried
  after the round drains, repeating until a round resolves nothing. Round count tracks dependency
  *chain depth*, not record count, so it converges in two or three rounds on real content graphs.
  Zero progress doubles as cycle detection. Costs memory bounded by how many records actually
  carry unresolved references.
- **Two passes.** Import everything with references unresolved, then re-read and wire them up.
  Simple and deterministic, but re-reads the Export — impossible for a Kafka or HTTP transport.
- **Advisory only.** Reference checks don't block; a separate reconciliation heals them later.
  Closest to how real migrations run — content lands first, links heal after — at the cost of the
  import no longer guaranteeing referential integrity at completion.

## Consequences

- The demo's `ImportResult` is identical at any degree of parallelism, because nothing in it
  depends on order. That property survives the decision to skip deferral, and the test suite
  still asserts it.
- Re-enqueueing work into the bounded channel would have needed care: writing a retry into a full
  channel from that channel's own consumer deadlocks, since the only readers that could drain it
  are the blocked writers. Not building retry avoids the hazard entirely.
