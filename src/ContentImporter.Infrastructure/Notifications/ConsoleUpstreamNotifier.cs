using ContentImporter.Application.Notifications;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContentImporter.Infrastructure.Notifications
{
    /// <summary>
    /// Demo implementation: prints each notification to the console instead of publishing it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This exists so the import can be watched end to end without a broker running. The JSON it
    /// prints is deliberately the whole event and nothing else - what you see on screen is the
    /// message body that would go on the wire, so the demo and the real thing differ only in
    /// transport.
    /// </para>
    /// <para>
    /// A production adapter publishes to Kafka, Azure Service Bus or similar. Because that is what
    /// <see cref="IUpstreamNotifier"/> was designed around, swapping it changes one registration
    /// in the composition root and no calling code: the pipeline already awaits the call, already
    /// passes a CancellationToken, and already treats notification as something that can be slow.
    /// See the sketch below for the shape a Kafka adapter takes.
    /// </para>
    /// </remarks>
    public sealed class ConsoleUpstreamNotifier : IUpstreamNotifier
    {
        /*
         * What the real thing looks like - a Kafka adapter, using Confluent.Kafka.
         * Left as a sketch rather than an implementation: no broker is running, and the package
         * is not referenced.
         *
         *     private readonly IProducer<string, string> _producer;
         *
         *     public KafkaUpstreamNotifier(ProducerConfig config)
         *     {
         *         // Built ONCE and reused. A producer owns TCP connections to the brokers and
         *         // batches records in the background - building one per message would give up
         *         // the batching and open a connection storm under load.
         *         _producer = new ProducerBuilder<string, string>(config).Build();
         *     }
         *
         *     public async Task NotifyAsync(ImportEvent importEvent, CancellationToken cancellationToken)
         *     {
         *         await _producer.ProduceAsync(
         *             topic: "wcms.content.imported",
         *             new Message<string, string>
         *             {
         *                 // The partition key, and the one decision that matters most here.
         *                 // Kafka guarantees order within a partition, not across a topic. Keying
         *                 // on the content id puts every event about one piece of content on the
         *                 // same partition, so an update can never overtake the create that
         *                 // preceded it. Key on something else - or leave it null for round-robin
         *                 // - and consumers see those two events out of order at random.
         *                 Key = (importEvent as ContentImported)?.Id ?? importEvent.EventId,
         *
         *                 Value = JsonSerializer.Serialize(importEvent, importEvent.GetType()),
         *
         *                 // The run id travels as a header so a consumer can correlate a whole
         *                 // import without parsing the body.
         *                 Headers = new Headers
         *                 {
         *                     { "event-id", Encoding.UTF8.GetBytes(importEvent.EventId) }
         *                 }
         *             },
         *             cancellationToken).ConfigureAwait(false);
         *     }
         *
         * Producer config worth setting, and why:
         *
         *     Acks = Acks.All              a write is acknowledged only once every in-sync replica
         *                                  has it, so a broker failing does not lose the event
         *     EnableIdempotence = true     the producer de-duplicates its own retries; without it
         *                                  a retried send after a timeout publishes twice
         *     MessageSendMaxRetries = 3    transient broker errors are normal, not exceptional
         *
         * Two things this demo does not solve, and would have to be faced for real:
         *
         *   Delivery is at-least-once, never exactly-once. Even with idempotence the consumer can
         *   see a duplicate after a rebalance, so consumers must be idempotent themselves - which
         *   is why the events carry a stable content id rather than a per-send GUID.
         *
         *   The repository write and this publish are not one transaction. If the process dies
         *   between them, the content is stored and nothing upstream ever hears about it. The
         *   usual fix is a transactional outbox: write the event into the same database, in the
         *   same transaction as the content, and let a separate relay publish it.
         */
        //
        // fable5: --st*
        // 真实实现的样子 —— 一个基于 Confluent.Kafka 的 Kafka adapter。
        // 只留作草图（sketch）而不是实现：没有 broker 在运行，也没有引用那个 package。
        //
        // 草图里各处注释的翻译：
        //
        // （constructor 内）producer 只构建一次并复用。producer 持有到 brokers 的 TCP 连接，
        // 并在后台对 records 做批处理（batching）—— 如果每条消息都新建一个 producer，
        // 就会失去 batching，并在高负载下引发连接风暴（connection storm）。
        //
        // （Key 处）partition key，是这里最关键的一个决定。Kafka 只保证 partition 内有序，
        // 不保证整个 topic 有序。用 content id 作为 key，可以让关于同一份 content 的所有 event
        // 落在同一个 partition，这样 update 永远不可能超越（overtake）先于它的 create。
        // 如果用别的做 key —— 或者留空让它 round-robin —— consumers 就会随机地看到这两个 event 乱序。
        //
        // （Headers 处）run id 作为 header 传递，这样 consumer 不用解析 body 就能把整个 import 关联起来。
        //
        // 值得设置的 producer config 及原因：
        //
        //     Acks = Acks.All              只有当所有 in-sync replica 都拿到数据后写入才被确认，
        //                                  因此某个 broker 挂掉不会丢失 event
        //     EnableIdempotence = true     producer 会对自己的重试去重；不开的话，
        //                                  超时后重发会导致重复发布
        //     MessageSendMaxRetries = 3    broker 的瞬态（transient）错误是常态，不是异常
        //
        // 这个 demo 没有解决、真实系统必须面对的两件事：
        //
        //   投递语义是 at-least-once，永远不是 exactly-once。即使开了 idempotence，
        //   consumer 在 rebalance 之后仍可能看到重复 —— 所以 consumers 自身必须幂等，
        //   这也是 events 携带稳定的 content id 而不是每次发送新 GUID 的原因。
        //
        //   repository 写入和这次 publish 不在同一个事务里。如果进程在两者之间挂掉，
        //   content 已经存了，但 upstream 永远不会听说。常见解法是 transactional outbox：
        //   把 event 写进同一个数据库、同一个事务，再由独立的 relay 负责发布。
        // *en--

        // Bright ANSI (9x), not the standard set - standard blue on black is unreadable. The
        // same palette and the same clock as the operator log, so the two read as one stream.
        //
        // fable5: --st*
        // 用亮色 ANSI（9x 系列）而不是标准色 —— 标准蓝色在黑色背景上根本看不清。
        // 与 operator log 共用同一套配色（palette）和同一个时钟，两路输出读起来就像同一条 stream。
        // *en--
        private const string Reset = "\u001b[0m";

        private const string Timestamp = "\u001b[90m";

        private const string PerItemHeading = "\u001b[1;92m";

        private const string RunHeading = "\u001b[1;95m";

        private const string Subject = "\u001b[1;97m";

        private const string Body = "\u001b[96m";

        // No escapes at all when output is redirected or NO_COLOR is set - https://no-color.org.
        // fable5: --st* 当输出被重定向（redirect）或设置了 NO_COLOR 时，完全不输出任何 escape —— 见 https://no-color.org。 *en--
        private static readonly bool Colourise =
            !Console.IsOutputRedirected &&
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));

        private static string Colour(string code) => Colourise ? code : string.Empty;

        private static readonly JsonSerializerOptions Format = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        // Wall clock, not elapsed-since-construction, so these lines interleave meaningfully with
        // the operator log. Two output streams sharing one clock is what lets a reader pair a
        // PUBLISHING line with the CONTENT IMPORTED that answers it.
        //
        // fable5: --st*
        // 用墙上时钟（wall clock），而不是“从构造起经过的时间”，这样这些输出行才能与 operator log
        // 有意义地交错。两路输出流共用一个时钟，读者才能把一行 PUBLISHING
        // 和回应它的那行 CONTENT IMPORTED 配成一对。
        // *en--

        /// <summary>
        /// Writes the event to the console. Returns a completed task: printing is synchronous, and
        /// there is nothing here to await. The signature is async anyway because the broker
        /// implementation this stands in for genuinely is.
        /// </summary>
        public Task NotifyAsync(ImportEvent importEvent, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(importEvent);

            Write(importEvent switch
            {
                ContentImported imported => Block(PerItemHeading, "CONTENT IMPORTED", imported.Id, imported),
                ImportCompleted completed => Block(RunHeading, "IMPORT COMPLETED", string.Empty, completed),
                _ => Block(PerItemHeading, importEvent.GetType().Name, string.Empty, importEvent)
            });

            return Task.CompletedTask;
        }

        private string Block(string headingColour, string heading, string subject, ImportEvent importEvent)
        {
            var text = new StringBuilder();

            // Colour with ANSI escapes rather than Console.ForegroundColor. Setting the colour,
            // writing and resetting is three operations, and consumers publish concurrently -
            // one thread's colour would bleed into another's line. Escape codes travel inside
            // the string, so the single Write below stays atomic.
            //
            // fable5: --st*
            // 用 ANSI escape 上色，而不是 Console.ForegroundColor。设颜色、写、重置颜色是三个操作，
            // 而 consumers 是并发发布的 —— 一个线程的颜色会渗（bleed）进另一个线程的行。
            // escape 码随字符串本身传递，所以下面这次单独的 Write 保持原子性。
            // *en--
            text.Append(Colour(Timestamp)).Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append(Colour(Reset));

            text.Append("  ").Append(Colour(headingColour)).Append(heading).Append(Colour(Reset));

            if (subject.Length > 0)
            {
                text.Append("  ").Append(Colour(Subject)).Append(subject).Append(Colour(Reset));
            }

            text.AppendLine();

            text.Append(Colour(Body));

            // Indent the message so the header line stands out when several interleave.
            // fable5: --st* 给 message 加缩进，这样当多个 block 交错时，header 行才显眼。 *en--
            foreach (string line in JsonSerializer.Serialize(importEvent, importEvent.GetType(), Format).Split('\n'))
            {
                text.Append("                              ").AppendLine(line.TrimEnd('\r'));
            }

            text.Append(Colour(Reset));

            return text.ToString();
        }

        /// <summary>
        /// One call, one string. Console.Out is synchronised, so a single Write is atomic - but
        /// several are not, and a multi-line message written line by line would interleave with
        /// another consumer's mid-brace. Composing first is what keeps the output readable while
        /// the work stays genuinely parallel.
        /// </summary>
        private static void Write(string block) => Console.Write(block);
    }
}
