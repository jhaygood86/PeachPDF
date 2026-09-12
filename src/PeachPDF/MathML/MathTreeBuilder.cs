#region PeachPDF - A .NET library for rendering HTML to PDF
//
// Builds a MathNode presentation tree from an IMathSourceNode - the MathML equivalent of
// SvgTreeBuilder. A single recursive pass (unlike SvgTreeBuilder's two-pass id-collection + build:
// MathML has no forward-reference id system like SVG's url(#id), so nothing needs pre-registering).
//
// Presentation-markup element set: MathML Core's (mi/mn/mo/mtext/ms, mrow, mfrac, msqrt/mroot,
// mstyle, mspace, mphantom, mpadded, merror, msub/msup/msubsup, munder/mover/munderover,
// mmultiscripts, mtable/mtr/mlabeledtr/mtd, menclose), plus best-effort handling of the MathML-3-only
// elements it dropped: mfenced desugars to a row of synthesized mo fence/separator tokens around its
// children (well-defined, cheap); maction renders only its selected (or first) child, discarding the
// rest (no interactivity - PDF has no notion of it here); <semantics> renders only its first
// (presentation-markup) child, <annotation>/<annotation-xml> are recorded but never laid out/painted.
// Elementary math (mstack/mlongdiv) is out of scope entirely - see the accepted-gap note filed for it.
//
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using PeachPDF.Html.Adapters;

namespace PeachPDF.MathML
{
    internal static class MathTreeBuilder
    {
        /// <summary>Builds a <see cref="MathDocument"/> from the root <c>&lt;math&gt;</c> source node.</summary>
        public static MathDocument Build(IMathSourceNode mathRoot, RAdapter adapter)
        {
            var display = mathRoot.GetAttribute("display") == "block" ? "block" : "inline";

            // MathML Core §2.1: display="block" (or the legacy MathML 3 mode="display") starts
            // displaystyle true; inline starts it false.
            var context = new MathBuildContext(
                DisplayStyle: display == "block",
                ScriptLevel: 0,
                FontSizePt: mathRoot.FontSizePt,
                Adapter: adapter);

            context = ApplyStyleAttributes(mathRoot, context);

            // <math>'s own children follow the same "implicit row only if more than one" rule as
            // mtd/mstyle/mphantom (BuildRowOrSingle) - NOT mrow's own "always a row, even with one
            // child" rule. A single <semantics> child (by far the most common shape MathJax/Pandoc/
            // LaTeXML output produces) is then unwrapped to its first, presentation-markup child by
            // BuildNode's own "semantics" case below - there is no separate root-level special case for
            // it, since <math><semantics>...</semantics></math> is structurally no different from
            // <math><mrow>...</mrow></math> as far as this dispatch is concerned.
            var root = BuildRowOrSingle(mathRoot.Children.ToArray(), context);

            return new MathDocument { Root = root, Display = display, ResolveFont = mathRoot.GetFontAtSize };
        }

        /// <summary>Applies <c>displaystyle</c>/<c>scriptlevel</c>/<c>mathsize</c> overrides carried by
        /// <paramref name="node"/> itself - used both for <c>&lt;math&gt;</c>'s own attributes and for
        /// <c>&lt;mstyle&gt;</c>'s (MathML 3 §3.3.4). <c>mathcolor</c> is deliberately not folded into
        /// the threaded context here - see <see cref="MathNode.Color"/>'s own doc comment for why it's
        /// resolved per-node instead.</summary>
        static MathBuildContext ApplyStyleAttributes(IMathSourceNode node, MathBuildContext context)
        {
            var displayStyle = MathAttributeParser.ParseDisplayStyle(node.GetAttribute("displaystyle"), context.DisplayStyle);
            var scriptLevel = MathAttributeParser.ParseScriptLevel(node.GetAttribute("scriptlevel"), context.ScriptLevel);
            var fontSizePt = MathAttributeParser.ParseMathSize(node.GetAttribute("mathsize"), context.FontSizePt);
            return context with { DisplayStyle = displayStyle, ScriptLevel = scriptLevel, FontSizePt = fontSizePt };
        }

        /// <summary>This node's own effective color: its <c>mathcolor</c> attribute if present,
        /// otherwise its already-CSS-cascaded <see cref="IMathSourceNode.Color"/>.</summary>
        static Html.Adapters.Entities.RColor ResolveColor(IMathSourceNode node, MathBuildContext context) =>
            MathAttributeParser.TryParseColor(node.GetAttribute("mathcolor"), context.Adapter) ?? node.Color;

        /// <summary>Builds one child list as a single node (no wrapping) when it has exactly one entry,
        /// or an inferred <see cref="MathRowNode"/> otherwise (MathML Core's implicit-mrow rule for a
        /// schema whose grammar names a single "argument" position).</summary>
        static MathNode BuildRowOrSingle(IReadOnlyList<IMathSourceNode> children, MathBuildContext context)
        {
            if (children.Count == 1)
                return BuildNode(children[0], context);

            return new MathRowNode
            {
                DisplayStyle = context.DisplayStyle,
                ScriptLevel = context.ScriptLevel,
                Color = children.Count > 0 ? ResolveColor(children[0], context) : Html.Adapters.Entities.RColor.Empty,
                FontSizePt = context.FontSizePt,
                Children = children.Select(c => BuildNode(c, context)).ToArray(),
            };
        }

        static MathNode BuildNode(IMathSourceNode node, MathBuildContext context)
        {
            return node.Name switch
            {
                "mrow" => BuildRow(node, context),
                "mi" => BuildToken(node, context, MathTokenKind.Identifier),
                "mn" => BuildToken(node, context, MathTokenKind.Number),
                "mo" => BuildOperatorToken(node, context),
                "mtext" => BuildToken(node, context, MathTokenKind.Text),
                "ms" => BuildToken(node, context, MathTokenKind.StringLiteral),
                "mstyle" => BuildStyle(node, context),
                "mfrac" => BuildFraction(node, context),
                "msqrt" => BuildRadical(node, context, hasIndex: false),
                "mroot" => BuildRadical(node, context, hasIndex: true),
                "msub" => BuildScript(node, context, hasSub: true, hasSup: false),
                "msup" => BuildScript(node, context, hasSub: false, hasSup: true),
                "msubsup" => BuildScript(node, context, hasSub: true, hasSup: true),
                "munder" => BuildUnderOver(node, context, hasUnder: true, hasOver: false),
                "mover" => BuildUnderOver(node, context, hasUnder: false, hasOver: true),
                "munderover" => BuildUnderOver(node, context, hasUnder: true, hasOver: true),
                "mmultiscripts" => BuildMultiscripts(node, context),
                "mtable" => BuildTable(node, context),
                "mspace" => BuildSpace(node, context),
                "mphantom" => BuildPhantom(node, context),
                "mpadded" => BuildPadded(node, context),
                "menclose" => BuildEnclose(node, context),
                "merror" => BuildError(node, context),
                "mfenced" => BuildFenced(node, context),
                "maction" => BuildAction(node, context),
                "semantics" => BuildRowOrSingle(
                    node.Children.FirstOrDefault() is { } first ? [first] : Array.Empty<IMathSourceNode>(), context),
                _ => BuildRow(node, context), // unknown element: degrade to an anonymous row of its children
            };
        }

        static MathNode BuildRow(IMathSourceNode node, MathBuildContext context) => new MathRowNode
        {
            DisplayStyle = context.DisplayStyle,
            ScriptLevel = context.ScriptLevel,
            Color = ResolveColor(node, context),
            FontSizePt = context.FontSizePt,
            Children = node.Children.Select(c => BuildNode(c, context)).ToArray(),
        };

        static MathTokenNode BuildToken(IMathSourceNode node, MathBuildContext context, MathTokenKind kind)
        {
            var mathVariant = node.GetAttribute("mathvariant");
            var text = node.GetTextContent();
            if (kind == MathTokenKind.Identifier)
                text = ApplyAutomaticItalic(text, mathVariant);

            return new()
            {
                DisplayStyle = context.DisplayStyle,
                ScriptLevel = context.ScriptLevel,
                Color = ResolveColor(node, context),
                FontSizePt = context.FontSizePt,
                Kind = kind,
                Text = text,
                MathVariant = mathVariant,
            };
        }

        /// <summary>MathML Core §4.2's <c>math-auto</c> text-transform, which the UA stylesheet applies
        /// to every <c>&lt;mi&gt;</c> by default: a text node made of exactly one character is replaced
        /// by its Appendix C.1 "math italic" codepoint (<see cref="MathItalicMappings"/>), unless
        /// <c>mathvariant="normal"</c> cancels it. Only this one attribute-driven cancellation is
        /// consulted - an author overriding <c>text-transform</c> directly via CSS on <c>mi</c> (rather
        /// than the MathML <c>mathvariant</c> attribute) is not, since <see cref="IMathSourceNode"/>
        /// deliberately doesn't expose general CSS property resolution to this tree builder (see its own
        /// doc comment), and real-world markup uses <c>mathvariant="normal"</c> for this, not CSS.</summary>
        static string ApplyAutomaticItalic(string text, string? mathVariant)
        {
            if (mathVariant?.Trim() == "normal")
                return text;

            return text.Length == 1 && MathItalicMappings.TryGetItalic(text[0], out var italicCodePoint)
                ? char.ConvertFromUtf32(italicCodePoint)
                : text;
        }

        static MathTokenNode BuildOperatorToken(IMathSourceNode node, MathBuildContext context) => new()
        {
            DisplayStyle = context.DisplayStyle,
            ScriptLevel = context.ScriptLevel,
            Color = ResolveColor(node, context),
            FontSizePt = context.FontSizePt,
            Kind = MathTokenKind.Operator,
            Text = node.GetTextContent(),
            MathVariant = node.GetAttribute("mathvariant"),
            Form = MathAttributeParser.TryParseForm(node.GetAttribute("form")),
            Stretchy = MathAttributeParser.TryParseBool(node.GetAttribute("stretchy")),
            Symmetric = MathAttributeParser.TryParseBool(node.GetAttribute("symmetric")),
            LargeOp = MathAttributeParser.TryParseBool(node.GetAttribute("largeop")),
            MovableLimits = MathAttributeParser.TryParseBool(node.GetAttribute("movablelimits")),
            Accent = MathAttributeParser.TryParseBool(node.GetAttribute("accent")),
            Fence = MathAttributeParser.TryParseBool(node.GetAttribute("fence")),
            Separator = MathAttributeParser.TryParseBool(node.GetAttribute("separator")),
            LSpace = MathAttributeParser.TryParseLength(node.GetAttribute("lspace")),
            RSpace = MathAttributeParser.TryParseLength(node.GetAttribute("rspace")),
            MinSize = MathAttributeParser.TryParseLength(node.GetAttribute("minsize")),
            MaxSize = MathAttributeParser.TryParseLength(node.GetAttribute("maxsize")),
        };

        static MathNode BuildStyle(IMathSourceNode node, MathBuildContext context)
        {
            var styled = ApplyStyleAttributes(node, context);
            return BuildRowOrSingle(node.Children.ToArray(), styled);
        }

        static MathNode BuildFraction(IMathSourceNode node, MathBuildContext context)
        {
            var children = node.Children.ToArray();
            var numeratorSource = children.ElementAtOrDefault(0);
            var denominatorSource = children.ElementAtOrDefault(1);

            // MathML 3 §3.3.4: both the numerator and denominator are laid out one scriptlevel deeper,
            // and never in displaystyle, regardless of the fraction's own displaystyle.
            var childContext = context with { ScriptLevel = context.ScriptLevel + 1, DisplayStyle = false };

            return new MathFractionNode
            {
                DisplayStyle = context.DisplayStyle,
                ScriptLevel = context.ScriptLevel,
                Color = ResolveColor(node, context),
                FontSizePt = context.FontSizePt,
                Numerator = numeratorSource is not null ? BuildNode(numeratorSource, childContext) : EmptyRow(childContext),
                Denominator = denominatorSource is not null ? BuildNode(denominatorSource, childContext) : EmptyRow(childContext),
                LineThickness = MathAttributeParser.TryParseLength(node.GetAttribute("linethickness")),
                Bevelled = MathAttributeParser.TryParseBool(node.GetAttribute("bevelled")) ?? false,
            };
        }

        static MathNode BuildRadical(IMathSourceNode node, MathBuildContext context, bool hasIndex)
        {
            var children = node.Children.ToArray();

            if (!hasIndex)
            {
                // msqrt: an implicit mrow over ALL children, same displaystyle/scriptlevel as this node.
                return new MathRadicalNode
                {
                    DisplayStyle = context.DisplayStyle,
                    ScriptLevel = context.ScriptLevel,
                    Color = ResolveColor(node, context),
                    FontSizePt = context.FontSizePt,
                    Radicand = BuildRowOrSingle(children, context),
                    Index = null,
                };
            }

            // mroot: exactly two arguments - the radicand (same context as this node) and the index
            // (two scriptlevels deeper, never in displaystyle - MathML 3 §3.3.4).
            var radicandSource = children.ElementAtOrDefault(0);
            var indexSource = children.ElementAtOrDefault(1);
            var indexContext = context with { ScriptLevel = context.ScriptLevel + 2, DisplayStyle = false };

            return new MathRadicalNode
            {
                DisplayStyle = context.DisplayStyle,
                ScriptLevel = context.ScriptLevel,
                Color = ResolveColor(node, context),
                FontSizePt = context.FontSizePt,
                Radicand = radicandSource is not null ? BuildNode(radicandSource, context) : EmptyRow(context),
                Index = indexSource is not null ? BuildNode(indexSource, indexContext) : null,
            };
        }

        static MathNode BuildScript(IMathSourceNode node, MathBuildContext context, bool hasSub, bool hasSup)
        {
            var children = node.Children.ToArray();
            int i = 0;
            var baseSource = children.ElementAtOrDefault(i++);
            var subSource = hasSub ? children.ElementAtOrDefault(i++) : null;
            var supSource = hasSup ? children.ElementAtOrDefault(i) : null;

            // MathML 3 §3.3.4: sub/superscripts are laid out one scriptlevel deeper and never in
            // displaystyle; the base keeps the surrounding context unchanged.
            var scriptContext = context with { ScriptLevel = context.ScriptLevel + 1, DisplayStyle = false };

            return new MathScriptNode
            {
                DisplayStyle = context.DisplayStyle,
                ScriptLevel = context.ScriptLevel,
                Color = ResolveColor(node, context),
                FontSizePt = context.FontSizePt,
                Base = baseSource is not null ? BuildNode(baseSource, context) : EmptyRow(context),
                Sub = subSource is not null ? BuildNode(subSource, scriptContext) : null,
                Sup = supSource is not null ? BuildNode(supSource, scriptContext) : null,
            };
        }

        static MathNode BuildUnderOver(IMathSourceNode node, MathBuildContext context, bool hasUnder, bool hasOver)
        {
            var children = node.Children.ToArray();
            int i = 0;
            var baseSource = children.ElementAtOrDefault(i++);
            var underSource = hasUnder ? children.ElementAtOrDefault(i++) : null;
            var overSource = hasOver ? children.ElementAtOrDefault(i) : null;

            var accentOverride = MathAttributeParser.TryParseBool(node.GetAttribute("accent"));
            var accentUnderOverride = MathAttributeParser.TryParseBool(node.GetAttribute("accentunder"));

            // MathML 3 §3.3.4: a non-accent under/overscript is laid out one scriptlevel deeper and
            // never in displaystyle (the same rule as a limit); an accent is laid out at the surrounding
            // size, unshrunk. Whether a given script is treated as an accent depends on the *core
            // operator*'s own `accent` property when not overridden here - that's font/operator-
            // dictionary knowledge MathLayoutEngine has and this tree-building pass doesn't, so build
            // both the scaled and unscaled shape decision as "scaled unless this node's own explicit
            // accent(under) override says otherwise"; MathLayoutEngine still re-checks the core operator
            // for the common case where no explicit override is present (see its own remarks).
            var underContext = accentUnderOverride == true
                ? context
                : context with { ScriptLevel = context.ScriptLevel + 1, DisplayStyle = false };
            var overContext = accentOverride == true
                ? context
                : context with { ScriptLevel = context.ScriptLevel + 1, DisplayStyle = false };

            return new MathUnderOverNode
            {
                DisplayStyle = context.DisplayStyle,
                ScriptLevel = context.ScriptLevel,
                Color = ResolveColor(node, context),
                FontSizePt = context.FontSizePt,
                Base = baseSource is not null ? BuildNode(baseSource, context) : EmptyRow(context),
                Under = underSource is not null ? BuildNode(underSource, underContext) : null,
                Over = overSource is not null ? BuildNode(overSource, overContext) : null,
                AccentOverride = accentOverride,
                AccentUnderOverride = accentUnderOverride,
            };
        }

        static MathNode BuildMultiscripts(IMathSourceNode node, MathBuildContext context)
        {
            var children = node.Children.ToArray();
            var baseSource = children.ElementAtOrDefault(0);
            var scriptContext = context with { ScriptLevel = context.ScriptLevel + 1, DisplayStyle = false };

            var postPairs = new List<MathScriptPair>();
            var prePairs = new List<MathScriptPair>();
            var target = postPairs;

            // Remaining children after the base come in (sub, sup) pairs, until an optional
            // <mprescripts/> marker switches the rest into prescript pairs (MathML 3 §3.4.7). Either
            // half of a pair may be the placeholder <none/>, which contributes null.
            for (int i = 1; i < children.Length; i += 2)
            {
                if (children[i].Name == "mprescripts")
                {
                    target = prePairs;
                    i -= 1; // no pair consumed - realign so the next iteration starts right after the marker
                    continue;
                }

                var subSource = children[i];
                var supSource = children.ElementAtOrDefault(i + 1);

                var sub = subSource.Name == "none" ? null : BuildNode(subSource, scriptContext);
                var sup = supSource is null || supSource.Name == "none" ? null : BuildNode(supSource, scriptContext);
                target.Add(new MathScriptPair(sub, sup));
            }

            return new MathMultiscriptsNode
            {
                DisplayStyle = context.DisplayStyle,
                ScriptLevel = context.ScriptLevel,
                Color = ResolveColor(node, context),
                FontSizePt = context.FontSizePt,
                Base = baseSource is not null ? BuildNode(baseSource, context) : EmptyRow(context),
                PostScripts = postPairs,
                PreScripts = prePairs,
            };
        }

        static MathNode BuildTable(IMathSourceNode node, MathBuildContext context)
        {
            // MathML 3 §3.3.4: table cells default to displaystyle=false regardless of the table's own.
            var cellContext = context with { DisplayStyle = false };
            var columnAlign = node.GetAttribute("columnalign");
            var rowAlign = node.GetAttribute("rowalign");

            var rows = node.Children.Where(c => c.Name is "mtr" or "mlabeledtr")
                .Select(row => BuildTableRow(row, cellContext))
                .ToArray();

            return new MathTableNode
            {
                DisplayStyle = context.DisplayStyle,
                ScriptLevel = context.ScriptLevel,
                Color = ResolveColor(node, context),
                FontSizePt = context.FontSizePt,
                Rows = rows,
                ColumnAlign = columnAlign,
                RowAlign = rowAlign,
            };
        }

        static MathTableRowNode BuildTableRow(IMathSourceNode row, MathBuildContext cellContext)
        {
            var cellSources = row.Children.ToArray();
            MathTableCellNode? label = null;
            int start = 0;

            if (row.Name == "mlabeledtr" && cellSources.Length > 0)
            {
                label = BuildTableCell(cellSources[0], cellContext);
                start = 1;
            }

            var cells = cellSources.Skip(start).Select(c => BuildTableCell(c, cellContext)).ToArray();
            return new MathTableRowNode
            {
                DisplayStyle = cellContext.DisplayStyle,
                ScriptLevel = cellContext.ScriptLevel,
                Color = cellSources.Length > 0 ? ResolveColor(cellSources[0], cellContext) : Html.Adapters.Entities.RColor.Empty,
                FontSizePt = cellContext.FontSizePt,
                Cells = cells,
                Label = label,
            };
        }

        static MathTableCellNode BuildTableCell(IMathSourceNode cellSource, MathBuildContext cellContext)
        {
            // A bare mtd's own child, per MathML Core, is itself already the "mtd" element (mtable's
            // rows/cells are structural, not free-form) - but a plain non-mtd child (malformed markup)
            // is tolerated by treating it as its own cell content directly.
            var isMtd = cellSource.Name == "mtd";
            var contentChildren = isMtd ? cellSource.Children.ToArray() : [cellSource];

            return new MathTableCellNode
            {
                DisplayStyle = cellContext.DisplayStyle,
                ScriptLevel = cellContext.ScriptLevel,
                Color = ResolveColor(cellSource, cellContext),
                FontSizePt = cellContext.FontSizePt,
                Content = BuildRowOrSingle(contentChildren, cellContext),
                ColumnAlign = isMtd ? cellSource.GetAttribute("columnalign") : null,
                RowAlign = isMtd ? cellSource.GetAttribute("rowalign") : null,
            };
        }

        static MathNode BuildSpace(IMathSourceNode node, MathBuildContext context) => new MathSpaceNode
        {
            DisplayStyle = context.DisplayStyle,
            ScriptLevel = context.ScriptLevel,
            Color = ResolveColor(node, context),
            FontSizePt = context.FontSizePt,
            Width = MathAttributeParser.TryParseLength(node.GetAttribute("width")) ?? MathLength.Zero,
            Height = MathAttributeParser.TryParseLength(node.GetAttribute("height")) ?? MathLength.Zero,
            Depth = MathAttributeParser.TryParseLength(node.GetAttribute("depth")) ?? MathLength.Zero,
        };

        static MathNode BuildPhantom(IMathSourceNode node, MathBuildContext context) => new MathPhantomNode
        {
            DisplayStyle = context.DisplayStyle,
            ScriptLevel = context.ScriptLevel,
            Color = ResolveColor(node, context),
            FontSizePt = context.FontSizePt,
            Content = BuildRowOrSingle(node.Children.ToArray(), context),
        };

        static MathNode BuildPadded(IMathSourceNode node, MathBuildContext context) => new MathPaddedNode
        {
            DisplayStyle = context.DisplayStyle,
            ScriptLevel = context.ScriptLevel,
            Color = ResolveColor(node, context),
            FontSizePt = context.FontSizePt,
            Content = BuildRowOrSingle(node.Children.ToArray(), context),
            Width = node.GetAttribute("width"),
            Height = node.GetAttribute("height"),
            Depth = node.GetAttribute("depth"),
            LSpace = node.GetAttribute("lspace"),
            VOffset = node.GetAttribute("voffset"),
        };

        static MathNode BuildEnclose(IMathSourceNode node, MathBuildContext context) => new MathEncloseNode
        {
            DisplayStyle = context.DisplayStyle,
            ScriptLevel = context.ScriptLevel,
            Color = ResolveColor(node, context),
            FontSizePt = context.FontSizePt,
            Content = BuildRowOrSingle(node.Children.ToArray(), context),
            Notation = node.GetAttribute("notation") ?? "longdiv",
        };

        static MathNode BuildError(IMathSourceNode node, MathBuildContext context) => new MathErrorNode
        {
            DisplayStyle = context.DisplayStyle,
            ScriptLevel = context.ScriptLevel,
            Color = ResolveColor(node, context),
            FontSizePt = context.FontSizePt,
            Content = BuildRowOrSingle(node.Children.ToArray(), context),
        };

        /// <summary>Desugars <c>&lt;mfenced&gt;</c> (MathML 3 §3.3.9, dropped by MathML Core) into a
        /// plain row: an open-fence <c>mo</c>, each child separated by a cyclically-repeated separator
        /// <c>mo</c> (from the <c>separators</c> attribute, default <c>","</c>), and a close-fence
        /// <c>mo</c> - the same shape authoring an equivalent <c>&lt;mrow&gt;</c> by hand would produce.
        /// An empty <c>open</c>/<c>close</c> attribute omits that fence entirely.</summary>
        static MathNode BuildFenced(IMathSourceNode node, MathBuildContext context)
        {
            var open = node.GetAttribute("open") ?? "(";
            var close = node.GetAttribute("close") ?? ")";
            var separators = (node.GetAttribute("separators") ?? ",").Where(c => !char.IsWhiteSpace(c)).ToArray();

            var children = node.Children.ToArray();
            var items = new List<MathNode>();
            var color = ResolveColor(node, context);

            if (open.Length > 0)
                items.Add(MakeFenceToken(open, context, color, fence: true, form: "prefix"));

            for (int i = 0; i < children.Length; i++)
            {
                if (i > 0 && separators.Length > 0)
                {
                    var separator = separators[Math.Min(i - 1, separators.Length - 1)];
                    items.Add(MakeFenceToken(separator.ToString(), context, color, fence: false, form: "infix", separator: true));
                }

                items.Add(BuildNode(children[i], context));
            }

            if (close.Length > 0)
                items.Add(MakeFenceToken(close, context, color, fence: true, form: "postfix"));

            return new MathRowNode
            {
                DisplayStyle = context.DisplayStyle,
                ScriptLevel = context.ScriptLevel,
                Color = color,
                FontSizePt = context.FontSizePt,
                Children = items,
            };
        }

        /// <summary>Builds a synthesized (not backed by any real source element) fence/separator token
        /// for <see cref="BuildFenced"/> - inherits <paramref name="color"/> from the enclosing
        /// <c>&lt;mfenced&gt;</c> itself, since a synthesized token has no source node of its own to
        /// read a cascaded color from.</summary>
        static MathTokenNode MakeFenceToken(
            string text, MathBuildContext context, Html.Adapters.Entities.RColor color, bool fence, string form, bool separator = false) => new()
        {
            DisplayStyle = context.DisplayStyle,
            ScriptLevel = context.ScriptLevel,
            Color = color,
            FontSizePt = context.FontSizePt,
            Kind = MathTokenKind.Operator,
            Text = text,
            Form = form,
            Fence = fence,
            Separator = separator,
            Stretchy = fence ? true : null,
        };

        /// <summary>Renders only the selected (or, absent a valid <c>selection</c>, the first) child of
        /// <c>&lt;maction&gt;</c> (MathML 3 §3.7, reduced to this minimal behavior by MathML Core - PDF
        /// has no notion of the toggle/tooltip/statusline interactivity the other action types imply).
        /// An <c>&lt;maction&gt;</c> with no children renders as an empty row.</summary>
        static MathNode BuildAction(IMathSourceNode node, MathBuildContext context)
        {
            var children = node.Children.ToArray();
            if (children.Length == 0)
                return EmptyRow(context);

            var selection = 1;
            if (int.TryParse(node.GetAttribute("selection"), out var parsed) && parsed is >= 1)
                selection = parsed;

            var index = Math.Min(selection, children.Length) - 1;
            return BuildNode(children[index], context);
        }

        static MathRowNode EmptyRow(MathBuildContext context) => new()
        {
            DisplayStyle = context.DisplayStyle,
            ScriptLevel = context.ScriptLevel,
            Color = Html.Adapters.Entities.RColor.Empty,
            FontSizePt = context.FontSizePt,
            Children = [],
        };
    }
}
