using ContentImporter.Application.Repositories;
using ContentImporter.Domain.Entities;
using System.Collections.Concurrent;

namespace ContentImporter.Infrastructure.Repositories
{
    /// <summary>
    /// Stands in for the database. A real one would swap in here and nothing else would change.
    /// </summary>
    public sealed class InMemoryContentRepository : IContentRepository
    {
        // ConcurrentDictionary, not Dictionary + lock: every consumer writes to this at once.
        private readonly ConcurrentDictionary<string, ContentItem> _items = new();

        public int Count => _items.Count;

        public Task<bool> UpsertAsync(ContentItem item, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(item);

            cancellationToken.ThrowIfCancellationRequested();

            // TryAdd tells us whether it was new; AddOrUpdate would overwrite without saying.
            // Doing both keeps the upsert honest: re-importing overwrites, and reports it.
            var isNew = _items.TryAdd(item.Id, item);

            if (!isNew)
            {
                _items[item.Id] = item;
            }

            return Task.FromResult(isNew);
        }

        /// <summary>Everything stored, for the demo to print at the end.</summary>
        public IReadOnlyCollection<ContentItem> All() => _items.Values.ToArray();
    }
}
