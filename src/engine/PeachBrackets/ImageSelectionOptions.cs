namespace PeachBrackets;

/// <summary>Which of the images a <c>srcset</c> (or a <c>&lt;picture&gt;</c>'s <c>&lt;source&gt;</c> elements) offers a document loads.</summary>
public enum ImageSelectionPolicy
{
    /// <summary>The candidate with the highest pixel density, whatever the display: the best quality for paged output.</summary>
    HighestDensity,

    /// <summary>
    /// The smallest candidate that is sharp enough for the display: its pixels cover the size the image is shown at times
    /// <see cref="HtmlDocumentOptions.DevicePixelRatio"/> times the render scale (within <see cref="ImageSelectionOptions.Tolerance"/>).
    /// When none is, the largest within <see cref="ImageSelectionOptions.MaxDecodePixels"/>.
    /// </summary>
    BestForTarget,
}

/// <summary>How a document picks among the images of a <c>srcset</c>; see <see cref="HtmlDocumentOptions.ImageSelection"/>.</summary>
public sealed record ImageSelectionOptions
{
    /// <summary>Which candidate wins. <see cref="ImageSelectionPolicy.BestForTarget"/> by default.</summary>
    public ImageSelectionPolicy Policy { get; init; } = ImageSelectionPolicy.BestForTarget;

    /// <summary>
    /// How much less dense than the target a candidate may be and still count as sharp enough: 1.1 (the default) accepts a candidate with 10%
    /// fewer pixels than the display wants, so a 1.5x display does not jump to a 2x image for a 1.4x one. At least 1.
    /// </summary>
    public double Tolerance { get; init; } = 1.1;

    /// <summary>
    /// The most pixels (width times height) a chosen candidate may have, or <see langword="null"/> (the default) for no limit. A candidate over it is
    /// chosen only when every candidate is. The height is taken from the <c>width</c> and <c>height</c> attributes of the <c>&lt;img&gt;</c>, and is
    /// the width when they give no ratio; the candidate's pixel size is only known for width descriptors and once the displayed size is known.
    /// </summary>
    public long? MaxDecodePixels { get; init; }

    /// <summary>
    /// Whether <see cref="HtmlDocument.SetRenderScale"/> counts when choosing: with <see langword="true"/> (the default) a zoomed-in document loads
    /// denser images, with <see langword="false"/> zoom does not change which image is chosen.
    /// </summary>
    public bool UpgradeOnZoom { get; init; } = true;

    /// <summary>The policy of paged output: the highest density, whatever the display.</summary>
    internal static ImageSelectionOptions HighestDensityPolicy { get; } = new() { Policy = ImageSelectionPolicy.HighestDensity };

    /// <summary>The policy of a screen document that sets none.</summary>
    internal static ImageSelectionOptions ScreenDefault { get; } = new();
}
