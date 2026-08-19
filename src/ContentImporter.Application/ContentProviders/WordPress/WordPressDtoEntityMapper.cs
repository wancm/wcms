using ContentImporter.Application.ContentProviders.Dtos;
using ContentImporter.Domain.Entities;
using System.Globalization;

namespace ContentImporter.Application.ContentProviders.WordPress
{
    internal class WordPressDtoEntityMapper : IDtoEntityMapper<WordPressDto>
    {
        private const string ProviderCode = "wordpress";

        // post_date_gmt looks like this: no "T", no offset.
        // fable5: --st* post_date_gmt 长这个样子：没有 "T"，也没有时区偏移（offset）。 *en--
        private const string GmtFormat = "yyyy-MM-dd HH:mm:ss";

        // Core WordPress stores language once on the channel, never on an item, and the source
        // reads only channel.items - so there is nothing per-item to read here.
        //
        // fable5: --st*
        // WordPress core 只在 channel 上存一次语言，从不存在单个 item 上，而这个 source
        // 只读取 channel.items —— 所以这里没有任何 per-item 的语言信息可读。
        // *en--
        private const string DefaultLanguage = "en-US";

        // No validation here: WordPressDtoValidator already rejected the items not worth mapping.
        // Task.FromResult, not async: mapping is a pure transform over an in-memory DTO, so there
        // is nothing to await and no state machine worth paying for.
        //
        // fable5: --st*
        // 这里不做 validation：WordPressDtoValidator 已经拒绝了不值得 map 的 items。
        // 用 Task.FromResult 而不是 async：mapping 是对内存中 DTO 的纯变换（pure transform），
        // 没有任何东西可 await，也不值得为此付出 async state machine 的开销。
        // *en--
        public Task<ContentItem> MapAsync(WordPressDto dto)
        {
            string externalId = dto.PostId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

            return Task.FromResult(new ContentItem
            {
                // post_id alone is not an identity - two providers can both export item 201.
                // Derived, not generated: a fresh Guid per run would make every re-import look
                // like new content to ContentPublisher's duplicate check.
                //
                // fable5: --st*
                // 单靠 post_id 不构成身份（identity）—— 两个 provider 都可能导出 item 201。
                // Id 是派生（derived）的，不是生成（generated）的：如果每次 run 都产生新的 Guid，
                // 那么在 ContentPublisher 的重复检查看来，每次 re-import 都会像是全新的 content。
                // *en--
                Id = $"{ProviderCode}:{externalId}",
                ProviderCode = ProviderCode,
                ExternalId = externalId,
                Title = dto.Title?.Trim() ?? string.Empty,
                Body = dto.ContentEncoded ?? string.Empty,
                Language = DefaultLanguage,
                ContentType = dto.PostType?.ToLowerInvariant() ?? string.Empty,
                PublishedAt = ParsePublishedAt(dto.PostDateGmt)
            });
        }

        // Anything unusable means "never published" - a draft legitimately has no date.
        // That includes WordPress's "0000-00-00 00:00:00" placeholder, which fails the exact
        // parse on its own: month 00 and day 00 are not dates, so it needs no special case.
        //
        // fable5: --st*
        // 任何不可用的值都当作“从未发布”—— draft 本来就可以合法地没有日期。
        // 这也包括 WordPress 的占位符 "0000-00-00 00:00:00"，它本身就通不过精确解析（exact parse）：
        // 月份 00 和日期 00 不是有效日期，所以不需要为它写特殊分支。
        // *en--
        private static DateTimeOffset? ParsePublishedAt(string? postDateGmt)
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
