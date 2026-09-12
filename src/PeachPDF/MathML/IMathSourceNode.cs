using System.Collections.Generic;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;

namespace PeachPDF.MathML
{
    /// <summary>
    /// A minimal, source-agnostic view of one node of a MathML element tree, so
    /// <see cref="MathTreeBuilder"/> can build the same <see cref="MathNode"/> tree regardless of where
    /// the underlying markup came from. Only one implementation exists today
    /// (<see cref="CssBoxMathSourceNode"/>, wrapping an inline <c>&lt;math&gt;</c>'s already-parsed
    /// <c>CssBox</c> subtree) - unlike SVG's <c>ISvgSourceNode</c>, MathML has no standalone-document
    /// entry point (no <c>&lt;img src="x.mml"&gt;</c> equivalent) - but the seam is kept for symmetry
    /// with that precedent and so unit tests can build a tree from a lightweight fake without a real
    /// HTML parse.
    /// <para>
    /// Deliberately does not expose CSS cascade/style-matching hooks the way <c>ISvgSourceNode</c>
    /// does: MathML's own attributes (<c>mathvariant</c>, <c>displaystyle</c>, <c>scriptlevel</c>,
    /// <c>stretchy</c>, ...) are plain XML attributes, not CSS properties, so there is nothing here to
    /// match against a stylesheet. Genuine CSS properties that do apply to math content (<c>color</c>,
    /// <c>font-family</c>, <c>font-size</c>) have already been fully cascaded by the time this node is
    /// read - see <see cref="Color"/>/<see cref="FontSizePt"/> - exactly the same way SVG reads
    /// already-cascaded values for the properties it doesn't have its own presentation-attribute
    /// registry for.
    /// </para>
    /// </summary>
    internal interface IMathSourceNode
    {
        /// <summary>The element's tag name, in its original document case (e.g. "mfrac").</summary>
        string Name { get; }

        /// <summary>Gets the value of an attribute by its original-case name, or null if not present.</summary>
        string? GetAttribute(string name);

        /// <summary>Every attribute this element carries, name to value - used only by
        /// <see cref="MathMlSerializer"/> to re-serialize the authored markup (e.g. for the <c>/AF</c>
        /// MathML-source attachment on a tagged <c>Formula</c> structure element), not by
        /// <see cref="MathTreeBuilder"/> itself, which reads named attributes directly.</summary>
        IEnumerable<KeyValuePair<string, string>> Attributes { get; }

        /// <summary>This node's direct child elements, in document order (text nodes are not included -
        /// see <see cref="GetTextContent"/>).</summary>
        IEnumerable<IMathSourceNode> Children { get; }

        /// <summary>The concatenated text content of this node's children - meaningful only for a token
        /// element (<c>mi</c>/<c>mn</c>/<c>mo</c>/<c>mtext</c>/<c>ms</c>), whose children are text runs,
        /// not further MathML elements.</summary>
        string GetTextContent();

        /// <summary>This element's already-cascaded CSS <c>color</c> (<c>CssBox.ActualColor</c> for the
        /// live-DOM implementation) - MathML's own <c>mathcolor</c> attribute, when present, overrides
        /// this at build time (see <see cref="MathTreeBuilder"/>).</summary>
        RColor Color { get; }

        /// <summary>This element's already-cascaded CSS <c>font-size</c>, in points
        /// (<c>CssBox.ActualFont.Size</c> for the live-DOM implementation) - the base <c>mathsize</c>
        /// resolves relative to.</summary>
        double FontSizePt { get; }

        /// <summary>Resolves this element's own font-family/style/weight at an explicit point size
        /// (<c>CssBox.GetActualFontAtSize</c> for the live-DOM implementation) - used by
        /// <c>MathLayoutEngine</c> to get the math typeface at each distinct scriptlevel-scaled size a
        /// formula needs, without re-deriving which size to use.</summary>
        RFont GetFontAtSize(double sizePt);
    }
}
