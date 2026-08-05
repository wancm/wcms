# Content Import

The facility that lets a customer migrate their content from their existing WCMS into ours,
and notifies upstream systems once that content is available.

This is the ubiquitous language for the domain. It was written before the code and is
deliberately larger than v1 — some terms name things that exist in the code today, others name
things the design has room for but has not built. The **Built as** column says which is which, so
nobody reads a term here and goes looking for a class that was never written.

Status of v1 is in [PLAN.md](./PLAN.md). Decisions are in [docs/adr/](./docs/adr/).

---

## Language

### The source side

**Source Provider**
The WCMS a customer is migrating away from — AEM, WordPress, a legacy CMS.
_Avoid_: source system, legacy system, vendor
**Built as**: WordPress only, in `ContentProviders/WordPress/`.

**Provider Code**
The stable identifier for a Source Provider. It is the key that selects which
provider-specific deserializer, validator and mapper handle a record.
_Avoid_: source type, provider id, format
**Built as**: a `string` on `SourceContentItem`, resolved by `PipelineExecutorFactory`.
A typed value object was considered and dropped — one provider did not justify it.

**Export**
The body of content a customer hands us to migrate — a file, a stream, a topic.
Arbitrarily large; never held whole in memory.
_Avoid_: dump, feed, payload, batch
**Built as**: the `*.json` files in `data/wordpress/`. The "never held whole in memory" part is
the aspiration, not v1 — `WordPressJsonContentSource` loads one export whole with
`JsonDocument.ParseAsync`, and says so in a comment.

**Source Record**
One item of content as it arrives from the Export, still in the Source Provider's own
shape and not yet understood.
_Avoid_: raw item, message, row
**Built as**: `SourceContentItem` — the raw JSON of one item, its Provider Code, a
`CorrelationId`, and the file it came from.

### The middle

**Canonical Content**
A Source Record after it has been mapped out of the provider's shape into the one shape
the rest of the pipeline understands. Every Source Provider maps into this; nothing
downstream knows which provider a record came from.
_Avoid_: candidate, normalized model, intermediate model, DTO
**Not built as a separate type.** With one provider, canonical and domain coincide, so
`WordPressDtoEntityMapper` maps the DTO straight to `Content Item`. The seam where this type
would reappear is that mapper.

**Enrichment**
Adding information to Canonical Content that the Export did not carry, or carried only
partially — resolved from configuration, from another data source, or calculated.
An author's name from an author id; an asset's URL and dimensions from an asset id.
_Avoid_: augmentation, hydration, decoration
**Not built.** No Master Data in the demo, so there was nothing to enrich from.

**Master Data**
Reference data the Export points at but does not contain — authors, categories, assets,
previously-migrated content. Assumed already present in the target.
_Avoid_: lookup data, reference tables
**Not built.**

### When content isn't ready

**Unresolved Reference**
A pointer from Canonical Content to Master Data or to other content that is not in the target
_yet_. Distinct from a broken reference, which never will be — though this import cannot tell
the two apart, and treats both as a failed item.
_Avoid_: missing reference, dangling reference, broken link
**Not built**, deliberately — see
[ADR-0002](./docs/adr/0002-order-independent-reference-resolution.md).

### The target side

**Content Item**
Content once it exists in our WCMS, constructed through the domain's own invariants.
This is the model our system already owns; the import feeds it, it is not shaped by
the import.
_Avoid_: page, article, document, entity
**Built as**: `ContentItem`, an immutable record with `required init` properties. Identity is
`"{providerCode}:{externalId}"` — derived, not generated, so a re-import lands on the same key
instead of duplicating. Note the invariants are `required` properties rather than a guarded
factory; a `Create` method returning a Result was designed and not built.

**Import Run**
One execution of the import for one Export. Has a start, a duration, counts of what
succeeded and failed, and ends by announcing itself.
_Avoid_: job, batch, session
**Built as**: one call to `ImportPipeline.RunAsync`, producing an `ImportResult` and one
`ImportCompleted` event. Every event from a run shares one `EventId`.
Caveat worth knowing: `Imported` counts successful upserts, not distinct items, so an export
re-importing an item counts it twice.

**Upstream System**
A system outside ours that needs to know when content has been imported and becomes
available.
_Avoid_: downstream system, subscriber, consumer
**Built as**: the `IUpstreamNotifier` port, with `ConsoleUpstreamNotifier` standing in for a
broker. The port name follows this glossary — it was `IEventPublisher` in the first design.

---

## Two distinctions the code depends on

**Stored is not Available.** An item is stored when the repository has upserted it; it is
available when `ContentPublisher` has published it. Upstream Systems are told at the second
point, never the first. That is why publishing is a separate pass and not a step inside the
consumer loop.

**A failed item is not a failed run.** Per-item failures are collected as `ImportError` and the
run continues. Only cancellation stops it. A run that imported 10 items and rejected 4 is a
successful run that reports 4 errors.
