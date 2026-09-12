using PeachPDF.Html.Adapters;

namespace PeachPDF.MathML
{
    /// <summary>
    /// The inherited build-time context threaded through <see cref="MathTreeBuilder"/>'s recursion -
    /// mirrors <c>SvgTreeBuilder</c>'s own <c>InheritedPaint</c>/<c>FontContext</c> records. Carries
    /// only what MathML Core's schema-specific rules actually inherit or override structurally
    /// (<see cref="DisplayStyle"/>/<see cref="ScriptLevel"/>/<see cref="FontSizePt"/> - see MathML 3
    /// §3.3.4's per-element default-rendering rules); <see cref="MathNode.Color"/> is resolved directly
    /// per node from its own already-CSS-cascaded color plus any <c>mathcolor</c> override, with no
    /// inheritance bookkeeping needed here (CSS's own inheritance already did that work).
    /// </summary>
    internal readonly record struct MathBuildContext(
        bool DisplayStyle,
        int ScriptLevel,
        double FontSizePt,
        RAdapter Adapter);
}
