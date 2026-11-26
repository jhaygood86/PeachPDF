using Microsoft.Extensions.Logging;

namespace PeachBrackets.Scripting.Tests;

/// <summary>An <see cref="ILogger"/> that keeps what it is given, so a test can assert on what a script said and what went wrong.</summary>
internal sealed class CollectingLogger : ILogger
{
    private readonly object _gate = new();

    public List<(LogLevel Level, EventId Id, string Text, object? State)> Entries { get; } = [];

    /// <summary>The descriptions of scripts that threw, failed to parse, hit a limit or could not be loaded.</summary>
    public List<string> Errors => Select(e => e.Id == ScriptLogEvents.ScriptFailed || e.Id == ScriptLogEvents.ScriptLoadFailed).ToList();

    /// <summary>What the scripts said through <c>console</c> and <c>Log</c>, as <c>Level:text</c>.</summary>
    public List<string> Messages => Entries.Where(e => e.Id == ScriptLogEvents.Message).Select(e => e.Level + ":" + e.Text).ToList();

    /// <summary>Just the text of what the scripts said.</summary>
    public List<string> Texts => Select(e => e.Id == ScriptLogEvents.Message).ToList();

    private IEnumerable<string> Select(Func<(LogLevel Level, EventId Id, string Text, object? State), bool> where)
    {
        lock (_gate)
            return Entries.Where(where).Select(e => e.Text).ToList();
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        lock (_gate)
            Entries.Add((logLevel, eventId, formatter(state, exception), state));
    }
}
