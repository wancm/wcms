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

        /// <summary>Our import contract's limit. WordPress itself stores post_title as TEXT.</summary>
        internal const int TitleMaxLength = 255;

        private const string PublishedStatus = "publish";

        // WordPress writes this literal instead of omitting the date. It parses as nothing.
        private const string NoDate = "0000-00-00 00:00:00";

        // Every status core WordPress can export. "inherit" is what attachments carry.
        private static readonly HashSet<string> KnownStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            PublishedStatus, "draft", "pending", "private", "future", "trash", "inherit"
        };

        public Task<bool> ValidateAsync(WordPressDto obj)
        {
            var isValidPostId = ValidatePostId(obj);
            var isValidTitle = ValidateTitle(obj);
            var isValidStatus = ValidateStatus(obj);
            var isValidPublishDate = ValidatePublishDate(obj);

            // Deliberately no short-circuit: every rule runs even once one has failed, so this
            // can return the list of reasons later instead of a bare bool.
            // Task.FromResult, not async: these are pure predicates over an in-memory DTO, so
            // there is nothing to await and no state machine worth paying for.
            return Task.FromResult(
                isValidPostId && isValidTitle && isValidStatus && isValidPublishDate);
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
