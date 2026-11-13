using Microsoft.Graphics.Canvas;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace PeachBrackets.WinUI;

public sealed partial class HtmlView
{
    // What scrolling into new content costs is mostly the first time: the words of a strip are shaped and rasterized, and its images decoded and uploaded,
    // all on the UI thread in the middle of the scroll. While the view is idle the bands around the viewport are painted into a scratch target nobody sees, so that
    // those caches are full when the scroll arrives and painting the real strip is only a lookup and a draw.
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _warmTimer;
    private bool[] _warmed = [];
    private HtmlDocument? _warmedDocument;
    private bool _warmFailed;

    /// <summary>Whether painting ahead is switched off by the environment (<c>PEACH_NO_WARM</c>), for telling what it costs from what the page does.</summary>
    private static readonly bool DisableWarming = Environment.GetEnvironmentVariable("PEACH_NO_WARM") is { Length: > 0 };

    // Starts (or restarts) the wait for the view to be still. Every scroll step restarts it, so nothing is painted ahead while the user is moving.
    [ExcludeFromCodeCoverage(Justification = "Needs a window and a dispatcher; the choice of band is HtmlViewLogic.NextWarmBand, tested on its own.")]
    private void QueueWarm(int milliseconds = 120)
    {
        if (DisableWarming || _warmFailed || _document is null || _scrollMode == HtmlViewScrollMode.Disabled || _context is null)
            return;

        _warmTimer ??= CreateWarmTimer();
        _warmTimer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        _warmTimer.Stop();
        _warmTimer.Start();
    }

    private Microsoft.UI.Dispatching.DispatcherQueueTimer CreateWarmTimer()
    {
        var timer = DispatcherQueue.CreateTimer();
        timer.IsRepeating = false;
        timer.Tick += (_, _) => WarmNextBand();
        return timer;
    }

    [ExcludeFromCodeCoverage(Justification = "Needs a window, a device and a dispatcher; the same painting as a region the surface asks for.")]
    private void WarmNextBand()
    {
        if (_document is not { } document || _context is not { } context || _scrollMode == HtmlViewScrollMode.Disabled || _viewportMode || _restyling
            || _pageTops.Length == 0 || _pageBackground.A != 255 || !_loaded)
        {
            return;
        }

        if (!ReferenceEquals(_warmedDocument, document))
        {
            _warmedDocument = document;
            _warmed = new bool[(int)Math.Ceiling(_canvas.Height / HtmlViewLogic.WarmBandHeight)];
        }

        var zoom = Math.Max(_scroller.ZoomFactor, 0.01);
        var middle = (_scroller.VerticalOffset + (_scroller.ViewportHeight / 2)) / zoom;
        var band = HtmlViewLogic.NextWarmBand(_warmed.Length, middle, _warmed);
        if (band < 0)
            return;

        _warmed[band] = true;
        var started = Stopwatch.GetTimestamp();
        try
        {
            var top = band * HtmlViewLogic.WarmBandHeight;
            var bound = new Windows.Foundation.Rect(0, top, Math.Max(1, _canvas.Width), Math.Min(HtmlViewLogic.WarmBandHeight, _canvas.Height - top));
            var target = RegionTarget(_canvas, bound);
            using (var offscreen = target.CreateDrawingSession())
            {
                offscreen.Clear(_pageBackground);
                PaintPages(offscreen, bound, document, context, target, bound.Left, bound.Top, drawingIntoSurface: false);
            }
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ArgumentException or InvalidOperationException or ObjectDisposedException)
        {
            // A lost device or a document that went away under the paint: painting ahead is only an optimization, so stop trying for this document.
            _warmFailed = true;
            return;
        }

        HtmlViewDiagnostics.Trace?.Invoke($"Warm band {band}: {HtmlViewDiagnostics.MillisecondsSince(started):F1} ms");

        // The next band after a moment, so input that arrives between two is answered between them and not after the whole page.
        QueueWarm(40);
    }
}
