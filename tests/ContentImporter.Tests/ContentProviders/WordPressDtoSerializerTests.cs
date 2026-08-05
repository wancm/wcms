using System.Text.Json;
using ContentImporter.Application.ContentProviders;
using ContentImporter.Application.ContentProviders.ContentSource;
using ContentImporter.Application.ContentProviders.Dtos;
using ContentImporter.Application.ContentProviders.WordPress;

namespace ContentImporter.Tests.ContentProviders;

/// <summary>
/// The serializer turns the raw JSON a content source put in SourceContentItem.Content into the
/// WordPress wire DTO. Nothing is interpreted here - that is the entity mapper's job.
/// </summary>
public sealed class WordPressDtoSerializerTests
{
    private static readonly IDtoSerializer<WordPressDto> Serializer = new WordPressDtoSerializer();

    private static SourceContentItem Item(string json) =>
        new("WordPress") { CorrelationId = "corr-1", Content = json };

    /// <summary>One item exactly as WordPressJsonContentSource yields it.</summary>
    private const string PostJson = """
        {
          "title": "This is dummy title D",
          "link": "https://dummy.example.com/2026/01/dummy-post-d/",
          "creator": "dummyadmin",
          "guid": "https://dummy.example.com/?p=201",
          "content_encoded": "<p>body</p>",
          "excerpt_encoded": "an excerpt",
          "post_id": 201,
          "post_date_gmt": "2026-01-04 00:00:00",
          "post_name": "dummy-post-d",
          "status": "publish",
          "post_parent": 0,
          "post_type": "post",
          "is_sticky": 0,
          "categories": [
            { "domain": "category", "nicename": "dummy-category", "name": "Dummy Category" },
            { "domain": "post_tag", "nicename": "dummy-tag", "name": "Dummy Tag" }
          ],
          "postmeta": [ { "key": "_thumbnail_id", "value": "302" } ]
        }
        """;

    [Fact]
    public async Task Raw_export_item_becomes_a_populated_dto()
    {
        WordPressDto dto = await Serializer.SerializeAsync(Item(PostJson));

        Assert.Equal(201, dto.PostId);
        Assert.Equal("This is dummy title D", dto.Title);
        Assert.Equal("dummy-post-d", dto.PostName);
        Assert.Equal("post", dto.PostType);
        Assert.Equal("publish", dto.Status);
        Assert.Equal("dummyadmin", dto.Creator);
        Assert.Equal("<p>body</p>", dto.ContentEncoded);
        Assert.Equal("2026-01-04 00:00:00", dto.PostDateGmt);
        Assert.Equal(0, dto.PostParent);
        Assert.Equal(0, dto.IsSticky);
    }

    [Fact]
    public async Task Nested_categories_and_postmeta_survive_the_deserialize()
    {
        WordPressDto dto = await Serializer.SerializeAsync(Item(PostJson));

        // The item-level taxonomy names differ from the channel-level ones: domain/nicename here.
        Assert.Equal(2, dto.Categories!.Count);
        Assert.Equal("category", dto.Categories[0].Domain);
        Assert.Equal("dummy-category", dto.Categories[0].NiceName);
        Assert.Equal("post_tag", dto.Categories[1].Domain);

        WordPressPostMetaDto meta = Assert.Single(dto.PostMeta!);
        Assert.Equal("_thumbnail_id", meta.Key);
        Assert.Equal("302", meta.Value);
    }

    [Fact]
    public async Task An_attachment_deserializes_like_any_other_item()
    {
        // WXR has no media section - an image is an item with post_type "attachment".
        WordPressDto dto = await Serializer.SerializeAsync(Item(
            """{ "post_id": 301, "post_type": "attachment", "status": "inherit", "post_parent": 102 }"""));

        Assert.Equal("attachment", dto.PostType);
        Assert.Equal("inherit", dto.Status);
        Assert.Equal(102, dto.PostParent);
    }

    [Fact]
    public async Task Absent_properties_become_null_instead_of_throwing()
    {
        // Nothing on the DTO is required, so a sparse item is a deserialize the validator can
        // then reject with a useful message - not a JsonException naming a byte offset.
        WordPressDto dto = await Serializer.SerializeAsync(Item("{}"));

        Assert.Null(dto.PostId);
        Assert.Null(dto.Title);
        Assert.Null(dto.Categories);
    }

    [Fact]
    public async Task Unparseable_draft_date_survives_as_text()
    {
        // WordPress writes this literal for drafts. Binding dates to DateTimeOffset would turn
        // an ordinary draft into a deserialization failure, so they stay strings.
        WordPressDto dto = await Serializer.SerializeAsync(
            Item("""{ "post_date": "0000-00-00 00:00:00" }"""));

        Assert.Equal("0000-00-00 00:00:00", dto.PostDate);
    }

    [Fact]
    public async Task Unknown_plugin_properties_are_ignored()
    {
        WordPressDto dto = await Serializer.SerializeAsync(
            Item("""{ "post_id": 9, "some_plugin_field": true }"""));

        Assert.Equal(9, dto.PostId);
    }

    [Fact]
    public async Task Json_null_is_reported_as_a_json_error()
    {
        JsonException error = await Assert.ThrowsAsync<JsonException>(
            () => Serializer.SerializeAsync(Item("null")));

        // The correlation id is what an operator quotes when asking what happened to one item.
        Assert.Contains("corr-1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Malformed_json_is_reported_as_a_json_error()
    {
        // A JsonException per item is what lets the pipeline isolate one bad item and carry on.
        await Assert.ThrowsAsync<JsonException>(() => Serializer.SerializeAsync(Item("{ not json")));
    }
}
