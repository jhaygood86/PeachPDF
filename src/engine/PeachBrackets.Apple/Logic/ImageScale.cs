namespace PeachBrackets.Apple.Logic;

/// <summary>
/// The numbers a view hands <c>HtmlDocument</c> so a <c>srcset</c> image is chosen for how large it is shown: the device pixel ratio, and when
/// the zoom has moved far enough from the one images were chosen for to be worth loading denser ones.
/// </summary>
internal static class ImageScale
{
    /// <summary>How long the zoom has to stay put before images are chosen again, so a pinch or a wheel burst costs one pass at its end.</summary>
    public const int SettleMilliseconds = 250;

    /// <summary>The smallest growth in zoom, as a factor, that is worth a pass: finer steps would load images the eye cannot tell apart.</summary>
    public const double MinimumStep = 1.05;

    /// <summary>The device pixel ratio for a platform's display scale, falling back to 1 for a value that is not a positive number.</summary>
    /// <param name="displayScale">the platform's scale (the rasterization scale, the display density)</param>
    public static double DevicePixelRatio(double displayScale) =>
        double.IsFinite(displayScale) && displayScale > 0 ? displayScale : 1;

    /// <summary>Whether <paramref name="zoom"/> is far enough above <paramref name="chosenFor"/>, the zoom images were last chosen for, to choose again. Zooming out never is.</summary>
    /// <param name="chosenFor">the zoom the images in the document were last chosen for</param>
    /// <param name="zoom">the zoom the view is at</param>
    public static bool NeedsUpgrade(double chosenFor, double zoom) =>
        double.IsFinite(zoom) && zoom > 0 && zoom > chosenFor * MinimumStep;
}
