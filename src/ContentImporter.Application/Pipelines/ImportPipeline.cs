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
    //
    // fable5: --st*
    // 只接收两个值得替换（swap）的 collaborator。channel 不在其中：它是属于单次 run 的 state，
    // 所以在 RunAsync 内部创建，而不是通过注入（inject）传进来。
    // *en--
    public class ImportPipeline(
        IContentRepository repository,
        IUpstreamNotifier notifier)
    {
        public async Task<ImportResult> RunAsync(IContentSource source, ILogger logger, CancellationToken cancellationToken = default)
        {
            // merely for logging and tracing purposes, we generate a unique event id for each import operation.
            // fable5: --st* 仅用于 logging 和 tracing：为每一次 import 操作生成一个唯一的 event id。 *en--
            var eventId = Guid.NewGuid().ToString();

            try
            {
                ArgumentNullException.ThrowIfNull(source);

                var stopwatch = Stopwatch.StartNew();

                //--- Content item processing -------------------------------------------------------

                logger.LogInformation("Import {EventId} started.", eventId);

                var counters = new ImportCounters();

                // to pass errors as reference between producer and consumers, we use a concurrent bag.
                // fable5: --st* 为了让 producer 和 consumers 以引用方式共享 errors，这里用一个 ConcurrentBag。 *en--
                var errors = new ConcurrentBag<ImportError>();

                var persistedContentItems = new ConcurrentBag<ContentItem>();

                // Per run, by construction. Completing a Channel<T>'s writer is terminal, so a
                // channel shared between runs would leave the second one reading a closed channel
                // and importing nothing. Building it here means that lifetime cannot be got wrong.
                //
                // fable5: --st*
                // 按构造方式即保证是 per-run 的。complete Channel<T> 的 writer 是终结性操作，
                // 所以在多个 run 之间共享 channel 会让第二次 run 读到一个已关闭的 channel，
                // 结果什么都没导入。在这里创建它，意味着这个 lifetime 不可能被弄错。
                // *en--
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
                //
                // fable5: --st*
                // 为每个 consumer 开一个 async Task，所有 consumer 一起消费（drain）同一个 channel。
                // 这只是为了 demo 演示：
                // 如果 source 来自单个 Kafka partition，或任何保证顺序的来源，千万不要这样开多个 async Task。
                // 一旦某个 task 里出错且无法 replay，offset 的跟踪就会被破坏。
                //
                // 如果用 Kafka，应该依靠不同的 partition 来做并行/负载均衡（load balance）。
                //
                // 我假设 items 之间相互独立，所以顺序不会被保留，也不需要保留 —— 只有一种情况值得注意：
                // 如果某个 page 的 layout 引用了 child component，就需要先导入它的 children。
                // 这里没有任何机制强制这一点，这也是为什么 repository 按 id upsert，
                // 而不对 items 到达的顺序做任何假设。
                // *en--
                var consumers = new Task[ImportPipelineOptions.Default.MaxDegreeOfParallelism];
                for (var i = 0; i < consumers.Length; i++)
                {
                    var consumer = new ChannelConsumer();
                    consumers[i] = consumer.ConsumeAsync(eventId, channel.Reader, counters, errors, persistedContentItems, repository, cancellationToken);

                }

                // Finishes when the producer has read the whole source and every consumer has
                // drained what it wrote. Completing the writer is what lets the consumers stop.
                //
                // fable5: --st*
                // 当 producer 读完整个 source、且每个 consumer 都把 producer 写入的内容消费完时结束。
                // 正是 complete writer 这个动作，让 consumers 得以停下来。
                // *en--
                await Task.WhenAll(consumers.Append(producer)).ConfigureAwait(false);

                //--- Publishing --------------------------------------------------------------------

                // A separate pass, not part of the consumer loop above: storing content and
                // publishing it are different steps, and upstream is only told once an item is
                // published. The cost is holding every item until the channel drains, which a
                // fully streaming pipeline would avoid by publishing inline. It also shows a
                // second concurrency shape:
                // Parallel.ForEachAsync over a fixed collection, with a ConcurrentDictionary
                // de-duplicating and an event raised from many threads at once.
                //
                // fable5: --st*
                // 这是独立的一遍（pass），不放在上面的 consumer loop 里：存储 content 和发布（publish）content
                // 是两个不同的步骤，而且只有当 item 发布之后才通知 upstream。代价是必须把所有 item
                // 一直持有到 channel 排空为止 —— 完全 streaming 的 pipeline 会通过内联发布来避免这一点。
                // 它同时也展示了第二种并发形态（concurrency shape）：
                // 对固定集合做 Parallel.ForEachAsync，用 ConcurrentDictionary 去重（de-duplicate），
                // 并从多个线程同时触发（raise）一个 event。
                // *en--
                var publisher = new ContentPublisher(ImportPipelineOptions.Default.MaxDegreeOfParallelism, notifier, logger);

                // ConcurrentQueue because this handler runs on every worker thread at once.
                // fable5: --st* 用 ConcurrentQueue，因为这个 handler 会在所有 worker thread 上同时运行。 *en--
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
                //
                // fable5: --st*
                // Failed 统计的是 item 数，不是 error 数。当 source 本身出问题时两者会不一样：
                // 那是一个不属于任何 item 的 error，绝不能把它读成“一个失败的 item”。
                // *en--
                var result = new ImportResult(
                    counters.Imported,
                    counters.Failed,
                    errors.ToArray(),
                    stopwatch.Elapsed);

                // The run-level event, sent once whatever happened - including when items failed.
                // fable5: --st* run 级别的 event：无论发生了什么都只发送一次 —— 包括有 items 失败的情况。 *en--
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
                //
                // fable5: --st*
                // 用 message template 加参数，而不是 interpolated string：interpolated 的写法会把 id
                // 直接烤进 message 文本里，日志检索时就无法把它当作一个 field 来过滤。
                // *en--
                logger.LogError(ex, "Import {EventId} failed.", eventId);
                throw;
            }
        }
    }

    // Shared by every consumer, so these are incremented with Interlocked rather than ++.
    // Public fields rather than properties because Interlocked.Increment needs a ref to a field.
    //
    // fable5: --st*
    // 被所有 consumer 共享，所以这些计数用 Interlocked 递增，而不是 ++。
    // 用 public field 而不是 property，是因为 Interlocked.Increment 需要对 field 的 ref。
    // *en--
    public sealed class ImportCounters
    {
        public int Imported;

        // Items read but not imported: rejected by validation, or thrown out by an error.
        // Counted apart from the error list, which also holds run-level failures such as an
        // unreadable export - those belong to no particular item.
        //
        // fable5: --st*
        // 已读取但未成功导入的 items：被 validation 拒绝，或者因异常被丢弃。
        // 与 error list 分开计数 —— error list 里还包含 run 级失败（例如 export 无法读取），
        // 那些不属于任何具体的 item。
        // *en--
        public int Failed;
    }
}
