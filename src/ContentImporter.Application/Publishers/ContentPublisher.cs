using ContentImporter.Application.Notifications;
using ContentImporter.Domain.Entities;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace ContentImporter.Application.Publishers
{
    /// <summary>
    /// Publishes stored content - the step that makes it available - and tells upstream systems
    /// once it is.
    /// </summary>
    public sealed class ContentPublisher
    {
        private readonly int _maxParallelism;

        private readonly IUpstreamNotifier _notifier;

        // Content already published in this run, keyed by id. ConcurrentDictionary because every
        // worker writes to it at once.
        private readonly ConcurrentDictionary<string, ContentItem> _importedContents = new();

        private readonly ILogger _logger;

        public ContentPublisher(int maxParallelism, IUpstreamNotifier notifier, ILogger logger)
        {
            _maxParallelism = maxParallelism;
            _notifier = notifier;
            _logger = logger;
        }

        public event EventHandler<ContentPublishedEventArgs>? ContentPublished;

        public async Task PublishAsync(
            string eventId,
            IEnumerable<ContentItem> contents,
            CancellationToken cancellationToken = default)
        {
            /*
             * MaxDegreeOfParallelism caps how many iterations may be in progress at once - four
             * means at most four items being published concurrently. It is a ceiling, not a
             * thread count: the runtime decides how many threads that actually takes, and does
             * not reserve them.
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
                    // Logged, not notified. An operator wants to see work start; an upstream
                    // system does not - it is told when content is available, and "starting" is
                    // not that. Same event, two audiences, two channels.
                    _logger.LogInformation("PUBLISHING  {Id}", content.Id);

                    await PublishContentAsync(content, token).ConfigureAwait(false);

                    // TryAdd is the de-duplication. The same content can reach here twice - once
                    // per export that carried it - and upstream should hear about it once, not
                    // once per source row.
                    if (!_importedContents.TryAdd(content.Id, content))
                    {
                        return;
                    }

                    OnContentPublished(content);

                    // Notified here, and deliberately not from the event above. An EventHandler
                    // returns void, so awaiting inside one is impossible: the call would be
                    // fire-and-forget, its exceptions unobserved, and the run could finish before
                    // the notification did. Inside Parallel.ForEachAsync the await is real.
                    //
                    // After publishing, never before - an upstream system acting on this must be
                    // able to find the content it was told about.
                    await _notifier
                        .NotifyAsync(ContentImported.From(content, eventId), token)
                        .ConfigureAwait(false);
                }).ConfigureAwait(false);
        }

        private static async Task PublishContentAsync(
            ContentItem content,
            CancellationToken cancellationToken)
        {
            // Stands in for the database or API call that publishing really costs. The variable
            // delay is also what makes the parallelism visible: without it the work finishes too
            // fast for any two items to overlap on screen.
            await Task.Delay(Random.Shared.Next(100, 500), cancellationToken).ConfigureAwait(false);
        }

        private void OnContentPublished(ContentItem content)
        {
            ContentPublished?.Invoke(this, new ContentPublishedEventArgs(content));
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
