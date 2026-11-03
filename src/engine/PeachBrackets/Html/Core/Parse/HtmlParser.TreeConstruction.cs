using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Html.Parse
{
    /// <summary>
    /// The HTML tree construction algorithm (<see href="https://html.spec.whatwg.org/multipage/parsing.html#tree-construction"/>) over the box
    /// tree: the stack of open elements, the list of active formatting elements, scopes, the adoption agency algorithm, the "in body", table
    /// and select insertion modes, and foster parenting.
    /// </summary>
    /// <remarks>
    /// The insertion mode is not stored: it is read off the stack (<see cref="ModeOf"/>), because the standard switches mode exactly when it
    /// pushes or pops the element that names it. Foster parenting is why the stack is a list of its own: an element fostered out of a table is
    /// on the stack above the table but not in it.
    /// </remarks>
    internal static partial class HtmlParser
    {
        /// <summary>What a parse is in the middle of: the root it builds under, the stack of open elements, the list of active formatting elements.</summary>
        private sealed class ParserState(CssBox root)
        {
            /// <summary>The box the document is built under; below the stack of open elements and not itself an element of it.</summary>
            public CssBox Root { get; } = root;

            /// <summary>The stack of open elements; the outermost first.</summary>
            public List<CssBox> Stack { get; } = [];

            /// <summary>The current node: the innermost open element, or <see cref="Root"/> when none is.</summary>
            public CssBox Current => Stack.Count > 0 ? Stack[^1] : Root;

            /// <summary>The list of active formatting elements; oldest first.</summary>
            public List<FormattingEntry> Formatting { get; } = [];

            /// <summary>Whether an element inserted where a table, row or section is the current node goes before the table instead.</summary>
            public bool FosterParenting { get; set; }

            /// <summary>The form element pointer: the last <c>form</c> opened and not yet closed by its end tag.</summary>
            public CssBox? FormPointer { get; set; }

            /// <summary>The head element pointer: the <c>head</c> of a document being parsed.</summary>
            public CssBox? HeadPointer { get; set; }

            /// <summary>Whether this is a whole document (the document insertion modes apply) rather than a fragment.</summary>
            public bool IsDocument { get; init; }

            /// <summary>Where a document is, from before its <c>html</c> element to after its end; <see cref="DocumentPhase.InBody"/> from the start for a fragment.</summary>
            public DocumentPhase Phase { get; set; } = DocumentPhase.InBody;

            /// <summary>The stack of template insertion modes: one for each open <c>template</c>, the innermost last.</summary>
            public List<Mode> TemplateModes { get; } = [];

            /// <summary>Whether a line feed that starts the next text is dropped (after <c>pre</c>, <c>listing</c> and <c>textarea</c>).</summary>
            public bool IgnoreNextNewline { get; set; }
        }

        /// <summary>The document-level insertion modes, which are not a function of the stack.</summary>
        private enum DocumentPhase
        {
            Initial,
            BeforeHtml,
            BeforeHead,
            InHead,
            AfterHead,
            InBody,
            AfterBody,
            AfterAfterBody
        }

        /// <summary>An element in the list of active formatting elements, a marker, or the adoption agency's bookmark.</summary>
        private sealed class FormattingEntry
        {
            private FormattingEntry(CssBox? box, CssBox? owner, bool isBookmark)
            {
                Box = box;
                Owner = owner;
                IsBookmark = isBookmark;
            }

            public static FormattingEntry ForElement(CssBox box) => new(box, null, false);

            /// <summary>A marker, which a <c>td</c>, <c>th</c>, <c>caption</c>, <c>applet</c>, <c>object</c> or <c>marquee</c> puts on the list while it is open.</summary>
            public static FormattingEntry MarkerFor(CssBox owner) => new(null, owner, false);

            public static FormattingEntry Bookmark() => new(null, null, true);

            /// <summary>The element; null for a marker and the bookmark.</summary>
            public CssBox? Box { get; set; }

            /// <summary>The element whose being open a marker stands for.</summary>
            public CssBox? Owner { get; }

            public bool IsBookmark { get; }

            public bool IsMarker => Box is null && !IsBookmark;
        }

        /// <summary>A start tag, as the insertion modes pass it around.</summary>
        private readonly record struct StartTag(string Name, string LowerName, Dictionary<string, string>? Attributes, bool IsSingle);

        /// <summary>Where a node goes: as the last child of <see cref="Parent"/>, or in front of <see cref="Before"/> in it.</summary>
        private readonly record struct InsertionPlace(CssBox Parent, CssBox? Before);

        private enum Scope
        {
            Default,
            ListItem,
            Button,
            Table
        }

        private enum Mode
        {
            InBody,
            InTemplate,
            InTable,
            InCaption,
            InColumnGroup,
            InTableBody,
            InRow,
            InCell,
            InSelect,
            InSelectInTable
        }

        private const int AdoptionOuterLoopLimit = 8;
        private const int AdoptionInnerLoopLimit = 3;
        private const int NoahsArkLimit = 3;
        private const int ReprocessLimit = 16;

        private static FrozenSet<string> Names(params string[] names) => names.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        private static readonly FrozenSet<string> FormattingNames =
            Names("a", "b", "big", "code", "em", "font", "i", "nobr", "s", "small", "strike", "strong", "tt", "u");

        // https://html.spec.whatwg.org/multipage/parsing.html#special
        private static readonly FrozenSet<string> SpecialNames = Names(
            "address", "applet", "area", "article", "aside", "base", "basefont", "bgsound", "blockquote", "body", "br", "button", "caption",
            "center", "col", "colgroup", "dd", "details", "dir", "div", "dl", "dt", "embed", "fieldset", "figcaption", "figure", "footer",
            "form", "frame", "frameset", "h1", "h2", "h3", "h4", "h5", "h6", "head", "header", "hgroup", "hr", "html", "iframe", "img",
            "input", "keygen", "li", "link", "listing", "main", "marquee", "menu", "meta", "nav", "noembed", "noframes", "noscript",
            "object", "ol", "p", "param", "plaintext", "pre", "script", "search", "section", "select", "source", "style", "summary",
            "table", "tbody", "td", "template", "textarea", "tfoot", "th", "thead", "title", "tr", "track", "ul", "wbr", "xmp");

        // The SVG and MathML elements that are in the special category and bound a scope (case matters in foreign content).
        private static readonly FrozenSet<string> ForeignIntegrationNames =
            new[] { "foreignObject", "desc", "title", "mi", "mo", "mn", "ms", "mtext", "annotation-xml" }.ToFrozenSet(StringComparer.Ordinal);

        private static readonly FrozenSet<string> ScopeBoundaryNames =
            Names("applet", "caption", "html", "table", "td", "th", "marquee", "object", "template");

        // The start tags that close a p element in button scope.
        private static readonly FrozenSet<string> ParagraphClosingStartTags = Names(
            "address", "article", "aside", "blockquote", "center", "details", "dialog", "dir", "div", "dl", "fieldset", "figcaption",
            "figure", "footer", "header", "hgroup", "main", "menu", "nav", "ol", "p", "search", "section", "summary", "ul",
            "h1", "h2", "h3", "h4", "h5", "h6", "pre", "listing", "form", "li", "dd", "dt", "plaintext", "table", "hr", "xmp");

        // The start tags that do not reconstruct the active formatting elements before they insert an element.
        private static readonly FrozenSet<string> NoReconstructStartTags = Names(
            "html", "body", "base", "basefont", "bgsound", "link", "meta", "noframes", "script", "style", "template", "title", "head",
            "param", "source", "track", "textarea", "iframe", "noembed", "noscript", "rb", "rtc", "rp", "rt", "caption", "col",
            "colgroup", "frame", "frameset", "tbody", "td", "th", "thead", "tfoot", "tr",
            "address", "article", "aside", "blockquote", "center", "details", "dialog", "dir", "div", "dl", "fieldset", "figcaption",
            "figure", "footer", "header", "hgroup", "main", "menu", "nav", "ol", "p", "search", "section", "summary", "ul",
            "h1", "h2", "h3", "h4", "h5", "h6", "pre", "listing", "form", "li", "dd", "dt", "plaintext", "table", "hr");

        // The start tags of table parts that mean nothing outside a table: ignored in body.
        private static readonly FrozenSet<string> TablePartStartTags =
            Names("caption", "col", "colgroup", "tbody", "td", "tfoot", "th", "thead", "tr");

        // The end tags that close their element when it is in scope, and are ignored when it is not.
        private static readonly FrozenSet<string> ScopedEndTags = Names(
            "address", "article", "aside", "blockquote", "button", "center", "details", "dialog", "dir", "div", "dl", "fieldset",
            "figcaption", "figure", "footer", "header", "hgroup", "listing", "main", "menu", "nav", "ol", "pre", "search", "section",
            "summary", "ul", "dd", "dt", "applet", "marquee", "object");

        // The end tags of a fragment's html and body, and of the legacy frame elements, are answered by closing the nearest open element of that name.
        private static readonly FrozenSet<string> NearestAncestorEndTags = Names("html", "body", "head", "frameset");

        // Text goes to the current node without the active formatting elements being reconstructed in these.
        private static readonly FrozenSet<string> NoReconstructTextParents =
            Names("head", "html", "title", "style", "script", "textarea", "noscript");

        // Elements whose content is text, not markup: the tokenizer hands it over as one run, and it goes into the element whatever the mode is.
        private static readonly FrozenSet<string> RawTextNames =
            Names("title", "style", "script", "textarea", "xmp", "iframe", "noembed", "noframes", "plaintext");

        // The start tags that break out of foreign content.
        private static readonly FrozenSet<string> ForeignBreakoutNames = Names(
            "b", "big", "blockquote", "body", "br", "center", "code", "dd", "div", "dl", "dt", "em", "embed", "h1", "h2", "h3", "h4", "h5", "h6",
            "head", "hr", "i", "img", "li", "listing", "menu", "meta", "nobr", "ol", "p", "pre", "ruby", "s", "small", "span", "strong", "strike",
            "sub", "sup", "table", "tt", "u", "ul", "var");

        // SVG element names the tokenizer lowercases but the standard gives mixed case.
        private static readonly FrozenDictionary<string, string> SvgElementNames = new[]
        {
            "altGlyph", "altGlyphDef", "altGlyphItem", "animateColor", "animateMotion", "animateTransform", "clipPath", "feBlend", "feColorMatrix",
            "feComponentTransfer", "feComposite", "feConvolveMatrix", "feDiffuseLighting", "feDisplacementMap", "feDistantLight", "feDropShadow",
            "feFlood", "feFuncA", "feFuncB", "feFuncG", "feFuncR", "feGaussianBlur", "feImage", "feMerge", "feMergeNode", "feMorphology",
            "feOffset", "fePointLight", "feSpecularLighting", "feSpotLight", "feTile", "feTurbulence", "foreignObject", "glyphRef",
            "linearGradient", "radialGradient", "textPath"
        }.ToFrozenDictionary(n => n.ToLowerInvariant(), n => n);

        private static readonly FrozenSet<string> TableStructureNames = Names("table", "tbody", "tfoot", "thead", "tr");
        private static readonly FrozenSet<string> CaptionAndCellNames = Names("caption", "td", "th");
        private static readonly FrozenSet<string> TableContextNames = Names("table", "template", "html");
        private static readonly FrozenSet<string> TableBodyContextNames = Names("tbody", "tfoot", "thead", "template", "html");
        private static readonly FrozenSet<string> RowContextNames = Names("tr", "template", "html");
        private static readonly FrozenSet<string> ImpliedEndTagNames = Names("dd", "dt", "li", "optgroup", "option", "p", "rb", "rp", "rt", "rtc");

        private static bool IsNamed(CssBox box, string name) =>
            box.HtmlTag is { } tag && !box.IsWithinForeignContent && tag.Name.Equals(name, StringComparison.OrdinalIgnoreCase);

        private static bool IsAmong(CssBox box, FrozenSet<string> names) =>
            box.HtmlTag is { } tag && !box.IsWithinForeignContent && names.Contains(tag.Name);

        private static bool IsHeading(CssBox box) =>
            box.HtmlTag is { Name: { Length: 2 } n } && !box.IsWithinForeignContent && (n[0] is 'h' or 'H') && n[1] is >= '1' and <= '6';

        private static bool IsSpecial(CssBox box) =>
            box.HtmlTag is not { } tag || (box.IsWithinForeignContent ? ForeignIntegrationNames.Contains(tag.Name) : SpecialNames.Contains(tag.Name));

        private static bool IsHtmlWhitespace(string text)
        {
            foreach (var c in text)
            {
                if (c is not (' ' or '\t' or '\n' or '\r' or '\f')) return false;
            }

            return true;
        }

        private static bool IsInStack(ParserState state, CssBox box)
        {
            var stack = state.Stack;
            for (var i = stack.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(stack[i], box)) return true;
            }

            return false;
        }

        private static int IndexInStack(ParserState state, CssBox box)
        {
            var stack = state.Stack;
            for (var i = stack.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(stack[i], box)) return i;
            }

            return -1;
        }

        // Pops the elements down to and including the one given (which must be open).
        private static void PopThrough(ParserState state, CssBox element)
        {
            var index = IndexInStack(state, element);
            if (index < 0) return;

            state.Stack.RemoveRange(index, state.Stack.Count - index);
            AfterPop(state);
        }

        private static void Pop(ParserState state)
        {
            if (state.Stack.Count == 0) return;

            state.Stack.RemoveAt(state.Stack.Count - 1);
            AfterPop(state);
        }

        // What follows elements leaving the stack: their markers go from the list of active formatting elements and their templates' modes from the stack of modes.
        private static void AfterPop(ParserState state)
        {
            ClearClosedMarkers(state);

            if (state.TemplateModes.Count > 0)
            {
                var open = state.Stack.Count(b => IsNamed(b, "template"));
                while (state.TemplateModes.Count > open)
                    state.TemplateModes.RemoveAt(state.TemplateModes.Count - 1);
            }
        }

        private static bool IsScopeBoundary(CssBox box, Scope scope)
        {
            var name = box.HtmlTag!.Name;
            if (box.IsWithinForeignContent)
                return scope != Scope.Table && ForeignIntegrationNames.Contains(name);

            if (scope == Scope.Table)
                return name.Equals("html", StringComparison.OrdinalIgnoreCase) || name.Equals("table", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("template", StringComparison.OrdinalIgnoreCase);

            return ScopeBoundaryNames.Contains(name)
                || (scope == Scope.Button && name.Equals("button", StringComparison.OrdinalIgnoreCase))
                || (scope == Scope.ListItem && (name.Equals("ol", StringComparison.OrdinalIgnoreCase) || name.Equals("ul", StringComparison.OrdinalIgnoreCase)));
        }

        // The open element with this name that is in scope (https://html.spec.whatwg.org/multipage/parsing.html#has-an-element-in-the-specific-scope), or null.
        private static CssBox? FindInScope(ParserState state, string name, Scope scope = Scope.Default)
        {
            for (var i = state.Stack.Count - 1; i >= 0; i--)
            {
                var open = state.Stack[i];
                if (IsNamed(open, name)) return open;
                if (IsScopeBoundary(open, scope)) return null;
            }

            return null;
        }

        private static bool IsInScope(ParserState state, CssBox element)
        {
            for (var i = state.Stack.Count - 1; i >= 0; i--)
            {
                var open = state.Stack[i];
                if (ReferenceEquals(open, element)) return true;
                if (IsScopeBoundary(open, Scope.Default)) return false;
            }

            return false;
        }

        private static bool InTableScope(ParserState state, string name) => FindInScope(state, name, Scope.Table) is not null;

        private static bool HasSectionInTableScope(ParserState state)
        {
            for (var i = state.Stack.Count - 1; i >= 0; i--)
            {
                var open = state.Stack[i];
                if (IsNamed(open, "tbody") || IsNamed(open, "thead") || IsNamed(open, "tfoot")) return true;
                if (IsScopeBoundary(open, Scope.Table)) return false;
            }

            return false;
        }

        // The select in select scope: every element but option and optgroup bounds it.
        private static CssBox? FindSelectInSelectScope(ParserState state)
        {
            for (var i = state.Stack.Count - 1; i >= 0; i--)
            {
                var open = state.Stack[i];
                if (IsNamed(open, "select")) return open;
                if (!(IsNamed(open, "option") || IsNamed(open, "optgroup"))) return null;
            }

            return null;
        }

        private static void CloseParagraphInButtonScope(ParserState state)
        {
            if (FindInScope(state, HtmlConstants.P, Scope.Button) is { } paragraph)
                PopThrough(state, paragraph);
        }

        private static void GenerateImpliedEndTags(ParserState state, bool exceptRtc = false)
        {
            while (state.Stack.Count > 0 && IsAmong(state.Current, ImpliedEndTagNames) && !(exceptRtc && IsNamed(state.Current, "rtc")))
                Pop(state);
        }

        // Pops until the current node is one of these (or nothing is open).
        private static void ClearBackTo(ParserState state, FrozenSet<string> context)
        {
            while (state.Stack.Count > 0 && !IsAmong(state.Current, context))
                Pop(state);
        }

        // Drops the markers whose element has been closed, with the entries after them ("clear the list of active formatting elements up to the last marker").
        private static void ClearClosedMarkers(ParserState state)
        {
            var list = state.Formatting;
            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].IsMarker && !IsInStack(state, list[i].Owner!))
                    list.RemoveRange(i, list.Count - i);
            }
        }

        private static HtmlTag CopyOf(HtmlTag tag) =>
            new(tag.Name, false, tag.Attributes is { Count: > 0 } attributes
                ? attributes.ToDictionary(a => a.Key, a => a.Value, StringComparer.OrdinalIgnoreCase)
                : null);

        // An element with the same name and attributes as the one given, not yet in the tree.
        private static CssBox CloneOf(CssBox box) => CssBox.CreateBox(CopyOf(box.HtmlTag!));

        #region Insertion

        // https://html.spec.whatwg.org/multipage/parsing.html#appropriate-place-for-inserting-a-node
        private static InsertionPlace AppropriatePlace(ParserState state, CssBox? overrideTarget = null)
        {
            var target = overrideTarget ?? state.Current;
            if (state.FosterParenting && IsAmong(target, TableStructureNames))
            {
                var stack = state.Stack;
                var tableIndex = -1;
                for (var i = stack.Count - 1; i >= 0; i--)
                {
                    if (IsNamed(stack[i], "table"))
                    {
                        tableIndex = i;
                        break;
                    }
                }

                if (tableIndex < 0)
                    return new InsertionPlace(stack.Count > 0 ? stack[0] : state.Root, null);

                var table = stack[tableIndex];
                if (table.ParentBox is { } parent)
                    return new InsertionPlace(parent, table);

                return new InsertionPlace(tableIndex > 0 ? stack[tableIndex - 1] : state.Root, null);
            }

            return new InsertionPlace(target, null);
        }

        private static void Attach(CssBox box, InsertionPlace place)
        {
            box.ParentBox = place.Parent;
            if (place.Before is not null) box.SetBeforeBox(place.Before);
        }

        // Inserts an element for a start tag and, unless it is a void element, makes it the current node.
        private static CssBox InsertElement(ParserState state, StartTag tag)
        {
            var place = AppropriatePlace(state);
            var box = CssBox.CreateBox(new HtmlTag(tag.Name, tag.IsSingle, tag.Attributes), place.Parent);
            if (place.Before is not null) box.SetBeforeBox(place.Before);

            if (!tag.IsSingle)
            {
                state.Stack.Add(box);
                EnsureWithinMaxDepth(box);

                if (!box.IsWithinForeignContent)
                    AfterStartTagInserted(state, tag.LowerName, box);
            }

            return box;
        }

        private static CssBox InsertImplied(ParserState state, string name) =>
            InsertElement(state, new StartTag(name, name, null, false));

        private static void InsertText(ParserState state, string text)
        {
            var place = AppropriatePlace(state);

            // A MathML token's text stays on its element (see CssBox.ForeignText).
            if (place.Before is null && place.Parent.TryAbsorbForeignText(text)) return;

            var box = CssBox.CreateBox(place.Parent);
            box.Text = text;
            if (place.Before is not null) box.SetBeforeBox(place.Before);
        }

        #endregion

        #region Active formatting elements

        // Index of the last entry after the last marker for an element of this name, or -1.
        private static int LastFormattingIndex(ParserState state, string name)
        {
            var list = state.Formatting;
            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].IsMarker) return -1;
                if (list[i].Box is { } box && IsNamed(box, name)) return i;
            }

            return -1;
        }

        private static int IndexOfFormatting(ParserState state, CssBox box)
        {
            var list = state.Formatting;
            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(list[i].Box, box)) return i;
            }

            return -1;
        }

        // https://html.spec.whatwg.org/multipage/parsing.html#push-onto-the-list-of-active-formatting-elements
        private static void PushFormatting(ParserState state, CssBox element)
        {
            var list = state.Formatting;
            var matches = 0;
            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].IsMarker) break;
                if (list[i].Box is { } other && other.HtmlTag!.HasSameSignature(element.HtmlTag!) && ++matches == NoahsArkLimit)
                {
                    list.RemoveAt(i);
                    break;
                }
            }

            list.Add(FormattingEntry.ForElement(element));
        }

        // https://html.spec.whatwg.org/multipage/parsing.html#reconstruct-the-active-formatting-elements
        private static void ReconstructFormatting(ParserState state)
        {
            var list = state.Formatting;
            if (list.Count == 0) return;
            if (list[^1].IsMarker || IsInStack(state, list[^1].Box!)) return;

            var index = list.Count - 1;
            while (index > 0 && !(list[index - 1].IsMarker || IsInStack(state, list[index - 1].Box!)))
                index--;

            for (; index < list.Count; index++)
            {
                var place = AppropriatePlace(state);
                var clone = CssBox.CreateBox(CopyOf(list[index].Box!.HtmlTag!), place.Parent);
                if (place.Before is not null) clone.SetBeforeBox(place.Before);

                state.Stack.Add(clone);
                EnsureWithinMaxDepth(clone);
                list[index].Box = clone;
            }
        }

        // The a element after the last marker, if there is one.
        private static CssBox? OpenAnchorInList(ParserState state)
        {
            var index = LastFormattingIndex(state, "a");
            return index < 0 ? null : state.Formatting[index].Box;
        }

        #endregion

        #region Start tags

        // What follows the insertion of a start tag's element.
        private static void AfterStartTagInserted(ParserState state, string name, CssBox element)
        {
            if (FormattingNames.Contains(name))
                PushFormatting(state, element);
            else if (name is "applet" or "object" or "marquee" or "td" or "th" or "caption" or "template")
                state.Formatting.Add(FormattingEntry.MarkerFor(element));

            if (name == "template")
                state.TemplateModes.Add(Mode.InTemplate);
        }

        // The insertion mode, which the standard keeps in a variable and switches whenever it pushes or pops the element that names it: the
        // innermost table part, cell or select on the stack.
        private static Mode ModeOf(ParserState state)
        {
            var stack = state.Stack;
            for (var i = stack.Count - 1; i >= 0; i--)
            {
                var open = stack[i];
                if (open.IsWithinForeignContent || open.HtmlTag is not { } tag) continue;

                switch (tag.Name.ToLowerInvariant())
                {
                    case "td" or "th": return Mode.InCell;
                    case "tr": return Mode.InRow;
                    case "tbody" or "thead" or "tfoot": return Mode.InTableBody;
                    case "caption": return Mode.InCaption;
                    case "colgroup": return Mode.InColumnGroup;
                    case "table": return Mode.InTable;
                    case "template": return Mode.InTemplate;
                    case "select":
                        for (var j = i - 1; j >= 0; j--)
                        {
                            if (IsAmong(stack[j], TableStructureNames) || IsAmong(stack[j], CaptionAndCellNames))
                                return Mode.InSelectInTable;
                        }

                        return Mode.InSelect;
                }
            }

            return Mode.InBody;
        }

        private static void ProcessStartTag(ParserState state, StartTag tag)
        {
            state.IgnoreNextNewline = false;
            for (var i = 0; i < ReprocessLimit; i++)
            {
                if (!StartTagInCurrentMode(state, tag)) return;
            }
        }

        // Returns true when the token is to be processed again, which it is after the element that stood in its way has been closed.
        private static bool StartTagInCurrentMode(ParserState state, StartTag tag)
        {
            if (state.Current.IsWithinForeignContent)
                return ForeignStartTag(state, tag);

            if (state.TemplateModes.Count == 0 && state.Phase != DocumentPhase.InBody)
                return PhaseStartTag(state, tag);

            return ModeOf(state) switch
            {
                Mode.InTemplate => TemplateStartTag(state, tag),
                Mode.InTable => TableStartTag(state, tag),
                Mode.InTableBody => TableBodyStartTag(state, tag),
                Mode.InRow => RowStartTag(state, tag),
                Mode.InCell => CellStartTag(state, tag),
                Mode.InCaption => CaptionStartTag(state, tag),
                Mode.InColumnGroup => ColumnGroupStartTag(state, tag),
                Mode.InSelect or Mode.InSelectInTable => SelectStartTag(state, tag),
                _ => BodyStartTag(state, tag)
            };
        }

        // The "in body" rules for a start tag.
        private static bool BodyStartTag(ParserState state, StartTag tag)
        {
            if (TablePartStartTags.Contains(tag.LowerName)) return false;
            if (tag.LowerName == "form" && state.FormPointer is not null) return false;

            switch (tag.LowerName)
            {
                case "html" when state.Stack.Count > 0 && IsNamed(state.Stack[0], "html"):
                    MergeMissingAttributes(state.Stack[0], tag.Attributes);
                    return false;
                case "body" when state.Stack.Count > 1 && IsNamed(state.Stack[1], "body") && state.TemplateModes.Count == 0:
                    MergeMissingAttributes(state.Stack[1], tag.Attributes);
                    return false;
                case "head" when state.IsDocument:
                    return false;
                case "image":
                    tag = new StartTag("img", "img", tag.Attributes, true);
                    break;
            }

            PrepareStartTag(state, tag.LowerName);
            var box = InsertElement(state, tag);
            if (tag.LowerName == "form") state.FormPointer = box;
            if (tag.LowerName is "pre" or "listing" or "textarea") state.IgnoreNextNewline = true;

            return false;
        }

        // An html or body start tag that is not the first adds the attributes the element does not have yet.
        private static void MergeMissingAttributes(CssBox element, Dictionary<string, string>? attributes)
        {
            if (attributes is null) return;

            foreach (var (name, value) in attributes)
            {
                if (!element.HtmlTag!.HasAttribute(name)) element.HtmlTag.SetAttribute(name, value);
            }
        }

        // The rules of "in table" for a token they have no rule for: those of "in body", with foster parenting on.
        private static bool BodyStartTagFostered(ParserState state, StartTag tag)
        {
            var before = state.FosterParenting;
            state.FosterParenting = true;
            try
            {
                return BodyStartTag(state, tag);
            }
            finally
            {
                state.FosterParenting = before;
            }
        }

        private static bool TableStartTag(ParserState state, StartTag tag)
        {
            switch (tag.LowerName)
            {
                case "caption" or "colgroup" or "tbody" or "tfoot" or "thead":
                    ClearBackTo(state, TableContextNames);
                    InsertElement(state, tag);
                    return false;
                case "col":
                    ClearBackTo(state, TableContextNames);
                    InsertImplied(state, "colgroup");
                    return true;
                case "td" or "th" or "tr":
                    ClearBackTo(state, TableContextNames);
                    InsertImplied(state, "tbody");
                    return true;
                case "table":
                    if (FindInScope(state, "table", Scope.Table) is not { } table) return false;
                    PopThrough(state, table);
                    return true;
                case "style" or "script" or "template":
                    InsertElement(state, tag);
                    return false;
                case "input" when string.Equals(tag.Attributes?.GetValueOrDefault("type"), "hidden", StringComparison.OrdinalIgnoreCase):
                    InsertElement(state, tag);
                    return false;
                case "form":
                    if (state.FormPointer is not null) return false;
                    state.FormPointer = InsertElement(state, tag);
                    Pop(state);
                    return false;
                default:
                    return BodyStartTagFostered(state, tag);
            }
        }

        private static bool TableBodyStartTag(ParserState state, StartTag tag)
        {
            switch (tag.LowerName)
            {
                case "tr":
                    ClearBackTo(state, TableBodyContextNames);
                    InsertElement(state, tag);
                    return false;
                case "td" or "th":
                    ClearBackTo(state, TableBodyContextNames);
                    InsertImplied(state, "tr");
                    return true;
                case "caption" or "col" or "colgroup" or "tbody" or "tfoot" or "thead":
                    if (!HasSectionInTableScope(state)) return false;
                    ClearBackTo(state, TableBodyContextNames);
                    Pop(state);
                    return true;
                default:
                    return TableStartTag(state, tag);
            }
        }

        private static bool RowStartTag(ParserState state, StartTag tag)
        {
            switch (tag.LowerName)
            {
                case "td" or "th":
                    ClearBackTo(state, RowContextNames);
                    InsertElement(state, tag);
                    return false;
                case "caption" or "col" or "colgroup" or "tbody" or "tfoot" or "thead" or "tr":
                    if (!InTableScope(state, "tr")) return false;
                    ClearBackTo(state, RowContextNames);
                    Pop(state);
                    return true;
                default:
                    return TableStartTag(state, tag);
            }
        }

        private static bool CellStartTag(ParserState state, StartTag tag)
        {
            if (TablePartStartTags.Contains(tag.LowerName))
            {
                if (!InTableScope(state, "td") && !InTableScope(state, "th")) return false;
                CloseCell(state);
                return true;
            }

            return BodyStartTag(state, tag);
        }

        private static bool CaptionStartTag(ParserState state, StartTag tag)
        {
            if (TablePartStartTags.Contains(tag.LowerName))
            {
                if (FindInScope(state, "caption", Scope.Table) is not { } caption) return false;
                PopThrough(state, caption);
                return true;
            }

            return BodyStartTag(state, tag);
        }

        private static bool ColumnGroupStartTag(ParserState state, StartTag tag)
        {
            switch (tag.LowerName)
            {
                case "html":
                    return BodyStartTag(state, tag);
                case "col":
                    InsertElement(state, tag);
                    return false;
                case "template":
                    InsertElement(state, tag);
                    return false;
                default:
                    if (!IsNamed(state.Current, "colgroup")) return false;
                    Pop(state);
                    return true;
            }
        }

        private static bool SelectStartTag(ParserState state, StartTag tag)
        {
            switch (tag.LowerName)
            {
                case "option":
                    if (IsNamed(state.Current, "option")) Pop(state);
                    InsertElement(state, tag);
                    return false;
                case "optgroup" or "hr":
                    if (IsNamed(state.Current, "option")) Pop(state);
                    if (IsNamed(state.Current, "optgroup")) Pop(state);
                    InsertElement(state, tag);
                    return false;
                case "select":
                    if (FindSelectInSelectScope(state) is { } open) PopThrough(state, open);
                    return false;
                case "input" or "keygen" or "textarea":
                    if (FindSelectInSelectScope(state) is not { } select) return false;
                    PopThrough(state, select);
                    return true;
                case "script" or "template":
                    InsertElement(state, tag);
                    return false;
                case "caption" or "table" or "tbody" or "tfoot" or "thead" or "tr" or "td" or "th" when ModeOf(state) == Mode.InSelectInTable:
                    PopThrough(state, state.Stack.Last(b => IsNamed(b, "select")));
                    return true;
                default:
                    return false;
            }
        }

        // Closes the open table cell and its markers on the list of active formatting elements.
        private static void CloseCell(ParserState state)
        {
            GenerateImpliedEndTags(state);
            for (var i = state.Stack.Count - 1; i >= 0; i--)
            {
                if (IsNamed(state.Stack[i], "td") || IsNamed(state.Stack[i], "th"))
                {
                    PopThrough(state, state.Stack[i]);
                    return;
                }
            }
        }

        // Everything the "in body" insertion mode does for a start tag before it inserts the element.
        private static void PrepareStartTag(ParserState state, string name)
        {
            if (name is "li")
            {
                CloseListItem(state, static n => n is "li");
            }
            else if (name is "dd" or "dt")
            {
                CloseListItem(state, static n => n is "dd" or "dt");
            }

            if (ParagraphClosingStartTags.Contains(name))
                CloseParagraphInButtonScope(state);

            if (name.Length == 2 && name[0] == 'h' && name[1] is >= '1' and <= '6' && IsHeading(state.Current))
                Pop(state);

            if (name is "rb" or "rtc" && FindInScope(state, "ruby") is not null)
                GenerateImpliedEndTags(state);
            else if (name is "rp" or "rt" && FindInScope(state, "ruby") is not null)
                GenerateImpliedEndTags(state, exceptRtc: true);

            if (name == "button" && FindInScope(state, "button") is { } button)
                PopThrough(state, button);

            if (name is "option" or "optgroup" && IsNamed(state.Current, "option"))
                Pop(state);

            if (name == "a" && OpenAnchorInList(state) is { } anchor)
            {
                RunAdoptionAgency(state, "a");
                var index = IndexOfFormatting(state, anchor);
                if (index >= 0) state.Formatting.RemoveAt(index);
            }

            if (name == "nobr")
            {
                ReconstructFormatting(state);
                if (FindInScope(state, "nobr") is not null)
                {
                    RunAdoptionAgency(state, "nobr");
                    ReconstructFormatting(state);
                }
            }

            if (!NoReconstructStartTags.Contains(name))
                ReconstructFormatting(state);
        }

        // The loop of the li, dd and dt start tags: an open item of the same kind is closed, looking up through anything that is not special.
        private static void CloseListItem(ParserState state, Func<string, bool> isItem)
        {
            for (var i = state.Stack.Count - 1; i >= 0; i--)
            {
                var node = state.Stack[i];
                if (!node.IsWithinForeignContent && node.HtmlTag is { } tag && isItem(tag.Name.ToLowerInvariant()))
                {
                    PopThrough(state, node);
                    return;
                }

                if (IsSpecial(node) && !(IsNamed(node, "address") || IsNamed(node, "div") || IsNamed(node, "p")))
                    return;
            }
        }

        #endregion

        #region Document, template and foreign content

        private static bool TemplateStartTag(ParserState state, StartTag tag)
        {
            var top = state.TemplateModes[^1];
            if (top != Mode.InTemplate)
                return StartTagInMode(state, top, tag);

            switch (tag.LowerName)
            {
                case "base" or "basefont" or "bgsound" or "link" or "meta" or "noframes" or "script" or "style" or "template" or "title":
                    InsertElement(state, tag);
                    return false;
                case "caption" or "colgroup" or "tbody" or "tfoot" or "thead":
                    state.TemplateModes[^1] = Mode.InTable;
                    return true;
                case "col":
                    state.TemplateModes[^1] = Mode.InColumnGroup;
                    return true;
                case "tr":
                    state.TemplateModes[^1] = Mode.InTableBody;
                    return true;
                case "td" or "th":
                    state.TemplateModes[^1] = Mode.InRow;
                    return true;
                default:
                    state.TemplateModes[^1] = Mode.InBody;
                    return true;
            }
        }

        private static bool StartTagInMode(ParserState state, Mode mode, StartTag tag) => mode switch
        {
            Mode.InTable => TableStartTag(state, tag),
            Mode.InTableBody => TableBodyStartTag(state, tag),
            Mode.InRow => RowStartTag(state, tag),
            Mode.InColumnGroup => ColumnGroupStartTag(state, tag),
            _ => BodyStartTag(state, tag)
        };

        private static bool PhaseStartTag(ParserState state, StartTag tag)
        {
            switch (state.Phase)
            {
                case DocumentPhase.Initial:
                    state.Phase = DocumentPhase.BeforeHtml;
                    return true;
                case DocumentPhase.BeforeHtml:
                    if (tag.LowerName == "html")
                    {
                        InsertElement(state, tag);
                        state.Phase = DocumentPhase.BeforeHead;
                        return false;
                    }

                    InsertImplied(state, "html");
                    state.Phase = DocumentPhase.BeforeHead;
                    return true;
                case DocumentPhase.BeforeHead:
                    if (tag.LowerName == "html") return BodyStartTag(state, tag);
                    if (tag.LowerName == "head")
                    {
                        state.HeadPointer = InsertElement(state, tag);
                        state.Phase = DocumentPhase.InHead;
                        return false;
                    }

                    state.HeadPointer = InsertImplied(state, "head");
                    state.Phase = DocumentPhase.InHead;
                    return true;
                case DocumentPhase.InHead:
                    return HeadStartTag(state, tag);
                case DocumentPhase.AfterHead:
                    return AfterHeadStartTag(state, tag);
                default:
                    if (tag.LowerName == "html") return BodyStartTag(state, tag);
                    state.Phase = DocumentPhase.InBody;
                    return true;
            }
        }

        private static bool HeadStartTag(ParserState state, StartTag tag)
        {
            switch (tag.LowerName)
            {
                case "html":
                    return BodyStartTag(state, tag);
                case "base" or "basefont" or "bgsound" or "link" or "meta":
                    InsertElement(state, tag);
                    if (!tag.IsSingle) Pop(state);
                    return false;
                case "title" or "noscript" or "noframes" or "style" or "script" or "template":
                    InsertElement(state, tag);
                    return false;
                case "head":
                    return false;
                default:
                    if (state.HeadPointer is { } head) PopThrough(state, head);
                    state.Phase = DocumentPhase.AfterHead;
                    return true;
            }
        }

        private static bool AfterHeadStartTag(ParserState state, StartTag tag)
        {
            switch (tag.LowerName)
            {
                case "html":
                    return BodyStartTag(state, tag);
                case "body" or "frameset":
                    InsertElement(state, tag);
                    state.Phase = DocumentPhase.InBody;
                    return false;
                case "base" or "basefont" or "bgsound" or "link" or "meta" or "noframes" or "script" or "style" or "template" or "title":
                    // Head content after the head goes into it: the head is put back on the stack for the token.
                    if (state.HeadPointer is not { } head) return false;
                    state.Stack.Add(head);
                    try
                    {
                        HeadStartTag(state, tag);
                    }
                    finally
                    {
                        state.Stack.Remove(head);
                    }

                    return false;
                case "head":
                    return false;
                default:
                    InsertImplied(state, "body");
                    state.Phase = DocumentPhase.InBody;
                    return true;
            }
        }

        private static bool PhaseEndTag(ParserState state, string name)
        {
            // A title, style or script left open in the head or after it still ends with its own end tag.
            if (state.Phase is DocumentPhase.InHead or DocumentPhase.AfterHead && state.Stack.Count > 0
                && !IsNamed(state.Current, "head") && !IsNamed(state.Current, "html"))
            {
                CloseByAnyOtherEndTag(state, name);
                return false;
            }

            var closesHead = name is "head" or "body" or "html" or "br";
            switch (state.Phase)
            {
                case DocumentPhase.Initial:
                    state.Phase = DocumentPhase.BeforeHtml;
                    return true;
                case DocumentPhase.BeforeHtml:
                    if (!closesHead) return false;
                    InsertImplied(state, "html");
                    state.Phase = DocumentPhase.BeforeHead;
                    return true;
                case DocumentPhase.BeforeHead:
                    if (!closesHead) return false;
                    state.HeadPointer = InsertImplied(state, "head");
                    state.Phase = DocumentPhase.InHead;
                    return true;
                case DocumentPhase.InHead:
                    if (!closesHead) return false;
                    if (state.HeadPointer is { } head) PopThrough(state, head);
                    state.Phase = DocumentPhase.AfterHead;
                    return name != "head";
                case DocumentPhase.AfterHead:
                    if (name is not ("body" or "html" or "br")) return false;
                    InsertImplied(state, "body");
                    state.Phase = DocumentPhase.InBody;
                    return true;
                case DocumentPhase.AfterBody:
                    if (name == "html")
                    {
                        state.Phase = DocumentPhase.AfterAfterBody;
                        return false;
                    }

                    state.Phase = DocumentPhase.InBody;
                    return true;
                default:
                    state.Phase = DocumentPhase.InBody;
                    return true;
            }
        }

        // Text before the body starts: leading white space is dropped (before the head) or kept (in and after it), and the rest starts what comes next.
        // Returns true when the rest of the text is to be processed again.
        private static bool PhaseText(ParserState state, ref string text)
        {
            var rest = text.TrimStart(' ', '\t', '\n', '\r', '\f');
            var whitespace = text[..^rest.Length];

            switch (state.Phase)
            {
                case DocumentPhase.Initial:
                    if (rest.Length == 0) return false;
                    state.Phase = DocumentPhase.BeforeHtml;
                    break;
                case DocumentPhase.BeforeHtml:
                    if (rest.Length == 0) return false;
                    InsertImplied(state, "html");
                    state.Phase = DocumentPhase.BeforeHead;
                    break;
                case DocumentPhase.BeforeHead:
                    if (rest.Length == 0) return false;
                    state.HeadPointer = InsertImplied(state, "head");
                    state.Phase = DocumentPhase.InHead;
                    break;
                case DocumentPhase.InHead:
                    if (whitespace.Length > 0) InsertText(state, whitespace);
                    if (rest.Length == 0) return false;
                    if (state.HeadPointer is { } head) PopThrough(state, head);
                    state.Phase = DocumentPhase.AfterHead;
                    break;
                case DocumentPhase.AfterHead:
                    if (whitespace.Length > 0) InsertText(state, whitespace);
                    if (rest.Length == 0) return false;
                    InsertImplied(state, "body");
                    state.Phase = DocumentPhase.InBody;
                    break;
                default:
                    if (rest.Length == 0)
                    {
                        InsertBodyText(state, text);
                        return false;
                    }

                    state.Phase = DocumentPhase.InBody;
                    return true;
            }

            text = rest;
            return true;
        }

        // At the end of the source, a document that has not got as far as its body gets the rest of its skeleton.
        private static void FinishDocument(ParserState state)
        {
            if (state.Phase == DocumentPhase.Initial) state.Phase = DocumentPhase.BeforeHtml;

            if (state.Phase == DocumentPhase.BeforeHtml)
            {
                InsertImplied(state, "html");
                state.Phase = DocumentPhase.BeforeHead;
            }

            if (state.Phase == DocumentPhase.BeforeHead)
            {
                state.HeadPointer = InsertImplied(state, "head");
                state.Phase = DocumentPhase.InHead;
            }

            if (state.Phase == DocumentPhase.InHead)
            {
                if (state.HeadPointer is { } head) PopThrough(state, head);
                state.Phase = DocumentPhase.AfterHead;
            }

            if (state.Phase == DocumentPhase.AfterHead)
            {
                InsertImplied(state, "body");
                state.Phase = DocumentPhase.InBody;
            }
        }

        // A start tag with an SVG or MathML element open: HTML elements that cannot be there end the foreign content first.
        private static bool ForeignStartTag(ParserState state, StartTag tag)
        {
            var breaksOut = ForeignBreakoutNames.Contains(tag.LowerName)
                || (tag.LowerName == "font" && tag.Attributes is { } a && (a.ContainsKey("color") || a.ContainsKey("face") || a.ContainsKey("size")));

            if (breaksOut)
            {
                while (state.Stack.Count > 0 && state.Current.IsWithinForeignContent && !ForeignIntegrationNames.Contains(state.Current.HtmlTag!.Name))
                    Pop(state);

                if (!state.Current.IsWithinForeignContent) return true;
            }
            else if (SvgElementNames.TryGetValue(tag.LowerName, out var fixedName) && state.Stack.Any(b => b is CssBoxSvg))
            {
                tag = tag with { Name = fixedName };
            }

            InsertElement(state, tag);
            return false;
        }

        #endregion

        #region Text

        private static void ProcessText(ParserState state, string text)
        {
            if (state.IgnoreNextNewline)
            {
                state.IgnoreNextNewline = false;
                if (text.StartsWith("\r\n", StringComparison.Ordinal)) text = text[2..];
                else if (text.StartsWith('\n')) text = text[1..];
                if (text.Length == 0) return;
            }

            for (var i = 0; i < ReprocessLimit; i++)
            {
                if (state.Current.IsWithinForeignContent || (state.Stack.Count > 0 && IsAmong(state.Current, RawTextNames)))
                {
                    InsertText(state, text);
                    return;
                }

                if (state.TemplateModes.Count == 0 && state.Phase != DocumentPhase.InBody)
                {
                    if (!PhaseText(state, ref text)) return;
                    continue;
                }

                switch (ModeOf(state))
                {
                    case Mode.InTemplate:
                        InsertBodyText(state, text);
                        return;
                    case Mode.InTable or Mode.InTableBody or Mode.InRow:
                        if (IsAmong(state.Current, TableStructureNames) && IsHtmlWhitespace(text))
                        {
                            InsertText(state, text);
                            return;
                        }

                        InsertTextFostered(state, text);
                        return;
                    case Mode.InColumnGroup:
                        if (IsHtmlWhitespace(text))
                        {
                            InsertText(state, text);
                            return;
                        }

                        if (!IsNamed(state.Current, "colgroup")) return;
                        Pop(state);
                        continue;
                    case Mode.InSelect or Mode.InSelectInTable:
                        InsertText(state, text);
                        return;
                    default:
                        InsertBodyText(state, text);
                        return;
                }
            }
        }

        // Text is "a character token" of the "in body" insertion mode, which reconstructs the active formatting elements first.
        private static void InsertBodyText(ParserState state, string text)
        {
            if (state.Formatting.Count > 0 && !(state.Current.HtmlTag is { } parentTag && NoReconstructTextParents.Contains(parentTag.Name)))
                ReconstructFormatting(state);

            InsertText(state, text);
        }

        private static void InsertTextFostered(ParserState state, string text)
        {
            var before = state.FosterParenting;
            state.FosterParenting = true;
            try
            {
                InsertBodyText(state, text);
            }
            finally
            {
                state.FosterParenting = before;
            }
        }

        #endregion

        #region End tags

        private static void ProcessEndTag(ParserState state, string name)
        {
            state.IgnoreNextNewline = false;

            if (state.Stack.Count == 0 && (state.TemplateModes.Count > 0 || state.Phase == DocumentPhase.InBody)) return;

            if (state.Stack.Count > 0 && state.Current.IsWithinForeignContent)
            {
                // https://html.spec.whatwg.org/multipage/parsing.html#parsing-main-inforeign: look for the element through the foreign ones.
                for (var i = state.Stack.Count - 1; i >= 0 && state.Stack[i].IsWithinForeignContent; i--)
                {
                    if (state.Stack[i].HtmlTag!.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        PopThrough(state, state.Stack[i]);
                        return;
                    }
                }
            }

            for (var i = 0; i < ReprocessLimit; i++)
            {
                if (!EndTagInCurrentMode(state, name)) return;
            }
        }

        private static bool EndTagInCurrentMode(ParserState state, string name)
        {
            if (state.TemplateModes.Count == 0 && state.Phase != DocumentPhase.InBody)
                return PhaseEndTag(state, name);

            switch (ModeOf(state))
            {
                case Mode.InTemplate: return TemplateEndTag(state, name);
                case Mode.InTable: return TableEndTag(state, name);
                case Mode.InTableBody: return TableBodyEndTag(state, name);
                case Mode.InRow: return RowEndTag(state, name);
                case Mode.InCell: return CellEndTag(state, name);
                case Mode.InCaption: return CaptionEndTag(state, name);
                case Mode.InColumnGroup: return ColumnGroupEndTag(state, name);
                case Mode.InSelect or Mode.InSelectInTable: return SelectEndTag(state, name);
                default:
                    BodyEndTag(state, name);
                    return false;
            }
        }

        private static bool TableEndTag(ParserState state, string name)
        {
            switch (name)
            {
                case "table":
                    if (FindInScope(state, "table", Scope.Table) is { } table) PopThrough(state, table);
                    return false;
                case "body" or "caption" or "col" or "colgroup" or "html" or "tbody" or "td" or "tfoot" or "th" or "thead" or "tr":
                    return false;
                default:
                    var before = state.FosterParenting;
                    state.FosterParenting = true;
                    try
                    {
                        BodyEndTag(state, name);
                    }
                    finally
                    {
                        state.FosterParenting = before;
                    }

                    return false;
            }
        }

        private static bool TableBodyEndTag(ParserState state, string name)
        {
            switch (name)
            {
                case "tbody" or "tfoot" or "thead":
                    if (!InTableScope(state, name)) return false;
                    ClearBackTo(state, TableBodyContextNames);
                    Pop(state);
                    return false;
                case "table":
                    if (!HasSectionInTableScope(state)) return false;
                    ClearBackTo(state, TableBodyContextNames);
                    Pop(state);
                    return true;
                case "body" or "caption" or "col" or "colgroup" or "html" or "td" or "th" or "tr":
                    return false;
                default:
                    return TableEndTag(state, name);
            }
        }

        private static bool RowEndTag(ParserState state, string name)
        {
            switch (name)
            {
                case "tr":
                    if (!InTableScope(state, "tr")) return false;
                    ClearBackTo(state, RowContextNames);
                    Pop(state);
                    return false;
                case "table":
                    if (!InTableScope(state, "tr")) return false;
                    ClearBackTo(state, RowContextNames);
                    Pop(state);
                    return true;
                case "tbody" or "tfoot" or "thead":
                    if (!InTableScope(state, name) || !InTableScope(state, "tr")) return false;
                    ClearBackTo(state, RowContextNames);
                    Pop(state);
                    return true;
                case "body" or "caption" or "col" or "colgroup" or "html" or "td" or "th":
                    return false;
                default:
                    return TableEndTag(state, name);
            }
        }

        private static bool CellEndTag(ParserState state, string name)
        {
            switch (name)
            {
                case "td" or "th":
                    if (!InTableScope(state, name)) return false;
                    GenerateImpliedEndTags(state);
                    PopThrough(state, FindInScope(state, name, Scope.Table)!);
                    return false;
                case "body" or "caption" or "col" or "colgroup" or "html":
                    return false;
                case "table" or "tbody" or "tfoot" or "thead" or "tr":
                    if (!InTableScope(state, name)) return false;
                    CloseCell(state);
                    return true;
                default:
                    BodyEndTag(state, name);
                    return false;
            }
        }

        private static bool CaptionEndTag(ParserState state, string name)
        {
            switch (name)
            {
                case "caption":
                    if (FindInScope(state, "caption", Scope.Table) is { } caption)
                    {
                        GenerateImpliedEndTags(state);
                        PopThrough(state, caption);
                    }

                    return false;
                case "table":
                    if (FindInScope(state, "caption", Scope.Table) is not { } open) return false;
                    PopThrough(state, open);
                    return true;
                case "body" or "col" or "colgroup" or "html" or "tbody" or "td" or "tfoot" or "th" or "thead" or "tr":
                    return false;
                default:
                    BodyEndTag(state, name);
                    return false;
            }
        }

        private static bool ColumnGroupEndTag(ParserState state, string name)
        {
            switch (name)
            {
                case "colgroup":
                    if (IsNamed(state.Current, "colgroup")) Pop(state);
                    return false;
                case "col":
                    return false;
                default:
                    if (!IsNamed(state.Current, "colgroup")) return false;
                    Pop(state);
                    return true;
            }
        }

        private static bool SelectEndTag(ParserState state, string name)
        {
            switch (name)
            {
                case "optgroup":
                    if (IsNamed(state.Current, "option") && state.Stack.Count > 1 && IsNamed(state.Stack[^2], "optgroup")) Pop(state);
                    if (IsNamed(state.Current, "optgroup")) Pop(state);
                    return false;
                case "option":
                    if (IsNamed(state.Current, "option")) Pop(state);
                    return false;
                case "select":
                    if (FindSelectInSelectScope(state) is { } select) PopThrough(state, select);
                    return false;
                case "caption" or "table" or "tbody" or "tfoot" or "tr" or "td" or "th" or "thead" when ModeOf(state) == Mode.InSelectInTable:
                    if (!InTableScope(state, name)) return false;
                    PopThrough(state, state.Stack.Last(b => IsNamed(b, "select")));
                    return true;
                default:
                    return false;
            }
        }

        // The "in body" rules for an end tag.
        private static void BodyEndTag(ParserState state, string name)
        {
            if (FormattingNames.Contains(name))
            {
                if (!RunAdoptionAgency(state, name))
                    CloseByAnyOtherEndTag(state, name);
            }
            else if (name == HtmlConstants.P)
            {
                if (FindInScope(state, HtmlConstants.P, Scope.Button) is { } paragraph)
                {
                    PopThrough(state, paragraph);
                }
                else
                {
                    // The one unmatched end tag that generates an element rather than being dropped.
                    InsertImplied(state, HtmlConstants.P);
                    Pop(state);
                }
            }
            else if (name == "li")
            {
                if (FindInScope(state, "li", Scope.ListItem) is { } item)
                    PopThrough(state, item);
            }
            else if (name.Length == 2 && name[0] is 'h' or 'H' && name[1] is >= '1' and <= '6')
            {
                CloseHeading(state);
            }
            else if (name is "body" or "html" && state.IsDocument)
            {
                if (FindInScope(state, "body") is null) return;
                state.Phase = name == "body" ? DocumentPhase.AfterBody : DocumentPhase.AfterAfterBody;
            }
            else if (name == "template")
            {
                var template = state.Stack.LastOrDefault(b => IsNamed(b, "template"));
                if (template is null) return;

                GenerateImpliedEndTags(state);
                PopThrough(state, template);
            }
            else if (name == "form")
            {
                var form = state.FormPointer;
                state.FormPointer = null;
                if (form is null || !IsInScope(state, form)) return;

                GenerateImpliedEndTags(state);
                state.Stack.Remove(form);
            }
            else if (ScopedEndTags.Contains(name))
            {
                if (FindInScope(state, name) is { } element)
                    PopThrough(state, element);
            }
            else if (TablePartStartTags.Contains(name))
            {
                // Table parts mean nothing outside a table.
            }
            else if (NearestAncestorEndTags.Contains(name))
            {
                for (var i = state.Stack.Count - 1; i >= 0; i--)
                {
                    if (IsNamed(state.Stack[i], name))
                    {
                        PopThrough(state, state.Stack[i]);
                        break;
                    }
                }
            }
            else
            {
                CloseByAnyOtherEndTag(state, name);
            }
        }

        private static bool TemplateEndTag(ParserState state, string name)
        {
            var top = state.TemplateModes[^1];
            if (top != Mode.InTemplate)
                return EndTagInMode(state, top, name);

            if (name == "template") BodyEndTag(state, name);
            return false;
        }

        private static bool EndTagInMode(ParserState state, Mode mode, string name) => mode switch
        {
            Mode.InTable => TableEndTag(state, name),
            Mode.InTableBody => TableBodyEndTag(state, name),
            Mode.InRow => RowEndTag(state, name),
            Mode.InColumnGroup => ColumnGroupEndTag(state, name),
            _ => BodyEndTagFalse(state, name)
        };

        private static bool BodyEndTagFalse(ParserState state, string name)
        {
            BodyEndTag(state, name);
            return false;
        }

        // An end tag for a heading closes the innermost open heading that is in scope, whichever level it is.
        private static void CloseHeading(ParserState state)
        {
            for (var i = state.Stack.Count - 1; i >= 0; i--)
            {
                var open = state.Stack[i];
                if (IsHeading(open))
                {
                    PopThrough(state, open);
                    return;
                }

                if (IsScopeBoundary(open, Scope.Default)) return;
            }
        }

        // https://html.spec.whatwg.org/multipage/parsing.html#parsing-main-inbody, "any other end tag": the nearest open element of that name
        // is closed, unless a special element is in the way, which ignores the end tag.
        private static void CloseByAnyOtherEndTag(ParserState state, string name)
        {
            for (var i = state.Stack.Count - 1; i >= 0; i--)
            {
                var node = state.Stack[i];
                if (IsNamed(node, name) || (node.IsWithinForeignContent && node.HtmlTag!.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    PopThrough(state, node);
                    return;
                }

                if (IsSpecial(node)) return;
            }
        }

        #endregion

        /// <summary>
        /// https://html.spec.whatwg.org/multipage/parsing.html#adoption-agency-algorithm: an end tag for a formatting element, which may have
        /// elements open inside it that end up outside it, or the other way about.
        /// </summary>
        /// <returns>false when the end tag is for the "any other end tag" rule to answer</returns>
        private static bool RunAdoptionAgency(ParserState state, string subject)
        {
            var stack = state.Stack;
            if (stack.Count > 0 && IsNamed(state.Current, subject) && IndexOfFormatting(state, state.Current) < 0)
            {
                Pop(state);
                return true;
            }

            for (var outer = 0; outer < AdoptionOuterLoopLimit; outer++)
            {
                var list = state.Formatting;
                var formattingIndex = LastFormattingIndex(state, subject);
                if (formattingIndex < 0) return false;

                var formatting = list[formattingIndex].Box!;
                var formattingPosition = IndexInStack(state, formatting);
                if (formattingPosition < 0)
                {
                    list.RemoveAt(formattingIndex);
                    return true;
                }

                if (!IsInScope(state, formatting)) return true;

                CssBox? furthest = null;
                for (var i = formattingPosition + 1; i < stack.Count; i++)
                {
                    if (IsSpecial(stack[i]))
                    {
                        furthest = stack[i];
                        break;
                    }
                }

                if (furthest is null)
                {
                    PopThrough(state, formatting);
                    list.RemoveAt(IndexOfFormatting(state, formatting));
                    return true;
                }

                var commonAncestor = formattingPosition > 0 ? stack[formattingPosition - 1] : state.Root;

                var bookmark = FormattingEntry.Bookmark();
                list.Insert(formattingIndex + 1, bookmark);

                var lastNode = furthest;
                var nodePosition = IndexInStack(state, furthest);
                for (var inner = 1; ; inner++)
                {
                    nodePosition--;
                    var node = stack[nodePosition];
                    if (ReferenceEquals(node, formatting)) break;

                    var entryIndex = IndexOfFormatting(state, node);
                    if (inner > AdoptionInnerLoopLimit && entryIndex >= 0)
                    {
                        list.RemoveAt(entryIndex);
                        entryIndex = -1;
                    }

                    if (entryIndex < 0)
                    {
                        stack.RemoveAt(nodePosition);
                        continue;
                    }

                    var clone = CloneOf(node);
                    list[entryIndex].Box = clone;
                    stack[nodePosition] = clone;
                    if (ReferenceEquals(lastNode, furthest))
                    {
                        list.Remove(bookmark);
                        list.Insert(IndexOfFormatting(state, clone) + 1, bookmark);
                    }

                    lastNode.ParentBox = clone;
                    lastNode = clone;
                }

                Attach(lastNode, AppropriatePlace(state, commonAncestor));
                EnsureWithinMaxDepth(lastNode);

                var replacement = CloneOf(formatting);
                replacement.SetAllBoxes(furthest);
                replacement.ParentBox = furthest;

                list[list.IndexOf(bookmark)] = FormattingEntry.ForElement(replacement);
                list.RemoveAt(IndexOfFormatting(state, formatting));

                stack.Remove(formatting);
                stack.Insert(IndexInStack(state, furthest) + 1, replacement);
                EnsureWithinMaxDepth(replacement);
            }

            return true;
        }
    }
}
