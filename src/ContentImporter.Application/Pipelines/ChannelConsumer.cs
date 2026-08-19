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
        //
        // fable5: --st*
        // 用 ANSI escape 而不是 Console.ForegroundColor：颜色随字符串本身一起传递，
        // 这样一次 log 调用仍然是单次 write，不会渗（bleed）到其他 worker 的输出行里。
        // *en--
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
                    //
                    // fable5: --st*
                    // item 自己声明所属的 provider，所以 executor 是按 item 逐个选择的，
                    // 而不是一次性注入。每次都创建新实例 —— 原因见 factory。
                    // *en--
                    var executor = _executorFactory.Create(item.ProviderCode);

                    // #1 raw JSON to DTO
                    // fable5: --st* 第 1 步：raw JSON 转成 DTO *en--
                    await executor.DeserializeDtoAsync(item).ConfigureAwait(false);

                    // #2 reject items the source should never have exported
                    // fable5: --st* 第 2 步：拒绝那些 source 本来就不应该导出的 items *en--
                    ValidationOutcome validation = await executor.ValidateDtoAsync().ConfigureAwait(false);

                    if (!validation.IsValid)
                    {
                        // The reasons travel with the error, so the report says what to fix
                        // rather than only that something was wrong.
                        //
                        // fable5: --st*
                        // 失败原因随 error 一起传递，这样报告能说明“该修什么”，
                        // 而不是只说“出了问题”。
                        // *en--
                        errors.Add(new ImportError(
                            $"eventId:{eventId} correlationId:{item.CorrelationId}",
                            $"Failed validation: {validation}"));

                        Interlocked.Increment(ref counters.Failed);

                        continue;
                    }

                    // #3 DTO to domain entity
                    // fable5: --st* 第 3 步：DTO 映射为 domain entity *en--
                    var contentItem = await executor.DtoMapEntityAsync().ConfigureAwait(false);

                    // #4 persist. Upsert by Id, so re-running the same export overwrites
                    // rather than duplicating.
                    // fable5: --st* 第 4 步：持久化（persist）。按 Id upsert，因此对同一份 export 重复执行是覆盖而不是重复插入。 *en--
                    var isNew = await repository.UpsertAsync(contentItem, cancellationToken).ConfigureAwait(false);

                    // False means the id was already stored, so this export superseded an earlier
                    // one. Worth seeing: it is the idempotency working, and it is also the moment
                    // a later file silently overwrites an earlier file's version of a page.
                    //
                    // fable5: --st*
                    // 返回 false 表示这个 id 已经存在，也就是这份 export 取代（supersede）了更早的一份。
                    // 这值得让人看到：一方面它证明幂等（idempotency）在起作用，另一方面这也正是
                    // “后来的文件悄悄覆盖了先前文件里同一个 page 的版本”发生的时刻。
                    // *en--
                    if (!isNew)
                    {
                        // Written straight to the console rather than through ILogger. The console
                        // logger parses ANSI escapes out of a message and applies them as console
                        // colours, which are a no-op once output is redirected - so a coloured log
                        // line silently loses its colour. One composed string, one write, same as
                        // the notifier: it cannot interleave with another worker's line either.
                        //
                        // fable5: --st*
                        // 直接写 console，而不是走 ILogger。console logger 会把 message 里的 ANSI escape
                        // 解析出来并转换成 console 颜色，而一旦输出被重定向（redirect），这些颜色就变成 no-op ——
                        // 于是带颜色的日志行会悄悄丢掉颜色。这里和 notifier 一样：拼好一个字符串、一次 write，
                        // 也就不可能与其他 worker 的输出行交错。
                        // *en--
                        Console.Write(
                            $"{Red}{DateTime.Now:HH:mm:ss.fff}  UPDATED     {contentItem.Id}" +
                            $" - existing record overwritten{Reset}{Environment.NewLine}");
                    }

                    // ConcurrentBag: every consumer adds to this at once. Publishing happens
                    // later, once the channel is drained.
                    // fable5: --st* ConcurrentBag：所有 consumer 会同时往里加。发布（publish）在稍后进行，等 channel 排空之后。 *en--
                    persistedContentItems.Add(contentItem);

                    Interlocked.Increment(ref counters.Imported);

                    // Upstream is not told here. Storing content is not the same as publishing it,
                    // and the brief says upstream hears when content is *available* - so the
                    // notification belongs after ContentPublisher, not after the upsert.
                    //
                    // fable5: --st*
                    // 这里不通知 upstream。存储 content 不等于发布 content，而题目要求 upstream
                    // 在 content *available*（可用）时才收到消息 —— 所以通知应该放在 ContentPublisher
                    // 之后，而不是 upsert 之后。
                    // *en--
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Per-item isolation: one malformed item is recorded and the worker moves on
                    // to the next. Only cancellation, caught above, stops the run.
                    //
                    // fable5: --st*
                    // per-item 隔离：某一个畸形（malformed）的 item 只会被记录下来，worker 继续处理下一个。
                    // 只有 cancellation（在上面单独 catch）才会终止整个 run。
                    // *en--
                    errors.Add(new ImportError($"eventId:{eventId} correlationId:{item.CorrelationId}", ex.Message));

                    Interlocked.Increment(ref counters.Failed);
                }
            }
        }
    }
}
