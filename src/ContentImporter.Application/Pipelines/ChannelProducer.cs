using ContentImporter.Application.ContentProviders.ContentSource;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ContentImporter.Application.Pipelines
{
    internal static class ChannelProducer
    {
        public static async Task ProduceAsync(
            string eventId,
            IContentSource source,
            ChannelWriter<SourceContentItem> writer,
            ConcurrentBag<ImportError> errors,
            CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var item in source.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    // writes items to the channel as they are read from the source
                    await writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                throw; // cancellation always propagates
            }
            catch (Exception ex)
            {
                // A broken source ends the run early but items already queued are
                // still imported; the failure is surfaced in the result.
                errors.Add(new ImportError(eventId, $"Source content read failed: {ex.Message}"));
            }
            finally
            {
                // The producer has finished writing. No more items will be added to this channel.
                // It does not delete items already inside the channel.
                // Consumers can continue processing all buffered items. Once the channel is empty, the consumer’s reading loop finishes.
                writer.TryComplete();
            }
        }
    }
}
