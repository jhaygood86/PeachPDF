using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Html.Parse
{
    /// <summary>
    /// The parts of the HTML tree construction algorithm (<see href="https://html.spec.whatwg.org/multipage/parsing.html#tree-construction"/>)
    /// that need a stack of open elements and a list of active formatting elements: scopes, the special category, "reconstruct the
    /// active formatting elements", the adoption agency algorithm and the "any other end tag" rule of the "in body" insertion mode.
    /// </summary>
    /// <remarks>
    /// The stack of open elements is not stored: it is the chain of parents from the insertion point up to (not including) the root, which
    /// is what the algorithm keeps it equal to - an element is open exactly when it is that chain's, and every step that pops elements
    /// moves the insertion point to a parent. The one step that rearranges the stack, the adoption agency, rearranges the tree the same
    /// way, so the chain stays the stack.
    /// </remarks>
    internal static partial class HtmlParser
    {
        /// <summary>What a parse is in the middle of: the root it builds under, the insertion point and the list of active formatting elements.</summary>
        private sealed class ParserState(CssBox root)
        {
            /// <summary>The box the document is built under; the bottom of the stack of open elements (not itself an element of it).</summary>
            public CssBox Root { get; } = root;

            /// <summary>The current node: the innermost open element, or <see cref="Root"/> when none is.</summary>
            public CssBox Current { get; set; } = root;

            /// <summary>The list of active formatting elements; oldest first.</summary>
            public List<FormattingEntry> Formatting { get; } = [];
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

        private enum Scope
        {
            Default,
            ListItem,
            Button
        }

        private const int AdoptionOuterLoopLimit = 8;
        private const int AdoptionInnerLoopLimit = 3;
        private const int NoahsArkLimit = 3;

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
            "colgroup", "frame", "frameset", "tbody", "td", "th", "thead", "tfoot", "tr", "option", "optgroup",
            "address", "article", "aside", "blockquote", "center", "details", "dialog", "dir", "div", "dl", "fieldset", "figcaption",
            "figure", "footer", "header", "hgroup", "main", "menu", "nav", "ol", "p", "search", "section", "summary", "ul",
            "h1", "h2", "h3", "h4", "h5", "h6", "pre", "listing", "form", "li", "dd", "dt", "plaintext", "table", "hr");

        // The end tags that close their element when it is in scope, and are ignored when it is not.
        private static readonly FrozenSet<string> ScopedEndTags = Names(
            "address", "article", "aside", "blockquote", "button", "center", "details", "dialog", "dir", "div", "dl", "fieldset",
            "figcaption", "figure", "footer", "header", "hgroup", "listing", "main", "menu", "nav", "ol", "pre", "search", "section",
            "summary", "ul", "form", "dd", "dt", "applet", "marquee", "object");

        // The end tags this parser still answers by closing up to the nearest open element of that name, until the table, select, template
        // and document insertion modes replace that.
        private static readonly FrozenSet<string> NearestAncestorEndTags = Names(
            "html", "body", "head", "frameset", "table", "tbody", "thead", "tfoot", "tr", "td", "th", "caption", "colgroup", "col",
            "select", "option", "optgroup", "template");

        // Text goes to the current node without the active formatting elements being reconstructed in these.
        private static readonly FrozenSet<string> NoReconstructTextParents = Names(
            "table", "tbody", "thead", "tfoot", "tr", "colgroup", "select", "optgroup", "option", "head", "html",
            "title", "style", "script", "textarea", "noscript");

        private static bool IsNamed(CssBox box, string name) =>
            box.HtmlTag is { } tag && !box.IsWithinForeignContent && tag.Name.Equals(name, StringComparison.OrdinalIgnoreCase);

        private static bool IsHeading(CssBox box) =>
            box.HtmlTag is { Name: { Length: 2 } n } && !box.IsWithinForeignContent && (n[0] is 'h' or 'H') && n[1] is >= '1' and <= '6';

        private static bool IsSpecial(CssBox box) =>
            box.HtmlTag is not { } tag || (box.IsWithinForeignContent ? ForeignIntegrationNames.Contains(tag.Name) : SpecialNames.Contains(tag.Name));

        private static bool IsInStack(ParserState state, CssBox box)
        {
            for (var open = state.Current; open is not null && !ReferenceEquals(open, state.Root); open = open.ParentBox)
            {
                if (ReferenceEquals(open, box)) return true;
            }

            return false;
        }

        // The open elements, the outermost first.
        private static List<CssBox> StackOf(ParserState state)
        {
            var stack = new List<CssBox>();
            for (var open = state.Current; open is not null && !ReferenceEquals(open, state.Root); open = open.ParentBox)
                stack.Add(open);

            stack.Reverse();
            return stack;
        }

        // Pops the elements down to and including the one given (which must be open).
        private static void PopThrough(ParserState state, CssBox element) => state.Current = element.ParentBox ?? state.Root;

        private static bool IsScopeBoundary(CssBox box, Scope scope)
        {
            var name = box.HtmlTag!.Name;
            if (box.IsWithinForeignContent)
                return ForeignIntegrationNames.Contains(name);

            return ScopeBoundaryNames.Contains(name)
                || (scope == Scope.Button && name.Equals("button", StringComparison.OrdinalIgnoreCase))
                || (scope == Scope.ListItem && (name.Equals("ol", StringComparison.OrdinalIgnoreCase) || name.Equals("ul", StringComparison.OrdinalIgnoreCase)));
        }

        // The open element with this name that is in scope (https://html.spec.whatwg.org/multipage/parsing.html#has-an-element-in-the-specific-scope), or null.
        private static CssBox? FindInScope(ParserState state, string name, Scope scope = Scope.Default)
        {
            for (var open = state.Current; open is not null && !ReferenceEquals(open, state.Root); open = open.ParentBox)
            {
                if (open.HtmlTag is null) continue;
                if (IsNamed(open, name)) return open;
                if (IsScopeBoundary(open, scope)) return null;
            }

            return null;
        }

        private static bool IsInScope(ParserState state, CssBox element)
        {
            for (var open = state.Current; open is not null && !ReferenceEquals(open, state.Root); open = open.ParentBox)
            {
                if (ReferenceEquals(open, element)) return true;
                if (open.HtmlTag is not null && IsScopeBoundary(open, Scope.Default)) return false;
            }

            return false;
        }

        private static void CloseParagraphInButtonScope(ParserState state)
        {
            if (FindInScope(state, HtmlConstants.P, Scope.Button) is { } paragraph)
                PopThrough(state, paragraph);
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
                var clone = CssBox.CreateBox(CopyOf(list[index].Box!.HtmlTag!), state.Current);
                state.Current = clone;
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
                PopThrough(state, state.Current);

            if (name == "button" && FindInScope(state, "button") is { } button)
                PopThrough(state, button);

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
            for (var node = state.Current; node is not null && !ReferenceEquals(node, state.Root); node = node.ParentBox)
            {
                if (!node.IsWithinForeignContent && node.HtmlTag is { } tag && isItem(tag.Name.ToLowerInvariant()))
                {
                    PopThrough(state, node);
                    return;
                }

                if (IsSpecial(node) && !(IsNamed(node, "address") || IsNamed(node, "div") || IsNamed(node, "p")))
                    return;
            }
        }

        // What follows the insertion of a start tag's element.
        private static void AfterStartTagInserted(ParserState state, string name, CssBox element)
        {
            if (FormattingNames.Contains(name))
                PushFormatting(state, element);
            else if (name is "applet" or "object" or "marquee" or "td" or "th" or "caption")
                state.Formatting.Add(FormattingEntry.MarkerFor(element));
        }

        // The end tag of a name this algorithm has a rule for in the "in body" insertion mode; the end tags that it leaves to the callers' rules are not.
        private static void ParseEndTag(ParserState state, string name)
        {
            if (ReferenceEquals(state.Current, state.Root)) return;

            if (state.Current.IsWithinForeignContent)
            {
                // https://html.spec.whatwg.org/multipage/parsing.html#parsing-main-inforeign: look for the element through the foreign ones.
                for (var node = state.Current; node is not null && !ReferenceEquals(node, state.Root) && node.IsWithinForeignContent; node = node.ParentBox)
                {
                    if (node.HtmlTag!.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    {
                        PopThrough(state, node);
                        return;
                    }
                }
            }

            if (FormattingNames.Contains(name))
            {
                if (!RunAdoptionAgency(state, name))
                    CloseByAnyOtherEndTag(state, name);
            }
            else if (name == HtmlConstants.P)
            {
                if (FindInScope(state, HtmlConstants.P, Scope.Button) is { } paragraph)
                    PopThrough(state, paragraph);
                else
                    HandleStrayParagraphEndTag(state.Current);
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
            else if (ScopedEndTags.Contains(name))
            {
                if (FindInScope(state, name) is { } element)
                    PopThrough(state, element);
            }
            else if (NearestAncestorEndTags.Contains(name))
            {
                for (var node = state.Current; node is not null && !ReferenceEquals(node, state.Root); node = node.ParentBox)
                {
                    if (IsNamed(node, name))
                    {
                        PopThrough(state, node);
                        break;
                    }
                }
            }
            else
            {
                CloseByAnyOtherEndTag(state, name);
            }
        }

        // An end tag for a heading closes the innermost open heading that is in scope, whichever level it is.
        private static void CloseHeading(ParserState state)
        {
            for (var open = state.Current; open is not null && !ReferenceEquals(open, state.Root); open = open.ParentBox)
            {
                if (IsHeading(open))
                {
                    PopThrough(state, open);
                    return;
                }

                if (open.HtmlTag is not null && IsScopeBoundary(open, Scope.Default)) return;
            }
        }

        // https://html.spec.whatwg.org/multipage/parsing.html#parsing-main-inbody, "any other end tag": the nearest open element of that name
        // is closed, unless a special element is in the way, which ignores the end tag.
        private static void CloseByAnyOtherEndTag(ParserState state, string name)
        {
            for (var node = state.Current; node is not null && !ReferenceEquals(node, state.Root); node = node.ParentBox)
            {
                if (IsNamed(node, name) || (node.IsWithinForeignContent && node.HtmlTag!.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    PopThrough(state, node);
                    return;
                }

                if (IsSpecial(node)) return;
            }
        }

        /// <summary>
        /// https://html.spec.whatwg.org/multipage/parsing.html#adoption-agency-algorithm: an end tag for a formatting element, which may have
        /// elements open inside it that end up outside it, or the other way about. Works on the tree the stack of open elements is the chain
        /// of parents of.
        /// </summary>
        /// <returns>false when the end tag is for the "any other end tag" rule to answer</returns>
        private static bool RunAdoptionAgency(ParserState state, string subject)
        {
            var current = state.Current;
            if (!ReferenceEquals(current, state.Root) && IsNamed(current, subject) && IndexOfFormatting(state, current) < 0)
            {
                PopThrough(state, current);
                return true;
            }

            for (var outer = 0; outer < AdoptionOuterLoopLimit; outer++)
            {
                var list = state.Formatting;
                var formattingIndex = LastFormattingIndex(state, subject);
                if (formattingIndex < 0) return false;

                var formatting = list[formattingIndex].Box!;
                var stack = StackOf(state);
                var formattingPosition = stack.IndexOf(formatting);
                if (formattingPosition < 0)
                {
                    list.RemoveAt(formattingIndex);
                    return true;
                }

                if (!IsInScope(state, formatting)) return true;

                var furthestPosition = -1;
                for (var i = formattingPosition + 1; i < stack.Count; i++)
                {
                    if (IsSpecial(stack[i]))
                    {
                        furthestPosition = i;
                        break;
                    }
                }

                if (furthestPosition < 0)
                {
                    PopThrough(state, formatting);
                    list.RemoveAt(formattingIndex);
                    return true;
                }

                var furthest = stack[furthestPosition];
                var commonAncestor = formatting.ParentBox ?? state.Root;

                var bookmark = FormattingEntry.Bookmark();
                list.Insert(formattingIndex + 1, bookmark);

                var lastNode = furthest;
                var nodePosition = furthestPosition;
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

                lastNode.ParentBox = commonAncestor;
                EnsureWithinMaxDepth(lastNode);

                var replacement = CloneOf(formatting);
                replacement.SetAllBoxes(furthest);
                replacement.ParentBox = furthest;

                var wasCurrent = ReferenceEquals(state.Current, furthest);
                list[list.IndexOf(bookmark)] = FormattingEntry.ForElement(replacement);
                list.RemoveAt(IndexOfFormatting(state, formatting));

                if (wasCurrent) state.Current = replacement;
                EnsureWithinMaxDepth(state.Current);
            }

            return true;
        }
    }
}
