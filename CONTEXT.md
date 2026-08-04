# Content Import

The facility that lets a customer migrate their content from their existing WCMS into ours,
and notifies upstream systems once that content is available.

## Language

### The source side

**Source Provider**:
The WCMS a customer is migrating away from — AEM, WordPress, a legacy CMS.
_Avoid_: source system, legacy system, vendor

**Provider Code**:
The stable identifier for a Source Provider. It is the key that selects which
provider-specific deserializer, validator and mapper handle a record.
_Avoid_: source type, provider id, format

**Export**:
The body of content a customer hands us to migrate — a file, a stream, a topic.
Arbitrarily large; never held whole in memory.
_Avoid_: dump, feed, payload, batch

**Source Record**:
One item of content as it arrives from the Export, still in the Source Provider's own
shape and not yet understood.
_Avoid_: raw item, message, row

### The middle

**Canonical Content**:
A Source Record after it has been mapped out of the provider's shape into the one shape
the rest of the pipeline understands. Every Source Provider maps into this; nothing
downstream knows which provider a record came from.
_Avoid_: candidate, normalized model, intermediate model, DTO

**Enrichment**:
Adding information to Canonical Content that the Export did not carry, or carried only
partially — resolved from configuration, from another data source, or calculated.
An author's name from an author id; an asset's URL and dimensions from an asset id.
_Avoid_: augmentation, hydration, decoration

**Master Data**:
Reference data the Export points at but does not contain — authors, categories, assets,
previously-migrated content. Assumed already present in the target.
_Avoid_: lookup data, reference tables

### When content isn't ready

**Unresolved Reference**:
A pointer from Canonical Content to Master Data or to other content that is not in the target
_yet_. Distinct from a broken reference, which never will be — though this import cannot tell
the two apart, and treats both as a failed item.
_Avoid_: missing reference, dangling reference, broken link

### The target side

**Content Item**:
Content once it exists in our WCMS, constructed through the domain's own invariants.
This is the model our system already owns; the import feeds it, it is not shaped by
the import.
_Avoid_: page, article, document, entity

**Import Run**:
One execution of the import for one Export. Has a start, a duration, counts of what
succeeded and failed, and ends by announcing itself.
_Avoid_: job, batch, session

**Upstream System**:
A system outside ours that needs to know when content has been imported and becomes
available.
_Avoid_: downstream system, subscriber, consumer
