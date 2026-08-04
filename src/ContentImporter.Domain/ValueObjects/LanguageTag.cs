using ContentImporter.Domain.Results;

namespace ContentImporter.Domain.ValueObjects;

/// <summary>
/// The language a piece of content is written in, as a well-formed IETF tag: <c>en</c>,
/// <c>en-MY</c>, <c>zh-Hans</c>-style two-part tags.
/// </summary>
/// <remarks>
/// <para>
/// This type answers one question and refuses the other. It checks that a tag is <em>well
/// formed</em>. It does not check that the target site <em>supports</em> the language, because
/// <c>en-MY</c> is a perfectly valid tag whether or not any particular customer accepts it —
/// supported-ness is per-customer policy that changes without a code change, so it belongs to
/// canonical validation in the Application layer. Same field, two rules, two layers.
/// </para>
/// <para>
/// Provider-specific spellings are rejected outright: WordPress's <c>en_MY</c> and a legacy CMS's
/// LCID <c>1033</c> both fail here. Normalising those is the mapper's job, and by the time a
/// record reaches the domain it must already have happened. This type is the last line of
/// defence, not the place things get repaired — if a new Source Provider's mapper forgets to
/// normalise, this is what catches it rather than a bad tag reaching storage.
/// </para>
/// </remarks>
public readonly record struct LanguageTag
{
    public static readonly DomainError Required = new(
        "language_tag.required",
        "A language tag is required.");

    public static readonly DomainError Malformed = new(
        "language_tag.malformed",
        "A language tag must look like 'en' or 'en-MY'. Source Provider spellings such as 'en_MY' " +
        "or '1033' must be normalised by the mapper before the record reaches the domain.");

    /// <summary>Longest tag this accepts, e.g. <c>eng-001</c>. Used to size the stack buffer.</summary>
    private const int MaxLength = 7;

    private LanguageTag(string value) => Value = value;

    /// <summary>The canonical tag: lower-case language, upper-case region.</summary>
    public string Value { get; }

    /// <inheritdoc cref="ExternalReference.IsEmpty"/>
    public bool IsEmpty => Value is null;

    public static Result<LanguageTag> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Result<LanguageTag>.Failure(Required);
        }

        ReadOnlySpan<char> trimmed = value.AsSpan().Trim();
        if (trimmed.Length > MaxLength)
        {
            return Result<LanguageTag>.Failure(Malformed);
        }

        int dash = trimmed.IndexOf('-');
        ReadOnlySpan<char> language = dash < 0 ? trimmed : trimmed[..dash];
        ReadOnlySpan<char> region = dash < 0 ? [] : trimmed[(dash + 1)..];

        // Two or three letters for the language. This is also what rejects '1033' and, because
        // an underscore is neither a letter nor a dash, what rejects 'en_MY'.
        if (language.Length is < 2 or > 3 || !IsAsciiLetters(language))
        {
            return Result<LanguageTag>.Failure(Malformed);
        }

        // A region is optional, but if present it is either a two-letter country code or a
        // three-digit UN M.49 area code. A second dash lands here and fails both tests.
        if (dash >= 0 &&
            !(region.Length == 2 && IsAsciiLetters(region)) &&
            !(region.Length == 3 && IsAsciiDigits(region)))
        {
            return Result<LanguageTag>.Failure(Malformed);
        }

        // Canonicalise into a stack buffer rather than building intermediate strings. A tag is at
        // most seven characters, so the stackalloc is safe and the whole operation costs a single
        // allocation — and none at all when the mapper already handed us a canonical tag, which
        // is the overwhelmingly common case.
        Span<char> canonical = stackalloc char[trimmed.Length];
        language.ToLowerInvariant(canonical);
        if (dash >= 0)
        {
            canonical[dash] = '-';
            region.ToUpperInvariant(canonical[(dash + 1)..]);
        }

        string result = value.AsSpan().SequenceEqual(canonical) ? value : new string(canonical);
        return Result<LanguageTag>.Success(new LanguageTag(result));
    }

    private static bool IsAsciiLetters(ReadOnlySpan<char> span)
    {
        foreach (char character in span)
        {
            if (!char.IsAsciiLetter(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiDigits(ReadOnlySpan<char> span)
    {
        foreach (char character in span)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    public override string ToString() => Value ?? string.Empty;
}
