using ContentImporter.Application.ContentProviders.WordPress;
using ContentImporter.Application.Pipelines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// This is a one time run demo console app that demonstrates the ContentImporter pipeline.
// It is not a long running service, so it does not call host.RunAsync() and does not use IHostApplicationLifetime.
// Instead, it builds the host, wires up a cancellation token for Ctrl+C, and runs the pipeline once.

// I don't have any contenxt, but I imagine the data ingest usually is API or message brokers like Kafka, Azure Service Bus, etc.
// In this demo, I'll do simple file ingest. I will use a simple in-memory channel to simulate the data ingest by a json file.

// Imagine a content file just being dropped in the wordpress folder now and triggers this pipeline to run.

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<ContentImporterChannel>();

using IHost host = builder.Build();

ILogger logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Import");

// Ctrl+C is a request, not a kill. Cancel = true stops the runtime from tearing
// the process down, which is what gives the pipeline the chance to observe the
// token, let in-flight items finish and still report its counts. A second Ctrl+C
// is not intercepted and ends the process immediately.
//
// Wired by hand rather than taken from IHostApplicationLifetime because that
// token is driven by ConsoleLifetime, a hosted service — and this app never
// calls host.RunAsync(), so no hosted service ever starts.
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

    var pipeline = new ImportPipeline(host.Services.GetRequiredService<ContentImporterChannel>());
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
