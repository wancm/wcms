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

        private static readonly string ExportPath =
            Path.Combine(AppContext.BaseDirectory, "data", "wordpress", "word-press.json");

        /// <summary>
        /// Reads @[channel].[items] from the WordPress JSON export file and yields SourceContentItem instances for each item in the export.
        /// </summary>
        public async IAsyncEnumerable<SourceContentItem> ReadAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await using FileStream stream = File.OpenRead(ExportPath);

            // Demo shortcut: this loads the whole export into memory. Fine for a sample file,
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
                    Content = item.GetRawText()
                };
            }
        }
    }
}
