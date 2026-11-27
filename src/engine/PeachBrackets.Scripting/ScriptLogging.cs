using Microsoft.Extensions.Logging;
using System.Collections;
using System.Collections.Generic;

namespace PeachBrackets.Scripting;

/// <summary>The <see cref="EventId"/>s of what <see cref="JintScriptOptions.Logger"/> receives, so a host can tell the kinds of entry apart.</summary>
public static class ScriptLogEvents
{
    /// <summary>A <c>console</c> or <c>Log</c> call: debug, information, warning or error text.</summary>
    public static EventId Message { get; } = new(1, "ScriptMessage");

    /// <summary>A script threw, failed to parse or was stopped by a limit.</summary>
    public static EventId ScriptFailed { get; } = new(2, "ScriptFailed");

    /// <summary>A <c>&lt;script src&gt;</c> could not be loaded.</summary>
    public static EventId ScriptLoadFailed { get; } = new(3, "ScriptLoadFailed");

    /// <summary>A <c>Log.data(name, value)</c> call; the state is a <see cref="ScriptDataEntry"/>.</summary>
    public static EventId Data { get; } = new(10, "ScriptData");

    /// <summary>
    /// The scripts stopped running. The text says what a page that is still waiting was waiting for: timers left past the virtual time limit, requests and
    /// images not answered, and the last events delivered. Information when work is left, debug otherwise.
    /// </summary>
    public static EventId PendingWork { get; } = new(11, "ScriptPendingWork");

    /// <summary>A script of a worker threw. The page hears of it as an error event on the <c>Worker</c> object, so this is information, not a failure of the page.</summary>
    public static EventId WorkerFailed { get; } = new(12, "WorkerFailed");
}

/// <summary>
/// The state of a <c>Log.data(name, value)</c> entry: a name and a value a script passes to the process that runs it. As a list of key-value pairs
/// it is what a structured logger records (<c>name</c> and <c>value</c>).
/// </summary>
/// <param name="Name">the name the script gave the data</param>
/// <param name="Value">the value, as text</param>
public sealed record ScriptDataEntry(string Name, string Value) : IReadOnlyList<KeyValuePair<string, object?>>
{
    /// <inheritdoc />
    public int Count => 2;

    /// <inheritdoc />
    public KeyValuePair<string, object?> this[int index] => index switch
    {
        0 => new("name", Name),
        1 => new("value", Value),
        _ => throw new System.ArgumentOutOfRangeException(nameof(index)),
    };

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        yield return this[0];
        yield return this[1];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public override string ToString() => Name + "=" + Value;
}

/// <summary>A script the host supplies to run in every document, with the name errors and the logger report it by.</summary>
/// <param name="Name">what the script is called in error reports (a file name, say)</param>
/// <param name="Code">the JavaScript</param>
public sealed record UserScript(string Name, string Code);
