using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PeachPDF.Html.Core.Dom
{
    internal static class CssContentEngine
    {
        /// <summary>
        /// One item of a <c>content</c> value that contains a <c>leader()</c> (css-content-3 §6) - a
        /// resolved text run, or the pattern a <c>leader()</c> item asked for (never both). Produced by
        /// <see cref="BuildContentSegments"/> and consumed by <see cref="CssBox.ParseToWordsWithLeaders"/>,
        /// since a leader-bearing content list has no single flat-string representation the way every
        /// other <c>content</c> value does.
        /// </summary>
        internal readonly record struct ContentSegment(string? Text, LeaderKind? Leader, string? CustomPattern);

        public static void ApplyContent(CssBox cssBox)
        {
            if (cssBox.Content is Keywords.None or Keywords.Normal)
            {
                return;
            }

            var tokens = CssValueParser.GetCssTokens(cssBox.Content);

            // Detect image content (url() or gradient functions) before building text
            if (tokens.Count > 0 && cssBox.HtmlContainer?.Adapter is RAdapter adapter)
            {
                var first = tokens[0];
                if (first is UrlToken ||
                    (first is FunctionToken ft && IsGradientFunctionName(ft.Data)))
                {
                    var image = new CssValueParser(adapter).ParseImage(cssBox.Content);
                    if (image != null)
                    {
                        cssBox.ContentImage = image;
                        return;
                    }
                }
            }

            // A leader() item's width is decided later, post-flow (CssLayoutEngine.ApplyLeaderFill), not
            // here - it has no text representation at all, so a content list containing one can't
            // collapse into cssBox.Text the way every other content value does. ParseToWordsWithLeaders
            // populates cssBox.Words directly and leaves Text null; CssContentEngine.GetTextContent's
            // Text-null fallback already walks Boxes instead, which a leader-bearing pseudo-element is
            // never a sensible target-text() target of anyway.
            if (ContainsLeader(tokens))
            {
                cssBox.ParseToWordsWithLeaders(BuildContentSegments(cssBox, tokens));
                return;
            }

            cssBox.Text = ResolveContentTokens(cssBox, tokens);
        }

        private static bool ContainsLeader(List<Token> tokens)
        {
            foreach (var token in tokens)
            {
                if (token is FunctionToken { Data: FunctionNames.Leader }) return true;
            }

            return false;
        }

        /// <summary>
        /// Splits a content list containing one or more <c>leader()</c> items into
        /// <see cref="ContentSegment"/>s - a resolved text run for every other token (reusing
        /// <see cref="ResolveContentTokens"/> one token at a time, so this never re-derives that switch's
        /// own per-token resolution), flushed whenever a <c>leader()</c> token is hit.
        /// </summary>
        private static List<ContentSegment> BuildContentSegments(CssBox cssBox, List<Token> tokens)
        {
            var segments = new List<ContentSegment>();
            var textBuffer = new StringBuilder();

            void FlushText()
            {
                if (textBuffer.Length > 0)
                {
                    segments.Add(new ContentSegment(textBuffer.ToString(), null, null));
                    textBuffer.Clear();
                }
            }

            foreach (var token in tokens)
            {
                if (token is FunctionToken { Data: FunctionNames.Leader } leaderToken)
                {
                    FlushText();
                    var (kind, pattern) = ResolveLeaderToken(leaderToken);
                    segments.Add(new ContentSegment(null, kind, pattern));
                    continue;
                }

                textBuffer.Append(ResolveContentTokens(cssBox, [token]));
            }

            FlushText();
            return segments;
        }

        /// <summary>
        /// Grammar already validated at parse time (<see cref="LeaderFunctionConverter"/>). The default
        /// return below is reached for the real, spec-legal <c>leader()</c> (zero-argument, defaults to
        /// <see cref="LeaderKind.Dotted"/>) as well as for any other malformed value, which degrades the
        /// same way rather than throwing.
        /// </summary>
        private static (LeaderKind Kind, string? Pattern) ResolveLeaderToken(FunctionToken leaderToken)
        {
            var args = leaderToken.ArgumentTokens
                .Where(t => t.Type != TokenType.Comma && t.Type != TokenType.Whitespace)
                .ToArray();

            if (args.Length == 1)
            {
                switch (args[0])
                {
                    case KeywordToken { Data: Keywords.Solid }:
                        return (LeaderKind.Solid, null);
                    case KeywordToken { Data: Keywords.Space }:
                        return (LeaderKind.Space, null);
                    case StringToken stringToken:
                        return (LeaderKind.Custom, stringToken.Data);
                }
            }

            return (LeaderKind.Dotted, null);
        }

        /// <summary>
        /// Resolves a tokenized <c>&lt;content-list&gt;</c> (strings, <c>counter()</c>, <c>attr()</c>,
        /// <c>content()</c>, <c>string()</c>) against <paramref name="cssBox"/> into its text value.
        /// Shared by <see cref="ApplyContent"/> (the <c>content</c> property) and
        /// <see cref="ResolveBookmarkLabel"/> (<c>bookmark-label</c>, whose value IS a
        /// <c>&lt;content-list&gt;</c> and nothing else) so both consume one resolver for the same
        /// grammar rather than each re-walking it independently.
        /// </summary>
        internal static string ResolveContentTokens(CssBox cssBox, List<Token> tokens)
        {
            var contentText = new StringBuilder();

            foreach (var token in tokens)
            {
                switch (token)
                {
                    case StringToken stringToken:
                        contentText.Append(stringToken.Data);
                        break;
                    case FunctionToken { Data: FunctionNames.Counter } functionToken:
                        {
                            AppendCounter(contentText, cssBox, functionToken);
                            break;
                        }
                    case FunctionToken { Data: "content" } contentFunctionToken:
                        {
                            var contentValue = ExtractContentValue(cssBox, contentFunctionToken);
                            if (!string.IsNullOrEmpty(contentValue))
                            {
                                contentText.Append(contentValue);
                            }
                            break;
                        }
                    case FunctionToken { Data: "string" } stringFunctionToken:
                        {
                            var stringValue = ExtractStringValue(cssBox, stringFunctionToken);
                            if (!string.IsNullOrEmpty(stringValue))
                            {
                                contentText.Append(stringValue);
                            }
                            break;
                        }
                    case FunctionToken { Data: "attr" } attrFunctionToken:
                        {
                            // Handle attr() function
                            if (attrFunctionToken.ArgumentTokens.Any())
                            {
                                var attrNameToken = attrFunctionToken.ArgumentTokens.FirstOrDefault();
                                if (attrNameToken is KeywordToken keywordToken)
                                {
                                    var attrName = keywordToken.Data;
                                    // Get attribute from parent element if this is a pseudo-element
                                    var sourceBox = cssBox.IsPseudoElement && cssBox.ParentBox != null
                                        ? cssBox.ParentBox
                                        : cssBox;
                                    var attrValue = sourceBox.GetAttribute(attrName, "");
                                    if (!string.IsNullOrEmpty(attrValue))
                                    {
                                        contentText.Append(attrValue);
                                    }
                                }
                            }
                            break;
                        }
                    case FunctionToken { Data: FunctionNames.TargetCounter } targetCounterToken:
                        {
                            AppendTargetCounter(contentText, cssBox, targetCounterToken);
                            break;
                        }
                    case FunctionToken { Data: FunctionNames.TargetText } targetTextToken:
                        {
                            var targetTextValue = ResolveTargetText(cssBox, targetTextToken);
                            if (!string.IsNullOrEmpty(targetTextValue))
                            {
                                contentText.Append(targetTextValue);
                            }
                            break;
                        }
                }
            }

            return contentText.ToString();
        }

        /// <summary>
        /// Appends the value of a <c>target-counter(&lt;target&gt;, &lt;counter-name&gt; [, &lt;style&gt;])</c>
        /// function (css-content-3 §5) to <paramref name="sb"/> - a counter's value at another element,
        /// not this one. <c>&lt;target&gt;</c> is resolved via <see cref="ResolveTarget"/>. The
        /// <c>page</c> counter name is UA magic (mirroring <see cref="MarginBoxRenderer"/>'s own handling
        /// of <c>counter(page)</c> in margin boxes), resolved against the final page the target box lands
        /// on - see <see cref="HtmlContainerInt"/>'s target-page convergence loop
        /// (<c>PerformLayoutOnePass</c>), which is what makes that page number real rather than the
        /// placeholder emitted before a page map exists. Any other counter name resolves immediately via
        /// <see cref="CssCounterEngine.GetCounter"/>, with no pagination dependency.
        /// </summary>
        private static void AppendTargetCounter(StringBuilder sb, CssBox cssBox, FunctionToken functionToken)
        {
            var arguments = functionToken.ArgumentTokens
                .Where(t => t.Type != TokenType.Comma && t.Type != TokenType.Whitespace)
                .ToArray();

            if (arguments.Length < 2 || arguments[1] is not KeywordToken counterNameToken)
            {
                return;
            }

            var targetBox = ResolveTarget(cssBox, arguments[0]);
            if (targetBox == null)
            {
                // Unresolved target (including the running-element/repeated-table-header gap this shares
                // with PDF bookmarks - see
                // .claude/accepted-gaps/target-counter-target-text-running-element-not-resolved.md) -
                // never throws, emits nothing, the same as a real UA's own "target not rendered" behavior.
                return;
            }

            var style = arguments.Length > 2 && arguments[2] is KeywordToken styleToken
                ? styleToken.Data
                : Keywords.Decimal;

            if (counterNameToken.Data.Equals("page", StringComparison.OrdinalIgnoreCase))
            {
                // A structural, permanent fact about this box's content - not "still needs its first
                // resolution" - so every convergence-loop pass keeps re-visiting it even after it has
                // resolved once, since a later pass's pagination can still move which page the target
                // lands on.
                cssBox.HasPendingTargetPageContent = true;
                var container = cssBox.HtmlContainer;

                if (container?.TargetPageMap is { } map)
                {
                    var targetRect = CommonUtils.GetFirstValueOrDefault(targetBox.Rectangles, targetBox.Bounds);
                    var pageIndex = PageAnchorResolver.ResolvePixelYToPage(
                        container, map.SlotToPage, map.MaxMappedSlot, map.FallbackPageCount, targetRect.Top);
                    sb.Append(CssCounterEngine.FormatCounterValue(pageIndex + 1, style));
                }
                else
                {
                    // No page map yet (the pre-layout DOM-construction pass, or before the target-page
                    // convergence loop's first iteration has run) - the same placeholder counter(page)
                    // already silently produces outside margin boxes today; the convergence loop revisits
                    // this box once a real map exists.
                    sb.Append(CssCounterEngine.FormatCounterValue(1, style));
                }

                return;
            }

            var counterValue = CssCounterEngine.GetCounter(targetBox, counterNameToken.Data)?.Value ?? 1;
            sb.Append(CssCounterEngine.FormatCounterValue(counterValue, style));
        }

        /// <summary>
        /// Resolves <c>target-text(&lt;target&gt; [, content | before | after | first-letter])</c>
        /// (css-content-3 §5). All four modes resolve against the target element, mirroring
        /// <see cref="ExtractContentValue"/>'s own <c>content()</c> mode dispatch (used by the
        /// <c>content</c> property and <c>bookmark-label</c>) - just against the resolved
        /// <paramref name="functionToken"/> target box instead of <paramref name="cssBox"/> itself.
        /// </summary>
        private static string? ResolveTargetText(CssBox cssBox, FunctionToken functionToken)
        {
            var arguments = functionToken.ArgumentTokens
                .Where(t => t.Type != TokenType.Comma && t.Type != TokenType.Whitespace)
                .ToArray();

            if (arguments.Length == 0)
            {
                return null;
            }

            var targetBox = ResolveTarget(cssBox, arguments[0]);
            if (targetBox == null)
            {
                return null;
            }

            var mode = arguments.Length > 1 && arguments[1] is KeywordToken modeToken
                ? modeToken.Data.ToLowerInvariant()
                : "content";

            return mode switch
            {
                "content" => ExtractText(targetBox),
                "before" => ExtractPseudoElementContent(targetBox, isBeforePseudo: true),
                "after" => ExtractPseudoElementContent(targetBox, isBeforePseudo: false),
                "first-letter" => ExtractFirstLetter(targetBox),
                _ => null
            };
        }

        /// <summary>
        /// Resolves <c>target-counter()</c>/<c>target-text()</c>'s shared <c>&lt;target&gt;</c> argument
        /// (<c>&lt;string&gt; | url(&lt;url&gt;) | attr(&lt;ident&gt;)</c> - the same shape
        /// <c>-peachpdf-bookmark-target</c> already established, see
        /// <see cref="BookmarkOutlineBuilder"/>) to the element it names, via
        /// <see cref="HtmlContainerInt.GetBoxById(CssBox, string)"/>. A <c>url()</c>/<c>attr()</c> value carries a
        /// leading <c>#</c> the same way an <c>&lt;a href="#id"&gt;</c> anchor does; a bare
        /// <c>&lt;string&gt;</c> target names the id directly. Never throws on an unresolved id - the
        /// caller treats that the same as any other unresolved target.
        /// </summary>
        private static CssBox? ResolveTarget(CssBox cssBox, Token targetToken)
        {
            var container = cssBox.HtmlContainer;
            if (container == null)
            {
                return null;
            }

            var id = targetToken switch
            {
                StringToken stringToken => stringToken.Data,
                UrlToken urlToken => string.IsNullOrEmpty(urlToken.Data) ? null : urlToken.Data,
                FunctionToken { Data: "attr" } attrToken => ResolveTargetAttr(cssBox, attrToken),
                _ => null
            };

            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            if (id[0] == '#')
            {
                id = id[1..];
            }

            return string.IsNullOrEmpty(id) ? null : container.GetBoxById(cssBox, id);
        }

        private static string? ResolveTargetAttr(CssBox cssBox, FunctionToken attrToken)
        {
            if (attrToken.ArgumentTokens.FirstOrDefault() is not KeywordToken nameToken)
            {
                return null;
            }

            var sourceBox = cssBox.IsPseudoElement && cssBox.ParentBox != null ? cssBox.ParentBox : cssBox;
            var value = sourceBox.GetAttribute(nameToken.Data, "");
            return string.IsNullOrEmpty(value) ? null : value;
        }

        /// <summary>
        /// Resolves <c>bookmark-label</c> (CSS Generated Content Module Level 3 §2) for a PDF outline
        /// entry's title. The property's initial value is the raw text <c>content(text)</c>, which the
        /// shared <see cref="ResolveContentTokens"/> switch already handles via its existing
        /// <c>content()</c>/<c>"text"</c> branch (<see cref="ExtractContentValue"/> -&gt;
        /// <see cref="ExtractText"/>) - so the default case needs no special-casing here.
        /// </summary>
        public static string ResolveBookmarkLabel(CssBox cssBox)
        {
            var tokens = CssValueParser.GetCssTokens(cssBox.BookmarkLabel);
            return ResolveContentTokens(cssBox, tokens);
        }

        /// <summary>
        /// Shared with <see cref="MarginBoxRenderer.ResolveContentImage"/> - a margin box's own
        /// <c>content</c> accepts the same <c>&lt;image&gt;</c> value grammar (CSS Paged Media Level 3
        /// §7), so both places must agree on which function names count as image content rather than
        /// re-deriving the list independently.
        /// </summary>
        internal static bool IsGradientFunctionName(string name) =>
            name is "linear-gradient" or "repeating-linear-gradient"
                 or "radial-gradient" or "repeating-radial-gradient"
                 or "conic-gradient" or "repeating-conic-gradient";

        /// <summary>
        /// Appends the value of a <c>counter(&lt;name&gt; [, &lt;style&gt;])</c> function to
        /// <paramref name="sb"/>, resolved against <paramref name="counterBox"/>. The optional second
        /// argument selects a counter style (<c>decimal</c>, <c>decimal-leading-zero</c>,
        /// <c>lower-roman</c>, ...); when omitted it defaults to <c>decimal</c>, and an unknown style
        /// falls back to <c>decimal</c> per CSS Counter Styles Level 3 §2 (both handled by
        /// <see cref="CssCounterEngine.FormatCounterValue"/>).
        /// </summary>
        private static void AppendCounter(StringBuilder sb, CssBox counterBox, FunctionToken functionToken)
        {
            var arguments = functionToken.ArgumentTokens
                .Where(t => t.Type != TokenType.Comma && t.Type != TokenType.Whitespace)
                .ToArray();

            if (arguments.Length == 0 || arguments[0] is not KeywordToken counterName)
            {
                return;
            }

            var counterValue = CssCounterEngine.GetCounter(counterBox, counterName.Data)?.Value ?? 1;

            var style = arguments.Length > 1 && arguments[1] is KeywordToken styleToken
                ? styleToken.Data
                : Keywords.Decimal;

            sb.Append(CssCounterEngine.FormatCounterValue(counterValue, style));
        }

        private static string? ExtractStringValue(CssBox cssBox, FunctionToken stringFunctionToken)
        {
            var arguments = stringFunctionToken.ArgumentTokens
   .Where(t => t.Type != TokenType.Comma && t.Type != TokenType.Whitespace)
        .ToArray();

            if (arguments.Length == 0)
            {
                return null;
            }

            // First argument is the named string identifier
            if (arguments[0] is not KeywordToken nameToken)
            {
                return null;
            }

            var stringName = nameToken.Data;

            // Second argument is the optional keyword (first, start, last, first-except)
            // Default is "first"
            var keyword = "first";
            if (arguments.Length > 1 && arguments[1] is KeywordToken keywordToken)
            {
                keyword = keywordToken.Data.ToLowerInvariant();
            }

            // Use the CssNamedStringEngine to retrieve the named string
            return GetNamedStringValue(cssBox, stringName, keyword);
        }

        private static string? GetNamedStringValue(CssBox cssBox, string name, string keyword) =>
            cssBox.HtmlContainer != null
                ? GetNamedStringValueFromDocument(cssBox.HtmlContainer, name, keyword)
                : GetNamedStringValueFromTree(cssBox, name, keyword);

        /// <summary>Document-level named strings, tracked on the HTML container as they're assigned during layout.</summary>
        private static string GetNamedStringValueFromDocument(HtmlContainerInt container, string name, string keyword)
        {
            NamedString? firstMatch = null;
            NamedString? lastMatch = null;

            foreach (var namedString in container.NamedStrings)
            {
                if (namedString.Name != name) continue;

                firstMatch ??= namedString;
                lastMatch = namedString;
            }

            return keyword switch
            {
                "first" => firstMatch?.Value ?? string.Empty,
                "start" => firstMatch?.Value ?? string.Empty, // TODO: Implement proper start logic (first on page)
                "last" => lastMatch?.Value ?? string.Empty,
                "first-except" => string.Empty, // TODO: Implement proper first-except logic
                _ => firstMatch?.Value ?? string.Empty
            };
        }

        /// <summary>Fallback tree-based search, used when the box has no HTML container to consult.</summary>
        private static string GetNamedStringValueFromTree(CssBox cssBox, string name, string keyword)
        {
            var box = cssBox;
            NamedString? nearestAssignment = null;
            NamedString? farthestAssignment = null;

            while (box != null)
            {
                if (box.NamedStrings.TryGetValue(name, out var namedString))
                {
                    nearestAssignment ??= namedString;
                    farthestAssignment = namedString;
                }
                box = box.ParentBox;
            }

            return keyword switch
            {
                "first" => farthestAssignment?.Value ?? string.Empty,
                "start" => farthestAssignment?.Value ?? string.Empty,
                "last" => nearestAssignment?.Value ?? string.Empty,
                "first-except" => string.Empty,
                _ => farthestAssignment?.Value ?? string.Empty
            };
        }

        private static string? ExtractContentValue(CssBox cssBox, FunctionToken contentFunctionToken)
        {
            // Default mode is "text" if no argument provided
            var mode = "text";

            if (contentFunctionToken.ArgumentTokens.Any())
            {
                var argToken = contentFunctionToken.ArgumentTokens.FirstOrDefault();
                if (argToken is KeywordToken keywordToken)
                {
                    mode = keywordToken.Data.ToLowerInvariant();
                }
            }

            return mode switch
            {
                "text" => ExtractText(cssBox),
                "before" => ExtractPseudoElementContent(cssBox, isBeforePseudo: true),
                "after" => ExtractPseudoElementContent(cssBox, isBeforePseudo: false),
                "first-letter" => ExtractFirstLetter(cssBox),
                _ => null
            };
        }

        internal static string? ExtractText(CssBox cssBox)
        {
            // Get the text content of the element (normalized whitespace)
            // If this is a pseudo-element, get the parent's text
            var sourceBox = cssBox.IsPseudoElement && cssBox.ParentBox != null
                ? cssBox.ParentBox
                : cssBox;

            return GetTextContent(sourceBox, excludePseudoElements: true);
        }

        private static string? ExtractPseudoElementContent(CssBox cssBox, bool isBeforePseudo)
        {
            // Find the pseudo-element box
            // If we're in a pseudo-element, look at the parent element's pseudo-elements
            var sourceBox = cssBox.IsPseudoElement && cssBox.ParentBox != null
                ? cssBox.ParentBox
                : cssBox;

            var pseudoElement = sourceBox.Boxes.FirstOrDefault(b =>
                isBeforePseudo ? b.IsBeforePseudoElement : b.IsAfterPseudoElement);

            if (pseudoElement == null)
            {
                return null;
            }

            // Extract the content value by evaluating the content property
            // This is similar to ApplyContent but returns the result instead of setting Text
            if (pseudoElement.Content is Keywords.None or Keywords.Normal)
            {
                return null;
            }

            var tokens = CssValueParser.GetCssTokens(pseudoElement.Content);
            var contentText = new StringBuilder();

            foreach (var token in tokens)
            {
                switch (token)
                {
                    case StringToken stringToken:
                        contentText.Append(stringToken.Data);
                        break;
                    case FunctionToken { Data: FunctionNames.Counter } functionToken:
                        {
                            AppendCounter(contentText, pseudoElement, functionToken);
                            break;
                        }
                    case FunctionToken { Data: "attr" } attrFunctionToken:
                        {
                            if (attrFunctionToken.ArgumentTokens.Any())
                            {
                                var attrNameToken = attrFunctionToken.ArgumentTokens.FirstOrDefault();
                                if (attrNameToken is KeywordToken keywordToken)
                                {
                                    var attrName = keywordToken.Data;
                                    var targetBox = pseudoElement.IsPseudoElement && pseudoElement.ParentBox != null
                                        ? pseudoElement.ParentBox
                                        : pseudoElement;
                                    var attrValue = targetBox.GetAttribute(attrName, "");
                                    if (!string.IsNullOrEmpty(attrValue))
                                    {
                                        contentText.Append(attrValue);
                                    }
                                }
                            }
                            break;
                        }
                        // Note: We don't recursively process content() here to avoid infinite loops
                        // If a pseudo-element's content contains content(), it would have already
                        // been evaluated when we extracted it
                }
            }

            return contentText.Length > 0 ? contentText.ToString() : null;
        }

        private static string? ExtractFirstLetter(CssBox cssBox)
        {
            var text = ExtractText(cssBox);
            return string.IsNullOrEmpty(text) ? null : text.Substring(0, 1);
        }

        private static string? GetTextContent(CssBox box, bool excludePseudoElements)
        {
            if (!string.IsNullOrEmpty(box.Text))
            {
                return box.Text;
            }

            var textBuilder = new StringBuilder();
            foreach (var childBox in box.Boxes)
            {
                // Skip pseudo-elements when extracting regular text content
                if (excludePseudoElements && childBox.IsPseudoElement)
                {
                    continue;
                }

                var childText = GetTextContent(childBox, excludePseudoElements);
                if (!string.IsNullOrEmpty(childText))
                {
                    textBuilder.Append(childText);
                }
            }

            return textBuilder.Length > 0 ? textBuilder.ToString() : null;
        }
    }
}
