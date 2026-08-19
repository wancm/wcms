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
            //
            // fable5: --st*
            // Content 是单个 export item 的 raw JSON，而 WordPressDto 自己固定（pin）了
            // property 名称，所以这里就是一次不带任何 options 的直接 deserialize。
            // 坏的 JSON 会抛异常 —— pipeline 会把它隔离为一个失败的 item。
            // *en--
            WordPressDto dto = JsonSerializer.Deserialize<WordPressDto>(sourceContentItem.Content)
                ?? throw new JsonException($"Item {sourceContentItem.CorrelationId} is JSON null.");

            // Nothing to await: the JSON is already a string in memory, so this is CPU work,
            // not I/O. Task.FromResult satisfies the async seam without an async state machine.
            //
            // fable5: --st*
            // 没有东西可 await：JSON 已经是内存里的字符串，所以这是 CPU 工作，不是 I/O。
            // Task.FromResult 既满足了 async 接缝（seam），又免去了 async state machine 的开销。
            // *en--
            return Task.FromResult(dto);
        }
    }
}
