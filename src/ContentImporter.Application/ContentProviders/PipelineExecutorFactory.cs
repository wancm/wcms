using ContentImporter.Application.ContentProviders.WordPress;

namespace ContentImporter.Application.ContentProviders
{
    /// <summary>
    /// Picks the pipeline executor for whichever source provider an item came from.
    /// </summary>
    public sealed class PipelineExecutorFactory
    {
        // A new instance every call, on purpose. Executors hold the current item's DTO in a
        // field across their three calls, so handing the same one to parallel consumers would
        // let them overwrite each other's item. Adding a provider is one more arm here.
        //
        // fable5: --st*
        // 每次调用都创建新实例，这是刻意的。executor 会在三次调用之间把当前 item 的 DTO
        // 保存在 field 里，所以如果把同一个实例交给并行的 consumers，它们就会互相覆盖对方的 item。
        // 新增一个 provider，只需要在这里多加一个分支（switch arm）。
        // *en--
        public IPipelineExecutor Create(string providerCode) => providerCode.ToUpperInvariant() switch
        {
            "WORDPRESS" => new WordPressPipelineExecutor(),
            _ => throw new NotSupportedException($"No pipeline executor for provider '{providerCode}'.")
        };
    }
}
