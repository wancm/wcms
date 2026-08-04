using ContentImporter.Application.ContentProviders.ContentSource;
using ContentImporter.Application.Notifications;
using ContentImporter.Application.Publishers;
using ContentImporter.Application.Repositories;
using ContentImporter.Domain.Entities;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace ContentImporter.Application.Pipelines
{
    public class ImportPipeline(
        ContentImporterChannel channel,
        IContentRepository repository,
        IUpstreamNotifier notifier)
    {
        public async Task<ImportResult> RunAsync(IContentSource source, ILogger logger, CancellationToken cancellationToken = default)
        {
            // merely for logging and tracing purposes, we generate a unique event id for each import operation.
            var eventId = Guid.NewGuid().ToString();

            try
            {
                ArgumentNullException.ThrowIfNull(source);

                var stopwatch = Stopwatch.StartNew();

                //--- Content item processing -------------------------------------------------------

                logger.LogInformation("Import {EventId} started.", eventId);

                var counters = new ImportCounters();

                // to pass errors as reference between producer and consumers, we use a concurrent bag.
                var errors = new ConcurrentBag<ImportError>();

                var persistedContentItems = new ConcurrentBag<ContentItem>();

                var producer = ChannelProducer.ProduceAsync(eventId, source, channel.Writer, errors, cancellationToken);

                // One worker per core, all draining the same channel. Items are independent, so
                // order is not preserved and does not need to be - except in one case worth
                // knowing about: a page whose layout references a child component would need its
                // children imported first. Nothing here enforces that, which is why the repository
                // upserts by id rather than assuming anything about the order items arrive in.
                var consumers = new Task[ImportPipelineOptions.Default.MaxDegreeOfParallelism];
                for (var i = 0; i < consumers.Length; i++)
                {
                    var consumer = new ChannelConsumer();

                    consumers[i] = consumer.ConsumeAsync(eventId, channel.Reader, counters, errors, persistedContentItems, repository, notifier, cancellationToken);
                }

                // Finishes when the producer has read the whole source and every consumer has
                // drained what it wrote. Completing the writer is what lets the consumers stop.
                await Task.WhenAll(consumers.Append(producer)).ConfigureAwait(false);

                //--- Publishing --------------------------------------------------------------------

                // Publishing belongs inside the consumer loop above, and in a real pipeline it
                // would be there. It is a separate pass here to show a second concurrency shape:
                // Parallel.ForEachAsync over a fixed collection, with a ConcurrentDictionary
                // de-duplicating and an event raised from many threads at once.
                var publisher = new ContentPublisher(ImportPipelineOptions.Default.MaxDegreeOfParallelism);

                // ConcurrentQueue because this handler runs on every worker thread at once.
                var auditLog = new ConcurrentQueue<string>();

                publisher.ContentPublished += (_, eventArgs) =>
                    auditLog.Enqueue($"{eventArgs.Content.Id} published on thread {Environment.CurrentManagedThreadId}");

                await publisher.PublishAsync(persistedContentItems, cancellationToken).ConfigureAwait(false);

                stopwatch.Stop();

                foreach (string entry in auditLog)
                {
                    logger.LogDebug("Import {EventId}: {Entry}", eventId, entry);
                }

                foreach (ImportError error in errors)
                {
                    logger.LogError("Import {EventId}: {CorrelationId} - {Message}", eventId, error.CorrelationId, error.Message);
                }

                var result = new ImportResult(
                    counters.Imported,
                    errors.Count,
                    errors.ToArray(),
                    stopwatch.Elapsed);

                // The run-level event, sent once whatever happened - including when items failed.
                await notifier.NotifyAsync(
                    new ImportCompleted
                    {
                        EventId = eventId,
                        Imported = result.Imported,
                        Failed = result.Failed,
                        Duration = result.Duration
                    },
                    cancellationToken).ConfigureAwait(false);

                return result;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"EventId={eventId}: An error occurred during the import pipeline execution.");
                throw;
            }
        }
    }

    public sealed class ImportCounters
    {
        public int Imported;
    }
}
