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
    // Takes only the two collaborators worth swapping. The channel is not among them: it is
    // state belonging to a single run, so it is built inside RunAsync rather than injected.
    public class ImportPipeline(
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

                // Per run, by construction. Completing a Channel<T>'s writer is terminal, so a
                // channel shared between runs would leave the second one reading a closed channel
                // and importing nothing. Building it here means that lifetime cannot be got wrong.
                var channel = new ContentImporterChannel();

                var producer = ChannelProducer.ProduceAsync(eventId, source, channel.Writer, errors, cancellationToken);

                // async Task for each consumer, all draining the same channel.
                // This is solely for demo purposes,
                // DO NOT do async Task if the source are from a single Kafka partition, or any other source that guarantees order.
                // It will break the offset tracking if error happens in one of the async task and can't be replay.
                //
                // If Kafka then we shall rely on differnt partition to parallel/load balance the traffic.
                //
                // I assume items are independent,
                // so order is not preserved and does not need to be - except in one case worth
                // knowing about: a page whose layout references a child component would need its
                // children imported first. Nothing here enforces that, which is why the repository
                // upserts by id rather than assuming anything about the order items arrive in.
                var consumers = new Task[ImportPipelineOptions.Default.MaxDegreeOfParallelism];
                for (var i = 0; i < consumers.Length; i++)
                {
                    var consumer = new ChannelConsumer();
                    consumers[i] = consumer.ConsumeAsync(eventId, channel.Reader, counters, errors, persistedContentItems, repository, cancellationToken);

                }

                // Finishes when the producer has read the whole source and every consumer has
                // drained what it wrote. Completing the writer is what lets the consumers stop.
                await Task.WhenAll(consumers.Append(producer)).ConfigureAwait(false);

                //--- Publishing --------------------------------------------------------------------

                // A separate pass, not part of the consumer loop above: storing content and
                // publishing it are different steps, and upstream is only told once an item is
                // published. The cost is holding every item until the channel drains, which a
                // fully streaming pipeline would avoid by publishing inline. It also shows a
                // second concurrency shape:
                // Parallel.ForEachAsync over a fixed collection, with a ConcurrentDictionary
                // de-duplicating and an event raised from many threads at once.
                var publisher = new ContentPublisher(ImportPipelineOptions.Default.MaxDegreeOfParallelism, notifier, logger);

                // ConcurrentQueue because this handler runs on every worker thread at once.
                var auditLog = new ConcurrentQueue<string>();

                publisher.ContentPublished += (_, eventArgs) =>
                    auditLog.Enqueue($"{eventArgs.Content.Id} published on thread {Environment.CurrentManagedThreadId}");

                await publisher.PublishAsync(eventId, persistedContentItems, cancellationToken).ConfigureAwait(false);

                stopwatch.Stop();

                foreach (string entry in auditLog)
                {
                    logger.LogDebug("Import {EventId}: {Entry}", eventId, entry);
                }

                foreach (ImportError error in errors)
                {
                    logger.LogError("Import {EventId}: {CorrelationId} - {Message}", eventId, error.CorrelationId, error.Message);
                }

                // Failed counts items, not errors. The two differ when the source itself breaks:
                // that is one error belonging to no item, and it must not read as one failed item.
                var result = new ImportResult(
                    counters.Imported,
                    counters.Failed,
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
                // Template plus argument, not an interpolated string: the interpolated form bakes
                // the id into the message text, so a log search cannot filter on it as a field.
                logger.LogError(ex, "Import {EventId} failed.", eventId);
                throw;
            }
        }
    }

    // Shared by every consumer, so these are incremented with Interlocked rather than ++.
    // Public fields rather than properties because Interlocked.Increment needs a ref to a field.
    public sealed class ImportCounters
    {
        public int Imported;

        // Items read but not imported: rejected by validation, or thrown out by an error.
        // Counted apart from the error list, which also holds run-level failures such as an
        // unreadable export - those belong to no particular item.
        public int Failed;
    }
}
