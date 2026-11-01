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

using MimeKit.Text;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using PeachBrackets.MathML;
using PeachPDF;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using HtmlUtils = PeachBrackets.Html.Utils.HtmlUtils;

namespace PeachBrackets.Html.Parse
{
    /// <summary>
    /// 
    /// </summary>
    internal static class HtmlParser
    {
        /// <summary>
        /// Parses the source html to css boxes tree structure.
        /// </summary>
        /// <param name="source">the html source to parse</param>
        /// <param name="root">the root box (null for document root)</param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static CssBox ParseDocument(string source, CssBox? root = null)
        {
            root ??= CssBox.CreateBlock();
            var curBox = root;
            MathSourceParser? mathParser = null;

            using var sourceReader = new StringReader(source);
            var tokenizer = new HtmlTokenizer(sourceReader);

            while (tokenizer.ReadNextToken(out var token))
            {
#if DEBUG
                Console.WriteLine($"parse token start : current box: {curBox}; tag: {token}");
#endif

                // Inside an inline <math> the markup goes into source elements, not boxes (see MathSourceElement), for as long as it
                // stays within what that can take; the first token that does not is handed to the ordinary path below, after the
                // elements so far have been turned into the boxes that path would have built.
                if (mathParser is not null)
                {
                    var step = mathParser.Feed(token);
                    if (step == MathStep.Continue) continue;

                    if (step == MathStep.Ended)
                    {
                        curBox = mathParser.Box.ParentBox!;
                        mathParser = null;
                        continue;
                    }

                    curBox = mathParser.Box.MaterializeSource(mathParser.Current)!;
                    mathParser = null;
                }

                switch (token.Kind)
                {
                    case HtmlTokenKind.Tag:
                        {
                            var tag = (HtmlTagToken)token;
                            var before = curBox;
                            ParseHtmlTag(tag, ref curBox);

                            if (!tag.IsEndTag && !ReferenceEquals(before, curBox) && curBox is CssBoxMath { SourceStarted: false } mathBox
                                && !ParseMathAsBoxes.Value && !IsInsideMath(mathBox))
                            {
                                mathParser = new MathSourceParser(mathBox);
                            }

                            break;
                        }
                    case HtmlTokenKind.Data:
                        {
                            var text = (HtmlDataToken)token;

                            if (curBox.HtmlTag?.Name.Equals(HtmlConstants.NoScript, StringComparison.OrdinalIgnoreCase) == true)
                            {
                                curBox = ParseDocument(text.Data, curBox);
                            }
                            else
                            {
                                AddTextBox(text, ref curBox);
                            }

                            break;

                        }
                    case HtmlTokenKind.CData:
                    case HtmlTokenKind.Comment:
                    case HtmlTokenKind.DocType:
                    case HtmlTokenKind.ScriptData:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            return root;
        }


        /// <summary>
        /// Inserts the <c>html</c> and <c>body</c> elements the HTML parsing algorithm creates implicitly
        /// (<see href="https://html.spec.whatwg.org/multipage/parsing.html#the-before-html-insertion-mode">"before html"</see>
        /// and <see href="https://html.spec.whatwg.org/multipage/parsing.html#the-after-head-insertion-mode">"after head"</see>
        /// insertion modes) when the source omits their start tags, so <c>body { }</c>/<c>html { }</c>
        /// rules match a fragment such as <c>&lt;p&gt;a&lt;/p&gt;</c> exactly as they match the written-out form.
        /// Leading head-content elements (<c>style</c>, <c>meta</c>, ...) stay outside the implicit body.
        /// Applied to whole documents only - never to a fragment spliced into an existing tree.
        /// </summary>
        public static void EnsureHtmlAndBody(CssBox root)
        {
            if (root.Boxes.Count == 0) return;

            var html = root.Boxes.Find(IsHtml);
            if (html is null)
            {
                html = CssBox.CreateBox(new HtmlTag("html", false, null), root);
                foreach (var child in root.Boxes.Where(b => b != html).ToList())
                    child.ParentBox = html;
            }

            if (html.Boxes.Exists(b => IsTag(b, "body") || IsTag(b, "frameset"))) return;

            var toMove = new List<CssBox>();
            var bodyStarted = false;
            foreach (var child in html.Boxes)
            {
                if (!bodyStarted)
                {
                    var isHeadContent = IsTag(child, "head") || (child.HtmlTag is { } t
                        && t.Name.ToLowerInvariant() is "title" or "meta" or "link" or "style" or "script" or "base" or "template");
                    var isBlankText = child.HtmlTag is null && string.IsNullOrWhiteSpace(child.Text);
                    if (isHeadContent || isBlankText) continue;
                    bodyStarted = true;
                }

                if (!IsTag(child, "head")) toMove.Add(child);
            }

            if (toMove.Count == 0) return;

            var body = CssBox.CreateBox(new HtmlTag("body", false, null), html);
            foreach (var child in toMove)
                child.ParentBox = body;

            static bool IsHtml(CssBox b) => IsTag(b, "html");
            static bool IsTag(CssBox b, string name) => b.HtmlTag?.Name.Equals(name, StringComparison.OrdinalIgnoreCase) == true;
        }


        #region Private methods

        /// <summary>
        /// Add html text anon box to the current box, this box will have the rendered text<br/>
        /// Adding box also for text that contains only whitespaces because we don't know yet if
        /// the box is preformatted. At later stage they will be removed if not relevant.
        /// </summary>
        /// <param name="token">the html token to parse</param>
        /// <param name="curBox">the current box in html tree parsing</param>
        private static void AddTextBox(HtmlDataToken token, ref CssBox curBox)
        {
            var text = token.Data;

            if (text == null) return;

            // A MathML token's text stays on its element (see CssBox.ForeignText).
            if (curBox.TryAbsorbForeignText(text)) return;

            var box = CssBox.CreateBox(curBox);
            box.Text = text;
        }


        /// <summary>
        /// Parse the html part, the part from prev parsing index to the beginning of the next html tag.<br/>
        /// </summary>
        /// <param name="token">the html tag token</param>
        /// <param name="curBox">the current box in html tree parsing</param>
        /// <returns>the end of the parsed part, the new start index</returns>
        private static void ParseHtmlTag(HtmlTagToken token, ref CssBox curBox)
        {
            if (ParseHtmlTag(token, out var tagName, out var tagAttributes))
            {
                if (!HtmlUtils.IsSingleTag(tagName.ToLowerInvariant()) && curBox.ParentBox != null)
                {
#if DEBUG
                    Console.WriteLine($"parse token, tag close: {tagName}. current box: {curBox}");
#endif
                    // need to find the parent tag to go one level up
                    var isParagraphEndTag = string.Equals(
                        tagName, HtmlConstants.P, StringComparison.OrdinalIgnoreCase);
                    var afterClose = isParagraphEndTag && !HasElementInButtonScope(curBox, HtmlConstants.P)
                        ? null
                        : DomUtils.FindParent(curBox.ParentBox!, tagName, curBox);

                    if (afterClose is not null)
                    {
                        curBox = afterClose;
                    }
                    else if (isParagraphEndTag)
                    {
                        // The one unmatched end tag that generates an element rather than being dropped.
                        HandleStrayParagraphEndTag(curBox);
                    }

                    // Every other unmatched end tag is a parse error the HTML5 tree construction algorithm
                    // simply ignores, leaving the insertion point where it is - NOT "close everything up
                    // to the document root", which would silently reparent the rest of the document under
                    // <body>/<html>.
                }
            }
            else if (!string.IsNullOrEmpty(tagName))
            {
#if DEBUG
                Console.WriteLine($"parse token, tag open: {tagName}. current box: {curBox}");
#endif
                while (true)
                {
                    if (curBox.HtmlTag is not null && HtmlUtils.CanEndTagBeOmitted(curBox.HtmlTag.Name.ToLowerInvariant(), tagName.ToLowerInvariant()))
                    {
                        var previousBox = curBox;
                        curBox = CloseElement(curBox, curBox.HtmlTag.Name);

#if DEBUG
                        Console.WriteLine($"parse token, prev tag omitted, closing: {previousBox}. current box is now: {curBox}");
#endif
                    }
                    else
                    {
                        break;
                    }
                }

                var isSingle = HtmlUtils.IsSingleTag(tagName.ToLowerInvariant()) || token.IsEmptyElement;
                var tag = new HtmlTag(tagName, isSingle, tagAttributes);

                if (isSingle)
                {
                    // the current box is not changed
                    CssBox.CreateBox(tag, curBox);
                }
                else
                {
                    // go one level down, make the new box the current box
                    curBox = CssBox.CreateBox(tag, curBox);
                    EnsureWithinMaxDepth(curBox);
                }
            }
        }

        /// <summary>
        /// The deepest an element may sit in the box tree. Cascade, layout, fragment emission and paint all walk the
        /// tree recursively, and a stack overflow takes down the whole process where a host cannot catch it. Layout's
        /// recursion is a chain of async state machines of several KB per level, so a 1 MB stack (the default of a
        /// Windows thread, and of a WebAssembly host) runs out at about 150 levels; layout, fragment emission, paint
        /// and the SVG walks check the stack themselves (<see cref="Utilities.StackGuard"/>) and fail as an
        /// <see cref="HtmlRenderException"/> at whatever depth the host's stack allows. This limit is the backstop for
        /// the walks that do not check - the cascade, which runs before any of them - and keeps a generated document
        /// of thousands of levels from reaching them. Browsers allow 512, but they do not recurse on the machine stack.
        /// </summary>
        internal const int MaxElementDepth = 400;

        private static void EnsureWithinMaxDepth(CssBox box)
        {
            var depth = 0;

            for (var ancestor = box; ancestor is not null; ancestor = ancestor.ParentBox)
            {
                if (++depth > MaxElementDepth)
                {
                    throw new HtmlRenderException(
                        $"The document nests elements more than {MaxElementDepth} levels deep, which the renderer does not support.",
                        HtmlRenderErrorType.HtmlParsing);
                }
            }
        }

        private static bool HasElementInButtonScope(CssBox currentBox, string tagName)
        {
            for (CssBox? box = currentBox; box is not null; box = box.ParentBox)
            {
                var openTagName = box.HtmlTag?.Name;
                if (string.Equals(openTagName, tagName, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (openTagName?.ToLowerInvariant() is "applet" or "caption" or "html" or "table"
                    or "td" or "th" or "marquee" or "object" or "template" or "button")
                {
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// Moves the insertion point up past <paramref name="tagName"/>'s own box. Used for an
        /// <i>implied</i> end tag (<see cref="HtmlUtils.CanEndTagBeOmitted"/>), where the element being
        /// closed is <paramref name="cssBox"/> itself and so is always found; an explicit end tag is
        /// resolved by its caller instead, which has a stray-end-tag case to answer that this does not
        /// (see <see cref="HandleStrayParagraphEndTag"/>).
        /// </summary>
        private static CssBox CloseElement(CssBox cssBox, string tagName)
        {
            var currentBox = DomUtils.FindParent(cssBox.ParentBox!, tagName, cssBox) ?? cssBox;

#if DEBUG
            Console.WriteLine($"parse token, closing: {cssBox}. current box is now: {currentBox}");
#endif

            return currentBox;
        }

        /// <summary>
        /// Handles a <c>&lt;/p&gt;</c> whose <c>&lt;p&gt;</c> is no longer open, by inserting an empty
        /// one — <see href="https://html.spec.whatwg.org/multipage/parsing.html#parsing-main-inbody">HTML
        /// §13.2.6.4.7 "in body"</see>: "An end tag whose tag name is <c>p</c>: if the stack of open
        /// elements does not have a <c>p</c> element in button scope, then this is a parse error; insert
        /// an HTML element for a <c>p</c> start tag token with no attributes. Close a <c>p</c> element."
        /// </summary>
        /// <remarks>
        /// <c>p</c> is one of only two end tags the spec answers this way rather than by ignoring an
        /// unmatched end tag outright (<c>br</c> is the other, and is turned into a <i>start</i> tag
        /// instead — not implemented here). The generated element is empty and immediately closed, so the
        /// insertion point does not move; what it changes is the <b>element count</b>, and therefore
        /// sibling-combinator matching.
        /// <para>
        /// This is not academic. It is exactly what Acid2 tests with
        /// <c>&lt;p&gt;&lt;table&gt;…&lt;/table&gt;&lt;/p&gt;&lt;p class="bad"&gt;</c>: <c>&lt;table&gt;</c>
        /// closes the first <c>&lt;p&gt;</c>, so the <c>&lt;/p&gt;</c> after it is stray and generates the
        /// empty paragraph that <c>.picture p + table + p</c> then matches. Without it that selector falls
        /// through onto <c>p.bad</c> instead, which is precisely the element the fixture means to keep it
        /// away from — <c>p.bad</c> takes a <c>margin-top: 3em</c> meant for the generated element and
        /// slides a black-and-red bar down across the middle of the face.
        /// </para>
        /// </remarks>
        private static void HandleStrayParagraphEndTag(CssBox curBox)
        {
            CssBox? fosterTable = null;

            for (var box = curBox; box is not null; box = box.ParentBox)
            {
                var name = box.HtmlTag?.Name.ToLowerInvariant();
                if (name == HtmlConstants.Select)
                    return;

                if (name is HtmlConstants.Td or HtmlConstants.Th or "template")
                    break;

                if (name == HtmlConstants.Table)
                {
                    fosterTable = box;
                    break;
                }
            }

#if DEBUG
            Console.WriteLine($"parse token, stray </p> generating an empty <p>. current box: {curBox}");
#endif

            var paragraph = new HtmlTag(HtmlConstants.P, isSingle: false);
            if (fosterTable?.ParentBox is { } fosterParent)
                CssBox.CreateBox(fosterParent, paragraph, fosterTable);
            else
                CssBox.CreateBox(paragraph, curBox);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="token"></param>
        /// <param name="name"></param>
        /// <param name="attributes"></param>
        /// <returns></returns>
        private static bool ParseHtmlTag(HtmlTagToken token, out string name, out Dictionary<string, string>? attributes)
        {
            var isClosing = token.IsEndTag;

            name = token.Name;

            attributes = null;

            if (!isClosing)
            {
                // A valueless attribute (<hr noshade>, <input disabled>) tokenizes with a null
                // value, but the HTML Standard gives it the empty string - which is what makes
                // an [attr] presence selector match it (Selectors 4 §6.1 tests for the
                // attribute, not for a value). Storing the null instead made every boolean
                // attribute invisible to `[disabled]`, `[hidden]`, `hr[noshade]` and friends,
                // while the same attribute written as `noshade=""` matched.
                // Built once, directly in the case-insensitive form HtmlTag keeps, first occurrence winning (HTML §13.2.5.33:
                // a duplicate attribute is a parse error and is dropped). No dictionary for an element with no attributes.
                var tokenAttributes = token.Attributes;
                if (tokenAttributes.Count > 0)
                {
                    attributes = new Dictionary<string, string>(tokenAttributes.Count, StringComparer.OrdinalIgnoreCase);
                    foreach (var attribute in tokenAttributes)
                        attributes.TryAdd(attribute.Name, attribute.Value ?? string.Empty);
                }
            }

            return isClosing;
        }

        #endregion

        /// <summary>
        /// Makes an inline <c>&lt;math&gt;</c> parse into boxes, as it did before it could parse into <see cref="MathSourceElement"/>s.
        /// For tests that check the two agree.
        /// </summary>
        internal static readonly AsyncLocal<bool> ParseMathAsBoxes = new();

        /// <summary>Whether <paramref name="box"/> is inside another inline <c>&lt;math&gt;</c>, whose builder reads the boxes under it.</summary>
        private static bool IsInsideMath(CssBox box)
        {
            for (var ancestor = box.ParentBox; ancestor is not null; ancestor = ancestor.ParentBox)
            {
                if (ancestor is CssBoxMath) return true;
            }

            return false;
        }

        private enum MathStep
        {
            Continue,
            Ended,
            Abandon
        }

        /// <summary>The MathML elements the source tree takes; any other name sends the markup down the ordinary path.</summary>
        private static readonly FrozenSet<string> MathElementNames = new[]
        {
            "mi", "mo", "mn", "mtext", "ms", "mspace", "mglyph", "mrow", "mfrac", "msqrt", "mroot", "mstyle", "merror", "mpadded",
            "mphantom", "mfenced", "menclose", "msub", "msup", "msubsup", "munder", "mover", "munderover", "mmultiscripts",
            "mprescripts", "none", "mtable", "mtr", "mtd", "mlabeledtr", "maction", "semantics", "annotation", "annotation-xml",
            "malignmark", "maligngroup"
        }.ToFrozenSet(StringComparer.Ordinal);

        /// <summary>
        /// Builds the source elements of one inline <c>&lt;math&gt;</c> from its tokens, doing what the ordinary path does for the same
        /// tokens (<see cref="ParseHtmlTag(HtmlTagToken, ref CssBox)"/>) in the cases it takes, and answering
        /// <see cref="MathStep.Abandon"/> before changing anything for a token it does not: an element that is not MathML, an end tag
        /// for nothing it has open, a <c>&lt;/p&gt;</c>.
        /// </summary>
        private sealed class MathSourceParser
        {
            internal MathSourceParser(CssBoxMath box)
            {
                Box = box;
                Current = box.BeginSource();
            }

            internal CssBoxMath Box { get; }

            /// <summary>The element the next token goes into: the open one, or the <c>&lt;math&gt;</c> itself.</summary>
            internal MathSourceElement Current { get; private set; }

            internal MathStep Feed(HtmlToken token)
            {
                switch (token.Kind)
                {
                    case HtmlTokenKind.Tag:
                        {
                            var tag = (HtmlTagToken)token;
                            return ParseHtmlTag(tag, out var name, out var attributes) ? Close(name) : Open(tag, name, attributes);
                        }
                    case HtmlTokenKind.Data:
                        {
                            if (((HtmlDataToken)token).Data is { } text) Current.AddText(text);
                            return MathStep.Continue;
                        }
                    default:
                        return MathStep.Continue;
                }
            }

            private MathStep Open(HtmlTagToken token, string name, Dictionary<string, string>? attributes)
            {
                if (string.IsNullOrEmpty(name)) return MathStep.Continue;
                if (!MathElementNames.Contains(name)) return MathStep.Abandon;

                var isSingle = token.IsEmptyElement;
                var element = Current.AddElement(new HtmlTag(name, isSingle, attributes));
                if (!isSingle) Current = element;

                return MathStep.Continue;
            }

            private MathStep Close(string name)
            {
                if (HtmlUtils.IsSingleTag(name.ToLowerInvariant())) return MathStep.Continue;
                if (string.Equals(name, HtmlConstants.P, StringComparison.OrdinalIgnoreCase)) return MathStep.Abandon;

                for (var element = Current; element is not null; element = element.ParentElement)
                {
                    if (!element.Tag!.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                    if (element.IsRoot) return MathStep.Ended;

                    Current = element.ParentElement!;
                    return MathStep.Continue;
                }

                return MathStep.Abandon;
            }
        }
    }
}