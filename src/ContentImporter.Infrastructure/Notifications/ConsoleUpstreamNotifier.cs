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

        // Bright ANSI (9x), not the standard set - standard blue on black is unreadable. The
        // same palette and the same clock as the operator log, so the two read as one stream.
        private const string Reset = "\u001b[0m";

        private const string Timestamp = "\u001b[90m";

        private const string PerItemHeading = "\u001b[1;92m";

        private const string RunHeading = "\u001b[1;95m";

        private const string Subject = "\u001b[1;97m";

        private const string Body = "\u001b[96m";

        // No escapes at all when output is redirected or NO_COLOR is set - https://no-color.org.
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
            text.Append(Colour(Timestamp)).Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append(Colour(Reset));

            text.Append("  ").Append(Colour(headingColour)).Append(heading).Append(Colour(Reset));

            if (subject.Length > 0)
            {
                text.Append("  ").Append(Colour(Subject)).Append(subject).Append(Colour(Reset));
            }

            text.AppendLine();

            text.Append(Colour(Body));

            // Indent the message so the header line stands out when several interleave.
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
