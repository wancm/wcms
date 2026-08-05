using ContentImporter.Application.ContentProviders;
using ContentImporter.Application.ContentProviders.Dtos;
using ContentImporter.Application.ContentProviders.WordPress;
using ContentImporter.Domain.Entities;

namespace ContentImporter.Tests.ContentProviders;

/// <summary>
/// Where WordPress's vocabulary becomes ours: its GMT text becomes a real instant and its
/// "0000-00-00" placeholder becomes "never published".
/// </summary>
public sealed class WordPressDtoEntityMapperTests
{
    private static readonly IDtoEntityMapper<WordPressDto> Mapper = new WordPressDtoEntityMapper();

    private static readonly WordPressDto PublishedPost = new()
    {
        PostId = 201,
        Title = "This is dummy title D",
        ContentEncoded = "<p>body</p>",
        PostType = "post",
        Status = "publish",
        PostDateGmt = "2026-01-04 00:00:00"
    };

    [Fact]
    public async Task A_published_post_becomes_content()
    {
        ContentItem item = await Mapper.MapAsync(PublishedPost);

        Assert.Equal("wordpress", item.ProviderCode);
        Assert.Equal("201", item.ExternalId);
        Assert.Equal("This is dummy title D", item.Title);
        Assert.Equal("<p>body</p>", item.Body);
        Assert.Equal("post", item.ContentType);
        Assert.Equal("en-US", item.Language);
    }

    [Fact]
    public async Task The_id_pairs_the_provider_with_the_source_id()
    {
        // This is the key ContentPublisher de-duplicates on, so mapping the same item twice
        // has to produce the same string both times. A generated id would not.
        ContentItem first = await Mapper.MapAsync(PublishedPost);
        ContentItem second = await Mapper.MapAsync(PublishedPost);

        Assert.Equal("wordpress:201", first.Id);
        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task Gmt_text_becomes_an_instant_at_zero_offset()
    {
        // post_date_gmt has no "T" and no offset, so the offset must be supplied, not inferred.
        ContentItem item = await Mapper.MapAsync(PublishedPost);

        Assert.Equal(new DateTimeOffset(2026, 1, 4, 0, 0, 0, TimeSpan.Zero), item.PublishedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0000-00-00 00:00:00")]
    [InlineData("not a date")]
    public async Task A_date_that_is_not_one_means_never_published(string? postDateGmt)
    {
        // The DTO keeps dates as text so a draft does not fail deserialization; turning the
        // unusable ones into null is this layer's job.
        ContentItem item = await Mapper.MapAsync(PublishedPost with { PostDateGmt = postDateGmt });

        Assert.Null(item.PublishedAt);
    }

    [Fact]
    public async Task An_attachment_maps_like_any_other_content()
    {
        ContentItem item = await Mapper.MapAsync(PublishedPost with
        {
            PostId = 301,
            Title = "dummy-image-one",
            PostType = "attachment",
            Status = "inherit",
            PostDateGmt = null,
            ContentEncoded = null
        });

        Assert.Equal("wordpress:301", item.Id);
        Assert.Equal("301", item.ExternalId);
        Assert.Equal("attachment", item.ContentType);
        Assert.Null(item.PublishedAt);
        Assert.Equal(string.Empty, item.Body);
    }

    [Fact]
    public async Task Post_type_casing_is_flattened()
    {
        // "Page" and "page" are not two different kinds of content.
        ContentItem item = await Mapper.MapAsync(PublishedPost with { PostType = "Page" });

        Assert.Equal("page", item.ContentType);
    }

    [Fact]
    public async Task Surrounding_whitespace_is_taken_off_the_title()
    {
        ContentItem item = await Mapper.MapAsync(PublishedPost with { Title = "  Spaced  " });

        Assert.Equal("Spaced", item.Title);
    }
}
