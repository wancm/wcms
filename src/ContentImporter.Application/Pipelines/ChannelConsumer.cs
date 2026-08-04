using ContentImporter.Application.ContentProviders;
using ContentImporter.Application.ContentProviders.ContentSource;
using ContentImporter.Domain.Entities;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace ContentImporter.Application.Pipelines
{
    public class ChannelConsumer
    {
        private readonly PipelineExecutorFactory _executorFactory = new();

        public async Task ConsumeAsync(
            string eventId,
            ChannelReader<SourceContentItem> reader,
            ImportCounters counters,
            ConcurrentBag<ImportError> errors,
            ConcurrentBag<ContentItem> persistedContentItems,
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
                    if (!await executor.ValidateDtoAsync().ConfigureAwait(false))
                    {
                        errors.Add(new ImportError(
                            $"eventId:{eventId} correlationId:{item.CorrelationId}",
                            "Failed validation."));

                        continue;
                    }

                    // #3 DTO to domain entity
                    var contentItem = await executor.DtoMapEntityAsync().ConfigureAwait(false);

                    // ConcurrentBag: every consumer adds to this at once. Publishing happens
                    // later, once the channel is drained.
                    persistedContentItems.Add(contentItem);

                    Interlocked.Increment(ref counters.Imported);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    errors.Add(new ImportError($"eventId:{eventId} correlationId:{item.CorrelationId}", ex.Message));
                }
            }
        }
    }
}
