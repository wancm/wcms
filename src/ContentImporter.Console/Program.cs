using ContentImporter.Application.ContentProviders.WordPress;
using ContentImporter.Application.Notifications;
using ContentImporter.Application.Pipelines;
using ContentImporter.Application.Repositories;
using ContentImporter.Infrastructure.Notifications;
using ContentImporter.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// A demo host for the content import pipeline. It runs one import and exits, which is why it
// builds the host but never calls host.RunAsync() - there is no hosted service and nothing to
// keep alive.
//
// A real ingest would be triggered by an API call or a message broker such as Azure Service Bus
// or Kafka. This demo uses the simplest trigger that shows the same shape: a folder of files.
// Treat it as "an export has just landed in data/wordpress, import it".
//
// One import at a time. The pipeline holds per-run state - a bounded channel, counters, an error
// bag - so overlapping runs would interleave into each other's results. A demo host has no reason
// to allow that: a second import waits for the first to finish.

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// One line per entry, timestamped on the same clock the notifier uses, so the operator log and
// the upstream messages can be read as one interleaved stream. The default two-line format puts
// the category on its own line, which is unreadable next to anything else printing concurrently.
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss.fff  ";
});

// Transient, because the channel is per-run state, not shared infrastructure. Channel<T> is
// single-use: completing the writer is terminal and cannot be undone, so a second import against
// the same instance writes to a closed channel and silently imports nothing. Transient hands each
// import a fresh one, which is what lets this host run more than once.
//
// Transient only works because the channel is resolved once per import, at the point the pipeline
// is built. Inject it into something longer-lived and that instance captures one channel for its
// own lifetime - a captive dependency - and the bug comes straight back.
builder.Services.AddTransient<ContentImporterChannel>();

// Singleton, because the store outlives any single import. That is the whole point: the second
// import of the same export has to find the first one's rows already there, or the upsert has
// nothing to be idempotent about. Transient would hand every worker its own empty database.
//
// This is also the only project allowed to name a concrete Infrastructure type. Swapping
// SqliteContentRepository for InMemoryContentRepository is this one line, and nothing in
// Application or Domain recompiles - which is the whole point of the layering.
builder.Services.AddSingleton<IContentRepository, SqliteContentRepository>();

// Singleton, and stateless besides a stopwatch, so every consumer shares one. This is the port a
// real broker plugs into: replace ConsoleUpstreamNotifier with a Service Bus or Kafka publisher
// and nothing in Application or Domain changes.
builder.Services.AddSingleton<IUpstreamNotifier, ConsoleUpstreamNotifier>();

using IHost host = builder.Build();

ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Import");

// Ctrl+C is a request to stop, not a kill. Setting Cancel = true stops the runtime tearing the
// process down, which is what gives the pipeline a chance to see the token, finish the items
// already in flight and still report its counts. A second Ctrl+C is not intercepted and ends
// the process immediately.
//
// Wired by hand rather than taken from IHostApplicationLifetime, because that token comes from
// ConsoleLifetime - a hosted service, and this app never starts one.
using CancellationTokenSource cancellation = new();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    logger.LogWarning("Cancellation requested — draining in-flight work. Ctrl+C again to abort.");
    cancellation.Cancel();
};

try
{

    var source = new WordPressJsonContentSource();

    var pipeline = new ImportPipeline(
        host.Services.GetRequiredService<ContentImporterChannel>(),
        host.Services.GetRequiredService<IContentRepository>(),
        host.Services.GetRequiredService<IUpstreamNotifier>());

    var result = await pipeline.RunAsync(source, logger, cancellation.Token).ConfigureAwait(false);

    logger.LogInformation("Import completed. Imported {Imported}, Failed {Failed}.", result.Imported, result.Failed);

    cancellation.Token.ThrowIfCancellationRequested();
    return 0;
}
catch (OperationCanceledException)
{
    // Cancellation is not a failure. It is the one exception the pipeline is
    // required to let through untouched, so the only thing left to do is report
    // it honestly and exit with 128 + SIGINT(2) — the shell's convention for
    // "terminated by Ctrl+C", which lets a wrapping script tell a cancelled run
    // apart from a failed one.
    logger.LogWarning("Import cancelled.");
    return 130;
}
catch (Exception exception)
{
    // Last line of defence: anything escaping the pipeline's per-item error
    // isolation is a run-level failure, and the process owes its caller a
    // non-zero exit code rather than a stack trace and a 0.
    logger.LogError(exception, "Import failed.");
    return 1;
}
