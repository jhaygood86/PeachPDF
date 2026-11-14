using PeachDrawing.Core;
using PeachDrawing.Text.Shaping;
using SkiaSharp;
using System;

namespace PeachDrawing.Skia;

// Text drawn as a hinted coverage mask placed on whole device pixels (see SkiaTextRendering). The mask is built from the PeachDrawing engine's own
// outlines by the shared raster canvas, as the Win2D backend does (Win2dCanvas.TextMask.cs), and composited without reading what is behind it.
public sealed partial class SkiaCanvas
{
    /// <summary>
    /// The largest text, in device pixels per em, that <see cref="SkiaTextRendering.Auto"/> draws as a hinted, corrected mask. Hinting and the
    /// weight correction matter at the sizes body text is set in and mean nothing for a headline, which a mask costs far more to draw.
    /// </summary>
    internal const double AutoMaxPixelsPerEm = 48;

    // Set by a SkiaComposer: its surface is an export, which Auto never gives color fringes.
    private bool _isExport;

    // A canvas for the content of a layer: what is behind its text is not the surface but a transparent group.
    private bool _isLayerContent;

    internal void MarkAsExport() => _isExport = true;

    /// <summary>
    /// Whether everything behind the text this canvas draws is opaque, which is what <see cref="SkiaTextRendering.Lcd"/> text needs: each color
    /// channel is blended with the coverage of its own stripe, which has no meaning over a pixel with an alpha channel. Set it when the surface was
    /// cleared to an opaque color and nothing translucent is left on it (a view that clears its canvas to the page background). It is
    /// <see langword="false"/> by default, so text is drawn as <see cref="SkiaTextRendering.Grayscale"/> unless the host says otherwise, and it never
    /// applies inside a layer whatever it says (a layer's content starts transparent).
    /// </summary>
    public bool OpaqueBackdrop { get; set; }

    /// <summary>
    /// Draws a run as a hinted, corrected coverage mask placed on whole device pixels. False, with nothing drawn, when this run cannot be drawn
    /// that way and the caller draws it as an outline: a rotated, skewed or unevenly scaled transform has no pixel grid to fit to, a color font's
    /// glyphs are not one color, a picture recorded for replay under another transform has no grid yet, and <see cref="SkiaTextRendering.Auto"/>
    /// leaves large text as vectors.
    /// </summary>
    private bool TryDrawMasked(string str, Font font, PaintColor color, PaintPoint point, double letterSpacing, FontPalette? palette, ShapeSettings features)
    {
        var requested = _context.TextRendering;
        if (requested == SkiaTextRendering.Outline || _countsAsTile || _blend != PaintBlendMode.Normal)
            return false;

        var typeface = font.Typeface!;
        if (typeface.HasBitmapGlyphs || typeface.HasSvgGlyphs || typeface.HasColorGlyphs)
            return false;

        // A translation and one scale: the only transform a pixel grid can be fitted under.
        var m = _canvas.TotalMatrix;
        if (m.SkewX != 0 || m.SkewY != 0 || m.Persp0 != 0 || m.Persp1 != 0 || m.Persp2 != 1 || !(m.ScaleX > 0) || !(m.ScaleY > 0)
            || Math.Abs(m.ScaleX - m.ScaleY) > 1e-4 * m.ScaleX)
        {
            return false;
        }

        double scale = m.ScaleX;
        if (requested == SkiaTextRendering.Auto && font.Size * _ppp * scale > AutoMaxPixelsPerEm)
            return false;

        if (color.A == 0)
            return true;

        // Whether this run has a coverage per stripe, and in which order the stripes run.
        var bgr = _context.LcdBgrStripes;
        var lcd = false;
        if (OpaqueBackdrop && !_isLayerContent)
        {
            if (requested == SkiaTextRendering.Lcd)
            {
                lcd = true;
            }
            else if (requested == SkiaTextRendering.Auto && !_isExport && _context.SystemText.Get() is { Subpixel: true } system)
            {
                lcd = true;
                bgr |= system.BgrStripes;
            }
        }

        // Shaped, hinted and rasterized once for a run at this place on the pixel grid; drawn again (as a view does for the strip it scrolls into sight)
        // it comes from the cache. The part of a run outside what can be seen is cut off.
        var clip = _canvas.DeviceClipBounds;
        var mask = _context.TextMasks.Get(_context, str, font, letterSpacing, _ppp, scale, scale,
            (point.X * scale) + m.TransX, (point.Y * scale) + m.TransY, palette, features, lcd ? 3 : 1, (clip.Left, clip.Top, clip.Right, clip.Bottom), out _);
        if (mask is not { } run)
            return false;

        if (run.Width == 0)
            return true;

        DrawMask(run, color, lcd, bgr);
        return true;
    }

    private void DrawMask(SkiaTextMask run, PaintColor color, bool lcd, bool bgr)
    {
        _canvas.Save();
        try
        {
            // The mask is in device pixels of the surface, whatever matrix the host has set.
            _canvas.SetMatrix(SKMatrix.Identity);
            if (lcd)
            {
                SkiaTextBlend.Lcd(run, color, bgr, out var keep, out var add);

                // dst * (1 - a) per channel, then + ink * a: two opaque images composited with Modulate and Plus, which every Skia backend does
                // without reading the pixels behind the text.
                DrawPixels(keep, run, SKBlendMode.Modulate);
                DrawPixels(add, run, SKBlendMode.Plus);
            }
            else
            {
                DrawPixels(SkiaTextBlend.Grayscale(run, color), run, SKBlendMode.SrcOver);
            }
        }
        finally
        {
            _canvas.Restore();
        }
    }

    private void DrawPixels(byte[] pixels, SkiaTextMask run, SKBlendMode mode)
    {
        var info = new SKImageInfo(run.Width, run.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var image = SKImage.FromPixelCopy(info, pixels);
        _context.Counts.Surface(1);
        try
        {
            using var paint = new SKPaint { BlendMode = mode, IsAntialias = false };
            _canvas.DrawImage(image, run.Left, run.Top, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None), paint);
        }
        finally
        {
            _context.Counts.Surface(-1);
        }
    }
}
