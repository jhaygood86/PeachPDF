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

            var quoteDepth = GetQuoteDepthAtStart(cssBox);
            cssBox.Text = ResolveContentTokens(cssBox, tokens, ref quoteDepth);
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
            var quoteDepth = GetQuoteDepthAtStart(cssBox);

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

                textBuffer.Append(ResolveContentTokens(cssBox, [token], ref quoteDepth));
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
        internal static string ResolveContentTokens(CssBox cssBox, List<Token> tokens, ref int quoteDepth)
        {
            var contentText = new StringBuilder();
            IReadOnlyList<(string Open, string Close)>? quotePairs = null;

            foreach (var token in tokens)
            {
                switch (token)
                {
                    case StringToken stringToken:
                        contentText.Append(stringToken.Data);
                        break;
                    case KeywordToken { Data: Keywords.OpenQuote or Keywords.NoOpenQuote or Keywords.CloseQuote or Keywords.NoCloseQuote } quoteToken:
                        {
                            quotePairs ??= GetQuotePairs(cssBox);
                            AppendQuote(contentText, quotePairs, quoteToken.Data, ref quoteDepth);
                            break;
                        }
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
            var quoteDepth = GetQuoteDepthAtStart(cssBox);
            return ResolveContentTokens(cssBox, tokens, ref quoteDepth);
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

        /// <summary>UA default quote pair (guillemets), matching <c>QuotesProperty</c>'s own fallback.</summary>
        private static readonly (string Open, string Close)[] DefaultQuotePairs = [("«", "»")];

        /// <summary>
        /// CSS 2.1 §12.2/§12.3.1 quote nesting depth just before <paramref name="box"/>'s own content
        /// list starts resolving - the net count of open-quote/no-open-quote minus close-quote/
        /// no-close-quote among everything that resolves earlier in document order. The spec says this
        /// depth is "independent of the nesting of the source document or the formatting structure" -
        /// i.e. one running value across the whole document - but <see cref="ApplyContent"/> can run more
        /// than once per box (the pagination convergence loop in <c>HtmlContainerInt</c> re-resolves
        /// target-counter(page) content) and <c>DomParser.CorrectTextBoxes</c> walks a box's children in
        /// reverse, so a single mutable counter would corrupt itself across re-runs/out-of-order visits.
        /// Recomputing this as a pure function of the box tree - mirroring
        /// <see cref="CssCounterEngine.GetCounter"/>'s own ancestor + preceding-sibling walk shape,
        /// memoized the same amortized way via <see cref="CssBox.QuoteDepthAtStart"/> - is safe against
        /// both, since it depends only on <see cref="CssBox.Content"/>'s raw text and the tree shape,
        /// neither of which changes across re-runs. Only the single previous sibling (or the parent, if
        /// there is none) is walked, not every earlier sibling, since <see cref="GetRawQuoteAggregate"/>
        /// already folds a sibling's own preceding-siblings' contributions into it when IT was resolved.
        /// <para>
        /// Known scope limitation: a box's OWN content list is only ever counted toward its later
        /// siblings' depth here, never toward its own children's - there is no "walk this box's own
        /// content, then descend" step for the box whose depth is actually being asked for (only for
        /// earlier siblings/ancestors, via <see cref="GetRawQuoteAggregate"/>). This is a non-issue for
        /// every real caller: <c>::before</c>/<c>::after</c>/<c>::marker</c> pseudo-elements and
        /// <c>bookmark-label</c> (the only places a literal quote-mode <c>content</c> value is ever
        /// declared in practice) never have non-pseudo children of their own to under-count. A plain
        /// element given both a literal <c>content</c> and real DOM children is already spec-invalid
        /// (generated content only applies to <c>::before</c>/<c>::after</c>) and separately never reaches
        /// this path in the first place - <c>DomParser.CorrectTextBoxes</c> treats any box whose
        /// <see cref="CssBox.Text"/> resolves non-null as a leaf and never calls <c>ApplyContent</c> on
        /// its children at all, so they never ask for a depth to under-count.
        /// </para>
        /// </summary>
        private static int GetQuoteDepthAtStart(CssBox box)
        {
            if (box.QuoteDepthAtStart is { } cached) return cached;

            var previousSibling = GetPreviousQuoteSibling(box);
            var depth = previousSibling is not null
                ? ApplyQuoteSubtree(previousSibling, GetQuoteDepthAtStart(previousSibling))
                : box.ParentBox is not null ? GetQuoteDepthAtStart(box.ParentBox) : 0;

            box.QuoteDepthAtStart = depth;
            return depth;
        }

        /// <summary>The nearest preceding sibling that isn't display:none or the synthetic
        /// table-grid-decoration box (issue #721 - <see cref="CssBox.IsTableGridDecorationBox"/>),
        /// mirroring <see cref="CssCounterEngine"/>'s own equivalent skip.</summary>
        private static CssBox? GetPreviousQuoteSibling(CssBox box)
        {
            var parent = box.ParentBox;
            if (parent is null) return null;

            for (var i = parent.Boxes.IndexOf(box) - 1; i >= 0; i--)
            {
                var sibling = parent.Boxes[i];
                if (sibling.DerivedStyle.ActualDisplay != Keywords.None && !sibling.IsTableGridDecorationBox)
                {
                    return sibling;
                }
            }

            return null;
        }

        /// <summary>
        /// The true (CSS 2.1 §12.2-clamped) quote depth after traversing <paramref name="box"/>'s own
        /// content list and its whole descendant subtree, given the real ambient
        /// <paramref name="startDepth"/> depth was at beforehand. <see cref="GetRawQuoteAggregate"/>
        /// caches this subtree's net change and minimum reached, both computed <em>unclamped</em> (as if
        /// starting from a hypothetical local zero, ignoring the "a close-quote that would go negative is
        /// ignored" rule) and so independent of any real ambient depth. Whenever
        /// <paramref name="startDepth"/> plus that minimum still can't go negative, the clamp provably
        /// never fires anywhere in the subtree, so the true result is just the ambient depth plus the
        /// cached raw delta - true for any <paramref name="startDepth"/>, which is what makes the
        /// aggregate safe to compute once and cache permanently. Only when it could underflow (an
        /// author's own close-quote/no-close-quote sequence genuinely outrunning what's open at that
        /// particular ambient depth - a malformed-content edge case, not the common "many quotes" case
        /// this caching targets) does this fall back to <see cref="ApplyQuoteSubtreeExact"/>'s exact,
        /// uncached per-token walk for just that one call.
        /// </summary>
        private static int ApplyQuoteSubtree(CssBox box, int startDepth)
        {
            var (rawDelta, localMin) = GetRawQuoteAggregate(box);
            return startDepth + localMin >= 0
                ? startDepth + rawDelta
                : ApplyQuoteSubtreeExact(box, startDepth);
        }

        /// <summary>
        /// Computes and memoizes (<see cref="CssBox.QuoteSubtreeRawDelta"/>/
        /// <see cref="CssBox.QuoteSubtreeLocalMin"/>) <paramref name="box"/>'s own content list plus its
        /// whole descendant subtree's net quote-depth change and minimum running value, both unclamped
        /// and starting from a hypothetical local zero - see <see cref="ApplyQuoteSubtree"/> for why that
        /// pair is enough to decide, for any real ambient depth, whether the CSS 2.1 §12.2 clamp could
        /// ever fire. A display:none/table-grid-decoration box (and everything under it, since neither
        /// generates any rendered content) contributes nothing, matching <see cref="CssCounterEngine"/>'s
        /// own skip of both. Each box's aggregate is computed at most once ever - resolving it folds in
        /// every already-resolved child's own cached aggregate rather than re-walking, and a later query
        /// for a different content-bearing box that shares an ancestor/subtree hits the same cache - so a
        /// document with many quote-bearing elements costs roughly one pass over the tree in total, not
        /// one pass per element.
        /// </summary>
        private static (int RawDelta, int LocalMin) GetRawQuoteAggregate(CssBox box)
        {
            if (box.QuoteSubtreeRawDelta is { } cachedDelta && box.QuoteSubtreeLocalMin is { } cachedMin)
            {
                return (cachedDelta, cachedMin);
            }

            var aggregate = (RawDelta: 0, LocalMin: 0);
            if (box.DerivedStyle.ActualDisplay != Keywords.None && !box.IsTableGridDecorationBox)
            {
                if (box.Content is not (Keywords.None or Keywords.Normal))
                {
                    aggregate = CombineQuoteAggregate(aggregate, ComputeContentListQuoteAggregate(CssValueParser.GetCssTokens(box.Content)));
                }

                foreach (var child in box.Boxes)
                {
                    aggregate = CombineQuoteAggregate(aggregate, GetRawQuoteAggregate(child));
                }
            }

            box.QuoteSubtreeRawDelta = aggregate.RawDelta;
            box.QuoteSubtreeLocalMin = aggregate.LocalMin;
            return aggregate;
        }

        /// <summary>Sequential composition of two unclamped quote-depth aggregates (<paramref name="a"/>
        /// then <paramref name="b"/>, in document order): the combined net change is additive, and the
        /// combined minimum is whichever is lower - <paramref name="a"/>'s own minimum, or
        /// <paramref name="b"/>'s minimum offset by everything <paramref name="a"/> already added
        /// (the standard "running-balance-with-a-floor" monoid, e.g. used for bracket-matching).</summary>
        private static (int RawDelta, int LocalMin) CombineQuoteAggregate((int RawDelta, int LocalMin) a, (int RawDelta, int LocalMin) b) =>
            (a.RawDelta + b.RawDelta, Math.Min(a.LocalMin, a.RawDelta + b.LocalMin));

        /// <summary>The unclamped (<see cref="ApplyQuoteSubtree"/>) quote-depth aggregate of one content
        /// token list alone, starting from a hypothetical local zero.</summary>
        private static (int RawDelta, int LocalMin) ComputeContentListQuoteAggregate(List<Token> tokens)
        {
            var delta = 0;
            var min = 0;
            foreach (var token in tokens)
            {
                if (token is not KeywordToken keywordToken) continue;

                switch (keywordToken.Data)
                {
                    case Keywords.OpenQuote:
                    case Keywords.NoOpenQuote:
                        delta += 1;
                        break;
                    case Keywords.CloseQuote:
                    case Keywords.NoCloseQuote:
                        delta -= 1;
                        break;
                }
                min = Math.Min(min, delta);
            }
            return (delta, min);
        }

        /// <summary>
        /// The exact, uncached CSS 2.1 §12.2-clamped walk of <paramref name="box"/>'s own content list
        /// and its whole descendant subtree given the real ambient <paramref name="startDepth"/> - reached
        /// only via <see cref="ApplyQuoteSubtree"/>'s rare fallback path, since (unlike
        /// <see cref="GetRawQuoteAggregate"/>'s aggregate) the clamped result genuinely depends on the
        /// caller's ambient depth and so can't be memoized as a single reusable number.
        /// </summary>
        private static int ApplyQuoteSubtreeExact(CssBox box, int startDepth)
        {
            if (box.DerivedStyle.ActualDisplay == Keywords.None || box.IsTableGridDecorationBox) return startDepth;

            var depth = startDepth;
            if (box.Content is not (Keywords.None or Keywords.Normal))
            {
                ApplyContentListQuoteDepth(CssValueParser.GetCssTokens(box.Content), ref depth);
            }

            foreach (var child in box.Boxes)
            {
                depth = ApplyQuoteSubtreeExact(child, depth);
            }

            return depth;
        }

        /// <summary>Depth-only half of quote-token handling (no text output), under the real CSS 2.1
        /// §12.2 clamp - the counterpart to <see cref="AppendQuote"/> used by
        /// <see cref="ApplyQuoteSubtreeExact"/>'s exact fallback walk.</summary>
        private static void ApplyContentListQuoteDepth(List<Token> tokens, ref int depth)
        {
            foreach (var token in tokens)
            {
                if (token is not KeywordToken keywordToken) continue;

                switch (keywordToken.Data)
                {
                    case Keywords.OpenQuote:
                    case Keywords.NoOpenQuote:
                        depth += 1;
                        break;
                    case Keywords.CloseQuote:
                    case Keywords.NoCloseQuote:
                        if (depth > 0) depth -= 1;
                        break;
                }
            }
        }

        /// <summary>
        /// Resolves one <c>open-quote</c>/<c>close-quote</c>/<c>no-open-quote</c>/<c>no-close-quote</c>
        /// token (CSS 2.1 §12.2) against <paramref name="quotePairs"/> (the caller's <c>quotes</c> value,
        /// resolved once via <see cref="GetQuotePairs"/> and reused across a whole content list rather
        /// than re-parsed per quote token), threading the running <paramref name="depth"/> and appending
        /// the selected quote glyph for the two quote-mark keywords. A <c>close-quote</c>/
        /// <c>no-close-quote</c> that would take the depth negative is ignored per spec - the depth stays
        /// at 0 and nothing is appended, but the rest of the content list is still processed.
        /// </summary>
        private static void AppendQuote(StringBuilder sb, IReadOnlyList<(string Open, string Close)> quotePairs, string quoteKeyword, ref int depth)
        {
            switch (quoteKeyword)
            {
                case Keywords.OpenQuote:
                    sb.Append(SelectQuote(quotePairs, depth, isOpen: true));
                    depth += 1;
                    break;
                case Keywords.NoOpenQuote:
                    depth += 1;
                    break;
                case Keywords.CloseQuote:
                    if (depth > 0)
                    {
                        depth -= 1;
                        sb.Append(SelectQuote(quotePairs, depth, isOpen: false));
                    }
                    break;
                case Keywords.NoCloseQuote:
                    if (depth > 0) depth -= 1;
                    break;
            }
        }

        /// <summary>Picks the pair for <paramref name="depth"/> (CSS 2.1 §12.3.1: depth beyond the
        /// number of declared pairs repeats the last pair), or the empty string if no pairs are
        /// configured (<c>quotes: none</c>).</summary>
        private static string SelectQuote(IReadOnlyList<(string Open, string Close)> pairs, int depth, bool isOpen)
        {
            if (pairs.Count == 0) return string.Empty;
            var index = Math.Min(depth, pairs.Count - 1);
            return isOpen ? pairs[index].Open : pairs[index].Close;
        }

        /// <summary>
        /// Parses <paramref name="cssBox"/>'s effective (already-inherited, cascade-resolved) <c>quotes</c>
        /// value - raw declared CSS text, the same storage model <c>content</c>/<c>counter-reset</c> use,
        /// re-tokenized independently of <c>QuotesProperty</c>'s own CSS-OM converter the same way every
        /// other <c>GeneratedContentArea</c> property already is (e.g. <see cref="ResolveContentTokens"/>
        /// re-tokenizes <c>Content</c> rather than consuming <c>ContentProperty</c>'s parsed form) - into
        /// ordered open/close pairs. Falls back to <see cref="DefaultQuotePairs"/> (matching
        /// <c>QuotesProperty</c>'s own UA-default guillemet pair) for <c>none</c>, or anything that isn't
        /// entirely made up of an even, non-zero number of strings - matching
        /// <c>StringsValueConverter</c>'s own all-or-nothing validation (a single non-string token
        /// invalidates the whole value there too), rather than silently discarding just the offending
        /// tokens.
        /// </summary>
        private static IReadOnlyList<(string Open, string Close)> GetQuotePairs(CssBox cssBox)
        {
            var raw = cssBox.Quotes;
            if (string.IsNullOrWhiteSpace(raw)) return DefaultQuotePairs;

            var tokens = CssValueParser.GetCssTokens(raw);

            if (tokens is [KeywordToken { Data: Keywords.None }])
            {
                return [];
            }

            if (tokens.Count == 0 || tokens.Count % 2 != 0 || tokens.Any(t => t is not StringToken))
            {
                return DefaultQuotePairs;
            }

            var pairs = new (string, string)[tokens.Count / 2];
            for (var i = 0; i < pairs.Length; i++)
            {
                pairs[i] = (((StringToken)tokens[i * 2]).Data, ((StringToken)tokens[i * 2 + 1]).Data);
            }
            return pairs;
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
            var quoteDepth = GetQuoteDepthAtStart(pseudoElement);
            IReadOnlyList<(string Open, string Close)>? quotePairs = null;

            foreach (var token in tokens)
            {
                switch (token)
                {
                    case StringToken stringToken:
                        contentText.Append(stringToken.Data);
                        break;
                    case KeywordToken { Data: Keywords.OpenQuote or Keywords.NoOpenQuote or Keywords.CloseQuote or Keywords.NoCloseQuote } quoteToken:
                        {
                            quotePairs ??= GetQuotePairs(pseudoElement);
                            AppendQuote(contentText, quotePairs, quoteToken.Data, ref quoteDepth);
                            break;
                        }
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
