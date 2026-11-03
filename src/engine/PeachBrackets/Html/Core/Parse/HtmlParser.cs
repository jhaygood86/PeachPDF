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
    internal static partial class HtmlParser
    {
        /// <summary>
        /// Parses the source html to css boxes tree structure.
        /// </summary>
        /// <param name="source">the html source to parse</param>
        /// <param name="root">the root box (null for document root)</param>
        /// <param name="document">whether the source is a whole document, which gets the <c>html</c>, <c>head</c> and <c>body</c> elements it leaves out,
        /// rather than a fragment whose elements go in as they come</param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static CssBox ParseDocument(string source, CssBox? root = null, bool document = false)
        {
            root ??= CssBox.CreateBlock();
            var state = new ParserState(root) { IsDocument = document, Phase = document ? DocumentPhase.Initial : DocumentPhase.InBody };
            MathSourceParser? mathParser = null;

            using var sourceReader = new StringReader(source);
            var tokenizer = new HtmlTokenizer(sourceReader);

            while (tokenizer.ReadNextToken(out var token))
            {
#if DEBUG
                Console.WriteLine($"parse token start : current box: {state.Current}; tag: {token}");
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
                        PopThrough(state, mathParser.Box);
                        mathParser = null;
                        continue;
                    }

                    // The elements the source tree held are boxes now; the ones still open go on the stack.
                    var deepest = mathParser.Box.MaterializeSource(mathParser.Current)!;
                    var opened = new List<CssBox>();
                    for (var open = deepest; open is not null && !ReferenceEquals(open, mathParser.Box); open = open.ParentBox)
                        opened.Add(open);

                    opened.Reverse();
                    state.Stack.AddRange(opened);
                    mathParser = null;
                }

                switch (token.Kind)
                {
                    case HtmlTokenKind.Tag:
                        {
                            var tag = (HtmlTagToken)token;
                            var before = state.Current;
                            ParseHtmlTag(tag, state);

                            if (!tag.IsEndTag && !ReferenceEquals(before, state.Current) && state.Current is CssBoxMath { SourceStarted: false } mathBox
                                && !ParseMathAsBoxes.Value && !IsInsideMath(mathBox))
                            {
                                mathParser = new MathSourceParser(mathBox);
                            }

                            break;
                        }
                    case HtmlTokenKind.Data:
                        {
                            var text = (HtmlDataToken)token;

                            if (state.Current.HtmlTag?.Name.Equals(HtmlConstants.NoScript, StringComparison.OrdinalIgnoreCase) == true)
                            {
                                ParseDocument(text.Data, state.Current);
                            }
                            else
                            {
                                if (text.Data is { } data) ProcessText(state, data);
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

            if (document) FinishDocument(state);

            return root;
        }


        #region Private methods

        /// <summary>
        /// Parse the html part, the part from prev parsing index to the beginning of the next html tag.<br/>
        /// </summary>
        /// <param name="token">the html tag token</param>
        /// <param name="state">the state of the tree being built</param>
        private static void ParseHtmlTag(HtmlTagToken token, ParserState state)
        {
            if (ParseHtmlTag(token, out var tagName, out var tagAttributes))
            {
                var lowerName = tagName.ToLowerInvariant();

                // </br> is a parse error that the tree builder answers as <br>, with no attributes.
                if (lowerName == "br")
                {
                    ProcessStartTag(state, new StartTag(tagName, lowerName, null, true));
                }
                else if (!HtmlUtils.IsSingleTag(lowerName))
                {
#if DEBUG
                    Console.WriteLine($"parse token, tag close: {tagName}. current box: {state.Current}");
#endif
                    // Every unmatched end tag is a parse error the HTML5 tree construction algorithm simply ignores, leaving the
                    // insertion point where it is - NOT "close everything up to the document root", which would silently reparent
                    // the rest of the document under <body>/<html>; </p> alone generates an element instead.
                    ProcessEndTag(state, lowerName);
                }
            }
            else if (!string.IsNullOrEmpty(tagName))
            {
#if DEBUG
                Console.WriteLine($"parse token, tag open: {tagName}. current box: {state.Current}");
#endif
                var lowerName = tagName.ToLowerInvariant();
                var isSingle = HtmlUtils.IsSingleTag(lowerName) || token.IsEmptyElement;
                ProcessStartTag(state, new StartTag(tagName, lowerName, tagAttributes, isSingle));
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
        /// tokens (<see cref="ParseHtmlTag(HtmlTagToken, ParserState)"/>) in the cases it takes, and answering
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