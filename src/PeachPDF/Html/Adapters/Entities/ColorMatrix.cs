using System.Numerics;

namespace PeachPDF.Html.Adapters.Entities
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
    /// spec-conformant PDF answer, and that finding shapes how <see cref="PdfSharpCore.Pdf.Advanced.PdfType4Function"/>
    /// and <c>RGraphics.DrawImageWithColorMatrix</c> are actually built. Two real PDF mechanisms exist
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
    /// an arbitrary <see cref="ColorMatrix"/>-shaped function (a future phase's legitimate use of
    /// <see cref="PdfSharpCore.Pdf.Advanced.PdfType4Function.BuildColorMatrixFunction"/>, wiring it in as
    /// an image's colour space rather than a graphics-state transfer function). It cannot retroactively
    /// recolor an already-composited transparency group's result the way an ExtGState parameter does -
    /// there is no PDF graphics-state entry that takes "the RGBA this group already produced" as input.
    /// Doing that for arbitrary vector content (paths, text, gradients, patterns already burned into a
    /// Form XObject's content stream via <c>rg</c>/<c>RG</c> operators) would require either rewriting
    /// every paint operator in the subtree to go through a shared DeviceN colour space (an invasive,
    /// whole-subtree change, not a compositing-time one) or rasterizing the tile to a bitmap and applying
    /// the matrix per pixel in software - which is exactly what browsers actually do when printing a
    /// CSS-<c>filter</c>ed, non-channel-independent subtree to PDF. This codebase's tiles
    /// (<c>RGraphics.CreateTile</c>) are deliberately never rasterized, so that path is out of scope for
    /// this foundational change and is left to whichever later phase adds a rasterization capability.
    /// </description></item>
    /// </list>
    /// <para>
    /// Net effect: <c>DrawImageWithColorMatrix</c> is spec-correct and real for the channel-independent
    /// subset (via <c>/TR</c>), and throws rather than silently mis-rendering for the cross-channel
    /// subset, instead of emitting a plausible-looking but non-conformant PDF construct.
    /// </para>
    /// </remarks>
    internal readonly struct ColorMatrix
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
    }
}
