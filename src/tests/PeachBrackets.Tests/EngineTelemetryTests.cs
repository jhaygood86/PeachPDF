using System.Collections.Concurrent;
using System.Diagnostics;

namespace PeachBrackets.Tests;

/// <summary>The timing spans of the load pipeline: recorded for a listener of the <c>PeachBrackets</c> source, and for no one else.</summary>
public class EngineTelemetryTests
{
    internal static ActivityListener Listen(ConcurrentBag<Activity> stopped) => new()
    {
        ShouldListenTo = source => source.Name == "PeachBrackets",
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        ActivityStopped = stopped.Add,
    };

    [Fact]
    public async Task LoadingAndLayingOutAPage_ReportsEachPhaseAsASpan()
    {
        var stopped = new ConcurrentBag<Activity>();
        using var listener = Listen(stopped);
        ActivitySource.AddActivityListener(listener);

        var (_, _) = await TestDocument.LoadAsync(TestDocument.Wrap("<style>p { color: red }</style><p>Hello</p>"));

        var names = stopped.Select(a => a.OperationName).ToHashSet();
        foreach (var phase in new[] { "html.parse", "css.scan", "fonts", "css.cascade", "layout" })
            Assert.Contains(phase, names);

        // A span has a real duration and a start time, so a listener can add them up.
        Assert.All(stopped.Where(a => a.OperationName is "html.parse" or "css.cascade" or "layout"), a => Assert.True(a.Duration >= TimeSpan.Zero));
    }

    [Fact]
    public async Task ADisposedListener_ReceivesNothingMore()
    {
        var stopped = new ConcurrentBag<Activity>();
        var listener = Listen(stopped);
        ActivitySource.AddActivityListener(listener);
        listener.Dispose();

        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<p>Hello</p>"));

        Assert.Single(document.Pages);
        Assert.Empty(stopped);
    }
}
