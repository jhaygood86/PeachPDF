using System;

namespace PeachBrackets.Scripting;

/// <summary>What a <see cref="JintScriptHost"/> allows scripts to do, and where it reports what they say and what goes wrong.</summary>
public sealed class JintScriptOptions
{
    /// <summary>
    /// The longest one script, or one timer or event callback, may run before it is stopped. The default is five seconds; <see cref="TimeSpan.Zero"/>
    /// means no limit.
    /// </summary>
    public TimeSpan ScriptTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How deep a script's calls may nest before it is stopped. The default is 400; 0 means no limit.</summary>
    public int MaxRecursionDepth { get; set; } = 400;

    /// <summary>The most memory, in bytes, the scripts of one document may allocate before they are stopped. The default is 256 MiB; 0 means no limit.</summary>
    public long MemoryLimitBytes { get; set; } = 256L * 1024 * 1024;

    /// <summary>
    /// How much time, on the clock the timers and animation frames run on, a document's scripts may take to settle. A document has no wall clock: the
    /// timers a script sets are played one after another as fast as they run, and a timer that would fire after this much virtual time never does.
    /// The default is ten seconds.
    /// </summary>
    public TimeSpan MaxVirtualTime { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The most timer and animation-frame callbacks one document runs while it settles, so a timer that sets itself again does not keep it from finishing. The default is 10,000.</summary>
    public int MaxCallbacks { get; set; } = 10_000;

    /// <summary>Whether <c>&lt;script src&gt;</c> is fetched and run. When false only inline scripts run. The default is true; the fetch goes through the same loader as the document's images and style sheets.</summary>
    public bool LoadExternalScripts { get; set; } = true;

    /// <summary>Whether a script's <c>type="module"</c> elements run (as ordinary scripts, with no <c>import</c>). The default is false: they are skipped.</summary>
    public bool RunModuleScripts { get; set; }

    /// <summary>Called for each <c>console</c> call, with the level (<c>log</c>, <c>info</c>, <c>warn</c>, <c>error</c>, <c>debug</c>) and the text. Null discards them.</summary>
    public Action<string, string>? Console { get; set; }

    /// <summary>
    /// Called when a script throws, fails to parse or is stopped by a limit, with a description. The document still loads with whatever the scripts
    /// did before the failure. Null discards the error.
    /// </summary>
    public Action<string>? ScriptError { get; set; }
}
