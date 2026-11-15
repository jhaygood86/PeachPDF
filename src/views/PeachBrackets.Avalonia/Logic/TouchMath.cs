namespace PeachBrackets.Avalonia.Logic;

/// <summary>The arithmetic of touch scrolling: fling speed and decay, track repeat timing, double tap and pinch.</summary>
internal static class TouchMath
{
    /// <summary>The speed (units per second) under which a fling has stopped.</summary>
    public const double FlingStopSpeed = 20;

    /// <summary>How long the first press on a scroll bar track waits before it repeats (seconds).</summary>
    public const double TrackRepeatDelay = 0.4;

    /// <summary>The time between repeats of a held press on a scroll bar track (seconds).</summary>
    public const double TrackRepeatInterval = 0.08;

    /// <summary>The longest time between two taps that make a double tap (milliseconds).</summary>
    public const double DoubleTapMilliseconds = 350;

    /// <summary>The farthest two taps of a double tap may be apart (device independent pixels).</summary>
    public const double DoubleTapDistance = 40;

    /// <summary>The share of a fling's speed left after <paramref name="seconds"/>: it slows by a constant share each moment, to a stop within about a second.</summary>
    public static double FlingDecay(double seconds) => Math.Exp(-seconds / 0.35);

    /// <summary>A finger's speed from its movement over a time, blended with the speed before it so one jittery sample does not throw a fling.</summary>
    public static double SmoothVelocity(double previous, double distance, double seconds) =>
        seconds > 0 ? (0.6 * (distance / seconds)) + (0.4 * previous) : previous;

    /// <summary>Whether a tap at a place and time follows a previous one closely enough to be the second of a double tap.</summary>
    public static bool IsDoubleTap(double previousX, double previousY, long previousMs, double x, double y, long nowMs) =>
        previousMs > 0 && nowMs - previousMs <= DoubleTapMilliseconds && Math.Abs(x - previousX) <= DoubleTapDistance && Math.Abs(y - previousY) <= DoubleTapDistance;

    /// <summary>The zoom a double tap goes to: back out to 1 when zoomed in, otherwise in to 2.</summary>
    public static double DoubleTapZoom(double zoom) => zoom > 1.01 ? 1 : 2;

    /// <summary>The zoom of a pinch: the zoom when it began, times how far the fingers have spread since.</summary>
    public static double PinchZoom(double startZoom, double scale) => startZoom * scale;
}
