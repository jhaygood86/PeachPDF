using CoreGraphics;
using CoreImage;
using Foundation;
using Metal;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;

namespace PeachDrawing.CoreGraphics;

/// <summary>
/// The pixel effects a layer or an image draw needs - blur, drop shadow, colour matrices, luminance to opacity - run by Core Image, on the GPU
/// through a Metal device when the system has one and on the CPU otherwise.
/// </summary>
internal sealed class CoreGraphicsEffects : IDisposable
{
    private readonly object _gate = new();
    private readonly IMTLDevice? _device;
    private CIContext? _context;

    public CoreGraphicsEffects(IMTLDevice? device) => _device = device;

    /// <summary>Whether effects run on a Metal device.</summary>
    public bool UsesGpu => _device is not null;

    private CIContext Context
    {
        get
        {
            lock (_gate)
            {
                // Drawing in CoreGraphics is in sRGB, not Core Image's default linear working space, so a blur averages the same numbers there as it does elsewhere.
#pragma warning disable CS0618 // the nullable form has no setter
                var options = new CIContextOptions { WorkingColorSpace = CoreGraphicsCanvas.Rgb, OutputColorSpace = CoreGraphicsCanvas.Rgb };
                if (_device is null)
                    options.UseSoftwareRenderer = true;
#pragma warning restore CS0618

                return _context ??= _device is not null ? CIContext.FromMetalDevice(_device, options) : new CIContext(options);
            }
        }
    }

    /// <summary>Applies <paramref name="effects"/> in order. The result is a new image the caller disposes, the same size as <paramref name="source"/>.</summary>
    /// <param name="source">the finished layer</param>
    /// <param name="effects">what to apply</param>
    /// <param name="pixelsPerUnit">how many pixels of <paramref name="source"/> a layout unit is, since sigmas and offsets are in layout units</param>
    public CGImage Apply(CGImage source, IReadOnlyList<LayerEffect> effects, double pixelsPerUnit)
    {
        using var input = CIImage.FromCGImage(source);
        var extent = input.Extent;
        var image = input;
        var owned = new List<CIImage>();
        try
        {
            foreach (var effect in effects)
            {
                var before = image;
                switch (effect)
                {
                    case BlurEffect blur:
                        image = Blur(image, extent, (blur.SigmaX + blur.SigmaY) / 2 * pixelsPerUnit, blur.ClampEdges);
                        break;

                    case DropShadowEffect shadow:
                        image = Shadow(image, extent, shadow, pixelsPerUnit);
                        break;

                    case PeachDrawing.Core.ColorMatrixEffect colorMatrix:
                        image = ColorMatrix(image, colorMatrix.Matrix);
                        break;
                }

                if (!ReferenceEquals(before, image))
                    owned.Add(image);
            }

            return Context.CreateCGImage(image, extent) ?? throw new InvalidOperationException("Core Image could not render the layer.");
        }
        finally
        {
            foreach (var made in owned)
                made.Dispose();
        }
    }

    private static CIImage Blur(CIImage image, CGRect extent, double sigma, bool clampEdges)
    {
        if (!(sigma > 0))
            return image;

        // Edges are blurred into transparency unless the layer asks for them held (a backdrop that continues past its bounds).
        var source = clampEdges ? image.CreateByClampingToExtent() : image;
        return source.CreateByApplyingGaussianBlur(sigma).ImageByCroppingToRect(extent);
    }

    private static CIImage Shadow(CIImage image, CGRect extent, DropShadowEffect shadow, double pixelsPerUnit)
    {
        var (r, g, b) = shadow.Color.ToRgb();
        var tint = new CIColorMatrix
        {
            InputImage = image,
            RVector = new CIVector(0, 0, 0, 0),
            GVector = new CIVector(0, 0, 0, 0),
            BVector = new CIVector(0, 0, 0, 0),
            AVector = new CIVector(0, 0, 0, shadow.Color.A / 255f),
            BiasVector = new CIVector(r / 255f, g / 255f, b / 255f, 0),
        };

        var shade = tint.OutputImage!;
        var sigma = (shadow.SigmaX + shadow.SigmaY) / 2 * pixelsPerUnit;
        if (sigma > 0)
            shade = shade.CreateByApplyingGaussianBlur(sigma);

        // Core Image's y axis points up, the canvas's down.
        shade = shade.ImageByApplyingTransform(CGAffineTransform.MakeTranslation((nfloat)(shadow.OffsetX * pixelsPerUnit), (nfloat)(-shadow.OffsetY * pixelsPerUnit)));
        return image.CreateByCompositingOverImage(shade).ImageByCroppingToRect(extent);
    }

    private static CIImage ColorMatrix(CIImage image, ColorMatrix matrix)
    {
        // Canvas colours are row vectors multiplied by the matrix; Core Image's output channel is the dot of a vector with the input colour.
        var m = matrix.Linear;
        var o = matrix.Offset;
        var filter = new CIColorMatrix
        {
            InputImage = image,
            RVector = new CIVector(m.M11, m.M21, m.M31, m.M41),
            GVector = new CIVector(m.M12, m.M22, m.M32, m.M42),
            BVector = new CIVector(m.M13, m.M23, m.M33, m.M43),
            AVector = new CIVector(m.M14, m.M24, m.M34, m.M44),
            BiasVector = new CIVector(o.X, o.Y, o.Z, o.W),
        };

        return filter.OutputImage!;
    }

    /// <summary>The image with <paramref name="matrix"/> applied. The caller disposes it.</summary>
    public CGImage ApplyColorMatrix(CGImage source, ColorMatrix matrix) =>
        Apply(source, [new PeachDrawing.Core.ColorMatrixEffect(matrix)], 1);

    /// <summary>The image as black whose opacity is the luminance of the original, so it can mask by brightness. The caller disposes it.</summary>
    public CGImage LuminanceToAlpha(CGImage source)
    {
        using var input = CIImage.FromCGImage(source);

        // A mask pixel's value is its luminance over black (luma times alpha): Core Image's matrix works on unpremultiplied colour, so a half-transparent
        // white would otherwise count as fully white.
        using var black = CIImage.ImageWithColor(CIColor.BlackColor).ImageByCroppingToRect(input.Extent);
        using var overBlack = input.CreateByCompositingOverImage(black);
        var filter = new CIColorMatrix
        {
            InputImage = overBlack,
            RVector = new CIVector(0, 0, 0, 0),
            GVector = new CIVector(0, 0, 0, 0),
            BVector = new CIVector(0, 0, 0, 0),
            AVector = new CIVector(0.2126f, 0.7152f, 0.0722f, 0),
            BiasVector = new CIVector(0, 0, 0, 0),
        };

        using var output = filter.OutputImage!;
        return Context.CreateCGImage(output, input.Extent) ?? throw new InvalidOperationException("Core Image could not render the mask.");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _context?.Dispose();
            _context = null;
        }
    }
}
