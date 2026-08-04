using ContentImporter.Domain.Entities;
using System.Collections.Concurrent;

namespace ContentImporter.Application.Publishers
{
    public sealed class ContentPublisher
    {
        private readonly int _maxParallelism;

        // Stores successfully imported content.
        private readonly ConcurrentDictionary<string, ContentItem>
            _importedContents = new();

        public ContentPublisher(int maxParallelism)
        {
            _maxParallelism = maxParallelism;
        }

        public event EventHandler<ContentPublishedEventArgs>? ContentPublished;

        public async Task PublishAsync(
            IEnumerable<ContentItem> contents,
            CancellationToken cancellationToken = default)
        {
            /*
             * if _maxParallelism = 4
             * 
             * This configures:
             * At most four content items being processed concurrently.
             * Stop processing when cancellationToken is cancelled.* 
             */
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = _maxParallelism,
                CancellationToken = cancellationToken
            };

            /*             
             * A maximum of 4 loop iterations may be in progress concurrently.
             * It does not guarantee exactly 4 threads or limit the entire application to 4 threads.
             */
            await Parallel.ForEachAsync(
                contents,
                options,
                async (content, token) =>
                {
                    await PublishContentAsync(content, token);

                    // Safe when called concurrently by multiple threads.
                    if (_importedContents.TryAdd(content.Id, content))
                    {
                        OnContentPublished(content);
                    }
                });
        }

        private static async Task PublishContentAsync(
            ContentItem content,
            CancellationToken cancellationToken)
        {
            Console.WriteLine(
                $"Publishing {content.Id} on thread ......" +
                $"{Environment.CurrentManagedThreadId}");

            // Simulate database or API work to publish the content.
            await Task.Delay(
                Random.Shared.Next(100, 500),
                cancellationToken);
        }

        private void OnContentPublished(ContentItem content)
        {
            ContentPublished?.Invoke(
                this,
                new ContentPublishedEventArgs(content));
        }
    }

    public sealed class ContentPublishedEventArgs : EventArgs
    {
        public ContentPublishedEventArgs(ContentItem content)
        {
            Content = content;
        }

        public ContentItem Content { get; }
    }
}
