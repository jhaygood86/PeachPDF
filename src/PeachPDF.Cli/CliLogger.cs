namespace PeachPDF.Cli;

/// <summary>
/// Minimal logger for the CLI: informational/debug messages go to stderr only when
/// <c>--verbose</c>/<c>--debug</c> is set, warnings always go to stderr, and every message is appended
/// to the <c>--log</c> file when one is configured.
/// </summary>
internal sealed class CliLogger : IDisposable
{
    private readonly bool _verbose;
    private readonly bool _debug;
    private readonly TextWriter? _file;

    public CliLogger(CliOptions options)
    {
        _verbose = options.Verbose;
        _debug = options.Debug;

        if (!string.IsNullOrEmpty(options.LogFile))
        {
            try
            {
                _file = new StreamWriter(options.LogFile, append: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                Console.Error.WriteLine($"warning: cannot open log file '{options.LogFile}': {ex.Message}");
            }
        }
    }

    public void Info(string message)
    {
        if (_verbose || _debug)
        {
            Console.Error.WriteLine(message);
        }

        _file?.WriteLine(message);
    }

    public void Debug(string message)
    {
        if (_debug)
        {
            Console.Error.WriteLine(message);
        }

        _file?.WriteLine(message);
    }

    public void Warn(string message)
    {
        Console.Error.WriteLine($"warning: {message}");
        _file?.WriteLine($"warning: {message}");
    }

    public void Dispose()
    {
        _file?.Flush();
        _file?.Dispose();
    }
}

/// <summary>Hands what a document's scripts log to the CLI's logger: debug and information as such, warnings and errors as warnings.</summary>
internal sealed class CliScriptLogger(CliLogger logger) : Microsoft.Extensions.Logging.ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => logLevel != Microsoft.Extensions.Logging.LogLevel.None;

    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var text = formatter(state, exception);
        switch (logLevel)
        {
            case Microsoft.Extensions.Logging.LogLevel.Trace or Microsoft.Extensions.Logging.LogLevel.Debug:
                logger.Debug(text);
                break;
            case Microsoft.Extensions.Logging.LogLevel.Information:
                logger.Info(text);
                break;
            default:
                logger.Warn(text);
                break;
        }
    }
}
