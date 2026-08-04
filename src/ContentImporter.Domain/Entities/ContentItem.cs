using ContentImporter.Domain.Results;
using ContentImporter.Domain.ValueObjects;

namespace ContentImporter.Domain.Entities;

/// <summary>
/// A piece of content in our WCMS. This is what an import produces and what Upstream Systems are
/// told about.
/// </summary>
/// <remarks>
/// <para>
/// Immutable, so the import's parallel workers can pass instances around without locking anything.
/// Identity is <see cref="Reference"/> — the pair of Source Provider and the id that provider gave
/// it — which is what the repository upserts on and therefore what makes a re-import idempotent.
/// </para>
/// <para>
/// <strong>Idempotent storage is not idempotent notification.</strong> Upserting by
/// <see cref="Reference"/> means re-running an Export leaves storage in the same state, and it is
/// tempting to call that idempotent and move on. But the pipeline publishes a ContentImported
/// event for every item it processes, so a second run of an identical Export tells Upstream
/// Systems about a hundred thousand pieces of content that did not change by a single character.
/// That is survivable — at-least-once delivery with idempotent consumers is the normal contract
/// for event-driven systems — but "the import is idempotent" needs the qualifier "in storage"
/// said out loud rather than discovered.
/// </para>
/// <para>
/// Fixing it properly is deliberately out of scope, and would look like this. Give this type a
/// fingerprint: a digest over the fields that carry <em>content</em> — Title, Body, Language,
/// Type, PublishedAt — and pointedly not over metadata such as importedAtUtc or
/// migrationBatchId, because re-importing the same article on a different day has not changed the
/// article. Choosing which fields count is a judgement about content, which is why it would live
/// here rather than in the repository or the pipeline. The repository's upsert would then report
/// Inserted, Updated or Unchanged, and the pipeline would publish only when something actually
/// changed.
/// </para>
/// <para>
/// Two traps in that, worth knowing before anyone implements it. Fields must be length-prefixed or
/// delimited before hashing, or <c>"ab" + "c"</c> and <c>"a" + "bc"</c> hash identically and two
/// different items collide. And it must be a stable hash — <see cref="string.GetHashCode()"/> is
/// seeded per process in .NET, so a fingerprint written to a database stops matching after a
/// restart. <c>SHA256.HashData(ReadOnlySpan{byte})</c>, the static overload, avoids both the
/// instability and an allocation per item.
/// </para>
/// </remarks>
public sealed class ContentItem
{
    /// <summary>
    /// Titles beyond this are refused rather than truncated. Silently shortening a customer's
    /// content during a migration loses data they cannot get back; failing the item leaves it in
    /// the import report where somebody can decide what to do.
    /// </summary>
    public const int MaxTitleLength = 512;

    public static readonly DomainError ReferenceRequired = new(
        "content_item.reference_required",
        "Content must carry the reference identifying it in its Source Provider.");

    public static readonly DomainError TitleRequired = new(
        "content_item.title_required",
        "Content must have a title.");

    public static readonly DomainError TitleTooLong = new(
        "content_item.title_too_long",
        $"A title may be at most {MaxTitleLength} characters.");

    public static readonly DomainError LanguageRequired = new(
        "content_item.language_required",
        "Content must declare the language it is written in.");

    public static readonly DomainError TypeRequired = new(
        "content_item.type_required",
        "Content must declare its type.");

    private ContentItem(
        ExternalReference reference,
        string title,
        string body,
        LanguageTag language,
        ContentType type,
        DateTimeOffset? publishedAt)
    {
        Reference = reference;
        Title = title;
        Body = body;
        Language = language;
        Type = type;
        PublishedAt = publishedAt;
    }

    /// <summary>Identity: which Source Provider, and which id within it.</summary>
    public ExternalReference Reference { get; }

    public string Title { get; }

    /// <summary>The content body. Empty is allowed — a stub page is still a page.</summary>
    public string Body { get; }

    public LanguageTag Language { get; }

    public ContentType Type { get; }

    /// <summary>When the Source Provider published this, if it ever was. Null means a draft.</summary>
    public DateTimeOffset? PublishedAt { get; }

    /// <summary>
    /// Builds content, or explains why it cannot be built.
    /// </summary>
    /// <remarks>
    /// Takes value objects rather than raw strings so that each one has already vouched for
    /// itself — the caller builds them first and handles their failures individually, which gives
    /// a far more useful import report than a single "this record was bad".
    /// <para>
    /// The <c>IsEmpty</c> checks are not redundant. Every value object here is a struct, so
    /// <c>default(LanguageTag)</c> exists and bypasses <c>Create</c> entirely. The aggregate is
    /// the last thing standing between that and storage.
    /// </para>
    /// </remarks>
    public static Result<ContentItem> Create(
        ExternalReference reference,
        string? title,
        string? body,
        LanguageTag language,
        ContentType type,
        DateTimeOffset? publishedAt)
    {
        if (reference.IsEmpty)
        {
            return Result<ContentItem>.Failure(ReferenceRequired);
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            return Result<ContentItem>.Failure(TitleRequired);
        }

        string trimmedTitle = title.Trim();
        if (trimmedTitle.Length > MaxTitleLength)
        {
            return Result<ContentItem>.Failure(TitleTooLong);
        }

        if (language.IsEmpty)
        {
            return Result<ContentItem>.Failure(LanguageRequired);
        }

        if (type.IsEmpty)
        {
            return Result<ContentItem>.Failure(TypeRequired);
        }

        return Result<ContentItem>.Success(
            new ContentItem(reference, trimmedTitle, body ?? string.Empty, language, type, publishedAt));
    }

    public override string ToString() => $"{Reference} ({Language}) {Title}";
}
