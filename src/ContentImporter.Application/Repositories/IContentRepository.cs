using ContentImporter.Domain.Entities;

namespace ContentImporter.Application.Repositories
{
    /// <summary>
    /// Where imported content is stored. Declared here, implemented in Infrastructure.
    /// </summary>
    public interface IContentRepository
    {
        /// <summary>
        /// Inserts the item, or overwrites the one already stored under the same
        /// <see cref="ContentItem.Id"/>. Returns true when it was new.
        /// </summary>
        /// <remarks>
        /// Upsert rather than insert is what makes a re-import idempotent: the same export run
        /// twice leaves the store in the same state instead of duplicating every item. The bool
        /// is what lets a caller tell "new content" from "content we already had".
        /// </remarks>
        Task<bool> UpsertAsync(ContentItem item, CancellationToken cancellationToken = default);
    }
}
