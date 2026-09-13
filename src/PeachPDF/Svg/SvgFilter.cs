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
    }

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
