using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using System;
using System.IO;
using System.Reflection;
using Windows.Graphics.DirectX;

namespace PeachDrawing.Win2D;

/// <summary>
/// The render targets the GPU text blend works in, kept by the render context and grown as needed: a copy of the pixels text is about to land on, and the
/// two layers its runs are added up in (their colour times coverage, and their coverage). All three are the size of the target being drawn to, at its DPI.
/// </summary>
internal sealed class Win2dLcdScratch(Win2dResourceCounts counts)
{
    private object? _device;
    private float _dpi;
    private int _width;
    private int _height;
    private int _smallFlushes;

    // A set of targets under this many pixels each is not worth making again for a smaller one.
    private const long MinimumShrinkPixels = 512 * 512;

    // How many flushes in a row needing a quarter of the targets or less it takes to make them smaller.
    private const int ShrinkAfter = 48;

    /// <summary>What the four targets take on the device, in bytes (zero before they are made).</summary>
    internal long ByteCount => Backdrop is null ? 0 : 4L * _width * _height * 4;

    internal string Describe() => Backdrop is null ? "none" : $"{_width}x{_height}px@{_dpi:F0}dpi";

    internal CanvasRenderTarget Backdrop { get; private set; } = null!;

    internal CanvasRenderTarget Ink { get; private set; } = null!;

    internal CanvasRenderTarget Alpha { get; private set; } = null!;

    internal CanvasRenderTarget Result { get; private set; } = null!;

    /// <summary>Makes the targets at least <paramref name="width"/> by <paramref name="height"/> device pixels at <paramref name="dpi"/> on this device.</summary>
    internal void Ensure(CanvasDevice device, float dpi, int width, int height)
    {
        if (ReferenceEquals(_device, device) && _dpi == dpi && _width >= width && _height >= height)
        {
            // Grown for one large flush, they would stay that size for good: a run of flushes that all need under a quarter of what is there lets them go.
            if (_width * (long)_height > 4L * MinimumShrinkPixels && 4L * width * height < (long)_width * _height)
            {
                if (++_smallFlushes < ShrinkAfter)
                    return;
            }
            else
            {
                _smallFlushes = 0;
                return;
            }
        }

        _smallFlushes = 0;

        Clear();
        _device = device;
        _dpi = dpi;

        // Grown in steps so a view that resizes does not make new targets for every size.
        _width = (width + 255) & ~255;
        _height = (height + 127) & ~127;
        var dipsWide = _width * 96f / dpi;
        var dipsHigh = _height * 96f / dpi;
        Backdrop = Make(device, dipsWide, dipsHigh, dpi);
        Ink = Make(device, dipsWide, dipsHigh, dpi);
        Alpha = Make(device, dipsWide, dipsHigh, dpi);
        Result = Make(device, dipsWide, dipsHigh, dpi);
    }

    private CanvasRenderTarget Make(CanvasDevice device, float dipsWide, float dipsHigh, float dpi)
    {
        var target = new CanvasRenderTarget(device, dipsWide, dipsHigh, dpi, DirectXPixelFormat.B8G8R8A8UIntNormalized, CanvasAlphaMode.Ignore);
        counts.LcdLayer(1);
        return target;
    }

    internal void Clear()
    {
        foreach (var target in new[] { Backdrop, Ink, Alpha, Result })
        {
            if (target is null)
                continue;

            try
            {
                target.Dispose();
            }
            catch (Exception ex) when (ex is ObjectDisposedException or System.Runtime.InteropServices.COMException)
            {
                // Already gone with a lost device.
            }

            counts.LcdLayer(-1);
        }

        Backdrop = Ink = Alpha = Result = null!;
        _device = null;
        _width = _height = 0;
    }
}

/// <summary>The pixel shader that blends text over the pixels behind it, one colour channel at a time (see <c>Shaders/LcdBlend.hlsl</c>).</summary>
internal static class Win2dLcdShader
{
    private static byte[]? _code;

    internal static PixelShaderEffect Create()
    {
        if (_code is null)
        {
            using var stream = typeof(Win2dLcdShader).Assembly.GetManifestResourceStream("PeachDrawing.Win2D.LcdBlend.bin")
                ?? throw new InvalidOperationException("The LCD blend shader is not embedded in the assembly.");
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            _code = memory.ToArray();
        }

        return new PixelShaderEffect(_code);
    }
}
