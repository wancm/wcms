using ContentImporter.Domain.Entities;
using ContentImporter.Domain.ValueObjects;

namespace ContentImporter.Tests.Domain;

public sealed class ContentItemTests
{
    private static readonly ExternalReference Reference =
        ExternalReference.Create("wordpress", "page-1001").Value;

    private static readonly LanguageTag Language = LanguageTag.Create("en-MY").Value;

    private static readonly ContentType Type = ContentType.Create("page").Value;

    [Fact]
    public void Valid_content_keeps_everything_it_was_given()
    {
        var publishedAt = new DateTimeOffset(2026, 8, 4, 9, 30, 0, TimeSpan.FromHours(8));

        var result = ContentItem.Create(
            Reference, "Travel Insurance", "Cover for your trip.", Language, Type, publishedAt);

        Assert.True(result.IsSuccess);
        var item = result.Value;
        Assert.Equal(Reference, item.Reference);
        Assert.Equal("Travel Insurance", item.Title);
        Assert.Equal("Cover for your trip.", item.Body);
        Assert.Equal(Language, item.Language);
        Assert.Equal(Type, item.Type);
        Assert.Equal(publishedAt, item.PublishedAt);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Content_without_a_title_is_refused(string? title)
    {
        var result = ContentItem.Create(Reference, title, "body", Language, Type, null);

        Assert.False(result.IsSuccess);
        Assert.Equal(ContentItem.TitleRequired, result.Error);
    }

    [Fact]
    public void A_title_is_trimmed()
    {
        var result = ContentItem.Create(Reference, "  Travel Insurance  ", "", Language, Type, null);

        Assert.True(result.IsSuccess);
        Assert.Equal("Travel Insurance", result.Value.Title);
    }

    [Fact]
    public void A_title_at_the_limit_is_accepted()
    {
        string title = new('a', ContentItem.MaxTitleLength);

        var result = ContentItem.Create(Reference, title, "", Language, Type, null);

        Assert.True(result.IsSuccess);
    }

    /// <summary>
    /// Refused rather than truncated: silently shortening a customer's title during a migration
    /// loses data they cannot recover, while failing the item leaves it in the import report.
    /// </summary>
    [Fact]
    public void A_title_past_the_limit_is_refused_rather_than_truncated()
    {
        string title = new('a', ContentItem.MaxTitleLength + 1);

        var result = ContentItem.Create(Reference, title, "", Language, Type, null);

        Assert.False(result.IsSuccess);
        Assert.Equal(ContentItem.TitleTooLong, result.Error);
    }

    [Fact]
    public void An_empty_body_is_allowed_because_a_stub_page_is_still_a_page()
    {
        var result = ContentItem.Create(Reference, "Travel Insurance", null, Language, Type, null);

        Assert.True(result.IsSuccess);
        Assert.Equal(string.Empty, result.Value.Body);
    }

    [Fact]
    public void Content_that_was_never_published_is_allowed()
    {
        var result = ContentItem.Create(Reference, "Draft", "", Language, Type, null);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.PublishedAt);
    }

    /// <summary>
    /// Every value object here is a struct, so <c>default</c> exists and skips validation
    /// entirely. These three tests are what stops an uninitialised struct reaching storage.
    /// </summary>
    [Fact]
    public void Content_with_an_uninitialised_reference_is_refused()
    {
        var result = ContentItem.Create(default, "Travel Insurance", "", Language, Type, null);

        Assert.False(result.IsSuccess);
        Assert.Equal(ContentItem.ReferenceRequired, result.Error);
    }

    [Fact]
    public void Content_with_an_uninitialised_language_is_refused()
    {
        var result = ContentItem.Create(Reference, "Travel Insurance", "", default, Type, null);

        Assert.False(result.IsSuccess);
        Assert.Equal(ContentItem.LanguageRequired, result.Error);
    }

    [Fact]
    public void Content_with_an_uninitialised_type_is_refused()
    {
        var result = ContentItem.Create(Reference, "Travel Insurance", "", Language, default, null);

        Assert.False(result.IsSuccess);
        Assert.Equal(ContentItem.TypeRequired, result.Error);
    }
}
