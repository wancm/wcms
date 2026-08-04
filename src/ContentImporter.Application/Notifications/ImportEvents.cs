using ContentImporter.Domain.Entities;

namespace ContentImporter.Application.Notifications
{
    /// <summary>
    /// Something an upstream system is told about. Immutable, so it can be handed to any number
    /// of subscribers on any number of threads without copying or locking.
    /// </summary>
    public abstract record ImportEvent
    {
        public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

        /// <summary>Ties every event from one import run together in a log or a trace.</summary>
        public required string EventId { get; init; }
    }

    /// <summary>
    /// One piece of content is now available in our WCMS.
    /// </summary>
    /// <remarks>
    /// Carries a projection of the content, never the content itself. The body of a page is
    /// unbounded customer HTML, and a notification is meant to say "this happened", not to ship a
    /// copy of the data - brokers cap message sizes, and a consumer invalidating a cache does not
    /// need the markup. A consumer that wants the body fetches it by <see cref="Id"/>; the pattern
    /// is claim-check. <see cref="BodyPreview"/> exists so a human reading the log can tell which
    /// page this was, not so a machine can reconstruct it.
    /// </remarks>
    public sealed record ContentImported : ImportEvent
    {
        public required string Id { get; init; }

        public required string ProviderCode { get; init; }

        public required string ExternalId { get; init; }

        public required string Title { get; init; }

        public required string ContentType { get; init; }

        public required string Language { get; init; }

        public DateTimeOffset? PublishedAt { get; init; }

        public required int BodyLength { get; init; }

        public required string BodyPreview { get; init; }

        /// <summary>How much of the body travels with the event, for a human reading it.</summary>
        private const int PreviewLength = 120;

        public static ContentImported From(ContentItem item, string eventId) => new()
        {
            EventId = eventId,
            Id = item.Id,
            ProviderCode = item.ProviderCode,
            ExternalId = item.ExternalId,
            Title = item.Title,
            ContentType = item.ContentType,
            Language = item.Language,
            PublishedAt = item.PublishedAt,
            BodyLength = item.Body.Length,
            BodyPreview = item.Body.Length <= PreviewLength
                ? item.Body
                : string.Concat(item.Body.AsSpan(0, PreviewLength), "...")
        };
    }

    /// <summary>
    /// The import run has finished. Sent once, whatever happened - including when items failed.
    /// </summary>
    public sealed record ImportCompleted : ImportEvent
    {
        public required int Imported { get; init; }

        public required int Failed { get; init; }

        public required TimeSpan Duration { get; init; }
    }
}
