namespace ContentImporter.Application.Pipelines
{
    internal class ImportPipelineOptions
    {
        private static readonly ImportPipelineOptions _options = new ImportPipelineOptions();

        public static ImportPipelineOptions Default => _options;

        /* Environment.ProcessorCount returns the number of logical processors available to the current .NET process. */

        /// <summary>Number of concurrent import workers (channel consumers).</summary>
        public int MaxDegreeOfParallelism { get; init; } = Environment.ProcessorCount;

        /// <summary>
        /// Capacity of the bounded channel between the reader and the workers.
        /// This is the pipeline's memory ceiling: at most this many items are
        /// buffered, regardless of how large the source export is (backpressure).
        /// </summary>
        public int ChannelCapacity { get; init; } = 256; // default to 256 items for demo purposes, can be tuned for performance
    }
}
