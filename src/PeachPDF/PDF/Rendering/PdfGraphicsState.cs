// Derived from PDFsharp (MIT) - see LICENSE.md

#nullable disable warnings

using System.Numerics;
using PeachDrawing.Core;
using PeachPDF.PDF.Drawing;
using PeachPDF.PDF;
using PeachPDF.PDF.Advanced;
using PeachPDF.PDF.Internal;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;

// ReSharper disable CompareOfFloatsByEqualityOperator

namespace PeachPDF.PDF.Rendering;

/// <summary>
/// Represents the current PDF graphics state.
/// </summary>
/// <remarks>
/// Completely revised for PDFsharp 1.4.
/// </remarks>
internal sealed class PdfGraphicsState
{
    public PdfGraphicsState(PdfGraphicsRenderer renderer)
    {
        _renderer = renderer;
    }
    readonly PdfGraphicsRenderer _renderer;

    public PdfGraphicsState Clone()
    {
        PdfGraphicsState state = (PdfGraphicsState)MemberwiseClone();
        return state;
    }

    internal int Level;

    internal InternalGraphicsState InternalState = null!;

    double _realizedLineWith = -1;
    int _realizedLineCap = -1;
    int _realizedLineJoin = -1;
    double _realizedMiterLimit = -1;
    DashStyle _realizedDashStyle = (DashStyle)(-1);
    string _realizedDashPattern = null!;
    PaintColor _realizedStrokeColor = PaintColor.Empty;
    bool _realizedStrokeOverPrint;

    public void RealizePen(Pen pen, PdfColorMode colorMode)
    {
        const string twoDecimals = PdfNumberFormats.TwoDecimals;
        const string format = PdfNumberFormats.ThreeDecimals;
        PaintColor color = pen.Paint is SolidBrush solidPaint ? solidPaint.PaintColor : PaintColor.Empty;
        const bool overPrint = false;
        color = ColorSpaceHelper.EnsureColorMode(colorMode, color);
        color = PdfColorConversionGuard.ApplyConversion(_renderer.Owner, color);
        color = PdfXColorSpaceGuard.RequireAllowedOrConvert(_renderer.Owner, color,
            "A stroke color (CSS border-color/outline-color/column-rule-color, or an SVG stroke)");

        if (_realizedLineWith != pen.Width)
        {
            _renderer.AppendFormatArgs("{0:" + format + "} w\n", pen.Width);
            _realizedLineWith = pen.Width;
        }

        if (_realizedLineCap != (int)pen.LineCap)
        {
            _renderer.AppendFormatArgs("{0} J\n", (int)pen.LineCap);
            _realizedLineCap = (int)pen.LineCap;
        }

        if (_realizedLineJoin != (int)pen.LineJoin)
        {
            _renderer.AppendFormatArgs("{0} j\n", (int)pen.LineJoin);
            _realizedLineJoin = (int)pen.LineJoin;
        }

        if (_realizedLineJoin == (int)LineJoin.Miter)
        {
            if (_realizedMiterLimit != (int)pen.MiterLimit && (int)pen.MiterLimit != 0)
            {
                _renderer.AppendFormatInt("{0} M\n", (int)pen.MiterLimit);
                _realizedMiterLimit = (int)pen.MiterLimit;
            }
        }

        if (_realizedDashStyle != pen.DashStyle || pen.DashStyle == DashStyle.Custom)
        {
            double dot = pen.Width;
            double dash = 3 * dot;

            // Line width 0 is not recommended but valid.
            DashStyle dashStyle = pen.DashStyle;
            if (dot == 0)
                dashStyle = DashStyle.Solid;

            switch (dashStyle)
            {
                case DashStyle.Solid:
                    _renderer.Append("[]0 d\n");
                    break;

                case DashStyle.Dash:
                    _renderer.AppendFormatArgs("[{0:" + twoDecimals + "} {1:" + twoDecimals + "}]0 d\n", dash, dot);
                    break;

                case DashStyle.Dot:
                    _renderer.AppendFormatArgs("[{0:" + twoDecimals + "}]0 d\n", dot);
                    break;

                case DashStyle.DashDot:
                    _renderer.AppendFormatArgs("[{0:" + twoDecimals + "} {1:" + twoDecimals + "} {1:" + twoDecimals + "} {1:" + twoDecimals + "}]0 d\n", dash, dot);
                    break;

                case DashStyle.DashDotDot:
                    _renderer.AppendFormatArgs("[{0:" + twoDecimals + "} {1:" + twoDecimals + "} {1:" + twoDecimals + "} {1:" + twoDecimals + "} {1:" + twoDecimals + "} {1:" + twoDecimals + "}]0 d\n", dash, dot);
                    break;

                case DashStyle.Custom:
                    {
                        StringBuilder pdf = new StringBuilder("[", 256);
                        int len = pen.DashPattern.Length;
                        for (int idx = 0; idx < len; idx++)
                        {
                            if (idx > 0)
                                pdf.Append(' ');
                            pdf.Append(PdfEncoders.ToString(pen.DashPattern[idx]));
                        }
                        // Make an even number of values look like in GDI+
                        if (len > 0 && len % 2 == 1)
                        {
                            pdf.Append(' ');
                            pdf.Append(PdfEncoders.ToString(0.2 * pen.Width));
                        }
                        pdf.AppendFormat(CultureInfo.InvariantCulture, "]{0:" + format + "} d\n", pen.DashOffset);
                        string pattern = pdf.ToString();

                        // BUG: drice2@ageone.de reported a realizing problem
                        // HACK: I remove the if clause
                        //if (_realizedDashPattern != pattern)
                        {
                            _realizedDashPattern = pattern;
                            _renderer.Append(pattern);
                        }
                    }
                    break;
            }
            _realizedDashStyle = dashStyle;
        }

        if (pen.Paint is not SolidBrush)
        {
            RealizeBrush(pen.Paint, colorMode, 0, 0, true);
        }
        else if (ResolveEffectiveColorMode(colorMode, color) != PdfColorMode.Cmyk)
        {
            // The IsEmpty guard mirrors the fill side (RealizeFillColor): after a brush/pattern
            // stroke the tracked color is reset to Empty, and a following solid stroke must then
            // re-emit its DeviceRGB color to switch the stroke color space back out of /Pattern -
            // otherwise it would silently keep stroking with the previous shape's shading pattern.
            // Under a mixed-color-space document (colorMode == Undefined) the previously realized
            // stroke color may itself have been CMYK - a ColorSpace mismatch alone must force a
            // re-emit even if the two colors' derived .Rgb values happen to coincide, since the
            // operator itself ('RG' vs 'K') needs to change, not just the numbers.
            if (_realizedStrokeColor.IsEmpty || _realizedStrokeColor.IsCmyk != color.IsCmyk ||
                _realizedStrokeColor.PackedRgb() != color.PackedRgb())
            {
                _renderer.Append(PdfEncoders.ToString(color, PdfColorMode.Rgb));
                _renderer.Append(" RG\n");
            }
        }
        else
        {
            if (!ColorSpaceHelper.IsEqualCmyk(_realizedStrokeColor, color))
            {
                _renderer.Append(PdfEncoders.ToString(color, PdfColorMode.Cmyk));
                _renderer.Append(" K\n");
            }
        }

        // A pen that strokes with a brush (e.g. an SVG stroke="url(#gradient)") carries no
        // meaningful pen.PaintColor: the brush constructor leaves it at the default transparent black,
        // so color.A is 0. The brush itself supplies the stroke's color, and any real transparency
        // rides the brush's own soft-mask ExtGState (see RealizeBrush), so the constant stroke
        // alpha must stay fully opaque. Driving /CA from color.A here would emit /CA 0 and make the
        // whole brush stroke invisible whenever the previously realized stroke alpha wasn't already
        // 0 (e.g. right after any opaque solid stroke).
        double strokeAlpha = pen.Paint is not SolidBrush ? 1.0 : color.Opacity();

        if (_renderer.Owner.Version >= 14 && (_realizedStrokeColor.IsEmpty || _realizedStrokeColor.Opacity() != strokeAlpha || _realizedStrokeOverPrint != overPrint))
        {
            // The IsEmpty guard mirrors RealizeFillColor's identical fix: a fresh PdfGraphicsState,
            // or right after a brush stroke resets _realizedStrokeColor to Empty (line 247 below),
            // starts with A == 0. Without this guard, a solid stroke whose own alpha is genuinely 0
            // right after such a reset evaluates "0 != 0" as false and skips emitting /CA, leaving
            // the stroke at whatever alpha the enclosing graphics state had instead of invisible.
            //
            // Must create transparency group - checked before any object/resource is built, so a
            // PDF/A-1 rejection never leaves a partially-built ExtGState behind.
            if (strokeAlpha < 1)
            {
                PdfATransparencyGuard.RequireAllowed(_renderer.Owner, _renderer._page,
                    "A stroke with alpha less than 1 (CSS/SVG stroke-opacity or a semi-transparent stroke color)");
            }

            PdfExtGState extGState = _renderer.Owner.ExtGStateTable.GetExtGStateStroke(strokeAlpha, overPrint);
            string gs = _renderer.Resources.AddExtGState(extGState);
            _renderer.AppendFormatString("{0} gs\n", gs);
        }
        // A brush stroke switched the stroke color space to /Pattern and painted through the brush;
        // invalidate the tracked stroke color (as RealizeBrush already does for the fill side) so a
        // following solid stroke re-establishes its own DeviceRGB/CMYK color instead of reusing this
        // pattern. Solid strokes keep tracking their realized color for the dedup above.
        _realizedStrokeColor = pen.Paint is not SolidBrush ? PaintColor.Empty : color;
        _realizedStrokeOverPrint = overPrint;
    }

    PaintColor _realizedFillColor = PaintColor.Empty;
    bool _realizedNonStrokeOverPrint;

    public void RealizeBrush(Brush brush, PdfColorMode colorMode, int renderingMode, double fontEmSize, bool isForPen = false)
    {
        // Rendering mode 2 is used for bold simulation; mode 3 paints no ink and therefore has no
        // brush to realize (color-font vector glyphs use it solely for selectable extraction text).
        // Reference: TABLE 5.3  Text rendering modes / Page 402
        if (renderingMode == 3)
            return;

        SolidBrush solidBrush = brush as SolidBrush;
        if (solidBrush is not null)
        {
            PaintColor color = solidBrush.PaintColor;
            const bool overPrint = false;

            if (renderingMode == 0)
            {
                RealizeFillColor(color, overPrint, colorMode);
            }
            else if (renderingMode == 2)
            {
                // Come here in case of bold simulation.
                RealizeFillColor(color, false, colorMode);
                //color = PaintColor.FromArgb(0, 128, 0);
                RealizePen(PdfPaints.CreatePen(color, fontEmSize * PdfConstants.BoldEmphasis), colorMode);
            }
            else
                throw new InvalidOperationException("Only rendering modes 0 and 2 are currently supported.");
        }
        else
        {
            if (renderingMode != 0)
                throw new InvalidOperationException("Rendering modes other than 0 can only be used with solid color brushes.");

            if (brush is PdfTilingBrush tilingBrush)
            {
                Debug.Assert(UnrealizedCtm.IsIdentity, "Must realize ctm first.");

                // A pattern's matrix maps pattern space (here the brush's own y-down space) to the page's default coordinate system.
                // The renderer writes y-up coordinates and keeps user transforms in its world transform, so the map is found the way a
                // gradient's coordinates are: three points of the brush's space go through the brush matrix, then WorldToView (which
                // applies the world transform and the y flip), then the view matrix. An affine map is fixed by three points.
                Matrix3x2 toDefault = _renderer.DefaultViewMatrix;
                toDefault = EffectiveCtm * toDefault;
                PaintPoint Map(double x, double y) => toDefault.Transform(_renderer.WorldToView(tilingBrush.Matrix.Transform(new PaintPoint(x, y))));
                PaintPoint origin = Map(0, 0), unitX = Map(1, 0), unitY = Map(0, 1);
                Matrix3x2 matrix = PdfMatrix.Create(unitX.X - origin.X, unitX.Y - origin.Y, unitY.X - origin.X, unitY.Y - origin.Y, origin.X, origin.Y);

                PdfTilingPattern pattern = new PdfTilingPattern(_renderer.Owner);
                pattern.SetupFromBrush(tilingBrush, matrix);
                string name = _renderer.Resources.AddPattern(pattern);

                // The ambient fill alpha left by the last solid fill would otherwise carry over onto the pattern; see the gradient branch.
                if (!isForPen)
                {
                    RealizeFillColor(_realizedFillColor.WithOpacity(1), false, colorMode);
                }

                if (isForPen)
                {
                    _renderer.AppendFormatString("/Pattern CS\n", name);
                    _renderer.AppendFormatString("{0} SCN\n", name);
                }
                else
                {
                    _renderer.AppendFormatString("/Pattern cs\n", name);
                    _renderer.AppendFormatString("{0} scn\n", name);
                }

                _realizedFillColor = PaintColor.Empty;
            }
            else if (PdfPaints.IsGradient(brush))
            {
                Debug.Assert(UnrealizedCtm.IsIdentity, "Must realize ctm first.");
                Matrix3x2 matrix = _renderer.DefaultViewMatrix;
                matrix = EffectiveCtm * matrix;
                PdfShadingPattern pattern = new PdfShadingPattern(_renderer.Owner);
                pattern.SetupFromBrush(brush, matrix, _renderer);
                string name = _renderer.Resources.AddPattern(pattern);

                if (pattern.AlphaExtGState is not null)
                {
                    PdfATransparencyGuard.RequireAllowed(_renderer.Owner, _renderer._page, "A gradient with a semi-transparent color stop");
                    string gsName = _renderer.Resources.AddExtGState(pattern.AlphaExtGState);
                    _renderer.AppendFormatString("{0} gs\n", gsName);
                }
                else if (!isForPen)
                {
                    // A fill-side pattern with no semi-transparent stops of its own is fully opaque,
                    // but nothing above emits a /ca for it - so the ambient fill alpha left by the
                    // LAST solid-color fill (RealizeFillColor, e.g. a `color: transparent` text draw
                    // immediately before this pattern fill - background-clip: text pairs the two
                    // routinely) silently carries over and can make this fill invisible too. The
                    // stroke side already guards against the identical leak (see RealizePen's
                    // `strokeAlpha = pen.Brush != null ? 1.0 : color.A` and its own remarks) - mirror
                    // it here for fills via the same reset Realize(PdfRasterImage,...) uses. Call
                    // RealizeFillColor directly (rather than RealizeNonStrokeTransparency) with an
                    // explicit overPrint:false - the pattern has no overprint of its own, and
                    // RealizeNonStrokeTransparency would otherwise carry over
                    // _realizedNonStrokeOverPrint, the ambient overprint flag left by that same last
                    // solid-color fill.
                    RealizeFillColor(_realizedFillColor.WithOpacity(1), false, colorMode);
                }

                if (isForPen)
                {
                    _renderer.AppendFormatString("/Pattern CS\n", name);
                    _renderer.AppendFormatString("{0} SCN\n", name);
                }
                else
                {
                    _renderer.AppendFormatString("/Pattern cs\n", name);
                    _renderer.AppendFormatString("{0} scn\n", name);
                }
                // Invalidate fill color.
                _realizedFillColor = PaintColor.Empty;
            }
        }
    }

    /// <summary>
    /// Resolves what space a color actually writes in for a given document <paramref name="colorMode"/> -
    /// <see cref="PdfColorMode.Undefined"/> (a mixed-color-space document: PDF permits both
    /// <c>rg</c>/<c>RG</c> and <c>k</c>/<c>K</c> operators in the same content stream) defers to the
    /// color's own <see cref="PaintColor.IsCmyk"/>, matching <see cref="PdfEncoders.ToString(PaintColor, PdfColorMode, bool)"/>'s
    /// own per-color dispatch - <see cref="RealizeFillColor"/>/<see cref="RealizePen"/> must resolve
    /// the same way, or a device-cmyk()-authored color would fall through their <c>!= Cmyk</c> branch
    /// (true for Undefined) and get force-written as RGB despite <see cref="ColorSpaceHelper.EnsureColorMode(PdfColorMode, PaintColor)"/>
    /// leaving it untouched under Undefined.
    /// </summary>
    private static PdfColorMode ResolveEffectiveColorMode(PdfColorMode colorMode, PaintColor color)
    {
        if (colorMode != PdfColorMode.Undefined) return colorMode;
        return color.IsCmyk ? PdfColorMode.Cmyk : PdfColorMode.Rgb;
    }

    private void RealizeFillColor(PaintColor color, bool overPrint, PdfColorMode colorMode)
    {
        color = ColorSpaceHelper.EnsureColorMode(colorMode, color);
        color = PdfColorConversionGuard.ApplyConversion(_renderer.Owner, color);
        color = PdfXColorSpaceGuard.RequireAllowedOrConvert(_renderer.Owner, color,
            "A fill color (CSS color/background-color, or an SVG fill)");

        if (ResolveEffectiveColorMode(colorMode, color) != PdfColorMode.Cmyk)
        {
            // Under a mixed-color-space document (colorMode == Undefined) the previously realized
            // fill color may itself have been CMYK - a ColorSpace mismatch alone must force a
            // re-emit even if the two colors' derived .Rgb values happen to coincide, since the
            // operator itself ('rg' vs 'k') needs to change, not just the numbers.
            if (_realizedFillColor.IsEmpty || _realizedFillColor.IsCmyk != color.IsCmyk ||
                _realizedFillColor.PackedRgb() != color.PackedRgb())
            {
                _renderer.Append(PdfEncoders.ToString(color, PdfColorMode.Rgb));
                _renderer.Append(" rg\n");
            }
        }
        else
        {
            if (_realizedFillColor.IsEmpty || !ColorSpaceHelper.IsEqualCmyk(_realizedFillColor, color))
            {
                _renderer.Append(PdfEncoders.ToString(color, PdfColorMode.Cmyk));
                _renderer.Append(" k\n");
            }
        }

        if (_renderer.Owner.Version >= 14 && (_realizedFillColor.IsEmpty || _realizedFillColor.Opacity() != color.Opacity() || _realizedNonStrokeOverPrint != overPrint))
        {
            // The IsEmpty guard mirrors the RGB-change check above: a fresh PdfGraphicsState (new
            // renderer/form, or right after RealizeBrush's gradient-pattern branch invalidates the
            // fill color) starts with _realizedFillColor == PaintColor.Empty, whose A is 0. Without this
            // guard, a fill whose own alpha is genuinely 0 right after such a reset evaluates
            // "0 != 0" as false and skips emitting /ca, leaving the PDF's actual alpha at whatever
            // the enclosing graphics state had (often opaque) instead of fully transparent.
            //
            // Must create transparency group - checked before any object/resource is built, so a
            // PDF/A-1 rejection never leaves a partially-built ExtGState behind.
            if (color.Opacity() < 1)
            {
                PdfATransparencyGuard.RequireAllowed(_renderer.Owner, _renderer._page,
                    "A fill with alpha less than 1 (CSS/SVG fill-opacity or a semi-transparent fill color)");
            }

            PdfExtGState extGState = _renderer.Owner.ExtGStateTable.GetExtGStateNonStroke(color.Opacity(), overPrint);
            string gs = _renderer.Resources.AddExtGState(extGState);
            _renderer.AppendFormatString("{0} gs\n", gs);
        }
        _realizedFillColor = color;
        _realizedNonStrokeOverPrint = overPrint;
    }

    internal void RealizeNonStrokeTransparency(double transparency, PdfColorMode colorMode)
    {
        RealizeFillColor(_realizedFillColor.WithOpacity(transparency), _realizedNonStrokeOverPrint, colorMode);
    }

    internal PdfFont _realizedFont = null!;
    string _realizedFontName = string.Empty;
    double _realizedFontSize;
    int _realizedRenderingMode;  // Reference: TABLE 5.2  Text state operators / Page 398
    double _realizedCharSpace;  // Reference: TABLE 5.2  Text state operators / Page 398

    /// <summary>The character spacing (<c>Tc</c>) last written, unrounded.</summary>
    internal double RealizedCharSpace => _realizedCharSpace;

    public void RealizeFont(TypefaceFont font, Brush brush, int renderingMode, double letterSpacing = 0)
    {
        const string format = PdfNumberFormats.ThreeDecimals;

        // Modes 0 (fill), 2 (fill then stroke), and 3 (neither: invisible extraction text).
        RealizeBrush(brush, _renderer._colorMode, renderingMode, font.Size); // _renderer.page.document.Options.ColorMode);

        // Realize rendering mode.
        if (_realizedRenderingMode != renderingMode)
        {
            _renderer.AppendFormatInt("{0} Tr\n", renderingMode);
            _realizedRenderingMode = renderingMode;
        }

        // Realize character spacing. CSS `letter-spacing` maps directly onto the PDF `Tc` operator
        // (TABLE 5.2, Page 398) - it applies additively to every glyph shown by a subsequent Tj/TJ,
        // so a single text-showing operation still suffices; no need to draw character-by-character.
        if (_realizedRenderingMode != 2)
        {
            if (_realizedCharSpace != letterSpacing)
            {
                _renderer.AppendFormatDouble("{0:" + format + "} Tc\n", letterSpacing);
                _realizedCharSpace = letterSpacing;
            }
        }
        else
        {
            // Bold-emphasis simulation already uses Tc for its own purpose (thickening strokes) -
            // letter-spacing adds on top of that, rather than overriding it.
            double charSpace = font.Size * PdfConstants.BoldEmphasis + letterSpacing;
            if (_realizedCharSpace != charSpace)
            {
                _renderer.AppendFormatDouble("{0:" + format + "} Tc\n", charSpace);
                _realizedCharSpace = charSpace;
            }
        }

        _realizedFont = null;
        string fontName = _renderer.GetFontName(font, out _realizedFont);
        if (fontName != _realizedFontName || _realizedFontSize != font.Size)
        {
            _renderer.AppendFormatFont("{0} {1:" + format + "} Tf\n", fontName, font.Size);
            _realizedFontName = fontName;
            _realizedFontSize = font.Size;
        }
    }

    public PaintPoint RealizedTextPosition;

    /// <summary>
    /// Indicates that the text transformation matrix currently skews 20° to the right.
    /// </summary>
    public bool ItalicSimulationOn;

    /// <summary>
    /// The already realized part of the current transformation matrix.
    /// </summary>
    public Matrix3x2 RealizedCtm = Matrix3x2.Identity;

    /// <summary>
    /// The not yet realized part of the current transformation matrix.
    /// </summary>
    public Matrix3x2 UnrealizedCtm = Matrix3x2.Identity;

    /// <summary>
    /// Product of RealizedCtm and UnrealizedCtm.
    /// </summary>
    public Matrix3x2 EffectiveCtm = Matrix3x2.Identity;

    /// <summary>
    /// Inverse of EffectiveCtm used for transformation.
    /// </summary>
    public Matrix3x2 InverseEffectiveCtm = Matrix3x2.Identity;

    public Matrix3x2 WorldTransform = Matrix3x2.Identity;

    ///// <summary>
    ///// The world transform in PDF world space.
    ///// </summary>

    public void AddTransform(Matrix3x2 value)
    {
        Matrix3x2 transform = value;
        // Take chirality into account and
        // invert the direction of rotation.
        transform.M12 = -value.M12;
        transform.M21 = -value.M21;
        
        UnrealizedCtm = transform * UnrealizedCtm;

        WorldTransform = value * WorldTransform;
    }

    /// <summary>
    /// Realizes the CTM.
    /// </summary>
    public void RealizeCtm()
    {
        //if (MustRealizeCtm)
        if (!UnrealizedCtm.IsIdentity)
        {
            Debug.Assert(!UnrealizedCtm.IsIdentity, "mrCtm is unnecessarily set.");

            const string format = PdfNumberFormats.SevenDecimals;

            Matrix3x2 matrix = UnrealizedCtm;
            // Use up to six decimal digits to prevent round up problems.
            _renderer.AppendFormatArgs("{0:" + format + "} {1:" + format + "} {2:" + format + "} {3:" + format + "} {4:" + format + "} {5:" + format + "} cm\n",
                matrix.M11, matrix.M12, matrix.M21, matrix.M22, matrix.M31, matrix.M32);

            RealizedCtm = UnrealizedCtm * RealizedCtm;
            UnrealizedCtm = Matrix3x2.Identity;
            EffectiveCtm = RealizedCtm;
            // A cumulative CTM can collapse (nested tiny scales, each invertible on its own) and has no inverse then; nothing
            // painted under it is visible, so fall back to identity rather than abort the document.
            InverseEffectiveCtm = EffectiveCtm.TryInvert(out var inverse) ? inverse : Matrix3x2.Identity;
        }
    }

    public void SetAndRealizeClipPath(GraphicsPath clipPath)
    {
        RealizeClipPath(clipPath);
    }

    void RealizeClipPath(GraphicsPath clipPath)
    {
        _renderer.BeginGraphicMode();
        RealizeCtm();
        _renderer.AppendPath(clipPath);
        _renderer.Append(clipPath.FillMode == FillMode.Nonzero ? "W n\n" : "W* n\n");
    }

}
