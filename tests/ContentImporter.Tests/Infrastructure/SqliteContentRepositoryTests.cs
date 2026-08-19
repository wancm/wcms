using ContentImporter.Domain.Entities;
using ContentImporter.Infrastructure.Repositories;

namespace ContentImporter.Tests.Infrastructure;

/// <summary>
/// The repository is what makes a re-import idempotent: upsert by Id, so the same export run
/// twice leaves the store holding one copy of each item, not two.
/// </summary>
public sealed class SqliteContentRepositoryTests
{
    private static ContentItem Item(string externalId, string title = "A title") => new()
    {
        Id = $"wordpress:{externalId}",
        ProviderCode = "wordpress",
        ExternalId = externalId,
        Title = title,
        Body = "<p>body</p>",
        Language = "en-US",
        ContentType = "post",
        PublishedAt = new DateTimeOffset(2026, 1, 4, 0, 0, 0, TimeSpan.Zero)
    };

    [Fact]
    public async Task A_stored_item_can_be_read_back_whole()
    {
        using SqliteContentRepository repository = new();

        Assert.True(await repository.UpsertAsync(Item("201")));

        ContentItem? stored = await repository.GetAsync("wordpress:201");

        Assert.NotNull(stored);
        Assert.Equal("wordpress:201", stored.Id);
        Assert.Equal("wordpress", stored.ProviderCode);
        Assert.Equal("201", stored.ExternalId);
        Assert.Equal("A title", stored.Title);
        Assert.Equal("<p>body</p>", stored.Body);
        Assert.Equal("en-US", stored.Language);
        Assert.Equal("post", stored.ContentType);
        Assert.Equal(new DateTimeOffset(2026, 1, 4, 0, 0, 0, TimeSpan.Zero), stored.PublishedAt);
    }

    [Fact]
    public async Task A_draft_round_trips_with_no_publication_date()
    {
        using SqliteContentRepository repository = new();

        await repository.UpsertAsync(Item("202") with { PublishedAt = null });

        ContentItem? stored = await repository.GetAsync("wordpress:202");

        Assert.NotNull(stored);
        Assert.Null(stored.PublishedAt);
    }

    [Fact]
    public async Task Re_importing_the_same_item_overwrites_instead_of_duplicating()
    {
        using SqliteContentRepository repository = new();

        Assert.True(await repository.UpsertAsync(Item("201", "First title")));
        Assert.False(await repository.UpsertAsync(Item("201", "Corrected title")));

        // One row, carrying the newer values - this is the idempotency claim.
        // fable5: --st* 只有一行，并且带的是较新的值 —— 这就是幂等性（idempotency）主张的内容。 *en--
        Assert.Equal(1, await repository.CountAsync());

        ContentItem? stored = await repository.GetAsync("wordpress:201");
        Assert.Equal("Corrected title", stored!.Title);
    }

    [Fact]
    public async Task Different_items_are_stored_side_by_side()
    {
        using SqliteContentRepository repository = new();

        await repository.UpsertAsync(Item("201"));
        await repository.UpsertAsync(Item("202"));

        Assert.Equal(2, await repository.CountAsync());
    }

    [Fact]
    public async Task Concurrent_writers_all_land()
    {
        // The pipeline runs N consumers against one repository instance. SQLite takes one
        // writer at a time, so this is the test that would catch the serialization going wrong.
        //
        // fable5: --st*
        // pipeline 会用 N 个 consumers 对着同一个 repository 实例运行。SQLite 同一时间
        // 只接受一个写入者，所以如果串行化（serialization）出了问题，就是这个测试来抓。
        // *en--
        using SqliteContentRepository repository = new();

        IEnumerable<Task> writes = Enumerable
            .Range(0, 200)
            .Select(i => repository.UpsertAsync(Item(i.ToString())));

        await Task.WhenAll(writes);

        Assert.Equal(200, await repository.CountAsync());
    }

    [Fact]
    public async Task Concurrent_writers_of_the_same_item_leave_one_row()
    {
        using SqliteContentRepository repository = new();

        IEnumerable<Task<bool>> writes = Enumerable
            .Range(0, 100)
            .Select(_ => repository.UpsertAsync(Item("201")));

        bool[] results = await Task.WhenAll(writes);

        // Exactly one writer saw it as new, however the 100 were interleaved.
        // fable5: --st* 无论这 100 次写入如何交错，恰好只有一个写入者把它视为新条目。 *en--
        Assert.Single(results, wasNew => wasNew);
        Assert.Equal(1, await repository.CountAsync());
    }

    [Fact]
    public async Task An_item_that_was_never_stored_reads_back_as_null()
    {
        using SqliteContentRepository repository = new();

        Assert.Null(await repository.GetAsync("wordpress:999"));
    }
}
