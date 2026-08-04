using ContentImporter.Domain.ValueObjects;

namespace ContentImporter.Tests.Domain;

public sealed class LanguageTagTests
{
    [Theory]
    [InlineData("en", "en")]
    [InlineData("en-MY", "en-MY")]
    [InlineData("EN-my", "en-MY")]
    [InlineData("  en-my  ", "en-MY")]
    [InlineData("es-419", "es-419")]
    [InlineData("eng", "eng")]
    public void A_well_formed_tag_is_accepted_and_canonicalised(string input, string expected)
    {
        var result = LanguageTag.Create(input);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Value);
    }

    /// <summary>
    /// The two spellings this domain most needs to refuse: WordPress writes an underscore, and a
    /// legacy CMS writes a Windows LCID. Both are the mapper's problem, and both reaching here
    /// means a mapper is missing a normalisation step.
    /// </summary>
    [Theory]
    [InlineData("en_MY")]
    [InlineData("1033")]
    public void A_source_provider_spelling_that_the_mapper_should_have_normalised_is_refused(string input)
    {
        var result = LanguageTag.Create(input);

        Assert.False(result.IsSuccess);
        Assert.Equal(LanguageTag.Malformed, result.Error);
    }

    [Theory]
    [InlineData("e")]
    [InlineData("engl")]
    [InlineData("en-M")]
    [InlineData("en-MYS")]
    [InlineData("en-MY-extra")]
    [InlineData("en MY")]
    [InlineData("en-")]
    public void A_malformed_tag_is_refused(string input)
    {
        var result = LanguageTag.Create(input);

        Assert.False(result.IsSuccess);
        Assert.Equal(LanguageTag.Malformed, result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_tag_is_refused(string? input)
    {
        var result = LanguageTag.Create(input);

        Assert.False(result.IsSuccess);
        Assert.Equal(LanguageTag.Required, result.Error);
    }

    /// <summary>
    /// The point of canonicalising: a WordPress export and a Sitecore export describing the same
    /// Malaysian English content must end up equal, or the same content lands twice.
    /// </summary>
    [Fact]
    public void Tags_that_differ_only_in_casing_describe_the_same_language()
    {
        var fromOneProvider = LanguageTag.Create("en-MY").Value;
        var fromAnother = LanguageTag.Create("EN-my").Value;

        Assert.Equal(fromOneProvider, fromAnother);
    }

    [Fact]
    public void A_tag_that_was_never_created_reports_itself_as_empty()
    {
        LanguageTag uninitialised = default;

        Assert.True(uninitialised.IsEmpty);
    }
}
