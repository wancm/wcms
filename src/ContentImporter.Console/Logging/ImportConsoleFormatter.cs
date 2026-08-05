using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using System.Text;

// NOT namespace ContentImporter.Console.*: inside that namespace the identifier "Console" binds
// to the namespace rather than System.Console.
namespace ContentImporter.Logging
{
    /// <summary>
    /// One line per entry, on the same clock and palette as the upstream notifier.
    /// </summary>
    /// <remarks>
    /// SimpleConsole's level colours are fixed constants - Information is DarkGreen, Error is
    /// black on DarkRed - and neither is legible on a black background.
    /// </remarks>
    public sealed class ImportConsoleFormatter : ConsoleFormatter
    {
        /// <summary>The name Program.cs selects this formatter by.</summary>
        public const string FormatterName = "import";

        private const string Reset = "\u001b[0m";

        private const string Timestamp = "\u001b[90m";

        private const string Information = "\u001b[94m";

        private const string Warning = "\u001b[93m";

        private const string Error = "\u001b[91m";

        private const string Verbose = "\u001b[90m";

        // No escapes at all when output is redirected or NO_COLOR is set - https://no-color.org.
        private static readonly bool Colourise =
            !Console.IsOutputRedirected &&
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));

        /// <summary>Creates the formatter under the name Program.cs selects it by.</summary>
        public ImportConsoleFormatter()
            : base(FormatterName)
        {
        }

        /// <summary>Writes one log entry as "HH:mm:ss.fff  &lt;level&gt;  &lt;message&gt;".</summary>
        public override void Write<TState>(
            in LogEntry<TState> logEntry,
            IExternalScopeProvider? scopeProvider,
            TextWriter textWriter)
        {
            string message = logEntry.Formatter(logEntry.State, logEntry.Exception);

            if (string.IsNullOrEmpty(message) && logEntry.Exception is null)
            {
                return;
            }

            string levelColour = ColourOf(logEntry.LogLevel);

            // One composed write, so a log line cannot interleave with a notifier block.
            var text = new StringBuilder();

            text.Append(Colour(Timestamp)).Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append(Colour(Reset));
            text.Append("  ").Append(Colour(levelColour)).Append(TokenOf(logEntry.LogLevel)).Append(Colour(Reset));

            // The message keeps the terminal's default foreground - only the level is coloured.
            text.Append("  ").Append(message);

            if (logEntry.Exception is not null)
            {
                text.AppendLine();
                text.Append(Colour(Error)).Append(logEntry.Exception).Append(Colour(Reset));
            }

            textWriter.Write(text.AppendLine().ToString());
        }

        // Four characters each, so messages line up whatever the level.
        private static string TokenOf(LogLevel level) => level switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => "none"
        };

        private static string ColourOf(LogLevel level) => level switch
        {
            LogLevel.Information => Information,
            LogLevel.Warning => Warning,
            LogLevel.Error or LogLevel.Critical => Error,
            _ => Verbose
        };

        private static string Colour(string code) => Colourise ? code : string.Empty;
    }
}
