using ContentImporter.Domain.Results;

namespace ContentImporter.Domain.ValueObjects;

/// <summary>
/// How a piece of content is identified in the Source Provider it came from: the provider's code
/// paired with the id that provider gave it.
/// </summary>
/// <remarks>
/// <para>
/// The pairing is the point. Two Source Providers can both export an item called
/// <c>page-1001</c>, so the external id alone is not an identity — only the pair is. This is what
/// the repository upserts on, and therefore what makes a re-import idempotent.
/// </para>
/// <para>
/// Comparison is ordinal and case-sensitive, which is a deliberate choice rather than a default
/// falling out of the language. Comparing case-insensitively would merge <c>ABC-1</c> and
/// <c>abc-1</c> into one item: helpful de-duplication if a Source Provider is careless with
/// casing, silent data loss if the two are genuinely different records. Losing content during a
/// migration is far worse than importing a near-duplicate a human can merge later, so we do not
/// guess.
/// </para>
/// </remarks>
public readonly record struct ExternalReference
{
    public static readonly DomainError ProviderCodeRequired = new(
        "external_reference.provider_code_required",
        "A provider code is required. Every Source Record must say which Source Provider it came from.");

    public static readonly DomainError ExternalIdRequired = new(
        "external_reference.external_id_required",
        "An external id is required. Content with no identity in its Source Provider cannot be upserted idempotently.");

    private ExternalReference(string providerCode, string externalId)
    {
        ProviderCode = providerCode;
        ExternalId = externalId;
    }

    /// <summary>The Source Provider this content was exported from, for example <c>wordpress</c>.</summary>
    public string ProviderCode { get; }

    /// <summary>The id the Source Provider gave this content, for example <c>page-1001</c>.</summary>
    public string ExternalId { get; }

    /// <summary>
    /// True for a reference that was never built through <see cref="Create"/>.
    /// </summary>
    /// <remarks>
    /// A struct always has a <c>default</c> value, and no amount of validation in
    /// <see cref="Create"/> can stop somebody writing <c>default(ExternalReference)</c>. That is
    /// the cost of choosing a struct over a class here — the aggregate has to check. The
    /// alternative, a sealed class, removes the hole but allocates for every value and turns every
    /// field into something that can be null instead.
    /// </remarks>
    public bool IsEmpty => ProviderCode is null || ExternalId is null;

    public static Result<ExternalReference> Create(string? providerCode, string? externalId)
    {
        if (string.IsNullOrWhiteSpace(providerCode))
        {
            return Result<ExternalReference>.Failure(ProviderCodeRequired);
        }

        if (string.IsNullOrWhiteSpace(externalId))
        {
            return Result<ExternalReference>.Failure(ExternalIdRequired);
        }

        return Result<ExternalReference>.Success(
            new ExternalReference(providerCode.Trim(), externalId.Trim()));
    }

    public override string ToString() => $"{ProviderCode}:{ExternalId}";
}
