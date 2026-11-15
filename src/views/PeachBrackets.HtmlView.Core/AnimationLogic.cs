using PeachDrawing.Core;
using System;

namespace PeachBrackets.HtmlViewCore;

/// <summary>The parts of the animation host that need no platform: when motion is allowed, what time the document is stepped to, and where a repainted region is in the page's pixels.</summary>
internal static class AnimationLogic
{
    /// <summary>
    /// Whether the system lets animations play, for a platform that reports an animation duration scale (Android's "remove animations" setting is that scale being 0, from developer options or the accessibility
    /// switch of the same name): 0 removes them and any other scale plays them.
    /// </summary>
    /// <param name="animatorDurationScale">the platform's animation duration scale</param>
    public static bool SystemAllowsMotion(float animatorDurationScale) => animatorDurationScale > 0f;

    /// <summary>
    /// The time the document is stepped to for each frame of the display's clock, leaving out the time the loop was stopped: the display's clock keeps running while
    /// nothing plays, but the animations must not, so the first frame after a restart is one frame after the last before the stop and not seconds.
    /// </summary>
    internal sealed class FrameClock
    {
        private TimeSpan _idle;
        private TimeSpan? _last;
        private bool _resuming;

        /// <summary>Marks the loop as starting again: the time since the last frame is idle time.</summary>
        public void Resume() => _resuming = _last is not null;

        /// <summary>The time to step the document to for a frame the display reports at <paramref name="real"/>.</summary>
        /// <param name="real">the frame time, from any clock that only increases</param>
        public TimeSpan Step(TimeSpan real)
        {
            if (_resuming && _last is { } stoppedAt)
                _idle += real - stoppedAt;

            _resuming = false;
            _last = real;
            return real - _idle;
        }
    }

    /// <summary>The time of a frame reported by a display clock, in nanoseconds on its own clock.</summary>
    public static TimeSpan FromFrameNanos(long frameTimeNanos) => TimeSpan.FromTicks(frameTimeNanos / 100);

    /// <summary>
    /// Where a region of a page (in points from the sheet's top-left) is in the page's device pixels at the current zoom: the pixels the tiles are cut from.
    /// </summary>
    /// <param name="region">the region, in points</param>
    /// <param name="pageTop">where the page starts, in DIPs</param>
    /// <param name="pixelsPerPoint">how many DIPs a point is</param>
    /// <param name="scale">device pixels per DIP (density times zoom)</param>
    public static (double Left, double Top, double Width, double Height) RegionToPixels(Rect region, double pageTop, double pixelsPerPoint, double scale) =>
        (region.X * pixelsPerPoint * scale, (pageTop + (region.Y * pixelsPerPoint)) * scale, region.Width * pixelsPerPoint * scale, region.Height * pixelsPerPoint * scale);
}
