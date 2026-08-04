using ContentImporter.Application.ContentProviders.ContentSource;
using ContentImporter.Application.Publishers;
using ContentImporter.Application.Repositories;
using ContentImporter.Domain.Entities;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace ContentImporter.Application.Pipelines
{
    public class ImportPipeline(ContentImporterChannel channel, IContentRepository repository)
    {
        public async Task<ImportResult> RunAsync(IContentSource source, ILogger logger, CancellationToken cancellationToken = default)
        {
            // merely for logging and tracing purposes, we generate a unique event id for each import operation.
            var eventId = Guid.NewGuid().ToString();

            try
            {
                ArgumentNullException.ThrowIfNull(source);

                var stopwatch = Stopwatch.StartNew();

                //--- Content Item Processing ----------------------------------------------------------------------------------------------

                Console.WriteLine("Content item processing started.");

                var counters = new ImportCounters();

                // to pass errors as reference between producer and consumers, we use a concurrent bag.
                var errors = new ConcurrentBag<ImportError>();

                var persistedContentItems = new ConcurrentBag<ContentItem>();

                var producer = ChannelProducer.ProduceAsync(eventId, source, channel.Writer, errors, cancellationToken);

                // todo: AI to rewrite better comments.
                // for now, we focus on speed and performance.
                // potential concurrency issues here, one of the biggest issue is parent-child dependencies.
                // example: A full page consits of few child components, if the parent page is processed before the child components,
                // it can lead to broken page upon the page rendering or even errors during the import process.
                var consumers = new Task[ImportPipelineOptions.Default.MaxDegreeOfParallelism];
                for (var i = 0; i < consumers.Length; i++)
                {
                    var consumer = new ChannelConsumer();

                    consumers[i] = consumer.ConsumeAsync(eventId, channel.Reader, counters, errors, persistedContentItems, repository, cancellationToken);
                }

                // we wait for all the consumers and producer to complete.
                // it will ends when the producer has completed and all the consumers have completed processing all the items in the channel.
                // source content is exhausted, and all the items have been processed by the consumers.
                await Task.WhenAll(consumers.Append(producer)).ConfigureAwait(false);

                Console.WriteLine("Content item processing completed.");

                //--- Publishing ----------------------------------------------------------------------------------------------

                // Note:
                // Pushling the content items actually shall be included into the consumer above to run together with the pipeline,
                // but for the sake of demonstrate threadsafe collection, I do it in a separate step.
                Console.WriteLine("Publishing started.");

                // Publish content items
                var publisher = new ContentPublisher(ImportPipelineOptions.Default.MaxDegreeOfParallelism);

                // Thread-safe collection used by the event subscriber.
                var auditLog = new ConcurrentQueue<string>();

                // Subscribe to the ContentPublished event.
                publisher.ContentPublished += (_, eventArgs) =>
                {
                    // This event handler may be called by multiple worker threads.
                    auditLog.Enqueue(
                        $"Imported {eventArgs.Content.Id} " +
                        $"on thread {Environment.CurrentManagedThreadId}" +
                        $"on Event {eventId}");
                };

                // Publish the content items concurrently.
                await publisher.PublishAsync(persistedContentItems);

                Console.WriteLine("Publishing completed.");
                Console.WriteLine();

                stopwatch.Stop();

                if (auditLog.Count > 0)
                {
                    Console.WriteLine("Audit log:");
                    foreach (var logEntry in auditLog)
                    {
                        logger.LogInformation($"EventId={eventId}: {logEntry}");
                        Console.WriteLine(logEntry);
                    }
                }

                if (errors.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("Errors:");
                    foreach (var error in errors)
                    {
                        logger.LogError($"EventId={eventId}: {error.CorrelationId} - {error.Message}");
                        Console.WriteLine(error);
                    }
                }

                var result = new ImportResult(
                    counters.Imported,
                    errors.Count,
                    errors.ToArray(),
                    stopwatch.Elapsed);

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
