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
        public IPipelineExecutor Create(string providerCode) => providerCode.ToUpperInvariant() switch
        {
            "WORDPRESS" => new WordPressPipelineExecutor(),
            _ => throw new NotSupportedException($"No pipeline executor for provider '{providerCode}'.")
        };
    }
}
