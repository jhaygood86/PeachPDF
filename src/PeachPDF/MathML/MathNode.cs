#region PeachPDF - A .NET library for rendering HTML to PDF
//
// The MathML presentation-tree scene-graph model, mirroring how SvgElement models the SVG scene
// graph: a plain, polymorphic data structure built once by MathTreeBuilder and then read by
// MathLayoutEngine (never a CssBox, never participates in CSS layout). One concrete type per MathML
// Core layout category (MathML 3 presentation markup elements MathML Core dropped - mfenced,
// elementary math, most of maction - are desugared/reduced at build time; see MathTreeBuilder).
//
#endregion

using System.Collections.Generic;

namespace PeachPDF.MathML
{
    /// <summary>Base type for every node in a built MathML presentation tree.</summary>
    internal abstract class MathNode
    {
        /// <summary>The <c>displaystyle</c> this node is laid out under (MathML Core's own inherited
        /// styling context, distinct from CSS display) - already resolved by <c>MathTreeBuilder</c>
        /// per MathML 3 §3.3.4's inheritance/override rules.</summary>
        public required bool DisplayStyle { get; init; }

        /// <summary>The <c>scriptlevel</c> this node is laid out under - already resolved (absolute,
        /// not a raw <c>+n</c>/<c>-n</c> delta) by <c>MathTreeBuilder</c>.</summary>
        public required int ScriptLevel { get; init; }

        /// <summary>This node's own <c>mathcolor</c>, or the inherited value if unset - resolved eagerly
        /// so <c>MathRenderer</c> never needs to walk back up the tree for it.</summary>
        public required Html.Adapters.Entities.RColor Color { get; init; }

        /// <summary>This node's base font size in points, <b>before</b> any <c>scriptlevel</c>-driven
        /// scaling - the CSS-cascaded <c>font-size</c> as further overridden by a <c>mathsize</c> chain
        /// (MathML 3 §3.2.2). Actual scriptlevel-driven scaling (via the resolved math font's
        /// <c>MathConstants.ScriptPercentScaleDown</c>/<c>ScriptScriptPercentScaleDown</c>) is
        /// <c>MathLayoutEngine</c>'s job, not this tree's - it needs the chosen math typeface, which
        /// isn't resolved yet when this tree is built.</summary>
        public required double FontSizePt { get; init; }
    }

    /// <summary>Horizontal grouping of a sequence of children (<c>mrow</c>), and MathML Core's implicit
    /// wrapping of a schema's multi-child arguments (e.g. an <c>msqrt</c> with more than one child, or
    /// an <c>mtd</c>'s multiple children) into one inferred row.</summary>
    internal sealed class MathRowNode : MathNode
    {
        public required IReadOnlyList<MathNode> Children { get; init; }
    }

    /// <summary>One of MathML's token elements (<c>mi</c>/<c>mn</c>/<c>mo</c>/<c>mtext</c>/<c>ms</c>) -
    /// a run of text with a font-variant hint and, for <c>mo</c> only, the operator properties below.</summary>
    internal sealed class MathTokenNode : MathNode
    {
        public required MathTokenKind Kind { get; init; }
        public required string Text { get; init; }

        /// <summary><c>mathvariant</c>, e.g. <c>"italic"</c>/<c>"bold"</c>/<c>"double-struck"</c> - the
        /// raw keyword; mapping it to the corresponding Unicode Mathematical Alphanumeric Symbols
        /// codepoints (or a synthesized style) is <c>MathLayoutEngine</c>'s job, not the tree's.</summary>
        public string? MathVariant { get; init; }

        // ---- <mo>-only operator properties (MathML 3 §3.2.5). Each is null when the author didn't
        // specify it explicitly; MathLayoutEngine applies its own operator-dictionary-informed default
        // for anything left null - this tree only records what was actually authored. ----

        public string? Form { get; init; }
        public bool? Stretchy { get; init; }
        public bool? Symmetric { get; init; }
        public bool? LargeOp { get; init; }
        public bool? MovableLimits { get; init; }
        public bool? Accent { get; init; }
        public bool? Fence { get; init; }
        public bool? Separator { get; init; }
        public MathLength? LSpace { get; init; }
        public MathLength? RSpace { get; init; }
        public MathLength? MinSize { get; init; }
        public MathLength? MaxSize { get; init; }
    }

    internal enum MathTokenKind { Identifier, Number, Operator, Text, StringLiteral }

    /// <summary>A fraction (<c>mfrac</c>).</summary>
    internal sealed class MathFractionNode : MathNode
    {
        public required MathNode Numerator { get; init; }
        public required MathNode Denominator { get; init; }

        /// <summary><c>linethickness</c> - null means the font/MathConstants-driven default.</summary>
        public MathLength? LineThickness { get; init; }

        /// <summary><c>bevelled="true"</c> (a slanted fraction bar, e.g. "1/2" style) - a MathML 3
        /// presentation attribute MathML Core does not define at all (confirmed: absent from the spec
        /// text), so this is parsed only for round-tripping/best-effort purposes; see the accepted-gap
        /// note for why it has no rendering effect.</summary>
        public bool Bevelled { get; init; }
    }

    /// <summary>A radical: <c>msqrt</c> (<see cref="Index"/> null) or <c>mroot</c> (<see cref="Index"/> set).</summary>
    internal sealed class MathRadicalNode : MathNode
    {
        public required MathNode Radicand { get; init; }
        public MathNode? Index { get; init; }
    }

    /// <summary>A sub/superscript pair: <c>msub</c> (<see cref="Sup"/> null), <c>msup</c>
    /// (<see cref="Sub"/> null), or <c>msubsup</c> (both set).</summary>
    internal sealed class MathScriptNode : MathNode
    {
        public required MathNode Base { get; init; }
        public MathNode? Sub { get; init; }
        public MathNode? Sup { get; init; }
    }

    /// <summary>An under/overscript pair: <c>munder</c> (<see cref="Over"/> null), <c>mover</c>
    /// (<see cref="Under"/> null), or <c>munderover</c> (both set).</summary>
    internal sealed class MathUnderOverNode : MathNode
    {
        public required MathNode Base { get; init; }
        public MathNode? Under { get; init; }
        public MathNode? Over { get; init; }

        /// <summary>Author-specified <c>accent</c>/<c>accentunder</c> overrides (MathML 3 §3.4.4/3.4.5);
        /// null defers to the over/under child's own core-operator <c>accent</c> property.</summary>
        public bool? AccentOverride { get; init; }
        public bool? AccentUnderOverride { get; init; }
    }

    /// <summary>One (sub, sup) pair in an <c>mmultiscripts</c> post- or pre-script list - either half
    /// may be the MathML placeholder <c>&lt;none/&gt;</c>, represented here as null.</summary>
    internal readonly record struct MathScriptPair(MathNode? Sub, MathNode? Sup);

    /// <summary><c>mmultiscripts</c>: a base plus arbitrarily many post-script pairs and (after
    /// <c>&lt;mprescripts/&gt;</c>) pre-script pairs.</summary>
    internal sealed class MathMultiscriptsNode : MathNode
    {
        public required MathNode Base { get; init; }
        public required IReadOnlyList<MathScriptPair> PostScripts { get; init; }
        public required IReadOnlyList<MathScriptPair> PreScripts { get; init; }
    }

    /// <summary>One cell of an <c>mtable</c> row (<c>mtd</c>) - multiple children are wrapped in an
    /// inferred <see cref="MathRowNode"/> by the builder, so <see cref="Content"/> is always a single node.</summary>
    internal sealed class MathTableCellNode : MathNode
    {
        public required MathNode Content { get; init; }
        public string? ColumnAlign { get; init; }
        public string? RowAlign { get; init; }
    }

    /// <summary>One row of an <c>mtable</c> (<c>mtr</c> or <c>mlabeledtr</c>).</summary>
    internal sealed class MathTableRowNode : MathNode
    {
        public required IReadOnlyList<MathTableCellNode> Cells { get; init; }

        /// <summary>The label cell of an <c>mlabeledtr</c> (its first child, per MathML 3 §3.5.3 -
        /// not counted among <see cref="Cells"/>), or null for a plain <c>mtr</c>.</summary>
        public MathTableCellNode? Label { get; init; }
    }

    /// <summary>A table/matrix (<c>mtable</c>).</summary>
    internal sealed class MathTableNode : MathNode
    {
        public required IReadOnlyList<MathTableRowNode> Rows { get; init; }
        public string? ColumnAlign { get; init; }
        public string? RowAlign { get; init; }
    }

    /// <summary>Explicit blank space (<c>mspace</c>).</summary>
    internal sealed class MathSpaceNode : MathNode
    {
        public required MathLength Width { get; init; }
        public required MathLength Height { get; init; }
        public required MathLength Depth { get; init; }
    }

    /// <summary>Content whose size is reserved but which is never actually drawn (<c>mphantom</c>).</summary>
    internal sealed class MathPhantomNode : MathNode
    {
        public required MathNode Content { get; init; }
    }

    /// <summary>Adjusts the space reserved around its content (<c>mpadded</c>) - the raw attribute
    /// strings are kept as-is (MathML 3 §3.3.6's grammar allows relative adjustments like <c>"+2pt"</c>
    /// or <c>"150%"</c> against the content's own natural metrics, which only <c>MathLayoutEngine</c>,
    /// not this tree, has enough context to resolve).</summary>
    internal sealed class MathPaddedNode : MathNode
    {
        public required MathNode Content { get; init; }
        public string? Width { get; init; }
        public string? Height { get; init; }
        public string? Depth { get; init; }
        public string? LSpace { get; init; }
        public string? VOffset { get; init; }
    }

    /// <summary>Encloses its content in a notation (<c>menclose</c>, MathML 3 §3.3.8) - <see cref="Notation"/>
    /// is the raw, space-separated <c>notation</c> attribute value (e.g. <c>"box"</c>, <c>"roundedbox"</c>,
    /// <c>"circle"</c>, <c>"top bottom"</c>, <c>"radical"</c>, ...; defaults to <c>"longdiv"</c> per spec
    /// if absent) - left unparsed here since only <c>MathLayoutEngine</c>/<c>MathRenderer</c> need to
    /// interpret which notations apply.</summary>
    internal sealed class MathEncloseNode : MathNode
    {
        public required MathNode Content { get; init; }
        public required string Notation { get; init; }
    }

    /// <summary>A syntax-error placeholder (<c>merror</c>) - rendered like an <c>mrow</c> of its content
    /// (MathML Core drops the "highlight as an error" presentational suggestion; PeachPDF renders the
    /// content plainly, same as any other row).</summary>
    internal sealed class MathErrorNode : MathNode
    {
        public required MathNode Content { get; init; }
    }
}
