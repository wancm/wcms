using ContentImporter.Domain.Results;

namespace ContentImporter.Domain.ValueObjects;

/// <summary>
/// What kind of thing a piece of content is — <c>page</c>, <c>article</c>, <c>landing-page</c>.
/// </summary>
/// <remarks>
/// <para>
/// Normalised to lower case, because Source Providers disagree about capitalisation and
/// <c>Page</c> and <c>page</c> are not two different kinds of content. Normalising here rather
/// than in each mapper means a new Source Provider gets the behaviour for free.
/// </para>
/// <para>
/// Which type names are <em>allowed</em> is not decided here, for the same reason
/// <see cref="LanguageTag"/> does not decide which languages are supported: the set of content
/// types a target site accepts is per-customer configuration, not a rule about what a content
/// type is.
/// </para>
/// </remarks>
public readonly record struct ContentType
{
    public static readonly DomainError Required = new(
        "content_type.required",
        "A content type is required.");

    private ContentType(string value) => Value = value;

    /// <summary>The normalised, lower-case type name.</summary>
    public string Value { get; }

    /// <inheritdoc cref="ExternalReference.IsEmpty"/>
    public bool IsEmpty => Value is null;

    public static Result<ContentType> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<ContentType>.Failure(Required);
        }

        string trimmed = value.Trim();

        // ToLowerInvariant always allocates, even when the input is already lower case. Checking
        // first means an already-normalised type name — which is most of them — costs nothing.
        string normalised = IsLowerInvariant(trimmed) ? trimmed : trimmed.ToLowerInvariant();

        return Result<ContentType>.Success(new ContentType(normalised));
    }

    private static bool IsLowerInvariant(string value)
    {
        foreach (char character in value)
        {
            if (char.IsUpper(character))
            {
                return false;
            }
        }

        return true;
    }

    public override string ToString() => Value ?? string.Empty;
}
