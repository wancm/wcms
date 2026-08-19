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
                    // fable5: --st* 一边从 source 读取 items，一边写入 channel。 *en--
                    await writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                throw; // cancellation always propagates
                // fable5: --st* cancellation 永远向上传播 *en--
            }
            catch (Exception ex)
            {
                // A broken source ends the run early but items already queued are
                // still imported; the failure is surfaced in the result.
                //
                // fable5: --st*
                // source 坏掉会让 run 提前结束，但已经进入队列的 items 仍会被导入；
                // 这次失败会体现在最终的 result 里。
                // *en--
                errors.Add(new ImportError(eventId, $"Source content read failed: {ex.Message}"));
            }
            finally
            {
                // The producer has finished writing. No more items will be added to this channel.
                // It does not delete items already inside the channel.
                // Consumers can continue processing all buffered items. Once the channel is empty, the consumer’s reading loop finishes.
                //
                // fable5: --st*
                // producer 已经写完，不会再有新的 items 进入这个 channel。
                // 这不会删除已经在 channel 里的 items。
                // consumers 可以继续处理所有已缓冲（buffered）的 items；channel 一旦为空，
                // consumer 的读取循环就会结束。
                // *en--
                writer.TryComplete();
            }
        }
    }
}
