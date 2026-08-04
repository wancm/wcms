namespace ContentImporter.Domain.Entities;

/// <summary>
/// A piece of content in our WCMS: what an import produces and what upstream systems hear about.
/// </summary>
public record ContentItem
{
    /// <summary>
    /// "wordpress:201" - the source provider paired with the id that provider gave this item.
    /// Derived rather than generated, so re-importing the same export lands on the same key
    /// instead of adding a duplicate.
    /// </summary>
    public required string Id { get; init; }

    public required string ProviderCode { get; init; }

    public required string ExternalId { get; init; }

    public required string Title { get; init; }

    /// <summary>Empty is allowed - a stub page is still a page.</summary>
    public string Body { get; init; } = string.Empty;

    /// <summary>BCP 47, e.g. en-US.</summary>
    public required string Language { get; init; }

    /// <summary>page, post, attachment ...</summary>
    public required string ContentType { get; init; }

    /// <summary>Null means never published, i.e. a draft.</summary>
    public DateTimeOffset? PublishedAt { get; init; }

    public override string ToString() => $"{Id} ({Language}) {Title}";
}
