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

        public Task<ValidationOutcome> ValidateAsync(WordPressDto obj)
        {
            // Deliberately no short-circuit: every rule runs even once one has failed, so this
            // returns the full list of reasons rather than only the first.
            // Task.FromResult, not async: these are pure predicates over an in-memory DTO, so
            // there is nothing to await and no state machine worth paying for.
            var failures = new List<string>();

            // Phrased for whoever reads the import report, naming the source field rather than
            // the rule - "post_id" is what they will search the export for.
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
