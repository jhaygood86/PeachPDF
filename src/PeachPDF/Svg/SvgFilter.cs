// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
//
// - Sun Tsu,
// "The Art of War"

using PeachPDF.Html.Adapters.Entities;
using System.Collections.Generic;
using System.Numerics;

namespace PeachPDF.Svg
{
    /// <summary>
    /// A <c>&lt;filter&gt;</c> definition - never painted directly, only referenced by an element's
    /// <c>filter</c> attribute (see <see cref="SvgElement.FilterRef"/>). Unlike <see cref="SvgMask"/>
    /// (whose content is a full paintable scene graph), a filter's content is an ordered primitive
    /// graph (<see cref="Primitives"/>) evaluated by <see cref="SvgFilterEvaluator"/> against tiles, not
    /// a set of child <see cref="SvgElement"/>s.
    /// </summary>
    /// <remarks>
    /// Only ever constructed by <c>SvgTreeBuilder.BuildFilter</c>, which returns null (so the filter is
    /// simply never registered in <see cref="SvgDocument.Filters"/>) for a <c>&lt;filter&gt;</c>
    /// containing anything outside the supported primitive/type/operator set - see that method's own
    /// remarks for the full rejected list. An element whose <c>filter</c> attribute names an
    /// unregistered id therefore paints completely unfiltered, exactly like a <c>mask</c>/<c>clip-path</c>
    /// reference to a nonexistent id - never a partially-applied graph.
    /// </remarks>
    internal sealed class SvgFilter
    {
        public string? Id { get; init; }

        /// <summary>Filter region. Interpreted per <see cref="FilterUnitsUserSpaceOnUse"/> - defaults (per spec) to -10%/-10%/120%/120% of the referencing element's bounding box.</summary>
        public double X { get; init; } = -0.1;
        public double Y { get; init; } = -0.1;
        public double Width { get; init; } = 1.2;
        public double Height { get; init; } = 1.2;

        /// <summary>False (the spec default) means the region above is <c>objectBoundingBox</c>-relative - fractions of the referencing element's bounding box.</summary>
        public bool FilterUnitsUserSpaceOnUse { get; init; }

        /// <summary>
        /// True (the spec default for THIS attribute, unlike <see cref="FilterUnitsUserSpaceOnUse"/>)
        /// means a primitive's own numeric parameters (currently only <see cref="FeOffset"/>'s
        /// <c>dx</c>/<c>dy</c> - the only primitive in this supported set with a user-space-flavored
        /// parameter) are literal user-space units; <c>primitiveUnits="objectBoundingBox"</c> (false)
        /// means they are fractions of the referencing element's bounding box width/height instead.
        /// </summary>
        public bool PrimitiveUnitsUserSpaceOnUse { get; init; } = true;

        public List<FilterPrimitive> Primitives { get; init; } = [];

        /// <summary>
        /// True when at least one primitive needs pixels: a blur, a lighting or morphology pass, a cross-channel colour matrix,
        /// arithmetic compositing, a non-linear transfer function, or a primitive subregion. Such a filter is evaluated in a
        /// bitmap by the raster backend (<see cref="SvgRasterFilterEvaluator"/>); a filter of only the vector primitives keeps
        /// running over PDF tiles and stays vector.
        /// </summary>
        public bool RequiresRaster
        {
            get
            {
                foreach (var primitive in Primitives)
                {
                    if (primitive.RequiresRaster)
                        return true;
                }

                return false;
            }
        }
    }

    /// <summary>A primitive subregion (<c>x</c>/<c>y</c>/<c>width</c>/<c>height</c> on a filter primitive); each side is null when omitted (and then follows the default subregion rule).</summary>
    internal readonly record struct FilterSubregion(double? X, double? Y, double? Width, double? Height);

    /// <summary>
    /// One node of a <see cref="SvgFilter"/>'s primitive graph. <see cref="In"/> is this primitive's
    /// input: null on the very first primitive in <see cref="SvgFilter.Primitives"/> means
    /// <c>SourceGraphic</c>, null on any later primitive means "the previous primitive's own result"
    /// (<c>lastResult</c>), and a non-null value is looked up first against every earlier primitive's
    /// own named <see cref="Result"/> and, failing that, against the reserved names
    /// <c>SourceGraphic</c>/<c>SourceAlpha</c> - see <see cref="SvgFilterEvaluator"/>.
    /// </summary>
    internal abstract class FilterPrimitive
    {
        public string? In { get; init; }

        /// <summary>This primitive's own named result, referenceable by a LATER primitive's <see cref="In"/>/<c>In2</c>/<c>Inputs</c> - null means this result is only reachable via the implicit "previous result" rule.</summary>
        public string? Result { get; init; }

        /// <summary>The primitive's own <c>x</c>/<c>y</c>/<c>width</c>/<c>height</c>, or null for the default subregion (the whole filter region).</summary>
        public FilterSubregion? Subregion { get; set; }

        /// <summary><c>color-interpolation-filters</c>: true (the initial value) computes in linear light, false in sRGB. Only the raster evaluation honours it.</summary>
        public bool LinearRgb { get; set; } = true;

        /// <summary>True when an <c>in</c>/<c>in2</c> (or an <c>feMergeNode</c>'s <c>in</c>) names <c>FillPaint</c>, <c>StrokePaint</c>, <c>BackgroundImage</c> or <c>BackgroundAlpha</c>, which only exist as pixels.</summary>
        public bool ReadsReservedInput { get; set; }

        /// <summary>Whether this primitive can only be evaluated over pixels.</summary>
        public virtual bool RequiresRaster => Subregion is not null || ReadsReservedInput;
    }

    /// <summary><c>feFlood</c> - fills the whole filter region with a flat color, ignoring <see cref="FilterPrimitive.In"/> (it has no real input; the SVG spec allows one to be specified but it's never consulted).</summary>
    internal sealed class FeFlood : FilterPrimitive
    {
        public required RColor Color { get; init; }
        public required double Opacity { get; init; }
    }

    /// <summary><c>feOffset</c> - translates its input by (<see cref="Dx"/>, <see cref="Dy"/>), resolved per <see cref="SvgFilter.PrimitiveUnitsUserSpaceOnUse"/>.</summary>
    internal sealed class FeOffset : FilterPrimitive
    {
        public required double Dx { get; init; }
        public required double Dy { get; init; }
    }

    /// <summary><c>feMerge</c> - layers each of <see cref="Inputs"/> (one per child <c>&lt;feMergeNode&gt;</c>, in document order) via ordinary src-over compositing. <see cref="FilterPrimitive.In"/> is unused (a merge has no single input).</summary>
    internal sealed class FeMerge : FilterPrimitive
    {
        public required IReadOnlyList<string?> Inputs { get; init; }
    }

    /// <summary><c>feTile</c> - repeats its input's own defined subregion across the whole filter region. Carries no extra parameters beyond the inherited <see cref="FilterPrimitive.In"/>.</summary>
    internal sealed class FeTile : FilterPrimitive
    {
    }

    /// <summary><c>feComposite</c> - Porter-Duff-composites <see cref="FilterPrimitive.In"/> with <see cref="In2"/> via <see cref="Operator"/> (one of <c>over</c>/<c>in</c>/<c>out</c>/<c>atop</c>/<c>xor</c> - <c>arithmetic</c> is rejected at parse time, see <see cref="SvgFilter"/>'s remarks).</summary>
    internal sealed class FeComposite : FilterPrimitive
    {
        public string? In2 { get; init; }
        public required string Operator { get; init; }

        /// <summary><c>k1</c>-<c>k4</c> of <c>operator="arithmetic"</c>: <c>k1*i1*i2 + k2*i1 + k3*i2 + k4</c> per premultiplied channel.</summary>
        public double K1 { get; init; }
        public double K2 { get; init; }
        public double K3 { get; init; }
        public double K4 { get; init; }

        public override bool RequiresRaster => Operator == "arithmetic" || base.RequiresRaster;
    }

    /// <summary><c>feBlend</c> - blends <see cref="FilterPrimitive.In"/> over <see cref="In2"/> with a PDF blend mode.</summary>
    internal sealed class FeBlend : FilterPrimitive
    {
        public string? In2 { get; init; }
        public required RBlendMode Mode { get; init; }
    }

    /// <summary>
    /// <c>feColorMatrix</c> - either a channel-independent (diagonal) <c>type="matrix"</c> transform
    /// (applied via <see cref="Matrix"/>, PDF <c>/TR</c>) or <c>type="luminanceToAlpha"</c>
    /// (<see cref="IsLuminanceToAlpha"/>, applied via a <c>/Luminosity</c> mask, unaffected by the
    /// channel-independence restriction since it's a masking operation, not a <c>/TR</c> color
    /// transform). <c>type="saturate"</c>/<c>type="hueRotate"</c> and any <c>type="matrix"</c> with a
    /// nonzero off-diagonal term are always rejected at parse time - see <see cref="ColorMatrix"/>'s
    /// remarks for why no PDF construct can express them.
    /// </summary>
    internal sealed class FeColorMatrix : FilterPrimitive
    {
        public required ColorMatrix Matrix { get; init; }
        public required bool IsLuminanceToAlpha { get; init; }

        public override bool RequiresRaster => (!IsLuminanceToAlpha && !Matrix.IsChannelIndependent) || base.RequiresRaster;
    }

    /// <summary>
    /// <c>feComponentTransfer</c> - per-channel <c>type="linear"</c> (<c>slope</c>/<c>intercept</c>)
    /// functions from up to three <c>&lt;feFuncR&gt;</c>/<c>&lt;feFuncG&gt;</c>/<c>&lt;feFuncB&gt;</c>
    /// children, composed into one diagonal <see cref="Matrix"/> (applied via PDF <c>/TR</c>, same
    /// mechanism as <see cref="FeColorMatrix"/>). A missing channel function defaults to identity
    /// (slope 1, intercept 0), per spec. <c>type="gamma"</c> is genuinely non-affine
    /// (<c>output = amplitude * pow(input, exponent) + offset</c>, not <c>slope * input + intercept</c>)
    /// so it does not fit this affine <see cref="ColorMatrix"/> shape at all - a correction to this
    /// feature's original scope, not a limitation of the parser; only <c>type="linear"</c> is supported.
    /// <c>type="table"</c>/<c>type="discrete"</c> stay rejected as originally planned (would need a PDF
    /// <c>FunctionType 0</c> sampled function, not built here). A non-identity <c>&lt;feFuncA&gt;</c> is
    /// also rejected (this primitive only composes R/G/B), rather than silently ignored.
    /// </summary>
    internal sealed class FeComponentTransfer : FilterPrimitive
    {
        public required ColorMatrix Matrix { get; init; }

        /// <summary>The R, G, B and A transfer functions, in that order, when any of them needs pixels (gamma, table, discrete, or a non-identity alpha function); null when <see cref="Matrix"/> says it all.</summary>
        public TransferFunction[]? Functions { get; init; }

        public override bool RequiresRaster => Functions is not null || base.RequiresRaster;
    }

    internal enum TransferKind { Identity, Table, Discrete, Linear, Gamma }

    /// <summary>One <c>feFuncR</c>/<c>feFuncG</c>/<c>feFuncB</c>/<c>feFuncA</c> child of <c>feComponentTransfer</c>.</summary>
    internal sealed record TransferFunction(TransferKind Kind, double[] Table, double Slope, double Intercept, double Amplitude, double Exponent, double Offset)
    {
        public static TransferFunction Identity { get; } = new(TransferKind.Identity, [], 1, 0, 1, 1, 0);
    }

    /// <summary><c>feGaussianBlur</c>: blurs by <see cref="StdDeviationX"/>/<see cref="StdDeviationY"/> user units.</summary>
    internal sealed class FeGaussianBlur : FilterPrimitive
    {
        public required double StdDeviationX { get; init; }
        public required double StdDeviationY { get; init; }

        public override bool RequiresRaster => true;
    }

    /// <summary><c>feDropShadow</c>: the input blurred, offset, flooded with <see cref="Color"/> and merged under the input.</summary>
    internal sealed class FeDropShadow : FilterPrimitive
    {
        public required double Dx { get; init; }
        public required double Dy { get; init; }
        public required double StdDeviationX { get; init; }
        public required double StdDeviationY { get; init; }
        public required RColor Color { get; init; }
        public required double Opacity { get; init; }

        public override bool RequiresRaster => true;
    }

    /// <summary><c>feMorphology</c>: erodes or dilates the input by a rectangular window of <see cref="RadiusX"/> x <see cref="RadiusY"/> user units.</summary>
    internal sealed class FeMorphology : FilterPrimitive
    {
        public required bool Dilate { get; init; }
        public required double RadiusX { get; init; }
        public required double RadiusY { get; init; }

        public override bool RequiresRaster => true;
    }

    internal enum FilterEdgeMode { Duplicate, Wrap, None }

    /// <summary><c>feConvolveMatrix</c>.</summary>
    internal sealed class FeConvolveMatrix : FilterPrimitive
    {
        public required int OrderX { get; init; }
        public required int OrderY { get; init; }
        public required double[] Kernel { get; init; }
        public required double Divisor { get; init; }
        public required double Bias { get; init; }
        public required int TargetX { get; init; }
        public required int TargetY { get; init; }
        public required FilterEdgeMode EdgeMode { get; init; }
        public required bool PreserveAlpha { get; init; }

        public override bool RequiresRaster => true;
    }

    /// <summary><c>feTurbulence</c>: Perlin noise from the SVG reference algorithm.</summary>
    internal sealed class FeTurbulence : FilterPrimitive
    {
        public required double BaseFrequencyX { get; init; }
        public required double BaseFrequencyY { get; init; }
        public required int NumOctaves { get; init; }
        public required double Seed { get; init; }
        public required bool Stitch { get; init; }
        public required bool FractalNoise { get; init; }

        public override bool RequiresRaster => true;
    }

    /// <summary><c>feDisplacementMap</c>: moves <see cref="FilterPrimitive.In"/>'s pixels by the channels of <see cref="In2"/>.</summary>
    internal sealed class FeDisplacementMap : FilterPrimitive
    {
        public string? In2 { get; init; }
        public required double Scale { get; init; }

        /// <summary>The channels (0 = R, 1 = G, 2 = B, 3 = A) that drive the x and y displacement.</summary>
        public required int XChannel { get; init; }
        public required int YChannel { get; init; }

        public override bool RequiresRaster => true;
    }

    /// <summary>
    /// <c>feImage</c>: a stand-alone image (<see cref="Image"/>, fitted into the primitive subregion by its own <c>preserveAspectRatio</c>) or a
    /// reference to an element of the document (<see cref="Target"/>, rendered in the filtered element's user space like <c>&lt;use&gt;</c>).
    /// A reference that resolves to nothing leaves the result transparent. Evaluated only over pixels.
    /// </summary>
    internal sealed class FeImage : FilterPrimitive
    {
        /// <summary>The id a <c>href="#id"</c> names; resolved to <see cref="Target"/> once every node of the document is known.</summary>
        public string? ReferenceId { get; init; }

        public SvgElement? Target { get; set; }

        /// <summary>The image resolved from a <c>data:</c>/prefetched href, laid out per the primitive subregion at paint time; null for an element reference.</summary>
        public SvgImageElement? Image { get; init; }

        public override bool RequiresRaster => true;
    }

    internal enum LightKind { Distant, Point, Spot }

    /// <summary>The <c>feDistantLight</c>/<c>fePointLight</c>/<c>feSpotLight</c> child of a lighting primitive.</summary>
    internal sealed record LightSource(
        LightKind Kind, double Azimuth, double Elevation,
        double X, double Y, double Z,
        double PointsAtX, double PointsAtY, double PointsAtZ,
        double SpecularExponent, double? LimitingConeAngle);

    /// <summary><c>feDiffuseLighting</c> and <c>feSpecularLighting</c>.</summary>
    internal sealed class FeLighting : FilterPrimitive
    {
        public required bool Specular { get; init; }
        public required double SurfaceScale { get; init; }

        /// <summary><c>diffuseConstant</c> or <c>specularConstant</c>.</summary>
        public required double Constant { get; init; }
        public required double SpecularExponent { get; init; }
        public required RColor LightingColor { get; init; }
        public required LightSource Light { get; init; }

        public override bool RequiresRaster => true;
    }

    /// <summary>
    /// Builds a <see cref="ColorMatrix"/> from a raw <c>feColorMatrix type="matrix"</c> <c>values</c>
    /// list - 20 numbers, row-major, SVG Filter Effects §15.17's own convention where ROW is the OUTPUT
    /// component and the first four columns are the INPUT components (the fifth is the constant term):
    /// <code>
    /// R' = a00*R + a01*G + a02*B + a03*A + a04
    /// G' = a10*R + a11*G + a12*B + a13*A + a14
    /// B' = a20*R + a21*G + a22*B + a23*A + a24
    /// A' = a30*R + a31*G + a32*B + a33*A + a34
    /// </code>
    /// <see cref="ColorMatrix.Linear"/>'s own convention is the transpose of this (row = INPUT, column =
    /// OUTPUT - see its remarks, driven by how <see cref="Vector4.Transform(Vector4, Matrix4x4)"/>
    /// actually multiplies), so building <see cref="ColorMatrix.Linear"/> field-by-field from the raw
    /// <c>values</c> list swaps each coefficient's row/column relative to the SVG table it came from,
    /// rather than copying it through unchanged.
    /// </summary>
    internal static class SvgColorMatrixTable
    {
        public static readonly double[] Identity =
        [
            1, 0, 0, 0, 0,
            0, 1, 0, 0, 0,
            0, 0, 1, 0, 0,
            0, 0, 0, 1, 0,
        ];

        public static ColorMatrix Build(double[] values)
        {
            float A(int outputRow, int inputCol) => (float)values[outputRow * 5 + inputCol];

            // Linear.M{row}{col}: row = INPUT (1-based), col = OUTPUT (1-based) - see this class's own
            // remarks. Row i of Linear (M(i+1)(1..4)) is therefore column i of the SVG table's 4x4
            // linear part: [a0i, a1i, a2i, a3i].
            var linear = new Matrix4x4(
                A(0, 0), A(1, 0), A(2, 0), A(3, 0),
                A(0, 1), A(1, 1), A(2, 1), A(3, 1),
                A(0, 2), A(1, 2), A(2, 2), A(3, 2),
                A(0, 3), A(1, 3), A(2, 3), A(3, 3));

            // The constant (5th) column maps directly, one per OUTPUT component - Offset isn't
            // transposed, since ColorMatrix.Apply/Compose both add it post-multiplication regardless of
            // Linear's row/column convention.
            var offset = new Vector4(A(0, 4), A(1, 4), A(2, 4), A(3, 4));

            return new ColorMatrix(linear, offset);
        }
    }
}
