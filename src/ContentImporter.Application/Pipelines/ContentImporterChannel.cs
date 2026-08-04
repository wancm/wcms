using ContentImporter.Application.ContentProviders.ContentSource;
using System.Threading.Channels;

namespace ContentImporter.Application.Pipelines
{
    public class ContentImporterChannel
    {
        /*
        * Channel<T> in .NET is a thread-safe asynchronous queue used to pass data between:
        * Producer: creates or receives work.
        * Consumer: reads and processes that work.
        * 
        * It implements the producer–consumer pattern. 
        * Microsoft describes channels as synchronization structures for asynchronously passing data between producers and consumers, 
        * usually in FIFO order
        */
        private readonly Channel<SourceContentItem> _channel = Channel.CreateBounded<SourceContentItem>(


            // Capacity of the bounded channel between the reader and the workers.
            // This is the pipeline's memory ceiling: at most this many items are buffered,
            // regardless of how large the source export is (backpressure).
            new BoundedChannelOptions(ImportPipelineOptions.Default.ChannelCapacity)
            {
                // the producer is the reader and merely read the source content and write to the channel,
                // no business logic is performed here, unlikely to be a bottleneck.

                // therefore, to make it simple to have a single reader for now.                
                SingleWriter = true,

                // if the max degree of parallelism is 1, we can have a single reader.                
                SingleReader = ImportPipelineOptions.Default.MaxDegreeOfParallelism == 1
            });

        public ChannelReader<SourceContentItem> Reader => _channel.Reader;

        public ChannelWriter<SourceContentItem> Writer => _channel.Writer;
    }
}
