using ContentImporter.Application.ContentProviders.ContentSource;
using ContentImporter.Application.ContentProviders.Dtos;
using System.Text.Json;

namespace ContentImporter.Application.ContentProviders.WordPress
{
    internal class WordPressDtoSerializer : IDtoSerializer<WordPressDto>
    {
        public Task<WordPressDto> SerializeAsync(SourceContentItem sourceContentItem)
        {
            // Content is the raw JSON of one export item, and WordPressDto pins its own property
            // names, so this is a straight deserialize with no options.
            // Bad JSON throws - the pipeline isolates that as one failed item.
            WordPressDto dto = JsonSerializer.Deserialize<WordPressDto>(sourceContentItem.Content)
                ?? throw new JsonException($"Item {sourceContentItem.CorrelationId} is JSON null.");

            // Nothing to await: the JSON is already a string in memory, so this is CPU work,
            // not I/O. Task.FromResult satisfies the async seam without an async state machine.
            return Task.FromResult(dto);
        }
    }
}
