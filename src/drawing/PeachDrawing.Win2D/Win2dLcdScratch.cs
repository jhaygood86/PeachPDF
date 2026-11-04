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

    internal CanvasRenderTarget Backdrop { get; private set; } = null!;

    internal CanvasRenderTarget Ink { get; private set; } = null!;

    internal CanvasRenderTarget Alpha { get; private set; } = null!;

    internal CanvasRenderTarget Result { get; private set; } = null!;

    /// <summary>Makes the targets at least <paramref name="width"/> by <paramref name="height"/> device pixels at <paramref name="dpi"/> on this device.</summary>
    internal void Ensure(CanvasDevice device, float dpi, int width, int height)
    {
        if (ReferenceEquals(_device, device) && _dpi == dpi && _width >= width && _height >= height)
            return;

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
