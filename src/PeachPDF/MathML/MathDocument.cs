using PeachPDF.Html.Adapters;

namespace PeachPDF.MathML
{
    /// <summary>
    /// The root of a built MathML presentation tree (mirrors <c>SvgDocument</c>'s role for SVG) -
    /// returned by <see cref="MathTreeBuilder.Build"/> and consumed by <c>MathLayoutEngine</c>.
    /// </summary>
    internal sealed class MathDocument
    {
        /// <summary>The presentation-markup root (with <c>&lt;semantics&gt;</c> already unwrapped to its
        /// first, presentation-markup child - see <see cref="MathTreeBuilder"/>).</summary>
        public required MathNode Root { get; init; }

        /// <summary>The <c>&lt;math&gt;</c> element's own <c>display</c> attribute: <c>"block"</c> or
        /// <c>"inline"</c> (the initial value, MathML Core §2.1). Read by <c>CssBoxMath</c> - a
        /// <c>display="block"</c> formula still participates in ordinary HTML inline-replaced-element
        /// flow the same way an inline <c>&lt;svg&gt;</c> does; this only affects the initial
        /// <c>displaystyle</c>/sizing MathML Core itself defines, not CSS box-level display.</summary>
        public required string Display { get; init; }

        /// <summary>
        /// Resolves the formula's one math typeface (the root <c>&lt;math&gt;</c> element's own
        /// cascaded font-family/style/weight - see <see cref="MathTreeBuilder"/>'s remarks) at an
        /// explicit point size. <see cref="MathLayoutEngine"/> uses this for every distinct
        /// scriptlevel-scaled size a formula needs, rather than resolving a separate font per node's own
        /// (possibly different) CSS font-family - the whole formula renders in one consistent typeface,
        /// which both matches how real math fonts are actually authored/used and keeps every structural
        /// measurement (fraction bars, radicals, stretchy variants) anchored to one MATH table.
        /// </summary>
        public required System.Func<double, RFont> ResolveFont { get; init; }
    }
}
