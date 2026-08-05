# WCMS Content Import Demo

A small .NET 8 demo of importing content from a legacy WCMS (WordPress) into a new one, and
notifying upstream systems once each item is available.

The pipeline is a bounded-channel producer/consumer: one reader streams items out of the export
files, N workers validate, map, and store them in parallel, and a publisher announces each stored
item exactly once.

---

## Prerequisites

- [.NET SDK 8.0](https://dotnet.microsoft.com/download/dotnet/8.0)

Check with:

```bash
dotnet --version    # expect 8.0.x
```

Nothing else is needed — no database, no message broker. Storage is in-memory SQLite and the
"broker" prints to the console.

---

## Run it

```bash
git clone https://github.com/wancm/wcms.git
cd wcms

dotnet build
dotnet test
dotnet run --project src/ContentImporter.Console
```

`dotnet run` performs the import once and exits. There is no hosted service to keep alive.

Press `Ctrl+C` during a run to cancel: in-flight work drains, counts are still reported, and the
process exits with 130. A second `Ctrl+C` aborts immediately.

### What you should see

Two output streams sharing one clock and one colour palette:

| Line | Meaning |
| --- | --- |
| `info` / `warn` / `fail` | the operator log |
| `CONTENT IMPORTED` | one per distinct item, announced upstream after it is published |
| `UPDATED … existing record overwritten` | a later export superseded an earlier one |
| `IMPORT COMPLETED` | the run-level event |

With the sample exports as shipped, a full run ends with:

```
Import completed. Imported 12, Failed 4.
```

Read those two numbers carefully — they count different things:

- **10 items are stored**, `wordpress:101` through `wordpress:110`, and 10 `CONTENT IMPORTED`
  events are raised. The publisher de-duplicates by id, so upstream hears about each item once.
- **`Imported` is 12**, because it counts successful *upserts*, not distinct items. Posts 101 and
  104 each appear in two exports, so each is written twice — 10 items + 2 re-imports = 12. Those
  two show up as `UPDATED` lines.
- **`Failed` is 4**, one per item rejected by validation. All four live in `word-press-04.json`
  alongside a valid item, which is the point: a per-item failure never aborts the run.

Colours are bright ANSI, chosen to stay legible on a black background. They are suppressed
automatically when output is redirected to a file or a pipe, and when `NO_COLOR` is set
(<https://no-color.org>):

```bash
NO_COLOR=1 dotnet run --project src/ContentImporter.Console > run.log
```

---

## Where the ingest data lives

Source of truth, and the copy you edit and commit:

```
src/ContentImporter.Console/data/wordpress/
├── word-press-01.json
├── word-press-02.json
├── word-press-03.json
├── word-press-04.json
└── word-press-05.json
```

`ContentImporter.Console.csproj` copies that folder next to the binary on build
(`CopyToOutputDirectory="PreserveNewest"`), and `WordPressJsonContentSource` reads it from there:

```
src/ContentImporter.Console/bin/Debug/net8.0/data/wordpress/
```

The path is resolved from `AppContext.BaseDirectory`, so it does not matter which directory you
run the app from. **Treat the `bin/` copy as a build artifact** — edit the files under `src/`, not
the ones under `bin/`.

Every `*.json` file in the folder is read; anything else is ignored. A missing folder yields
nothing rather than throwing.

### What the sample exports contain

| File | Items | Notes |
| --- | --- | --- |
| `word-press-01.json` | 101, 102, 103 | all valid — 101/102 are pages, 103 is a post |
| `word-press-02.json` | 104, 105 | all valid |
| `word-press-03.json` | 101, 106 | 101 again, retitled `… - corrected` |
| `word-press-04.json` | 901, 902, 903, 107 + one with no id | the four validation failures, plus a valid item that still imports |
| `word-press-05.json` | 104, 108, 109, 110 | 104 again retitled; 109 is a draft with no date |

---

## Adding or changing export files

### 1. File name decides precedence

Files are read in **ordinal name order**, and the last file read wins the repository's upsert. The
file name is therefore the contract for which export supersedes which — not the modification time,
which carries no information when a batch of files is uploaded at once.

Zero-pad the numbers. Names sort as text, so `word-press-10.json` sorts *before*
`word-press-2.json`.

### 2. Match the shape

The source reads `channel.items` and nothing else:

```json
{
  "channel": {
    "language": "en-US",
    "items": [
      {
        "title": "dummy test title 111",
        "creator": "dummyadmin",
        "guid": "https://dummy.example.com/?p=111",
        "content_encoded": "<!-- wp:paragraph --><p>Dummy body for 111.</p><!-- /wp:paragraph -->",
        "post_id": 111,
        "post_date_gmt": "2026-01-11 00:00:00",
        "post_name": "dummy-test-title-111",
        "status": "publish",
        "post_type": "post"
      }
    ]
  }
}
```

Those are the only fields the DTO, validator, and mapper actually read. `WordPressDto` accepts the
rest of the WordPress export vocabulary (`link`, `categories`, `postmeta`, and so on) and ignores
it, so pasting in a fuller export is safe.

Notes on the fields that matter:

- `post_id` is the identity. Content is stored as `wordpress:<post_id>`, so **re-using an id in a
  later file updates that item rather than adding one.**
- `post_date_gmt` is MySQL-shaped — `yyyy-MM-dd HH:mm:ss`, no `T`, no offset. WordPress writes the
  literal `"0000-00-00 00:00:00"` when there is no date; that parses to "never published" rather
  than failing.
- `content_encoded` is the post body (`content:encoded` in WXR).
- `post_type` is free-form (`post`, `page`, `attachment`, or a custom type).

### 3. Stay inside the validation rules

An item is rejected — counted as `Failed`, with the reason logged — if any of these fail:

| Rule | Message |
| --- | --- |
| `post_id` present and greater than zero | `post_id is mandatory and must be greater than zero` |
| `title` non-blank and ≤ 255 characters | `title is mandatory and must be 255 characters or fewer` |
| `status` is one WordPress exports: `publish`, `draft`, `pending`, `private`, `future`, `trash`, `inherit` | `status '…' is not one WordPress exports` |
| a **published** item carries a real `post_date_gmt` | `a published item must carry a real post_date_gmt` |

All rules run even after one has failed, so the report lists every reason at once rather than only
the first.

The last rule applies only to `status: "publish"`. A draft with `"0000-00-00 00:00:00"` is valid —
only published items owe a real date. That is what post 109 in `word-press-05.json` demonstrates.

A malformed item is isolated: it is recorded as an error and the run carries on.

### 4. Rebuild so the copy lands next to the binary

`dotnet run` builds first, so an edit under `src/` is picked up on the next run. If you build and
launch the binary separately, build after editing.

Two gotchas worth knowing:

- **Deleting a file under `src/` does not delete it from `bin/`.** MSBuild copies files into the
  output but never prunes orphans, so a removed export keeps being imported. Run `dotnet clean`,
  or delete `src/ContentImporter.Console/bin/`, after removing or renaming an export.
- **Dropping a file straight into `bin/…/data/wordpress/` works but is not persistent** — it is
  untracked by git and the next `dotnet clean` removes it. Fine for a one-off experiment, wrong
  for anything you want to keep.

To point the importer at a different folder entirely, `WordPressJsonContentSource` takes an
optional path:

```csharp
var source = new WordPressJsonContentSource("/path/to/exports");
```

---

## Project layout

| Project | Role |
| --- | --- |
| `ContentImporter.Domain` | what content *is* — entities only, no wire formats |
| `ContentImporter.Application` | the pipeline, ports, and provider adapters |
| `ContentImporter.Infrastructure` | adapters behind those ports — SQLite storage, console notifier |
| `ContentImporter.Console` | the composition root and demo host |
| `ContentImporter.Tests` | xunit, hand-rolled fakes, no mocking framework |

Dependencies point inward: `Console → Infrastructure → Application → Domain`. Swapping
`SqliteContentRepository` for `InMemoryContentRepository`, or the console notifier for a Kafka or
Service Bus publisher, is one line in `Program.cs` and recompiles nothing in `Application` or
`Domain`.

See `docs/adr/` for the two recorded architecture decisions.
