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
        //
        // fable5: --st*
        // 本次 run 中已发布的 content，以 id 为 key。用 ConcurrentDictionary，
        // 因为所有 worker 会同时写入它。
        // *en--
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
            //
            // fable5: --st*
            // MaxDegreeOfParallelism 限制的是同时进行中的迭代（iteration）数量上限 —— 设为 4
            // 意味着最多有 4 个 items 在并发发布。它是一个上限（ceiling），不是线程数：
            // 实际用多少线程由 runtime 决定，而且并不会预留（reserve）这些线程。
            // *en--
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = _maxParallelism,
                CancellationToken = cancellationToken
            };

            /*             
             * A maximum of 4 loop iterations may be in progress concurrently.
             * It does not guarantee exactly 4 threads or limit the entire application to 4 threads.
             */
            //
            // fable5: --st*
            // 同时进行中的循环迭代最多 4 个。
            // 它并不保证恰好使用 4 个线程，也不会把整个应用限制在 4 个线程以内。
            // *en--
            await Parallel.ForEachAsync(
                contents,
                options,
                async (content, token) =>
                {
                    // Logged, not notified. An operator wants to see work start; an upstream
                    // system does not - it is told when content is available, and "starting" is
                    // not that. Same event, two audiences, two channels.
                    //
                    // fable5: --st*
                    // 只记日志，不发通知。operator 想看到工作开始了；upstream 系统不需要 ——
                    // 它只在 content 可用（available）时才被告知，而“开始发布”并不是“可用”。
                    // 同一件事，两种受众，两条通道。
                    // *en--
                    _logger.LogInformation("PUBLISHING  {Id}", content.Id);

                    await PublishContentAsync(content, token).ConfigureAwait(false);

                    // TryAdd is the de-duplication. The same content can reach here twice - once
                    // per export that carried it - and upstream should hear about it once, not
                    // once per source row.
                    //
                    // fable5: --st*
                    // TryAdd 就是去重（de-duplication）机制。同一个 content 可能到达这里两次 ——
                    // 每份携带它的 export 各一次 —— 而 upstream 只应该听到一次，
                    // 而不是每个 source row 一次。
                    // *en--
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
                    //
                    // fable5: --st*
                    // 在这里发通知，而且刻意不放在上面那个 event 里。EventHandler 的返回值是 void，
                    // 所以在 handler 里 await 是不可能的：那样调用就成了 fire-and-forget，
                    // 它的 exception 无人观测（unobserved），而且 run 可能在通知发完之前就结束了。
                    // 在 Parallel.ForEachAsync 内部，这个 await 是真实生效的。
                    //
                    // 先发布、再通知，绝不颠倒 —— upstream 系统收到通知后去找这份 content，
                    // 必须能找得到。
                    // *en--
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
            //
            // fable5: --st*
            // 用来顶替真实发布所要付出的数据库或 API 调用成本。这个随机延迟同时也让并行变得肉眼可见：
            // 没有它的话，工作完成得太快，屏幕上根本看不到任何两个 items 的重叠。
            // *en--
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
