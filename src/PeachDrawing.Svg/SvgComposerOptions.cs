using PeachDrawing.Core;

namespace PeachDrawing.Svg;

/// <summary>What an <see cref="SvgComposer"/> writes into the documents it makes.</summary>
public sealed class SvgComposerOptions
{
    /// <summary>How text is written. <see cref="SvgTextMode.Outlines"/> by default.</summary>
    public SvgTextMode TextMode { get; set; } = SvgTextMode.Outlines;

    /// <summary>The number of decimals coordinates are written with, 0 to 9. 3 by default.</summary>
    public int Precision { get; set; } = 3;

    /// <summary>How a conic gradient is written. <see cref="SvgConicGradientMode.Wedges"/> by default, which every viewer draws.</summary>
    public SvgConicGradientMode ConicGradients { get; set; } = SvgConicGradientMode.Wedges;

    /// <summary>The document's <c>&lt;title&gt;</c>, or <see langword="null"/> for none.</summary>
    public string? Title { get; set; }

    /// <summary>The document's <c>&lt;desc&gt;</c>, or <see langword="null"/> for none.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// A colour the whole page is painted with before anything else, or <see langword="null"/> (the default) for a transparent page,
    /// which shows whatever the SVG is placed on.
    /// </summary>
    public PaintColor? Background { get; set; }
}
