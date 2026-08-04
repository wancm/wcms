using ContentImporter.Application.ContentProviders.Dtos;
using ContentImporter.Domain.Entities;
using System.Globalization;

namespace ContentImporter.Application.ContentProviders.WordPress
{
    internal class WordPressDtoEntityMapper : IDtoEntityMapper<WordPressDto>
    {
        private const string ProviderCode = "wordpress";

        // post_date_gmt looks like this: no "T", no offset.
        private const string GmtFormat = "yyyy-MM-dd HH:mm:ss";

        // Core WordPress stores language once on the channel, never on an item, and the source
        // reads only channel.items - so there is nothing per-item to read here.
        private const string DefaultLanguage = "en-US";

        // No validation here: WordPressDtoValidator already rejected the items not worth mapping.
        public async Task<ContentItem> MapAsync(WordPressDto dto)
        {
            string externalId = dto.PostId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

            return new ContentItem
            {
                // post_id alone is not an identity - two providers can both export item 201.
                // Derived, not generated: a fresh Guid per run would make every re-import look
                // like new content to ContentPublisher's duplicate check.
                Id = $"{ProviderCode}:{externalId}",
                ProviderCode = ProviderCode,
                ExternalId = externalId,
                Title = dto.Title?.Trim() ?? string.Empty,
                Body = dto.ContentEncoded ?? string.Empty,
                Language = DefaultLanguage,
                ContentType = dto.PostType?.ToLowerInvariant() ?? string.Empty,
                PublishedAt = await ParsePublishedAtAsync(dto.PostDateGmt)
            };
        }

        // Anything unusable means "never published" - a draft legitimately has no date.
        // That includes WordPress's "0000-00-00 00:00:00" placeholder, which fails the exact
        // parse on its own: month 00 and day 00 are not dates, so it needs no special case.
        private static async Task<DateTimeOffset?> ParsePublishedAtAsync(string? postDateGmt)
        {
            if (string.IsNullOrWhiteSpace(postDateGmt))
            {
                return null;
            }

            return DateTimeOffset.TryParseExact(
                postDateGmt,
                GmtFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset published)
                ? published
                : null;
        }
    }
}
