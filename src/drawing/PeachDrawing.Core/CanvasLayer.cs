using System;
using System.Collections.Generic;

namespace PeachDrawing.Core
{
    /// <summary>How a layer is composited onto the canvas that began it.</summary>
    /// <param name="Opacity">how opaque the finished layer is, from 0 (invisible) to 1; the layer's content is composited once, as a
    /// whole, so overlapping content inside it does not show through itself</param>
    /// <param name="BlendMode">how the finished layer is blended with what is already on the canvas</param>
    /// <param name="ColorMatrix">a colour transform applied to the finished layer before it is composited, or <see langword="null"/> for none</param>
    /// <param name="Bounds">the region the layer covers, in the coordinates of the canvas that began it, or <see langword="null"/> for
    /// everything from the canvas's origin to the bottom-right of its current clip. Content outside the region is not part of the layer.</param>
    /// <param name="Effects">effects applied to the finished layer, in order, before it is composited, or <see langword="null"/> for
    /// none. Effects work on pixels, so the layer is drawn into a bitmap covering exactly <paramref name="Bounds"/> and nothing
    /// outside it is kept: make the region large enough for the effects to spread into (see <c>PeachDrawing.RasterLayerEffects.GetInkMargin</c>).
    /// A canvas that cannot apply effects returns no layer at all (see <see cref="Canvas.BeginLayer"/>).</param>
    public readonly record struct LayerOptions(
        double Opacity = 1.0,
        PaintBlendMode BlendMode = PaintBlendMode.Normal,
        ColorMatrix? ColorMatrix = null,
        Rect? Bounds = null,
        IReadOnlyList<LayerEffect>? Effects = null);

    /// <summary>An effect applied to a finished layer. All distances are in the units of the canvas that began the layer.</summary>
    public abstract record LayerEffect
    {
        private protected LayerEffect()
        {
        }
    }

    /// <summary>Blurs the layer with a Gaussian of the given standard deviations.</summary>
    /// <param name="SigmaX">the horizontal standard deviation</param>
    /// <param name="SigmaY">the vertical standard deviation</param>
    public sealed record BlurEffect(double SigmaX, double SigmaY) : LayerEffect
    {
        /// <summary>
        /// Whether the pixels at the edge of the layer's content are held out past it, so the blur does not fade toward the edge. False
        /// (the default) lets the blur spread past the content into transparency, which is what a blurred shape wants; true is for a layer
        /// whose content fills its bounds and continues beyond them, such as the backdrop behind a panel.
        /// </summary>
        public bool ClampEdges { get; init; }

        /// <summary>A blur with the same standard deviation in both directions.</summary>
        /// <param name="sigma">the standard deviation</param>
        public BlurEffect(double sigma) : this(sigma, sigma)
        {
        }
    }

    /// <summary>Draws a blurred, offset, single-colour copy of the layer's shape underneath it.</summary>
    /// <param name="OffsetX">how far the shadow is moved to the right</param>
    /// <param name="OffsetY">how far the shadow is moved down</param>
    /// <param name="SigmaX">the horizontal standard deviation of the shadow's blur</param>
    /// <param name="SigmaY">the vertical standard deviation of the shadow's blur</param>
    /// <param name="Color">the shadow's colour; its alpha scales the shadow</param>
    public sealed record DropShadowEffect(double OffsetX, double OffsetY, double SigmaX, double SigmaY, PaintColor Color) : LayerEffect;

    /// <summary>Transforms the layer's colours.</summary>
    /// <param name="Matrix">the transform</param>
    public sealed record ColorMatrixEffect(ColorMatrix Matrix) : LayerEffect;

    /// <summary>What a convolution reads for a pixel that lies beyond the edge of the layer.</summary>
    public enum FilterEdgeMode
    {
        /// <summary>The nearest pixel inside the layer.</summary>
        Duplicate,

        /// <summary>The pixel on the opposite edge, as if the layer repeated.</summary>
        Wrap,

        /// <summary>Nothing: the pixel is left out of the sum.</summary>
        None,
    }

    /// <summary>How a <see cref="ComponentTransferEffect"/> maps one channel (<c>feComponentTransfer</c>'s <c>type</c>).</summary>
    public enum TransferFunctionKind
    {
        /// <summary>The channel is unchanged.</summary>
        Identity,

        /// <summary>The values are the channel at evenly spaced points; those between are interpolated.</summary>
        Table,

        /// <summary>The values are the channel in as many equal steps.</summary>
        Discrete,

        /// <summary><c>slope * value + intercept</c>.</summary>
        Linear,

        /// <summary><c>amplitude * value ^ exponent + offset</c>.</summary>
        Gamma,
    }

    /// <summary>
    /// Maps each color channel of the layer through a function of that channel alone (<c>feComponentTransfer</c>), given as a table of what each of the 256 values a
    /// channel can hold becomes. A table that is <see langword="null"/> leaves its channel as it is. The color channels are applied to the color as the layer
    /// holds it with its alpha taken out.
    /// </summary>
    /// <param name="Red">the 256 values the red channel becomes, or <see langword="null"/></param>
    /// <param name="Green">the 256 values the green channel becomes, or <see langword="null"/></param>
    /// <param name="Blue">the 256 values the blue channel becomes, or <see langword="null"/></param>
    /// <param name="Alpha">the 256 values the alpha becomes, or <see langword="null"/></param>
    public sealed record ComponentTransferEffect(byte[]? Red, byte[]? Green, byte[]? Blue, byte[]? Alpha) : LayerEffect
    {
        /// <summary>The table of a transfer function: what each of the 256 values a channel can hold becomes.</summary>
        /// <param name="kind">which function</param>
        /// <param name="tableValues">the values of <see cref="TransferFunctionKind.Table"/> and <see cref="TransferFunctionKind.Discrete"/> (none is the identity)</param>
        /// <param name="slope">the slope of <see cref="TransferFunctionKind.Linear"/></param>
        /// <param name="intercept">the intercept of <see cref="TransferFunctionKind.Linear"/></param>
        /// <param name="amplitude">the amplitude of <see cref="TransferFunctionKind.Gamma"/></param>
        /// <param name="exponent">the exponent of <see cref="TransferFunctionKind.Gamma"/></param>
        /// <param name="offset">the offset of <see cref="TransferFunctionKind.Gamma"/></param>
        /// <returns>256 values, each the channel's new value, from 0 to 255</returns>
        public static byte[] BuildTable(TransferFunctionKind kind, IReadOnlyList<double>? tableValues, double slope, double intercept, double amplitude, double exponent, double offset)
        {
            var table = new byte[256];
            var values = tableValues ?? [];
            for (var i = 0; i < 256; i++)
            {
                var c = i / 255.0;
                double v;
                switch (kind)
                {
                    case TransferFunctionKind.Table when values.Count > 0:
                    {
                        var n = values.Count - 1;
                        var k = Math.Min((int)Math.Floor(c * n), n);
                        v = k == n || n == 0 ? values[n] : values[k] + (c - (double)k / n) * n * (values[k + 1] - values[k]);
                        break;
                    }

                    case TransferFunctionKind.Discrete when values.Count > 0:
                        v = values[Math.Min((int)Math.Floor(c * values.Count), values.Count - 1)];
                        break;

                    case TransferFunctionKind.Linear:
                        v = slope * c + intercept;
                        break;

                    case TransferFunctionKind.Gamma:
                        v = amplitude * Math.Pow(c, exponent) + offset;
                        break;

                    default:
                        v = c;
                        break;
                }

                // Clamped before the integer conversion: an out-of-range double converts differently on different runtimes, and Pow(0, -1) is infinity.
                table[i] = double.IsNaN(v) ? (byte)0 : (byte)Math.Round(Math.Clamp(v * 255.0, 0.0, 255.0));
            }

            return table;
        }
    }

    /// <summary>Convolves the layer with a kernel (<c>feConvolveMatrix</c>): each pixel becomes the sum of its neighbors weighted by the kernel.</summary>
    /// <param name="Columns">how many columns the kernel has</param>
    /// <param name="Rows">how many rows the kernel has</param>
    /// <param name="Kernel">the weights, row by row, <c>Columns * Rows</c> of them</param>
    /// <param name="Divisor">what the sum is divided by (the sum of the kernel, or 1 when that is 0, when the caller has no other)</param>
    /// <param name="Bias">added to the divided sum</param>
    /// <param name="TargetX">the column of the kernel that sits over the pixel</param>
    /// <param name="TargetY">the row of the kernel that sits over the pixel</param>
    /// <param name="EdgeMode">what a pixel beyond the layer's edge is</param>
    /// <param name="PreserveAlpha">whether the alpha is left as it is and only the colors are convolved</param>
    public sealed record ConvolveMatrixEffect(int Columns, int Rows, IReadOnlyList<double> Kernel, double Divisor, double Bias, int TargetX, int TargetY, FilterEdgeMode EdgeMode, bool PreserveAlpha) : LayerEffect;

    /// <summary>
    /// Replaces the layer with Perlin noise (<c>feTurbulence</c>, with the SVG specification's reference algorithm, so a seed gives the same picture everywhere). The
    /// content drawn in the layer is not used.
    /// </summary>
    /// <param name="BaseFrequencyX">the noise's horizontal frequency, per unit</param>
    /// <param name="BaseFrequencyY">the noise's vertical frequency, per unit</param>
    /// <param name="Octaves">how many layers of noise are added; 0 draws nothing</param>
    /// <param name="Seed">the seed of the noise</param>
    /// <param name="FractalNoise">true for fractal noise, false for turbulence</param>
    /// <param name="Stitch">whether the noise is made to tile across the layer's edges</param>
    public sealed record TurbulenceEffect(double BaseFrequencyX, double BaseFrequencyY, int Octaves, long Seed, bool FractalNoise, bool Stitch) : LayerEffect;

    /// <summary>
    /// An isolated group of drawing begun with <see cref="Canvas.BeginLayer"/>: draw onto <see cref="Canvas"/>, in the same
    /// coordinates as the canvas that began the layer, and disposing the layer composites everything drawn as one piece onto that
    /// canvas.
    /// </summary>
    public sealed class CanvasLayer : IDisposable
    {
        private Action? _complete;

        /// <summary>Creates a layer that runs <paramref name="complete"/> once, when it is disposed.</summary>
        /// <param name="canvas">the canvas to draw the layer's content onto</param>
        /// <param name="complete">composites the finished layer onto the canvas that began it</param>
        /// <exception cref="ArgumentNullException">an argument is <see langword="null"/></exception>
        public CanvasLayer(Canvas canvas, Action complete)
        {
            ArgumentNullException.ThrowIfNull(canvas);
            ArgumentNullException.ThrowIfNull(complete);
            Canvas = canvas;
            _complete = complete;
        }

        /// <summary>The canvas to draw the layer's content onto.</summary>
        public Canvas Canvas { get; }

        /// <summary>Finishes the layer and composites it onto the canvas that began it. Disposing more than once does nothing.</summary>
        public void Dispose()
        {
            var complete = _complete;
            _complete = null;
            complete?.Invoke();
        }
    }
}
