namespace ContentImporter.Application.Notifications
{
    /// <summary>
    /// Tells upstream systems that content has arrived. Declared here, implemented in
    /// Infrastructure - a console writer today, a Service Bus or Kafka publisher later.
    /// </summary>
    /// <remarks>
    /// Async and cancellable because the implementation this stands in for talks to a network.
    /// The console adapter has nothing to await, which is exactly the point of the abstraction:
    /// the pipeline is written against the demanding case, so swapping in a real broker changes
    /// no calling code.
    /// <para>
    /// Implementations are called from every consumer at once and must be thread-safe.
    /// </para>
    /// </remarks>
    public interface IUpstreamNotifier
    {
        Task NotifyAsync(ImportEvent importEvent, CancellationToken cancellationToken = default);
    }
}
