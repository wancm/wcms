using ContentImporter.Application.ContentProviders.ContentSource;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace ContentImporter.Application.ContentProviders.WordPress
{
    public class WordPressJsonContentSource : IContentSource
    {
        // The folder is the contract: a file dropped in /wordpress came from WordPress.
        // In a real ingest each provider owns its own landing folder.
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
        /// Files are read oldest first, so when two exports carry the same post the newer one is
        /// written last and wins the repository's upsert. Name breaks ties, because files copied
        /// or unzipped together often share a timestamp.
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
                .OrderBy(file => file.LastWriteTimeUtc)
                .ThenBy(file => file.Name, StringComparer.Ordinal);

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
                    Content = item.GetRawText(),

                    // Which export this came from, so an error names a file rather than a guess.
                    Fields = new Dictionary<string, string> { ["sourceFile"] = export.Name }
                };
            }
        }
    }
}
