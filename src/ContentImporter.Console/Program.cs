using ContentImporter.Application.ContentProviders.WordPress;
using ContentImporter.Application.Notifications;
using ContentImporter.Application.Pipelines;
using ContentImporter.Application.Repositories;
using ContentImporter.Infrastructure.Notifications;
using ContentImporter.Infrastructure.Repositories;
using ContentImporter.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

// A demo host for the content import pipeline. It runs one import and exits, which is why it
// builds the host but never calls host.RunAsync() - there is no hosted service and nothing to
// keep alive.
//
// A real ingest would be triggered by an API call or a message broker such as Azure Service Bus.
//
// For order gurantees message broker such as Kafka is not suitable for this demo,
// because the pipeline runs multiple consumers in parallel that wil breaks the order (offset) of the messages.
//
// This demo uses the simplest trigger that shows the same shape: a folder of files.
// Treat it as "an export has just landed in data/wordpress, import it".
//
// One import at a time. The pipeline holds per-run state - a bounded channel, counters, an error
// bag - so overlapping runs would interleave into each other's results. A demo host has no reason
// to allow that: a second import waits for the first to finish.
//
// fable5: --st*
// 这是 content import pipeline 的 demo host。它执行一次 import 后就退出，所以这里只 build host，
// 从不调用 host.RunAsync() —— 因为没有 hosted service，也没有任何需要保持运行的东西。
//
// 真实的 ingest 会由 API call 或 message broker（例如 Azure Service Bus）来触发。
//
// 如果要保证 message 顺序（order guarantee），Kafka 这类 message broker 并不适合这个 demo，
// 因为 pipeline 会并行运行多个 consumer，这会破坏 message 的顺序（offset）。
//
// 这个 demo 采用能展示相同结构的最简单 trigger：一个存放文件的 folder。
// 可以把它理解为：“一份 export 刚刚落到 data/wordpress，把它 import 进来”。
//
// 同一时间只跑一个 import。pipeline 持有每次 run 独立的 state —— bounded channel、counters、
// error bag —— 如果多个 run 重叠，结果就会互相交错。demo host 没有理由允许这种情况：
// 第二个 import 要等第一个完成。
// *en--

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// One line per entry, timestamped on the same clock the notifier uses, so the operator log and
// the upstream messages can be read as one interleaved stream. A custom formatter rather than
// AddSimpleConsole because SimpleConsole's level colours are fixed constants - Information is
// DarkGreen, Error is black on DarkRed - and neither is legible on a black background.
//
// fable5: --st*
// 每条日志一行，时间戳与 notifier 使用同一个时钟，这样 operator log 和 upstream messages
// 可以当作一条交错的 stream 来阅读。用自定义 formatter 而不是 AddSimpleConsole，
// 是因为 SimpleConsole 的 log level 颜色是固定常量 —— Information 是 DarkGreen，
// Error 是黑字配 DarkRed 底 —— 在黑色背景上这两种都看不清。
// *en--
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.FormatterName = ImportConsoleFormatter.FormatterName);
builder.Logging.AddConsoleFormatter<ImportConsoleFormatter, ConsoleFormatterOptions>();

// The channel is deliberately not registered here. It is state belonging to one import, not a
// service: Channel<T> is single-use, since completing the writer is terminal, so a shared instance
// would leave the second import reading a closed channel and quietly importing nothing. The
// pipeline builds its own per run, which puts that lifetime beyond anyone's reach to get wrong.
//
// fable5: --st*
// channel 刻意没有在这里注册。它是属于单次 import 的 state，而不是一个 service：
// Channel<T> 是一次性的（single-use），因为 complete writer 是终结性操作，所以如果共享一个实例，
// 第二次 import 就会读到一个已关闭的 channel，然后什么都没导入却悄无声息。
// pipeline 在每次 run 时自己创建 channel，这样它的 lifetime 就不可能被任何人弄错。
// *en--

// Singleton, because the store outlives any single import. That is the whole point: the second
// import of the same export has to find the first one's rows already there, or the upsert has
// nothing to be idempotent about. Transient would hand every worker its own empty database.
//
// This is also the only project allowed to name a concrete Infrastructure type. Swapping
// SqliteContentRepository for InMemoryContentRepository is this one line, and nothing in
// Application or Domain recompiles - which is the whole point of the layering.
//
// fable5: --st*
// 注册为 Singleton，因为这个 store 的生命周期比任何单次 import 都长。这正是重点所在：
// 对同一份 export 的第二次 import 必须能看到第一次留下的 rows，否则 upsert 就没有
// “幂等（idempotent）”可言。如果用 Transient，每个 worker 都会拿到自己的一个空数据库。
//
// 这也是唯一允许直接引用 Infrastructure 具体类型的 project。把 SqliteContentRepository
// 换成 InMemoryContentRepository 只需改这一行，Application 和 Domain 都不需要重新编译 ——
// 这正是分层（layering）的全部意义。
// *en--
builder.Services.AddSingleton<IContentRepository, SqliteContentRepository>();

// Singleton, and stateless besides a stopwatch, so every consumer shares one. This is the port a
// real broker plugs into: replace ConsoleUpstreamNotifier with a Service Bus or Kafka publisher
// and nothing in Application or Domain changes.
//
// fable5: --st*
// 注册为 Singleton，除了一个 stopwatch 之外是无状态（stateless）的，所以所有 consumer 共享一个实例。
// 这就是真实 broker 的接入点（port）：把 ConsoleUpstreamNotifier 换成 Service Bus 或 Kafka
// 的 publisher，Application 和 Domain 里的代码不需要任何改动。
// *en--
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
//
// fable5: --st*
// Ctrl+C 是“请求停止”，不是“强杀（kill）”。把 Cancel 设为 true 可以阻止 runtime 直接拆掉进程，
// 这样 pipeline 才有机会看到 cancellation token，把已经 in-flight 的 items 处理完，
// 并且仍能报告统计数字。第二次 Ctrl+C 不会被拦截，会立即结束进程。
//
// 这里手动接线，而不是使用 IHostApplicationLifetime 提供的 token，因为那个 token 来自
// ConsoleLifetime —— 那是一个 hosted service，而这个 app 从不启动 hosted service。
// *en--
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
    //
    // fable5: --st*
    // cancellation 不算失败。它是 pipeline 唯一被要求原样放行（let through untouched）的 exception，
    // 所以这里能做的只有如实上报，并以 128 + SIGINT(2) 退出 —— 这是 shell 里
    // “被 Ctrl+C 终止”的约定 exit code，让外层脚本能区分“被取消的 run”和“失败的 run”。
    // *en--
    logger.LogWarning("Import cancelled.");
    return 130;
}
catch (Exception exception)
{
    // Last line of defence: anything escaping the pipeline's per-item error
    // isolation is a run-level failure, and the process owes its caller a
    // non-zero exit code rather than a stack trace and a 0.
    //
    // fable5: --st*
    // 最后一道防线：任何逃出 pipeline 的 per-item error isolation 的异常都属于 run 级失败，
    // 进程应该给调用方返回一个非零 exit code，而不是打一段 stack trace 然后返回 0。
    // *en--
    logger.LogError(exception, "Import failed.");
    return 1;
}
