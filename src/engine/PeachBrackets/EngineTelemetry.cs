using System.Diagnostics;

namespace PeachBrackets;

/// <summary>
/// The timing spans of the engine's load pipeline, as <see cref="System.Diagnostics.Activity"/> values of an <see cref="ActivitySource"/> named
/// <c>PeachBrackets</c>. Nothing is recorded, and starting a span costs a null check, until something listens (an
/// <see cref="ActivityListener"/>, OpenTelemetry, <c>dotnet-trace</c>): a host that wants to know where the time of a page went adds a listener for
/// that name and reads the durations. The spans: <c>html.parse</c>, <c>css.collect</c> (style sheets fetched and parsed), <c>css.scan</c>,
/// <c>fonts</c> (web fonts fetched and registered), <c>css.cascade</c>, <c>layout</c>, <c>resource.fetch</c> (one per web request, tagged with the
/// <c>url</c>, whether it was a <c>cache</c> hit, its <c>status</c> and size) and <c>image.load</c> (one per image, tagged with its <c>url</c>).
/// </summary>
internal static class EngineTelemetry
{
    /// <summary>The name a listener subscribes to.</summary>
    internal const string SourceName = "PeachBrackets";

    internal static readonly ActivitySource Source = new(SourceName);
}
