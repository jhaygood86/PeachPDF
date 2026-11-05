using System;

namespace PeachBrackets;

/// <summary>
/// The switch for the engine's verbose parse, layout and paint console trace. The trace exists only in Debug builds, and even there it is off
/// unless the environment variable <c>PEACHPDF_TRACE</c> is <c>1</c>: building each line formats a box (and, per token, the whole parser state),
/// which on a real page costs more than the work being traced, and a host with a console attached pays a write for every one.
/// </summary>
internal static class EngineTrace
{
    /// <summary>Whether the trace lines are written (read once; set the variable before the process starts).</summary>
    internal static readonly bool Enabled = Environment.GetEnvironmentVariable("PEACHPDF_TRACE") == "1";
}
