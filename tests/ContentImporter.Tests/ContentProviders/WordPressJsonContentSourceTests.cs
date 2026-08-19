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
    //
    // fable5: --st*
    // 每个测试类实例一个独立 folder —— xunit 会为每个测试各建一个实例，
    // 所以测试之间看不到彼此的文件，真实的 sample export 也永远不会被碰到。
    // *en--
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
    public async Task Exports_are_read_in_name_order()
    {
        // The name decides which export supersedes which, so the last one read wins the upsert.
        // fable5: --st* 文件名决定哪份 export 取代哪份，所以最后读到的那份会在 upsert 中胜出。 *en--
        WriteExport("word-press-02.json", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 2);
        WriteExport("word-press-01.json", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 1);

        List<SourceContentItem> items = await ReadAllAsync();

        Assert.Equal([1, 2], items.Select(PostIdOf));
    }

    [Fact]
    public async Task Modification_time_does_not_affect_the_order()
    {
        // Timestamps point the opposite way on purpose. Uploaded files are all written at once,
        // so mtime carries no information and must not be allowed to decide anything.
        //
        // fable5: --st*
        // 时间戳被故意设成相反的方向。上传的文件都是同一时刻写入的，
        // 所以 mtime 不携带任何信息，绝不能让它决定任何事情。
        // *en--
        WriteExport("aaa.json", new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), 1);
        WriteExport("bbb.json", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 2);

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
