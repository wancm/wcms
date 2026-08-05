using ContentImporter.Application.ContentProviders;
using ContentImporter.Application.ContentProviders.ContentSource;
using ContentImporter.Application.Notifications;
using ContentImporter.Application.Repositories;
using ContentImporter.Domain.Entities;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace ContentImporter.Application.Pipelines
{
    public class ChannelConsumer
    {
        // ANSI escapes rather than Console.ForegroundColor: the colour travels inside the string,
        // so one log call stays a single write and cannot bleed into another worker's line.
        private const string Red = "\u001b[91m";

        private const string Reset = "\u001b[0m";

        private readonly PipelineExecutorFactory _executorFactory = new();

        public async Task ConsumeAsync(
            string eventId,
            ChannelReader<SourceContentItem> reader,
            ImportCounters counters,
            ConcurrentBag<ImportError> errors,
            ConcurrentBag<ContentItem> persistedContentItems,
            IContentRepository repository,
            CancellationToken cancellationToken)
        {
            await foreach (var item in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    // The item names its own provider, so the executor is chosen per item rather
                    // than injected once. A fresh instance each time - see the factory for why.
                    var executor = _executorFactory.Create(item.ProviderCode);

                    // #1 raw JSON to DTO
                    await executor.DeserializeDtoAsync(item).ConfigureAwait(false);

                    // #2 reject items the source should never have exported
                    ValidationOutcome validation = await executor.ValidateDtoAsync().ConfigureAwait(false);

                    if (!validation.IsValid)
                    {
                        // The reasons travel with the error, so the report says what to fix
                        // rather than only that something was wrong.
                        errors.Add(new ImportError(
                            $"eventId:{eventId} correlationId:{item.CorrelationId}",
                            $"Failed validation: {validation}"));

                        Interlocked.Increment(ref counters.Failed);

                        continue;
                    }

                    // #3 DTO to domain entity
                    var contentItem = await executor.DtoMapEntityAsync().ConfigureAwait(false);

                    // #4 persist. Upsert by Id, so re-running the same export overwrites
                    // rather than duplicating.
                    var isNew = await repository.UpsertAsync(contentItem, cancellationToken).ConfigureAwait(false);

                    // False means the id was already stored, so this export superseded an earlier
                    // one. Worth seeing: it is the idempotency working, and it is also the moment
                    // a later file silently overwrites an earlier file's version of a page.
                    if (!isNew)
                    {
                        // Written straight to the console rather than through ILogger. The console
                        // logger parses ANSI escapes out of a message and applies them as console
                        // colours, which are a no-op once output is redirected - so a coloured log
                        // line silently loses its colour. One composed string, one write, same as
                        // the notifier: it cannot interleave with another worker's line either.
                        Console.Write(
                            $"{Red}{DateTime.Now:HH:mm:ss.fff}  UPDATED     {contentItem.Id}" +
                            $" - existing record overwritten{Reset}{Environment.NewLine}");
                    }

                    // ConcurrentBag: every consumer adds to this at once. Publishing happens
                    // later, once the channel is drained.
                    persistedContentItems.Add(contentItem);

                    Interlocked.Increment(ref counters.Imported);

                    // Upstream is not told here. Storing content is not the same as publishing it,
                    // and the brief says upstream hears when content is *available* - so the
                    // notification belongs after ContentPublisher, not after the upsert.
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Per-item isolation: one malformed item is recorded and the worker moves on
                    // to the next. Only cancellation, caught above, stops the run.
                    errors.Add(new ImportError($"eventId:{eventId} correlationId:{item.CorrelationId}", ex.Message));

                    Interlocked.Increment(ref counters.Failed);
                }
            }
        }
    }
}
