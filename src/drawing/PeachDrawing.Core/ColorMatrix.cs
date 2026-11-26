using System;
using System.Collections.Generic;
using System.Numerics;

namespace PeachDrawing.Core
{
    /// <summary>
    /// The same shape as SVG's <c>feColorMatrix type="matrix"</c> (SVG Filter Effects §15.17) and CSS
    /// Filter Effects' color-altering filter functions (<c>grayscale()</c>, <c>sepia()</c>,
    /// <c>saturate()</c>, <c>hue-rotate()</c>, <c>invert()</c>, <c>brightness()</c>, <c>contrast()</c>):
    /// an affine map <c>[R' G' B' A'] = Linear · [R G B A] + Offset</c> over premultiplied-free RGBA
    /// components in <c>[0, 1]</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this is a math-only type with no PDF-writing method on it.</b> The obvious next step -
    /// "apply this to whatever a Form XObject tile already painted" - does not have a general,
    /// spec-conformant PDF answer, and that finding shapes how a PDF Type 4 function
    /// and <c>Canvas.DrawImageWithColorMatrix</c> are actually built. Two real PDF mechanisms exist
    /// that are shaped like "run every color through a function", and neither does what a first read of
    /// "just make a Type 4 function and hang it off the graphics state" suggests:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>ExtGState <c>/TR</c> (transfer function, ISO 32000-1 §8.6.5.3).</b> The spec is explicit that
    /// this is a per-component, not per-color, operation: "a transfer function ... adjusts the values of
    /// a single colour component" and, when one function is given, "that function is applied to all
    /// process colorants" (each colorant separately - not to a colorant knowing the values of the
    /// *other* colorants). It has no way to see R while computing G. So <c>/TR</c> can reproduce any
    /// <see cref="ColorMatrix"/> whose <see cref="Linear"/> has no off-diagonal R/G/B coupling - CSS
    /// <c>brightness()</c>, <c>contrast()</c>, <c>invert()</c> are all genuinely diagonal (each output
    /// channel is a function of only that same input channel) and map exactly onto one or three
    /// independent <c>/TR</c> functions. <c>grayscale()</c>, <c>sepia()</c>, <c>saturate()</c> and
    /// <c>hue-rotate()</c> are not diagonal (each output channel mixes all three inputs) and cannot be
    /// expressed as a PDF transfer function at all, full stop - not "awkwardly", not "with a workaround",
    /// genuinely not representable by the construct the spec defines. <see cref="IsChannelIndependent"/>
    /// is the exact test for which side of this line a given matrix falls on.
    /// </description></item>
    /// <item><description>
    /// <b>A DeviceN/Separation colour space's tint-transform function (ISO 32000-1 §8.6.6.2, §7.10).</b>
    /// This genuinely can do arbitrary cross-channel mixing - a DeviceN tint-transform is exactly "an
    /// n-in, m-out function applied to every sample" - but it is a mechanism for *specifying what a
    /// colour value or an image's raw sample data means*, not for *re-processing something already
    /// painted*. It can turn a raster image's stored (r,g,b,a) sample bytes into displayed colour through
    /// an arbitrary <see cref="ColorMatrix"/>-shaped function (a legitimate use of
    /// a PDF Type 4 function, wiring it in as
    /// an image's colour space rather than a graphics-state transfer function). It cannot retroactively
    /// recolor an already-composited transparency group's result the way an ExtGState parameter does -
    /// there is no PDF graphics-state entry that takes "the RGBA this group already produced" as input.
    /// Doing that for arbitrary vector content (paths, text, gradients, patterns already burned into a
    /// Form XObject's content stream via <c>rg</c>/<c>RG</c> operators) would require either rewriting
    /// every paint operator in the subtree to go through a shared DeviceN colour space (an invasive,
    /// whole-subtree change, not a compositing-time one) or rasterizing the tile to a bitmap and applying
    /// the matrix per pixel in software - which is exactly what browsers actually do when printing a
    /// CSS-<c>filter</c>ed, non-channel-independent subtree to PDF. This codebase's tiles
    /// (<c>Canvas.CreateTile</c>) are deliberately never rasterized, so that path is out of scope for
    /// this foundational change and is left to whichever later phase adds a rasterization capability.
    /// </description></item>
    /// </list>
    /// <para>
    /// Net effect: <c>DrawImageWithColorMatrix</c> is spec-correct and real for the channel-independent
    /// subset (via <c>/TR</c>), and throws rather than silently mis-rendering for the cross-channel
    /// subset, instead of emitting a plausible-looking but non-conformant PDF construct.
    /// </para>
    /// </remarks>
    public readonly struct ColorMatrix
    {
        /// <summary>
        /// The linear (4x4) part of the affine map. <see cref="Apply"/> and <see cref="Compose"/> both
        /// go through <see cref="Vector4.Transform(Vector4, Matrix4x4)"/>, which treats a color as a
        /// row vector (<c>result = color * Linear</c>): per its documented implementation, output
        /// component <c>c</c> is <c>sum over r of color[r] * Linear[r, c]</c>. That makes each field's
        /// ROW (the first index in <c>M</c>row<c>col</c>) the INPUT component and its COLUMN the OUTPUT
        /// component - e.g. <c>Linear.M21</c> (row 2 = input G, col 1 = output R) is how much input G
        /// contributes to output R. This is the transpose of <c>feColorMatrix</c>'s own row-major table
        /// (SVG Filter Effects §15.17, where row = output); a caller building a <see cref="ColorMatrix"/>
        /// from such a table must transpose it first.
        /// </summary>
        public Matrix4x4 Linear { get; }

        /// <summary>
        /// The constant term added after <see cref="Linear"/> is applied, one component per output
        /// channel (R, G, B, A).
        /// </summary>
        public Vector4 Offset { get; }

        /// <summary>Creates a color matrix from its linear part and constant offset.</summary>
        public ColorMatrix(Matrix4x4 linear, Vector4 offset)
        {
            Linear = linear;
            Offset = offset;
        }

        /// <summary>
        /// The identity color matrix: output equals input, unchanged.
        /// </summary>
        public static readonly ColorMatrix Identity = new(Matrix4x4.Identity, Vector4.Zero);

        /// <summary>
        /// Applies this matrix to a single RGBA color: <c>Linear * color + Offset</c>. Used by tests and
        /// by any future caller that needs the transformed value of one already-known, static color
        /// (e.g. a solid text/fill color) - not by <c>DrawImageWithColorMatrix</c>, which composites
        /// against arbitrary already-painted tile content rather than a single known input color.
        /// </summary>
        public Vector4 Apply(Vector4 color) => Vector4.Transform(color, Linear) + Offset;

        /// <summary>
        /// Composes this matrix with <paramref name="appliedAfterThis"/>, producing the single matrix
        /// equivalent to applying <c>this</c> first and then <paramref name="appliedAfterThis"/> to the
        /// result - i.e. <c>result.Apply(c) == appliedAfterThis.Apply(this.Apply(c))</c> for every color
        /// <c>c</c>. Affine composition: the linear parts multiply (order matters - the second
        /// transform's matrix goes on the left), and the first transform's offset is carried through the
        /// second transform's linear part before the second transform's own offset is added.
        /// </summary>
        public ColorMatrix Compose(ColorMatrix appliedAfterThis)
        {
            var linear = Matrix4x4.Multiply(Linear, appliedAfterThis.Linear);
            var offset = Vector4.Transform(Offset, appliedAfterThis.Linear) + appliedAfterThis.Offset;
            return new ColorMatrix(linear, offset);
        }

        /// <summary>
        /// Whether every output channel among R, G, B depends on only its own matching input channel
        /// (i.e. <see cref="Linear"/> has no off-diagonal coupling among the R/G/B/A inputs feeding the
        /// R/G/B outputs) - the exact condition under which this matrix is representable as a PDF
        /// ExtGState <c>/TR</c> transfer function (see the type-level remarks). Input alpha's
        /// contribution to each of the R/G/B outputs is included in the check: a filter whose R/G/B
        /// output depends on input alpha could not be expressed as a transfer function either, since
        /// <c>/TR</c> only ever sees one color component's own value, never alpha. The matrix's own
        /// alpha OUTPUT (column 4 - <see cref="Matrix4x4.M14"/>/<see cref="Matrix4x4.M24"/>/
        /// <see cref="Matrix4x4.M34"/>/<see cref="Matrix4x4.M44"/>) is not checked - <c>/TR</c> is a
        /// color-space transfer function and is never applied to alpha, so nothing about how this matrix
        /// would compute alpha bears on whether it fits. See <see cref="Linear"/>'s remarks for why the
        /// row is the input component and the column is the output component here.
        /// </summary>
        public bool IsChannelIndependent
        {
            get
            {
                const float epsilon = 1e-5f;
                return
                    System.MathF.Abs(Linear.M12) < epsilon && System.MathF.Abs(Linear.M13) < epsilon &&
                    System.MathF.Abs(Linear.M21) < epsilon && System.MathF.Abs(Linear.M23) < epsilon &&
                    System.MathF.Abs(Linear.M31) < epsilon && System.MathF.Abs(Linear.M32) < epsilon &&
                    System.MathF.Abs(Linear.M41) < epsilon && System.MathF.Abs(Linear.M42) < epsilon && System.MathF.Abs(Linear.M43) < epsilon;
            }
        }

        // ---- the matrices CSS Filter Effects 1 section 3 gives its color functions

        /// <summary>The matrix of CSS <c>brightness()</c>: every color channel is multiplied by <paramref name="amount"/>.</summary>
        public static ColorMatrix Brightness(double amount)
        {
            var a = (float)amount;
            return new ColorMatrix(Matrix4x4.CreateScale(a, a, a), Vector4.Zero);
        }

        /// <summary>The matrix of CSS <c>contrast()</c>: each color channel's distance from the middle grey is multiplied by <paramref name="amount"/>.</summary>
        public static ColorMatrix Contrast(double amount)
        {
            var a = (float)amount;
            var intercept = 0.5f - 0.5f * a;
            return new ColorMatrix(Matrix4x4.CreateScale(a, a, a), new Vector4(intercept, intercept, intercept, 0f));
        }

        /// <summary>The matrix of CSS <c>invert()</c>; <paramref name="amount"/> is the share of the way to the negative, 0 to 1.</summary>
        public static ColorMatrix Invert(double amount)
        {
            var a = (float)Math.Clamp(amount, 0.0, 1.0);
            var slope = 1f - 2f * a;
            return new ColorMatrix(Matrix4x4.CreateScale(slope, slope, slope), new Vector4(a, a, a, 0f));
        }

        /// <summary>The matrix of CSS <c>opacity()</c>: the alpha is multiplied by <paramref name="amount"/> (0 to 1).</summary>
        public static ColorMatrix Opacity(double amount)
        {
            var matrix = Matrix4x4.Identity;
            matrix.M44 = (float)Math.Clamp(amount, 0.0, 1.0);
            return new ColorMatrix(matrix, Vector4.Zero);
        }

        /// <summary>The matrix of CSS <c>grayscale()</c>; <paramref name="amount"/> is the share of the way to grey, 0 to 1.</summary>
        public static ColorMatrix Grayscale(double amount)
        {
            var i = (float)(1 - Math.Clamp(amount, 0.0, 1.0));
            return FromRows(
                0.2126f + 0.7874f * i, 0.7152f - 0.7152f * i, 0.0722f - 0.0722f * i,
                0.2126f - 0.2126f * i, 0.7152f + 0.2848f * i, 0.0722f - 0.0722f * i,
                0.2126f - 0.2126f * i, 0.7152f - 0.7152f * i, 0.0722f + 0.9278f * i);
        }

        /// <summary>The matrix of CSS <c>sepia()</c>; <paramref name="amount"/> is the share of the way to sepia, 0 to 1.</summary>
        public static ColorMatrix Sepia(double amount)
        {
            var i = (float)(1 - Math.Clamp(amount, 0.0, 1.0));
            return FromRows(
                0.393f + 0.607f * i, 0.769f - 0.769f * i, 0.189f - 0.189f * i,
                0.349f - 0.349f * i, 0.686f + 0.314f * i, 0.168f - 0.168f * i,
                0.272f - 0.272f * i, 0.534f - 0.534f * i, 0.131f + 0.869f * i);
        }

        /// <summary>The matrix of CSS <c>saturate()</c> and of <c>feColorMatrix type="saturate"</c>: 1 leaves the colors, 0 is grey, more than 1 oversaturates.</summary>
        public static ColorMatrix Saturate(double amount)
        {
            var s = (float)amount;
            return FromRows(
                0.213f + 0.787f * s, 0.715f - 0.715f * s, 0.072f - 0.072f * s,
                0.213f - 0.213f * s, 0.715f + 0.285f * s, 0.072f - 0.072f * s,
                0.213f - 0.213f * s, 0.715f - 0.715f * s, 0.072f + 0.928f * s);
        }

        /// <summary>The matrix of CSS <c>hue-rotate()</c> and of <c>feColorMatrix type="hueRotate"</c>.</summary>
        /// <param name="radians">the angle to turn the hue by</param>
        public static ColorMatrix HueRotate(double radians)
        {
            var c = (float)Math.Cos(radians);
            var n = (float)Math.Sin(radians);
            return FromRows(
                0.213f + c * 0.787f - n * 0.213f, 0.715f - c * 0.715f - n * 0.715f, 0.072f - c * 0.072f + n * 0.928f,
                0.213f - c * 0.213f + n * 0.143f, 0.715f + c * 0.285f + n * 0.140f, 0.072f - c * 0.072f - n * 0.283f,
                0.213f - c * 0.213f - n * 0.787f, 0.715f - c * 0.715f + n * 0.715f, 0.072f + c * 0.928f + n * 0.072f);
        }

        /// <summary>The matrix of <c>feColorMatrix type="luminanceToAlpha"</c>: the color goes, and the alpha becomes the color's luminance.</summary>
        public static ColorMatrix LuminanceToAlpha() =>
            new(new Matrix4x4(
                    0, 0, 0, 0.2125f,
                    0, 0, 0, 0.7154f,
                    0, 0, 0, 0.0721f,
                    0, 0, 0, 0),
                Vector4.Zero);

        /// <summary>
        /// A matrix from the 20 numbers of <c>feColorMatrix type="matrix"</c>: four rows (the red, green, blue and alpha that come out) of five (the red, green, blue and
        /// alpha that went in, then a constant), in row order.
        /// </summary>
        /// <param name="values">the 20 numbers</param>
        /// <exception cref="ArgumentException"><paramref name="values"/> does not have 20 numbers</exception>
        public static ColorMatrix FromTable(IReadOnlyList<double> values)
        {
            ArgumentNullException.ThrowIfNull(values);
            if (values.Count != 20)
                throw new ArgumentException("A color matrix table has 20 numbers.", nameof(values));

            // The table has the outputs as rows; this type has the inputs as rows.
            float V(int row, int column) => (float)values[row * 5 + column];
            return new ColorMatrix(
                new Matrix4x4(
                    V(0, 0), V(1, 0), V(2, 0), V(3, 0),
                    V(0, 1), V(1, 1), V(2, 1), V(3, 1),
                    V(0, 2), V(1, 2), V(2, 2), V(3, 2),
                    V(0, 3), V(1, 3), V(2, 3), V(3, 3)),
                new Vector4(V(0, 4), V(1, 4), V(2, 4), V(3, 4)));
        }

        // The 3 x 3 table the specification gives (a row per output channel) as this type's matrix (a row per input channel); alpha passes through.
        private static ColorMatrix FromRows(float r0, float r1, float r2, float g0, float g1, float g2, float b0, float b1, float b2) =>
            new(new Matrix4x4(
                    r0, g0, b0, 0,
                    r1, g1, b1, 0,
                    r2, g2, b2, 0,
                    0, 0, 0, 1),
                Vector4.Zero);
    }
}
