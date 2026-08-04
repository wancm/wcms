using ContentImporter.Domain.ValueObjects;

namespace ContentImporter.Tests.Domain;

public sealed class ExternalReferenceTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_reference_without_a_provider_code_is_refused(string? providerCode)
    {
        var result = ExternalReference.Create(providerCode, "page-1001");

        Assert.False(result.IsSuccess);
        Assert.Equal(ExternalReference.ProviderCodeRequired, result.Error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_reference_without_an_external_id_is_refused(string? externalId)
    {
        var result = ExternalReference.Create("wordpress", externalId);

        Assert.False(result.IsSuccess);
        Assert.Equal(ExternalReference.ExternalIdRequired, result.Error);
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed_from_both_halves()
    {
        var result = ExternalReference.Create("  wordpress ", " page-1001  ");

        Assert.True(result.IsSuccess);
        Assert.Equal("wordpress", result.Value.ProviderCode);
        Assert.Equal("page-1001", result.Value.ExternalId);
    }

    [Fact]
    public void The_same_external_id_from_two_providers_is_two_different_items()
    {
        var wordPress = ExternalReference.Create("wordpress", "page-1001").Value;
        var legacy = ExternalReference.Create("legacy-cms", "page-1001").Value;

        Assert.NotEqual(wordPress, legacy);
    }

    [Fact]
    public void Two_references_to_the_same_content_are_equal()
    {
        var first = ExternalReference.Create("wordpress", "page-1001").Value;
        var second = ExternalReference.Create("wordpress", "page-1001").Value;

        Assert.Equal(first, second);
    }

    /// <summary>
    /// Pins the case-sensitivity decision so that changing it has to be deliberate. Merging these
    /// would de-duplicate a carelessly-cased Source Provider, but would silently lose content
    /// wherever the two ids genuinely mean different things.
    /// </summary>
    [Fact]
    public void External_ids_differing_only_by_case_are_not_the_same_content()
    {
        var upper = ExternalReference.Create("wordpress", "ABC-1").Value;
        var lower = ExternalReference.Create("wordpress", "abc-1").Value;

        Assert.NotEqual(upper, lower);
    }

    [Fact]
    public void A_reference_that_was_never_created_reports_itself_as_empty()
    {
        ExternalReference uninitialised = default;

        Assert.True(uninitialised.IsEmpty);
    }
}
