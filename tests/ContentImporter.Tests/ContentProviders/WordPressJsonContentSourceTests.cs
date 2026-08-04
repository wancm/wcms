using ContentImporter.Application.ContentProviders.ContentSource;
using ContentImporter.Application.ContentProviders.WordPress;
using System.Text.Json;

namespace ContentImporter.Tests.ContentProviders;

/// <summary>
/// The source reads every export in the WordPress folder. Order matters: the repository upserts
/// by id, so whichever file is read last wins when two exports carry the same post.
/// </summary>
public sealed class WordPressJsonContentSourceTests : IDisposable
{
    // Its own folder per test class instance - xunit builds one per test, so tests cannot
    // see each other's files and the real sample export is never touched.
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), $"wp-source-{Guid.NewGuid():N}");

    public WordPressJsonContentSourceTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private void WriteExport(string name, DateTime lastWriteUtc, params int[] postIds)
    {
        var items = postIds.Select(id =>
            $$"""{ "post_id": {{id}}, "title": "Post {{id}}", "post_type": "post", "status": "publish" }""");

        string path = Path.Combine(_folder, name);
        File.WriteAllText(path, $$"""{ "channel": { "title": "t", "items": [ {{string.Join(",", items)}} ] } }""");
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
    }

    private async Task<List<SourceContentItem>> ReadAllAsync()
    {
        var items = new List<SourceContentItem>();

        await foreach (SourceContentItem item in new WordPressJsonContentSource(_folder).ReadAsync())
        {
            items.Add(item);
        }

        return items;
    }

    private static long PostIdOf(SourceContentItem item) =>
        JsonDocument.Parse(item.Content).RootElement.GetProperty("post_id").GetInt64();

    [Fact]
    public async Task Items_from_every_export_in_the_folder_are_read()
    {
        WriteExport("zzz-first.json", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 101);
        WriteExport("aaa-second.json", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), 102, 103);

        List<SourceContentItem> items = await ReadAllAsync();

        Assert.Equal([101, 102, 103], items.Select(PostIdOf).Order());
    }

    [Fact]
    public async Task Older_exports_are_read_before_newer_ones()
    {
        // Names sort the other way on purpose: if this passed on name order it would prove nothing.
        WriteExport("zzz-older.json", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 1);
        WriteExport("aaa-newer.json", new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), 2);

        List<SourceContentItem> items = await ReadAllAsync();

        // Oldest first, so the newest export is written last and wins the upsert downstream.
        Assert.Equal([1, 2], items.Select(PostIdOf));
    }

    [Fact]
    public async Task Exports_sharing_a_timestamp_fall_back_to_name_order()
    {
        // Copying or unzipping a batch of files often gives them the same mtime to the second.
        var sameMoment = new DateTime(2026, 3, 3, 12, 0, 0, DateTimeKind.Utc);

        WriteExport("b-second.json", sameMoment, 2);
        WriteExport("a-first.json", sameMoment, 1);

        List<SourceContentItem> items = await ReadAllAsync();

        Assert.Equal([1, 2], items.Select(PostIdOf));
    }

    [Fact]
    public async Task Each_item_records_the_export_it_came_from()
    {
        WriteExport("named-export.json", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 101);

        List<SourceContentItem> items = await ReadAllAsync();

        Assert.All(items, item => Assert.Equal("named-export.json", item.Fields["sourceFile"]));
    }

    [Fact]
    public async Task Every_item_names_its_provider()
    {
        WriteExport("provider.json", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 101, 102);

        List<SourceContentItem> items = await ReadAllAsync();

        Assert.All(items, item => Assert.Equal("WordPress", item.ProviderCode));
    }

    [Fact]
    public async Task Non_json_files_in_the_folder_are_ignored()
    {
        WriteExport("export.json", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 101);
        File.WriteAllText(Path.Combine(_folder, "readme.txt"), "not an export");

        Assert.Single(await ReadAllAsync());
    }

    [Fact]
    public async Task An_empty_folder_yields_nothing()
    {
        Assert.Empty(await ReadAllAsync());
    }

    [Fact]
    public async Task A_missing_folder_yields_nothing_rather_than_throwing()
    {
        var source = new WordPressJsonContentSource(Path.Combine(_folder, "does-not-exist"));

        var items = new List<SourceContentItem>();
        await foreach (SourceContentItem item in source.ReadAsync())
        {
            items.Add(item);
        }

        Assert.Empty(items);
    }
}
