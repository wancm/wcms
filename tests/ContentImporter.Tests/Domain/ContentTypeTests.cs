using ContentImporter.Domain.ValueObjects;

namespace ContentImporter.Tests.Domain;

public sealed class ContentTypeTests
{
    [Theory]
    [InlineData("page", "page")]
    [InlineData("Page", "page")]
    [InlineData("LANDING-PAGE", "landing-page")]
    [InlineData("  Article  ", "article")]
    public void A_type_name_is_trimmed_and_lower_cased(string input, string expected)
    {
        var result = ContentType.Create(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_type_name_is_refused(string? input)
    {
        var result = ContentType.Create(input);

        Assert.False(result.IsSuccess);
        Assert.Equal(ContentType.Required, result.Error);
    }

    [Fact]
    public void Providers_that_disagree_about_capitalisation_still_mean_the_same_type()
    {
        var fromOneProvider = ContentType.Create("Page").Value;
        var fromAnother = ContentType.Create("page").Value;

        Assert.Equal(fromOneProvider, fromAnother);
    }

    [Fact]
    public void A_type_that_was_never_created_reports_itself_as_empty()
    {
        ContentType uninitialised = default;

        Assert.True(uninitialised.IsEmpty);
    }
}
