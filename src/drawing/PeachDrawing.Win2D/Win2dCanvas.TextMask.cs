using Microsoft.Graphics.Canvas;
using PeachDrawing.Core;
using PeachDrawing.Text.Shaping;
using System;
using System.Numerics;
using Windows.Graphics.DirectX;

namespace PeachDrawing.Win2D;

public sealed partial class Win2dCanvas
{
    /// <summary>
    /// The largest text, in device pixels per em, that <see cref="Win2dTextRendering.Auto"/> draws as a hinted, corrected mask. Hinting and the
    /// weight correction matter at the sizes body text is set in and mean nothing for a headline, which a mask costs far more to draw.
    /// </summary>
    internal const double AutoMaxPixelsPerEm = 48;

    // The bitmap this canvas's session draws into, when it can be read back (a composer's): what LCD text needs to blend each colour channel
    // against what is behind it. Borrowed from the composer, which owns and disposes it.
    private CanvasRenderTarget? _backdrop;

    // For a session the host owns: a reader of its Direct2D target, opened the first time LCD text could use it.
    private Win2dNativeTarget? _native;
    private bool _nativeTried;
    private (double X, double Y) _nativeOrigin;

    // What Windows says about ClearType, asked once per canvas rather than once per run.
    private bool? _clearType;
    private bool? _systemBgr;

    internal void SetBackdrop(CanvasRenderTarget target) => _backdrop = target;

    /// <summary>The reader of the session's target, or null when it has none that can be read and blended against (opened on first use).</summary>
    private Win2dNativeTarget? NativeTarget
    {
        get
        {
            if (!_nativeTried)
            {
                _nativeTried = true;
                var reader = Win2dNativeTarget.TryCreate(_session, _context.Counts);
                if (reader is { IsOpaque: true })
                {
                    _native = reader;
                    _nativeOrigin = reader.Origin();
                }
                else
                {
                    // A target with an alpha channel, or none that can be read, is never blended against.
                    reader?.Dispose();
                }
            }

            return _native;
        }
    }

    private void ReleaseNativeTarget()
    {
        _native?.Dispose();
        _native = null;
    }

    /// <summary>
    /// Draws a run as a hinted, corrected coverage mask placed on whole device pixels (see <see cref="Win2dTextRendering"/>). False, with
    /// nothing drawn, when this run cannot be drawn that way and the caller draws it as an outline: a rotated or skewed transform has no pixel
    /// grid to fit to, a colour font's glyphs are not one colour, and <see cref="Win2dTextRendering.Auto"/> leaves large text as vectors.
    /// </summary>
    private bool TryDrawMasked(string str, Font font, PaintColor color, PaintPoint point, double letterSpacing, FontPalette? palette, ShapeSettings features)
    {
        var typeface = font.Typeface!;
        if (typeface.HasBitmapGlyphs || typeface.HasSvgGlyphs || typeface.HasColorGlyphs)
            return false;

        var transform = _session.Transform;
        if (transform.M12 != 0 || transform.M21 != 0 || !(transform.M11 > 0) || !(transform.M22 > 0))
            return false;

        var requested = _context.TextRendering;
        var dpiScale = _session.Dpi / 96.0;
        var scaleX = transform.M11 * dpiScale;
        var scaleY = transform.M22 * dpiScale;
        if (requested == Win2dTextRendering.Auto && font.Size * _ppp * scaleY > AutoMaxPixelsPerEm)
            return false;

        // Over a render target this canvas can sample, text is blended on the GPU: runs are queued and composited by a pixel shader against the pixels
        // already drawn (see Win2dCanvas.GpuText.cs), so neither LCD nor grayscale text needs the pixels behind it read back.
        var gpu = _lcdTarget is not null && _blend == PaintBlendMode.Normal && _session.Blend == CanvasBlend.SourceOver;

        // Whether this run is drawn with a coverage per stripe. Auto does it only on screen (a session the host owns), where the pixels
        // behind the text can be read and ClearType is what the user has chosen; asking for Lcd does it wherever the pixels can be read.
        var lcd = requested switch
        {
            Win2dTextRendering.Lcd => gpu || CanReadBackdrop,
            Win2dTextRendering.Auto => (_clearType ??= Win2dSystemText.ClearTypeEnabled) && (gpu || (_backdrop is null && CanReadBackdrop)),
            _ => false,
        };

        (int Left, int Top, int Right, int Bottom)? limit = null;
        if (gpu)
            limit = (0, 0, (int)_lcdTarget!.SizeInPixels.Width, (int)_lcdTarget.SizeInPixels.Height);
        else if (lcd && _backdrop is null && _native is { } native)
            limit = ((int)Math.Floor(-_nativeOrigin.X), (int)Math.Floor(-_nativeOrigin.Y), (int)Math.Ceiling(native.Width - _nativeOrigin.X), (int)Math.Ceiling(native.Height - _nativeOrigin.Y));
        else if (_backdrop is { } page)
            limit = (0, 0, (int)page.SizeInPixels.Width, (int)page.SizeInPixels.Height);

        // Shaped, hinted and rasterized once for a run at this place on the pixel grid; drawn again (as a view does for the strip it scrolls into
        // sight) it comes from the cache. The part of a run outside what can be seen is cut off.
        var mask = _context.TextMasks.Get(_context, str, font, letterSpacing, _ppp, scaleX, scaleY,
            (point.X * scaleX) + (transform.M31 * dpiScale), (point.Y * scaleY) + (transform.M32 * dpiScale), palette, features, lcd ? 3 : 1, limit,
            out var maskKey, out var whole);
        if (mask is not { } run)
            return false;

        if (run.Width == 0)
            return true;

        if (gpu)
        {
            var bgr = lcd && (_context.LcdBgrStripes || (requested == Win2dTextRendering.Auto && (_systemBgr ??= Win2dSystemText.BgrStripes)));
            QueueText(run, color, maskKey, whole, bgr);
            return true;
        }

        FlushText();
        return DrawMask(run, color, lcd, requested);
    }

    private bool DrawMask(Win2dTextMask run, PaintColor color, bool lcd, Win2dTextRendering requested)
    {
        if (lcd)
        {
            var started = Win2dTextStatistics.Start();
            var read = TryReadBackdrop(run, out var backdrop);
            Win2dTextStatistics.Stop(Win2dTextStatistics.Stage.Readback, started);
            if (read)
            {
                var bgr = _context.LcdBgrStripes || (requested == Win2dTextRendering.Auto && (_systemBgr ??= Win2dSystemText.BgrStripes));
                started = Win2dTextStatistics.Start();
                var pixels = Win2dTextBlend.Lcd(run, color, backdrop, bgr);
                Win2dTextStatistics.Stop(Win2dTextStatistics.Stage.Blend, started);
                started = Win2dTextStatistics.Start();
                DrawDevicePixels(pixels, run, DirectXPixelFormat.B8G8R8A8UIntNormalized, copy: true);
                Win2dTextStatistics.Stop(Win2dTextStatistics.Stage.Upload, started);
                Win2dTextStatistics.Report(_context.TextMasks);
                return true;
            }
        }

        if (run.SubPixels != 1)
        {
            // The mask was made for LCD text but the backdrop could not be read (or is not to be): fold its stripes into one sample per pixel.
            run = Collapse(run);
        }

        var uploadStarted = Win2dTextStatistics.Start();
        DrawDevicePixels(Win2dTextBlend.Grayscale(run, color), run, DirectXPixelFormat.R8G8B8A8UIntNormalized, copy: false);
        Win2dTextStatistics.Stop(Win2dTextStatistics.Stage.Upload, uploadStarted);
        Win2dTextStatistics.Report(_context.TextMasks);
        return true;
    }

    // Reading back means a canvas whose target can be read (a bitmap of this backend's own, or a host's session over one) and the ordinary blend.
    // A pushed clip does not stop it: what is drawn inside a clip is in the target as soon as it is flushed, so the pixels behind the text are
    // there to be read.
    private bool CanReadBackdrop => _blend == PaintBlendMode.Normal && _session.Blend == CanvasBlend.SourceOver
        && (_backdrop is not null || NativeTarget is not null);

    private bool TryReadBackdrop(Win2dTextMask run, out byte[] backdrop)
    {
        backdrop = new byte[run.Width * run.Height * 4];
        if (_backdrop is { } target)
        {
            var size = target.SizeInPixels;
            if (run.Left < 0 || run.Top < 0 || run.Left + run.Width > size.Width || run.Top + run.Height > size.Height)
                return false;

            // Pending drawing is not in the bitmap until it is flushed.
            _session.Flush();
            backdrop = target.GetPixelBytes(run.Left, run.Top, run.Width, run.Height);
        }
        else if (_native is not { } native
                 || !native.TryRead(run.Left + (int)Math.Round(_nativeOrigin.X), run.Top + (int)Math.Round(_nativeOrigin.Y), run.Width, run.Height, backdrop))
        {
            return false;
        }

        // Blending a channel against a pixel that is not opaque is not defined here.
        for (var i = 3; i < backdrop.Length; i += 4)
        {
            if (backdrop[i] != 255)
                return false;
        }

        return true;
    }

    private static Win2dTextMask Collapse(Win2dTextMask run)
    {
        var samples = new byte[run.Width * run.Height];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = (byte)((run.Samples[i * 3] + run.Samples[i * 3 + 1] + run.Samples[i * 3 + 2]) / 3);

        return run with { Samples = samples, SubPixels = 1 };
    }

    /// <summary>Draws a bitmap of the mask's size with its top-left on the device pixel the mask starts at, whatever the session's transform and DPI.</summary>
    private void DrawDevicePixels(byte[] pixels, Win2dTextMask run, DirectXPixelFormat format, bool copy)
    {
        var dpi = _session.Dpi;
        using var bitmap = CanvasBitmap.CreateFromBytes(_session, pixels, run.Width, run.Height, format, dpi, Microsoft.Graphics.Canvas.CanvasAlphaMode.Premultiplied);

        var transform = _session.Transform;
        var blend = _session.Blend;
        _session.Transform = Matrix3x2.Identity;
        if (copy)
            _session.Blend = CanvasBlend.Copy;

        try
        {
            var scale = dpi / 96.0;
            _session.DrawImage(bitmap, (float)(run.Left / scale), (float)(run.Top / scale));
        }
        finally
        {
            _session.Blend = blend;
            _session.Transform = transform;
        }
    }
}
