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
        //
        // fable5: --st*
        // .NET 中的 Channel<T> 是一个线程安全的异步队列（asynchronous queue），用于在以下两者之间传递数据：
        // Producer：产生或接收工作。
        // Consumer：读取并处理这些工作。
        //
        // 它实现的是 producer–consumer pattern。
        // Microsoft 把 channel 描述为“用于在 producer 和 consumer 之间异步传递数据的同步结构
        // （synchronization structure）”，通常按 FIFO 顺序。
        // *en--
        private readonly Channel<SourceContentItem> _channel = Channel.CreateBounded<SourceContentItem>(


            // Capacity of the bounded channel between the reader and the workers.
            // This is the pipeline's memory ceiling: at most this many items are buffered,
            // regardless of how large the source export is (backpressure).
            //
            // fable5: --st*
            // reader 与 workers 之间这个 bounded channel 的容量（capacity）。
            // 这就是 pipeline 的内存上限（memory ceiling）：无论 source export 有多大，
            // 缓冲的 items 最多只有这么多（backpressure，背压）。
            // *en--
            new BoundedChannelOptions(ImportPipelineOptions.Default.ChannelCapacity)
            {
                // the producer is the reader and merely read the source content and write to the channel,
                // no business logic is performed here, unlikely to be a bottleneck.

                // therefore, to make it simple to have a single reader for now.
                //
                // fable5: --st*
                // producer 就是读取方，它只负责把 source content 读出来写进 channel，
                // 这里不执行任何业务逻辑，不太可能成为瓶颈（bottleneck）。
                //
                // 因此为了简单起见，目前只用单个写入方（single writer）。
                // *en--
                SingleWriter = true,

                // if the max degree of parallelism is 1, we can have a single reader.
                // fable5: --st* 如果 max degree of parallelism 是 1，就可以用单个 reader（SingleReader）。 *en--
                SingleReader = ImportPipelineOptions.Default.MaxDegreeOfParallelism == 1
            });

        public ChannelReader<SourceContentItem> Reader => _channel.Reader;

        public ChannelWriter<SourceContentItem> Writer => _channel.Writer;
    }
}
