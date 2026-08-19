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
        // fable5: --st* 用 ConcurrentDictionary 而不是 Dictionary + lock：所有 consumer 会同时写入它。 *en--
        private readonly ConcurrentDictionary<string, ContentItem> _items = new();

        public int Count => _items.Count;

        public Task<bool> UpsertAsync(ContentItem item, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(item);

            cancellationToken.ThrowIfCancellationRequested();

            // TryAdd tells us whether it was new; AddOrUpdate would overwrite without saying.
            // Doing both keeps the upsert honest: re-importing overwrites, and reports it.
            //
            // fable5: --st*
            // TryAdd 能告诉我们它是不是新条目；AddOrUpdate 会直接覆盖却什么都不说。
            // 两步都做才让 upsert 诚实：re-import 会覆盖，并且如实报告。
            // *en--
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
