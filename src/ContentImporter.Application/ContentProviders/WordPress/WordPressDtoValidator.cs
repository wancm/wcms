using ContentImporter.Application.ContentProviders.Dtos;

namespace ContentImporter.Application.ContentProviders.WordPress
{
    internal class WordPressDtoValidator : IDtoValidator<WordPressDto>
    {
        /* 
         * This step is the first level validation.
         * Validate the raw data that is received from the legacy WCMS (WordPress) and ensure that it meets the required criteria 
         * before processing it further.
         */
        //
        // fable5: --st*
        // 这一步是第一层 validation。
        // 校验从旧 WCMS（WordPress）收到的原始数据，确保它满足要求的标准，才允许进入后续处理。
        // *en--

        /// <summary>Our import contract's limit. WordPress itself stores post_title as TEXT.</summary>
        internal const int TitleMaxLength = 255;

        private const string PublishedStatus = "publish";

        // WordPress writes this literal instead of omitting the date. It parses as nothing.
        // fable5: --st* WordPress 不省略日期，而是写入这个字面量（literal）。它解析不出任何有效值。 *en--
        private const string NoDate = "0000-00-00 00:00:00";

        // Every status core WordPress can export. "inherit" is what attachments carry.
        // fable5: --st* WordPress core 可能导出的全部 status。"inherit" 是 attachment 携带的状态。 *en--
        private static readonly HashSet<string> KnownStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            PublishedStatus, "draft", "pending", "private", "future", "trash", "inherit"
        };

        public Task<ValidationOutcome> ValidateAsync(WordPressDto obj)
        {
            // Deliberately no short-circuit: every rule runs even once one has failed, so this
            // returns the full list of reasons rather than only the first.
            // Task.FromResult, not async: these are pure predicates over an in-memory DTO, so
            // there is nothing to await and no state machine worth paying for.
            //
            // fable5: --st*
            // 刻意不做短路（short-circuit）：即使某条规则已经失败，其余规则仍然全部执行，
            // 这样返回的是完整的失败原因列表，而不是只有第一条。
            // 用 Task.FromResult 而不是 async：这些都是对内存中 DTO 的纯谓词（pure predicate），
            // 没有东西可 await，也不值得为此付出 state machine 的开销。
            // *en--
            var failures = new List<string>();

            // Phrased for whoever reads the import report, naming the source field rather than
            // the rule - "post_id" is what they will search the export for.
            //
            // fable5: --st*
            // 措辞面向阅读 import report 的人：点名 source 字段而不是规则名 ——
            // 他们会拿 "post_id" 去 export 里搜索。
            // *en--
            if (!ValidatePostId(obj))
            {
                failures.Add("post_id is mandatory and must be greater than zero");
            }

            if (!ValidateTitle(obj))
            {
                failures.Add($"title is mandatory and must be {TitleMaxLength} characters or fewer");
            }

            if (!ValidateStatus(obj))
            {
                failures.Add($"status '{obj.Status}' is not one WordPress exports");
            }

            if (!ValidatePublishDate(obj))
            {
                failures.Add("a published item must carry a real post_date_gmt");
            }

            return Task.FromResult(
                failures.Count == 0 ? ValidationOutcome.Valid : new ValidationOutcome(failures));
        }

        public bool ValidatePostId(WordPressDto obj)
        {
            return obj.PostId > 0;
        }

        public bool ValidateTitle(WordPressDto obj)
        {
            return !string.IsNullOrWhiteSpace(obj.Title) && obj.Title.Length <= TitleMaxLength;
        }

        public bool ValidateStatus(WordPressDto obj)
        {
            return obj.Status is not null && KnownStatuses.Contains(obj.Status);
        }

        // Only published items owe us a date - a draft legitimately has none.
        // fable5: --st* 只有已发布（published）的 items 才必须有日期 —— draft 没有日期是合法的。 *en--
        public bool ValidatePublishDate(WordPressDto obj)
        {
            if (!string.Equals(obj.Status, PublishedStatus, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(obj.PostDateGmt) && obj.PostDateGmt != NoDate;
        }
    }
}
