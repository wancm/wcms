using ContentImporter.Application.ContentProviders.WordPress;
using ContentImporter.Application.Notifications;
using ContentImporter.Application.Pipelines;
using ContentImporter.Domain.Entities;
using ContentImporter.Infrastructure.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Concurrent;

namespace ContentImporter.Tests.Notifications;

/// <summary>
/// Upstream systems learn that content arrived through IUpstreamNotifier. These tests run the
/// real pipeline against a recording fake - no mocking framework, per the project's rules.
/// </summary>
public sealed class UpstreamNotificationTests : IDisposable
{
    /// <summary>
    /// Records what it was told. ConcurrentQueue because the pipeline's consumers all notify at
    /// once - a List here would corrupt itself and the test would fail for the wrong reason.
    /// </summary>
    private sealed class RecordingNotifier : IUpstreamNotifier
    {
        private readonly ConcurrentQueue<ImportEvent> _events = new();

        public IReadOnlyList<ImportEvent> Events => _events.ToArray();

        public Task NotifyAsync(ImportEvent importEvent, CancellationToken cancellationToken = default)
        {
            _events.Enqueue(importEvent);

            return Task.CompletedTask;
        }
    }

    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), $"wp-notify-{Guid.NewGuid():N}");

    public UpstreamNotificationTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private void WriteExport(string name, params int[] postIds)
    {
        var items = postIds.Select(id =>
            $$"""
              { "post_id": {{id}}, "title": "Post {{id}}", "post_type": "post", "status": "publish",
                "post_date_gmt": "2026-01-04 00:00:00", "content_encoded": "<p>body of {{id}}</p>" }
              """);

        File.WriteAllText(
            Path.Combine(_folder, name),
            $$"""{ "channel": { "title": "t", "items": [ {{string.Join(",", items)}} ] } }""");
    }

    private async Task<RecordingNotifier> RunAsync()
    {
        var notifier = new RecordingNotifier();

        using var repository = new SqliteContentRepository();

        await new ImportPipeline(new ContentImporterChannel(), repository, notifier)
            .RunAsync(new WordPressJsonContentSource(_folder), NullLogger.Instance)
            .ConfigureAwait(false);

        return notifier;
    }

    [Fact]
    public async Task Every_imported_item_is_announced_upstream()
    {
        WriteExport("export-01.json", 101, 102, 103);

        RecordingNotifier notifier = await RunAsync();

        ContentImported[] imported = [.. notifier.Events.OfType<ContentImported>()];

        Assert.Equal(3, imported.Length);
        Assert.Equal(["wordpress:101", "wordpress:102", "wordpress:103"], imported.Select(e => e.Id).Order());
    }

    [Fact]
    public async Task The_run_is_announced_once_when_it_finishes()
    {
        WriteExport("export-01.json", 101, 102);

        RecordingNotifier notifier = await RunAsync();

        ImportCompleted completed = Assert.Single(notifier.Events.OfType<ImportCompleted>());

        Assert.Equal(2, completed.Imported);
        Assert.Equal(0, completed.Failed);
        Assert.True(completed.Duration > TimeSpan.Zero);
    }

    [Fact]
    public async Task Every_event_carries_the_run_id_that_ties_them_together()
    {
        WriteExport("export-01.json", 101, 102);

        RecordingNotifier notifier = await RunAsync();

        // One import, one event id - that is what makes a log searchable after the fact.
        Assert.Single(notifier.Events.Select(e => e.EventId).Distinct());
    }

    [Fact]
    public async Task An_announcement_describes_the_content_without_carrying_it()
    {
        WriteExport("export-01.json", 201);

        RecordingNotifier notifier = await RunAsync();

        ContentImported imported = Assert.Single(notifier.Events.OfType<ContentImported>());

        Assert.Equal("wordpress:201", imported.Id);
        Assert.Equal("wordpress", imported.ProviderCode);
        Assert.Equal("201", imported.ExternalId);
        Assert.Equal("Post 201", imported.Title);
        Assert.Equal("post", imported.ContentType);
        Assert.Equal("en-US", imported.Language);
        Assert.Equal(new DateTimeOffset(2026, 1, 4, 0, 0, 0, TimeSpan.Zero), imported.PublishedAt);
    }

    [Fact]
    public void A_long_body_is_previewed_not_shipped()
    {
        // A notification says what happened; it does not replicate the data. Real brokers cap
        // message size, and a consumer invalidating a cache does not need the markup.
        var item = new ContentItem
        {
            Id = "wordpress:1",
            ProviderCode = "wordpress",
            ExternalId = "1",
            Title = "Long",
            Body = new string('x', 5_000),
            Language = "en-US",
            ContentType = "post"
        };

        ContentImported announced = ContentImported.From(item, "run-1");

        Assert.Equal(5_000, announced.BodyLength);
        Assert.True(announced.BodyPreview.Length < 200, "the preview must not carry the whole body");
        Assert.EndsWith("...", announced.BodyPreview, StringComparison.Ordinal);
    }

    [Fact]
    public void A_short_body_is_carried_whole_without_an_ellipsis()
    {
        var item = new ContentItem
        {
            Id = "wordpress:1",
            ProviderCode = "wordpress",
            ExternalId = "1",
            Title = "Short",
            Body = "<p>tiny</p>",
            Language = "en-US",
            ContentType = "post"
        };

        ContentImported announced = ContentImported.From(item, "run-1");

        Assert.Equal("<p>tiny</p>", announced.BodyPreview);
        Assert.Equal(11, announced.BodyLength);
    }

    [Fact]
    public async Task Items_from_every_export_are_announced()
    {
        WriteExport("export-01.json", 101);
        WriteExport("export-02.json", 102);

        RecordingNotifier notifier = await RunAsync();

        Assert.Equal(2, notifier.Events.OfType<ContentImported>().Count());
    }
}
