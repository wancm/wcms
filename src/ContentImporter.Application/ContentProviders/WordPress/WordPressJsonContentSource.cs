using ContentImporter.Application.ContentProviders.ContentSource;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ContentImporter.Application.ContentProviders.WordPress
{
    public class WordPressJsonContentSource : IContentSource
    {
        // The folder is the contract: a file dropped in /wordpress came from WordPress.
        // In a real ingest each provider owns its own landing folder.
        //
        // fable5: --st*
        // folder 本身就是契约（contract）：放进 /wordpress 的文件就是来自 WordPress 的。
        // 在真实的 ingest 中，每个 provider 拥有自己独立的落地目录（landing folder）。
        // *en--
        private const string ProviderCode = "WordPress";

        private static readonly string DefaultExportFolder =
            Path.Combine(AppContext.BaseDirectory, "data", "wordpress");

        private readonly string _exportFolder;

        /// <param name="exportFolder">
        /// Defaults to the WordPress landing folder beneath the running binary. Injectable so
        /// tests can point at a temp folder instead of the one the demo reads.
        /// </param>
        public WordPressJsonContentSource(string? exportFolder = null)
        {
            _exportFolder = exportFolder ?? DefaultExportFolder;
        }

        /// <summary>
        /// Reads @[channel].[items] from every JSON export in the WordPress folder and yields a
        /// SourceContentItem per item.
        /// </summary>
        /// <remarks>
        /// Files are read in name order, and the last one read wins the repository's upsert - so
        /// the file name is the contract for which export supersedes which. Not modification time:
        /// uploaded files are all written at once and would share a timestamp, leaving the order
        /// to chance. Names sort as text, so number them zero-padded (01, 02) - "10" sorts before
        /// "2" otherwise.
        /// </remarks>
        public async IAsyncEnumerable<SourceContentItem> ReadAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(_exportFolder))
            {
                yield break;
            }

            var exports = new DirectoryInfo(_exportFolder)
                .GetFiles("*.json")
                .OrderBy(file => file.Name, StringComparer.Ordinal);

            foreach (FileInfo export in exports)
            {
                await foreach (SourceContentItem item in ReadFileAsync(export, cancellationToken).ConfigureAwait(false))
                {
                    yield return item;
                }
            }
        }

        private static async IAsyncEnumerable<SourceContentItem> ReadFileAsync(
            FileInfo export,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await using FileStream stream = export.OpenRead();

            // Demo shortcut: this loads one whole export into memory. Fine for a sample file,
            // but the real thing would stream with Utf8JsonReader - see the README trade-off.
            //
            // fable5: --st*
            // demo 捷径（shortcut）：这里会把一整份 export 全部加载进内存。对示例文件来说没问题，
            // 但真实系统应该用 Utf8JsonReader 做 streaming —— 权衡（trade-off）见 README。
            // *en--
            using JsonDocument document = await JsonDocument
                .ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            JsonElement items = document.RootElement.GetProperty("channel").GetProperty("items");

            foreach (JsonElement item in items.EnumerateArray())
            {
                cancellationToken.ThrowIfCancellationRequested();

                yield return new SourceContentItem(ProviderCode)
                {
                    // Raw JSON of one item. The provider adapter resolved by ProviderCode
                    // is what later parses this into Fields.
                    //
                    // fable5: --st*
                    // 单个 item 的 raw JSON。之后由 ProviderCode 解析出的 provider adapter
                    // 负责把它 parse 成 Fields。
                    // *en--
                    Content = item.GetRawText(),

                    // Which export this came from, so an error names a file rather than a guess.
                    // fable5: --st* 记录这个 item 来自哪份 export，这样报错时能点名具体文件，而不是靠猜。 *en--
                    Fields = new Dictionary<string, string> { ["sourceFile"] = export.Name }
                };
            }
        }
    }
}
