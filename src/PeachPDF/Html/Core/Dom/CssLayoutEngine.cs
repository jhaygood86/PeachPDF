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

#nullable enable

using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Fragmentation;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Text.Bidi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// Helps on CSS Layout.
    /// </summary>
    internal static class CssLayoutEngine
    {
        /// <summary>
        /// Measure image box size by the width\height set on the box and the actual rendered image size.<br/>
        /// If no image exists for the box error icon will be set.
        /// </summary>
        /// <param name="imageWord">the image word to measure</param>
        public static void MeasureImageSize(CssRectImage imageWord)
        {
            ArgumentNullException.ThrowIfNull(imageWord);
            MeasureIntrinsicSize(imageWord, imageWord.Image?.Width, imageWord.Image?.Height);
        }

        /// <summary>
        /// Measure a replaced-element word's box size by the width/height set on the box and a given
        /// intrinsic size (null components mean "unknown", e.g. an image that failed to load, or an
        /// SVG with no viewBox/width/height). Shared by <see cref="MeasureImageSize"/> (backed by an
        /// <see cref="RImage"/>'s pixel size) and <c>CssBoxSvg</c>/<c>CssBoxImage</c>'s SVG-source
        /// sizing (backed by an <c>SvgDocument</c>'s viewBox/width/height).
        /// </summary>
        public static void MeasureIntrinsicSize(CssRect word, double? intrinsicWidth, double? intrinsicHeight)
        {
            ArgumentNullException.ThrowIfNull(word);
            ArgumentNullException.ThrowIfNull(word.OwnerBox);

            // Intrinsic sizes arrive in CSS-pixel space (a raster's device pixels / an SVG's user
            // units, both 1px = 1/96in per spec), while word.Width/Height are layout units (points).
            // Convert once here; every px-unit branch below applies the same shared factor.
            intrinsicWidth *= Length.PointsPerPx;
            intrinsicHeight *= Length.PointsPerPx;

            var width = new CssLength(word.OwnerBox.Width);
            var height = new CssLength(word.OwnerBox.Height);

            var hasImageTagWidth = TryResolveAbsolute(width, out var widthUnits);
            var hasImageTagHeight = TryResolveAbsolute(height, out var heightUnits);
            var scaleImageHeight = false;

            // The element's natural (intrinsic) width/height ratio, if it has one, combined with any CSS
            // `aspect-ratio` into the effective ratio used to size the missing axis (CSS Box Sizing 4 §5): a
            // bare `<ratio>` overrides the natural ratio, `auto <ratio>` prefers the natural ratio and falls
            // back to the specified one, and `auto`/no `aspect-ratio` uses the natural ratio alone. Null when
            // there is neither a natural ratio nor a usable specified one (the pre-existing behavior).
            double? intrinsicRatio = intrinsicWidth is > 0 && intrinsicHeight is > 0
                ? intrinsicWidth.Value / intrinsicHeight.Value
                : null;
            var effectiveRatio = ResolveReplacedRatio(word.OwnerBox.AspectRatio, intrinsicRatio);

            if (hasImageTagWidth)
            {
                word.Width = widthUnits;
            }
            else if (width is { Number: > 0, IsPercentage: true })
            {
                word.Width = width.Number * word.OwnerBox.ContainingBlock.Size.Width;
                scaleImageHeight = true;
            }
            else if (intrinsicWidth is > 0)
            {
                word.Width = intrinsicWidth.Value;
            }
            else
            {
                // 20 is the legacy broken-image placeholder size, in CSS pixels like the px branches.
                word.Width = hasImageTagHeight ? heightUnits / 1.14f : 20 * Length.PointsPerPx;
            }

            var maxWidth = new CssLength(word.OwnerBox.MaxWidth);
            if (maxWidth.Number > 0)
            {
                double maxWidthVal = -1;
                if (TryResolveAbsolute(maxWidth, out var maxWidthUnits))
                {
                    maxWidthVal = maxWidthUnits;
                }
                else if (maxWidth.IsPercentage)
                {
                    maxWidthVal = maxWidth.Number * word.OwnerBox.ContainingBlock.Size.Width;
                }

                if (maxWidthVal > -1 && word.Width > maxWidthVal)
                {
                    word.Width = maxWidthVal;
                    scaleImageHeight = !hasImageTagHeight;
                }
            }

            var minWidth = new CssLength(word.OwnerBox.MinWidth);
            if (minWidth.Number > 0)
            {
                double minWidthVal = -1;
                if (TryResolveAbsolute(minWidth, out var minWidthUnits))
                {
                    minWidthVal = minWidthUnits;
                }
                else if (minWidth.IsPercentage)
                {
                    minWidthVal = minWidth.Number * word.OwnerBox.ContainingBlock.Size.Width;
                }

                if (minWidthVal > -1 && word.Width < minWidthVal)
                {
                    word.Width = minWidthVal;
                    scaleImageHeight = !hasImageTagHeight;
                }
            }

            if (hasImageTagHeight)
            {
                word.Height = heightUnits;
            }
            else if (intrinsicHeight is > 0)
            {
                word.Height = intrinsicHeight.Value;
            }
            else
            {
                // 22.8 is the legacy placeholder height, in CSS pixels like the px branches.
                word.Height = word.Width > 0 ? word.Width * 1.14f : 22.8f * Length.PointsPerPx;
            }

            if (effectiveRatio is > 0)
            {
                // If only the width was set (or width was resolved from a percentage / clamped), ratio the
                // height from it via the effective width/height ratio.
                if ((hasImageTagWidth && !hasImageTagHeight) || scaleImageHeight)
                {
                    word.Height = word.Width / effectiveRatio.Value;
                }
                // If only the height was set, ratio the width from it.
                else if (hasImageTagHeight && !hasImageTagWidth)
                {
                    word.Width = word.Height * effectiveRatio.Value;
                }
            }

            // Apply max-height / min-height constraints, rescaling width by the image's aspect
            // ratio (mirroring the max-width/min-width handling above).
            var maxHeight = new CssLength(word.OwnerBox.MaxHeight);
            if (maxHeight.Number > 0)
            {
                double maxHeightVal = -1;
                if (TryResolveAbsolute(maxHeight, out var maxHeightUnits))
                {
                    maxHeightVal = maxHeightUnits;
                }
                else if (maxHeight.IsPercentage && word.OwnerBox.ContainingBlock.IsHeightCalculated)
                {
                    maxHeightVal = maxHeight.Number * word.OwnerBox.ContainingBlock.Size.Height;
                }

                if (maxHeightVal > -1 && word.Height > maxHeightVal)
                {
                    if (effectiveRatio is > 0 && word.Height > 0)
                    {
                        word.Width *= maxHeightVal / word.Height;
                    }
                    word.Height = maxHeightVal;
                }
            }

            var minHeight = new CssLength(word.OwnerBox.MinHeight);
            if (minHeight.Number > 0)
            {
                double minHeightVal = -1;
                if (TryResolveAbsolute(minHeight, out var minHeightUnits))
                {
                    minHeightVal = minHeightUnits;
                }
                else if (minHeight.IsPercentage && word.OwnerBox.ContainingBlock.IsHeightCalculated)
                {
                    minHeightVal = minHeight.Number * word.OwnerBox.ContainingBlock.Size.Height;
                }

                if (minHeightVal > -1 && word.Height < minHeightVal)
                {
                    if (effectiveRatio is > 0 && word.Height > 0)
                    {
                        word.Width *= minHeightVal / word.Height;
                    }
                    word.Height = minHeightVal;
                }
            }

            word.Height += word.OwnerBox.ActualBorderBottomWidth + word.OwnerBox.ActualBorderTopWidth + word.OwnerBox.ActualPaddingTop + word.OwnerBox.ActualPaddingBottom;
        }

        /// <summary>
        /// Resolves the effective width/height ratio for a replaced element from its CSS <c>aspect-ratio</c>
        /// and its natural (intrinsic) ratio, per CSS Box Sizing 4 §5. A bare <c>&lt;ratio&gt;</c> overrides the
        /// natural ratio; <c>auto &lt;ratio&gt;</c> prefers the natural ratio and falls back to the specified one
        /// when the element has none; <c>auto</c>, an absent property, or a zero-term ratio use the natural
        /// ratio alone. Returns null when neither a natural nor a usable specified ratio exists.
        /// </summary>
        private static double? ResolveReplacedRatio(string aspectRatio, double? intrinsicRatio)
        {
            if (string.IsNullOrEmpty(aspectRatio) || aspectRatio == Keywords.Auto)
                return intrinsicRatio;

            if (!AspectRatioGrammar.TryParse(CssValueParser.GetCssTokens(aspectRatio), out var ratio, out var hasAuto)
                || ratio is not (> 0))
                return intrinsicRatio; // bare `auto`, or a zero-term ratio: natural ratio only

            // `auto <ratio>` prefers the natural ratio; a bare `<ratio>` overrides it.
            return hasAuto ? intrinsicRatio ?? ratio.Value : ratio.Value;
        }

        /// <summary>
        /// Resolves an explicit absolute width/height/min/max on a replaced element to layout units
        /// (points) through the shared CSS-OM <see cref="Length"/> conversion — any absolute unit,
        /// spec-correct px (1px = 0.75pt) included, so replaced-element sizing agrees with the rest
        /// of the engine by construction.
        /// </summary>
        private static bool TryResolveAbsolute(CssLength length, out double layoutUnits)
        {
            layoutUnits = 0;
            if (length.HasError || length.IsPercentage || !(length.Number > 0))
                return false;

            if (!Length.TryParse(length.Length.Trim().ToLowerInvariant(), out var parsed) || !parsed.IsAbsolute)
                return false;

            layoutUnits = parsed.ToPixels(0, 0, 0);
            return true;
        }

        /// <summary>
        /// Creates line boxes for the specified block-box
        /// </summary>
        /// <param name="g"></param>
        /// <param name="blockBox"></param>
        /// <param name="resume">
        /// where an earlier fragmentainer's flow stopped, or null to lay the block's inline content out
        /// from the start.
        /// </param>
        public static async ValueTask<InlineBreakToken?> CreateLineBoxes(RGraphics g, CssBox blockBox, InlineBreakToken? resume = null)
        {
            ArgumentNullException.ThrowIfNull(g);
            ArgumentNullException.ThrowIfNull(blockBox);

            var context = blockBox.HtmlContainer?.CurrentFragmentainer;
            var fragmenting = context is { IsFragmenting: true };

            // A resumed flow continues the same block: its earlier lines live in a fragmentainer that
            // has already been filled, so they are neither cleared nor re-finalized.
            var completedLines = resume?.CompletedLineCount ?? 0;

            if (resume is not null)
            {
                // The record says this block had produced exactly `completedLines` lines when the break was
                // taken. Holding more than that means an attempt that has since been abandoned added them,
                // and re-finalizing a line that already carries its per-line rectangles throws
                // `An item with the same key has already been added` out of
                // CssLineBox.AssignRectanglesToBoxes. Conservative in both directions: a record naming line
                // n was written by the pass that finalized lines 0..n-1, so nothing an earlier
                // fragmentainer emitted can be inside the range this drops.
                //
                // Reachable since the table engine stopped running behind a detached fragmentainer
                // (issue #464): a multi-column container drives fragmentainers of its own and can abandon a
                // fill attempt, so a table inside one is laid out again over cells that still hold the
                // abandoned attempt's lines while the record still names the earlier count.
                blockBox.DiscardLineBoxesFrom(completedLines);
            }
            else
            {
                blockBox.LineBoxes.Clear();

                // A word carries no position of its own until the flow reaches it, and the position it
                // carries instead — document Y 0, or whatever an earlier layout of the same box left on
                // it — describes nothing. Y 0 lies inside the *first* slot's own band, so the first page's
                // fragment claimed every word a stopped flow never got to (issue #433). Saying up front
                // that this layout has placed none of them, and letting being positioned clear it
                // (CssRect.Top's setter), makes what survives the flow exactly what the flow did not
                // reach — the rule §4.1's own discarded line already follows, asked of the whole block.
                //
                // Only on the pass that opens the block: a resumed pass would otherwise take back the
                // words an earlier fragmentainer has already placed and frozen a fragment around.
                blockBox.AwaitPlacement();
            }

            var limitRight = blockBox.ClientRight;

            //Get the start x and y of the blockBox
            var startX = blockBox.ClientLeft;

            // Resumed content starts at the new fragmentainer's own content edge, below any border and padding
            // a `box-decoration-break: clone` box re-opens with there (css-break-3 §6.2). `text-indent`
            // applies to the first formatted line only (CSS Text §5), so it is not re-applied here.
            var startY = resume is not null && context is not null
                ? context.ResumeContentTop + (blockBox.HtmlContainer is { HasCloneDecorations: true }
                    ? DomUtils.ClonedBlockStart(blockBox, stopAt: null)
                    : 0)
                : blockBox.ClientTop;

            // A resumed pass's seed line rebuilds whole the line-in-progress the previous pass discarded
            // (a line box is monolithic, css-break-3 §4.1) - carrying its FollowsForcedBreak bit forward
            // is what lets `text-indent: each-line` (CSS Text 3 §3) still recognize a resumed line that
            // follows a forced break in the source, not just one born mid-fragmentainer.
            var seedLine = new CssLineBox(blockBox) { FollowsForcedBreak = resume?.FollowsForcedBreak ?? false };

            // text-indent's line-start-side placement (CSS Text 3 §3) is physical-left for LTR, so it is
            // added to the flow's starting X here; for RTL the line-start side is physical-right, which
            // FlowBox instead reserves by narrowing the wrap boundary (see its own `isRtl` handling) -
            // adding it here too would double-reserve the space and can make ApplyRightAlignment's
            // indent-aware flush target unreachable by a mere shift.
            var isRtl = blockBox.Direction.Value == DirectionMode.Rtl;

            CssLineBoxCoordinates coordinates = new()
            {
                Line = seedLine,
                CurrentX = startX + (!isRtl
                    ? GetLineTextIndent(blockBox, isFirstLine: resume is null, seedLine.FollowsForcedBreak)
                    : 0),
                CurrentY = startY,
                MaxRight = startX,
                MaxBottom = startY,
                Fragmentainer = fragmenting ? context : null,
                ResumeOrdinal = resume?.ResumeWordIndex ?? 0,
                SuppressLeadingWrap = resume is not null,
                // hyphenate-limit-lines (CSS Text 4 §6.3.5): a run of consecutive hyphenated lines that
                // straddles this boundary keeps counting rather than restarting at 0 - see
                // InlineBreakToken.ConsecutiveHyphenatedLines.
                ConsecutiveHyphenatedLines = resume?.ConsecutiveHyphenatedLines ?? 0
            };

            //Flow words and boxes
            await FlowBox(g, blockBox, blockBox, limitRight, 0, startX, coordinates);

            // A resumed flow's seed line is abandoned when its first word forces a wrap - a <br>, or
            // content that no longer fits. Leaving it behind would put an empty line box in the middle
            // of the block's line list, which every count-based rule downstream reads: orphans/widows
            // counts lines either side of a boundary, and ::first-line matches by index.
            if (resume is not null && seedLine.Words.Count == 0)
            {
                blockBox.LineBoxes.Remove(seedLine);
            }

            if (coordinates.Break is { } stopped)
            {
                // The line being built when the break was taken belongs to the next fragmentainer, so
                // it is discarded here and rebuilt whole by the resumed pass - a line box never
                // straddles a fragmentainer (css-break-3 §4.1). Everything still in the list is
                // complete, which is what the resumed pass must not re-finalize.
                blockBox.LineBoxes.Remove(coordinates.Line);

                // hyphenate-limit-last (CSS Text 4 §6.3.5): the line CreateLineBoxes just kept - now the
                // last one before this break - may not end in a hyphen the property forbids. Unlike
                // widows this is knowable the moment the break itself is discovered, synchronously: the
                // "last line" in question is the one this same pass just finished, not one that has yet
                // to be laid out.
                stopped = EnforceHyphenateLimitLastBeforeBreak(blockBox, coordinates.Line, context, stopped, completedLines);

                // Discarding the line leaves its words where they were being built, which is inside the
                // fragmentainer being left - and that fragmentainer's fragments are frozen at the end of this
                // pass, before the resumed pass re-places them. §4.1 has already decided they belong to the
                // next fragmentainer, so mark them as such; being positioned again clears it.
                UndoAbandonedHyphenationSplits(coordinates.Line);

                foreach (var word in coordinates.Line.Words)
                {
                    word.AwaitsTheNextFragmentainer = true;
                }

                FinalizeLineBoxes(blockBox, completedLines, blockFinished: false);

                return stopped with
                {
                    CompletedLineCount = blockBox.LineBoxes.Count,
                    LinesKeptHere = blockBox.LineBoxes.Count - completedLines
                };
            }

            // if width is not restricted we need to lower it to the actual width
            if (blockBox.ActualRight >= 90999)
            {
                blockBox.ActualRight = coordinates.MaxRight + blockBox.ActualPaddingRight + blockBox.ActualBorderRightWidth;
            }

            FinalizeLineBoxes(blockBox, completedLines);

            blockBox.ActualBottom = coordinates.MaxBottom + blockBox.ActualPaddingBottom + blockBox.ActualBorderBottomWidth;

            // handle limiting block height when overflow is hidden
            if (blockBox.Height != Keywords.Auto && blockBox.Overflow.Value == Overflow.Hidden && blockBox.ActualBottom - blockBox.Location.Y > blockBox.ActualHeight)
            {
                blockBox.ActualBottom = blockBox.Location.Y + blockBox.ActualHeight + blockBox.ActualPaddingBottom + blockBox.ActualPaddingTop;
            }

            return null;
        }

        /// <summary>
        /// Aligns and bubbles the line boxes this pass produced, starting at
        /// <paramref name="firstLine"/>. Lines below that index were emitted into an earlier
        /// fragmentainer; re-running alignment over them would re-align and re-bubble geometry that has
        /// already been consumed, against a later pass's <c>MaxRight</c>.
        /// </summary>
        /// <param name="blockBox">the block whose lines are being finalized</param>
        /// <param name="firstLine">the first line this pass produced; everything below it is already done</param>
        /// <param name="blockFinished">
        /// whether the flow reached the end of the block's content. False when it stopped at a
        /// fragmentation break, and <c>text-align: justify</c> is the one caller that has to know: a line
        /// that ends at a break is <b>not</b> the block's last line — the block continues in the next
        /// fragmentainer — so
        /// <see href="https://www.w3.org/TR/css-text-3/#text-align-property">CSS Text §7.3</see>'s
        /// "except the last line" exemption does not apply to it. Reading it off the list alone justified
        /// nothing at a page boundary, because the line the pass stopped on is the last one the list holds.
        /// </param>
        private static void FinalizeLineBoxes(CssBox blockBox, int firstLine, bool blockFinished = true)
        {
            for (var i = firstLine; i < blockBox.LineBoxes.Count; i++)
            {
                var lineBox = blockBox.LineBoxes[i];
                ApplyLeaderFill(lineBox);
                ApplyHorizontalAlignment(lineBox, blockFinished);
                ApplyBidiReordering(lineBox);
                BubbleRectangles(blockBox, lineBox);
                ApplyVerticalAlignment(lineBox);
                lineBox.AssignRectanglesToBoxes();
            }
        }

        /// <summary>
        /// Distributes the line's remaining width across every <c>leader()</c> item on it (css-content-3
        /// §6: shared equally when there's more than one), then shifts every later word by however much
        /// space each leader claimed - the classic "Chapter One .......... 12" table-of-contents idiom.
        /// Must run first, before <see cref="ApplyHorizontalAlignment"/>/<see cref="BubbleRectangles"/> -
        /// alignment, bidi reordering and rectangle bubbling all read final word positions, and
        /// <see cref="CssLineBox.Words"/> already aggregates every word on the line regardless of which
        /// <see cref="CssBox"/> owns it (the same aggregate <see cref="ApplyJustifyAlignment"/>/
        /// <see cref="ApplyCenterAlignment"/> already read), so this sees a <c>leader()</c> from one
        /// element and later words from a completely different sibling element with no extra plumbing -
        /// e.g. <c>&lt;a&gt;Chapter Two&lt;/a&gt;&lt;span class="fill"&gt;&lt;/span&gt;&lt;span
        /// class="page"&gt;&lt;/span&gt;</c> with the fill and page-number content declared on the two
        /// spans separately.
        /// </summary>
        /// <remarks>
        /// Shifts each word's <c>Left</c> in place rather than recomputing every word's absolute position
        /// from scratch (mirroring <see cref="ApplyJustifyAlignment"/>'s own approach, not
        /// <see cref="ApplyCenterAlignment"/>'s single-constant-<c>diff</c> one, since this shift is
        /// progressive - different amounts before/after each leader) - this preserves every other word's
        /// spacing exactly as <c>FlowBox</c> computed it (word-spacing, RTL split fragments, small-caps
        /// runs, <c>::first-line</c> overrides). <see cref="CssLineBox.Rectangles"/> needs no separate fix-
        /// up: it is only ever populated afterwards, by <see cref="BubbleRectangles"/>, which derives each
        /// box's rectangle fresh from its (now-shifted) words' own <c>Left</c>/<c>Right</c> extent.
        /// </remarks>
        private static void ApplyLeaderFill(CssLineBox lineBox)
        {
            var leaderCount = 0;
            foreach (var w in lineBox.Words)
            {
                if (w is CssRectLeader) leaderCount++;
            }

            if (leaderCount == 0) return; // fast path - leaders are rare on any given line

            var indent = GetLineTextIndent(lineBox.OwnerBox, lineBox.Equals(lineBox.OwnerBox.LineBoxes[0]), lineBox.FollowsForcedBreak);
            var availWidth = lineBox.OwnerBox.ClientRectangle.Width - indent;

            var nonLeaderWidth = 0d;
            foreach (var w in lineBox.Words)
            {
                if (w is not CssRectLeader) nonLeaderWidth += w.FullWidth;
            }

            var perLeaderWidth = Math.Max(0d, (availWidth - nonLeaderWidth) / leaderCount);

            var shift = 0d;
            foreach (var word in lineBox.Words)
            {
                if (word is CssRectLeader leader)
                {
                    leader.Left += shift;
                    leader.Width = perLeaderWidth;
                    shift += perLeaderWidth;
                }
                else if (shift != 0d)
                {
                    word.Left += shift;
                }
            }
        }

        /// <summary>
        /// Applies special vertical alignment for table-cells, and returns how far it moved the cell's
        /// content.
        /// </summary>
        /// <remarks>
        /// The distance is returned because this <b>offsets a subtree rather than assigning a
        /// position</b>, so it is neither idempotent nor self-reversing: a caller that has to take the
        /// alignment back — the table row loop, retracting a placement it decided against — can only do so
        /// by knowing what was applied. Measured before it was returned: a <c>rowspan</c> cell reached
        /// both as a <see cref="CssSpacingBox"/>'s <c>ExtendedBox</c> and as itself was aligned twice, and
        /// under the <c>vertical-align: middle</c> a <c>&lt;td&gt;</c> uses by default the second pass
        /// added a further quarter of the leftover room (24.75pt of 99pt on the fixture that found it).
        /// </remarks>
        /// <param name="g">the graphics context layout is running against</param>
        /// <param name="cell">the table cell whose content is being aligned in its box</param>
        /// <returns>the distance every child of <paramref name="cell"/> was offset by, zero where none was</returns>
        public static double ApplyCellVerticalAlignment(RGraphics g, CssBox cell)
        {
            ArgumentNullException.ThrowIfNull(g);
            ArgumentNullException.ThrowIfNull(cell);

            // CSS 2.1 §17.5.3: a table cell's vertical-align only recognizes this keyword subset - a
            // length/percentage (issue #603's own grammar addition) has no meaning here and falls to the
            // same no-op default as sub/super/text-top/text-bottom/PeachBaselineMiddle already do.
            var cellKeyword = cell.VerticalAlign.Value.Keyword;

            if (cellKeyword is VerticalAlignment.Top or VerticalAlignment.Baseline)
                return 0d;

            var cellBottom = cell.ClientBottom;

            // Measure over the cell's children, not the cell itself: GetMaximumBottom's own-height
            // fallback exists for a childless box with an explicit height, but the cell always has an
            // explicit ActualBottom by this point (the row just stretched it), so passing the cell as
            // startBox would clamp `bottom` to `cellBottom` and always compute a zero offset whenever the
            // cell also carries a CSS `height` (issue found via `height` + `vertical-align: middle`).
            var bottom = cell.ClientTop;
            foreach (var b in cell.Boxes)
            {
                bottom = CssBox.GetMaximumBottom(b, bottom);
            }

            var dist = cellKeyword switch
            {
                VerticalAlignment.Bottom => cellBottom - bottom,
                VerticalAlignment.Middle => (cellBottom - bottom) / 2,
                _ => 0d
            };

            foreach (var b in cell.Boxes)
            {
                b.OffsetTop(dist);
            }

            return dist;
        }

        public static void FloatBox(CssBox box)
        {
            if (box is { Float.Value: Floating.None, Clear.Value: ClearMode.None })
            {
                return;
            }

            var containingBox = box.ContainingBlock!;

            var limitRight = containingBox.ClientRight;

            //Get the start x and y of the blockBox
            var startX = containingBox.ClientLeft;
            var startY = box.Location.Y;

            var currentBoxIdx = containingBox.Boxes.IndexOf(box);

            switch (box.Float.Value)
            {
                case Floating.Left:
                    FloatBoxLeft(box, startX, startY, limitRight);
                    break;
                case Floating.Right:
                    FloatBoxRight(box, startX, startY, limitRight);
                    break;
            }

            if (box.Clear.Value is not ClearMode.None)
            {
                ClearBox(box, currentBoxIdx, containingBox);
            }
        }

        public static double GetActualMarginLeft(CssBox box, double? boxWidth = null)
        {
            var marginLeft = box.MarginLeft.Value;
            if (marginLeft.IsValue)
            {
                return CssValueParser.ParseLength(marginLeft.Value!.Value, box.ContainingBlock.Size.Width, box);
            }

            if (box.MarginRight.Value.IsValue) return 0;

            if (box.DerivedStyle.ActualDisplay.StartsWith("table-") && box.DerivedStyle.ActualDisplay != Keywords.TableCaption)
            {
                return 0;
            }

            // This will be used by the table layout engine later with boxWidth provided
            if (box.DerivedStyle.ActualDisplay is Keywords.Table && boxWidth is null)
            {
                return 0;
            }

            // The table layout engine passes the resolved table width explicitly (only tables reach
            // here with a non-null boxWidth) so a `margin: … auto` table centers against that width.
            if (boxWidth is not null)
            {
                return (box.ContainingBlock.Size.Width - boxWidth.Value) / 2;
            }

            return ResolveAutoHorizontalMargin(box);
        }

        public static double GetActualMarginRight(CssBox box, double? boxWidth = null)
        {
            var marginRight = box.MarginRight.Value;
            if (marginRight.IsValue)
            {
                return CssValueParser.ParseLength(marginRight.Value!.Value, box.ContainingBlock.Size.Width, box);
            }

            if (box.MarginLeft.Value.IsValue) return 0;

            if (box.DerivedStyle.ActualDisplay.StartsWith("table-") && box.DerivedStyle.ActualDisplay != Keywords.TableCaption)
            {
                return 0;
            }

            // This will be used by the table layout engine later with boxWidth provided
            if (box.DerivedStyle.ActualDisplay is Keywords.Table && boxWidth is null)
            {
                return 0;
            }

            // The table layout engine passes the resolved table width explicitly (only tables reach
            // here with a non-null boxWidth) so a `margin: … auto` table centers against that width.
            if (boxWidth is not null)
            {
                return (box.ContainingBlock.Size.Width - boxWidth.Value) / 2;
            }

            return ResolveAutoHorizontalMargin(box);
        }

        /// <summary>
        /// The used value of a horizontal <c>margin: auto</c> when <b>both</b> the left and right
        /// margins are <c>auto</c> on a non-table in-flow block (CSS 2.1 §10.3.3). Auto margins center
        /// the box only when its used width is <b>definite</b> and leaves free space in the containing
        /// block. An <c>auto</c> width <b>fills</b> the containing block, so its auto margins resolve to
        /// <c>0</c> (not centering) — this is what a <c>max-width</c>d, <c>margin: 0 auto</c> responsive
        /// wrapper needs: it fills the page when the page is narrower than the <c>max-width</c>, and
        /// only starts centering once the page grows past the <c>max-width</c> (which clamps the used
        /// width, making it definite again).
        /// </summary>
        private static double ResolveAutoHorizontalMargin(CssBox box)
        {
            var containingWidth = box.ContainingBlock.Size.Width;

            if (box.Width == Keywords.Auto || string.IsNullOrEmpty(box.Width))
            {
                // An auto width fills the containing block, so its auto margins are 0. `max-width` can
                // clamp the used width below the fill width — and `min-width` can then re-widen it (min
                // wins, CSS 2.1 §10.4) — making the box definite, after which the leftover space is
                // split to center it. Mirror GetBoxWidth's own max-then-min clamp so the two agree: a
                // filling box lands on `remaining == 0` and still returns 0.
                var usedContentWidth = containingWidth - box.ActualBoxSizeIncludedWidth;

                if (CssValueParser.IsValidLength(box.MaxWidth))
                {
                    usedContentWidth = Math.Min(usedContentWidth,
                        CssValueParser.ParseLength(box.MaxWidth, containingWidth, box));
                }

                if (box.MinWidth != "0" && CssValueParser.IsValidLength(box.MinWidth))
                {
                    usedContentWidth = Math.Max(usedContentWidth,
                        CssValueParser.ParseLength(box.MinWidth, containingWidth, box));
                }

                var clampedRemaining = containingWidth - (usedContentWidth + box.ActualBoxSizeIncludedWidth);
                return clampedRemaining > 0 ? clampedRemaining / 2 : 0;
            }

            // Definite width: split the genuine free space, accounting for the box's own
            // border/padding (ActualBoxSizingWidth is the used border-box width, already min/max-clamped).
            var remaining = containingWidth - box.ActualBoxSizingWidth;
            return remaining > 0 ? remaining / 2 : 0;
        }

        /// <summary>
        /// Recursively measures words inside the box
        /// </summary>
        /// <param name="box">the box to measure</param>
        /// <param name="g">Device to use</param>
        public static async ValueTask MeasureWords(CssBox box, RGraphics g)
        {
            foreach (var childBox in box.Boxes)
            {
                if (childBox.DerivedStyle.ActualDisplay == Keywords.None) continue;

                await childBox.MeasureWordsSize(g);
                await MeasureWords(childBox, g);
            }
        }

        public static async ValueTask<double> GetFitContentWidth(RGraphics g, CssBox box, double contentAreaWidth)
        {
            var maxIntrinsicWidth = await GetMaxContentWidth(g, box);

            var fitContentWidth = await GetLargestChildWidth(g, box, maxIntrinsicWidth);

            return fitContentWidth < contentAreaWidth ? fitContentWidth : contentAreaWidth;
        }

        public static async ValueTask<double> GetMinContentWidth(RGraphics g, CssBox box)
        {
            await MeasureWords(box, g);

            box.GetMinMaxWidth(out var minIntrinsicWidth, out _);

            return minIntrinsicWidth;
        }

        public static async ValueTask<double> GetMaxContentWidth(RGraphics g, CssBox box)
        {
            await MeasureWords(box, g);

            box.GetMinMaxWidth(out _, out var maxIntrinsicWidth);

            return maxIntrinsicWidth;
        }

        /// <summary>
        /// Whether <paramref name="box"/> is a "main column" box for per-page horizontal reflow (issue
        /// #143): the initial containing block (the synthetic root) or the <c>&lt;html&gt;</c>/
        /// <c>&lt;body&gt;</c> element.
        /// </summary>
        private static bool IsMainColumnBox(CssBox box) =>
            box.IsRoot
            || string.Equals(box.HtmlTag?.Name, "html", StringComparison.OrdinalIgnoreCase)
            || string.Equals(box.HtmlTag?.Name, "body", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whether <paramref name="box"/> (a candidate containing block) is an <b>unconstrained</b> main
        /// column: it and every main-column ancestor up to the root are main-column boxes with an
        /// auto-width, so the chain genuinely spans the page area and a child can safely adopt its own
        /// page's content width (issue #143). If any level carries an explicit/percentage <c>width</c> the
        /// chain no longer spans the page area, so per-page reflow is not applied there (an accepted gap -
        /// see docs / issues #199-#201).
        /// </summary>
        private static bool IsUnconstrainedMainColumn(CssBox box)
        {
            for (var b = box; b is not null; b = b.ParentBox)
            {
                if (!IsMainColumnBox(b)) return false;
                if (!string.IsNullOrEmpty(b.Width) && b.Width != Keywords.Auto) return false;
                if (b.IsRoot) break;
            }

            return true;
        }

        /// <summary>
        /// The total right-side inset (margin + border + padding) of a main-column containing block and
        /// its ancestors up to the root — the horizontal mirror of what <c>ContainingBlock.ClientLeft</c>
        /// already folds into the left side. Subtracted from the page-area right edge so a reflowed block
        /// respects its containing block's own right margin/border/padding (e.g. <c>body</c>'s UA-default
        /// 8px margin) instead of overrunning it to the page edge (issue #143).
        /// </summary>
        private static double MainColumnRightInset(CssBox box)
        {
            var inset = 0.0;
            for (var b = box; b is not null; b = b.ParentBox)
            {
                inset += b.ActualMarginRight + b.ActualBorderRightWidth + b.ActualPaddingRight;
                if (b.IsRoot) break;
            }

            return inset;
        }

        /// <summary>
        /// The used inline size of <paramref name="box"/>.
        /// </summary>
        /// <param name="g">the device context</param>
        /// <param name="box">the box to measure</param>
        /// <param name="blockTop">
        /// the border-box top to resolve the per-page measure against — the position the box is <i>about</i>
        /// to be placed at, which only the frame placing it knows (<c>CssBox.PlaceAndSizeBlockChild</c>). Omitted by
        /// the engines, which call this for a container whose <see cref="CssBox.Location"/> the frame above
        /// has already written, so the box's own coordinate is the truthful answer there.
        /// </param>
        public static async ValueTask<double> GetBoxWidth(RGraphics g, CssBox box, double? blockTop = null)
        {
            // Per-page horizontal reflow (issue #143). When a per-page @page rule overrides left/right
            // margins, content laid out on a page whose margins differ from the base uses THAT page's own
            // content-box width as its measure (CSS Paged Media 3: "the edges of the page area act as a
            // containing block for layout that occurs between page breaks"). Content stays anchored at the
            // base left origin, so the painter's existing per-page deltaX translate moves the (already
            // page-width) content to the page's true left edge. The available right edge is the page's own
            // area right MINUS the containing block's right inset (mirroring ContainingBlock.ClientLeft on
            // the left), so a margined body/html doesn't get overrun. Scoped to the unconstrained main
            // column (containing block chain is auto-width root/html/body): a box nested inside some other
            // (or constrained) block resolves against that block instead - deferred as an accepted gap
            // (#199-#201), since only the auto-width main column is guaranteed to span the page area.
            // Keyed off `blockTop` - the border-box top the frame placing this box has just decided on, not
            // the box's own Location.Y, which on the pass that places it still holds whatever position an
            // earlier layout generation left there (page 0's measure, on the first). The frame's offset is
            // settled before this runs precisely so the measure can come from the page the box lands on;
            // where no frame is placing the box (an engine measuring its own container), the box's
            // Location.Y has already been written and is the same answer.
            var availableRight = box.ContainingBlock.ClientRight;
            if (box.HtmlContainer is { } htmlContainer && htmlContainer.UseVariablePageWidth
                && IsUnconstrainedMainColumn(box.ContainingBlock))
            {
                availableRight = htmlContainer.PageContentRightOf(blockTop ?? box.Location.Y)
                                 - MainColumnRightInset(box.ContainingBlock);
            }

            var width = availableRight - box.ContainingBlock.ClientLeft - box.ActualMarginLeft - box.ActualMarginRight;

            if (box.Words.Count > 0)
            {
                width = box.Words.Sum(x => x.FullWidth);
            }

            // Per CSS2.1 10.3.3, `width` has no effect on a non-replaced inline-level box - a
            // replaced one (an image, or a resolved <object>) is already measured above via the
            // Words.Count>0 branch, so it's excluded from this guard the same way. Acid2's own
            // "#eyes-a object[type] { width: 7.5em; }" (the middle <object type="text/html"> in a
            // fallback chain, non-replaced, display:inline) is deliberately meant to have this width
            // ignored - without this guard, GetLargestChildWidth's recursive descendant walk (used
            // for position:absolute shrink-to-fit, e.g. ".eyes") wrongly folded 7.5em into the
            // ancestor's width, inflating it well past its real content.
            if (box.Width != Keywords.Auto && !string.IsNullOrEmpty(box.Width)
                && !(box.DerivedStyle.ActualDisplay == Keywords.Inline && box.Words.Count == 0))
            {
                // Absolute boxes resolve a percentage width against their nearest positioned ancestor
                // (CSS 2.1 §10.1), consistent with GetBoxHeight and the left/top positioning code.
                width = CssValueParser.ParseLength(box.Width, PercentageBase(box).Size.Width, box);
            }

            if (box is { Width: Keywords.Auto, Position.Value: not PositionMode.Absolute })
            {
                width -= box.ActualBoxSizeIncludedWidth;
            }

            if (box is { Width: Keywords.Auto, Position.Value: PositionMode.Absolute })
            {
                var absCb = PercentageBase(box);
                if (box.Left.Value.IsValue && box.Right.Value.IsValue)
                {
                    // CSS 2.1 §10.3.7: an absolutely-positioned box with auto width but both `left` and
                    // `right` set fills the space between them in the containing block (rather than shrinking
                    // to fit its content). This is what sizes a Charts.css area/line `td::before` (auto width,
                    // `inset: 0`) to cover its cell.
                    var left = CssValueParser.ParseLength(box.Left.Value.Value!.Value, absCb.Size.Width, box);
                    var right = CssValueParser.ParseLength(box.Right.Value.Value!.Value, absCb.Size.Width, box);
                    // An over-constrained fill (left + right wider than the containing block) clamps to 0, per
                    // CSS 2.1 §10.3.7 (a used width is never negative).
                    width = Math.Max(0, absCb.Size.Width - left - right - box.ActualMarginLeft - box.ActualMarginRight - box.ActualBoxSizeIncludedWidth);
                }
                else if (TryGetAspectRatioWidth(box, out var ratioWidth))
                {
                    // CSS Box Sizing 4 §5.1: an absolutely-positioned box with a preferred aspect ratio, a
                    // definite height and an auto (shrink-to-fit) width takes its width from the height via the
                    // ratio, rather than from its content (fit-content).
                    width = ratioWidth;
                }
                else
                {
                    width = await GetFitContentWidth(g, box, absCb.Size.Width);
                }
            }

            // Apply max-width constraint (before min-width, so min wins on conflict per CSS 2.1 §10.4)
            if (CssValueParser.IsValidLength(box.MaxWidth))
            {
                var maxW = CssValueParser.ParseLength(box.MaxWidth, box.ContainingBlock.Size.Width, box);
                width = Math.Min(width, maxW);
            }

            // Apply min-width constraint
            if (box.MinWidth != "0" && CssValueParser.IsValidLength(box.MinWidth))
            {
                var minW = CssValueParser.ParseLength(box.MinWidth, box.ContainingBlock.Size.Width, box);
                width = Math.Max(width, minW);
            }

            return width;
        }

        /// <summary>
        /// The box against whose size a percentage width/height on <paramref name="box"/> resolves. For an
        /// absolutely-positioned box that is the nearest positioned ancestor (CSS 2.1 §10.1) — the same box
        /// its <c>left</c>/<c>top</c> already measure from — rather than <see cref="CssBox.ContainingBlock"/>
        /// (the nearest in-flow block container), which they differ from when the parent block chain is not
        /// itself positioned. For every other box the two are the same.
        /// </summary>
        private static CssBox PercentageBase(CssBox box) =>
            box.Position.Value is PositionMode.Absolute ? DomUtils.GetNearestPositionedAncestor(box) : box.ContainingBlock;

        public static double? GetBoxHeight(CssBox box)
        {
            var height = box.ActualBoxSizingHeight;

            if (box == box.ContainingBlock && box.HtmlContainer is not null)
            {
                // Deliberately the BASE content band even when per-page @page margin overrides give
                // individual pages taller/shorter bands: the initial containing block (and therefore
                // percentage heights) resolves once for the whole document, not per page — see
                // "Per-page margin variation" in docs/html-css-support.md.
                height = Math.Max(height, box.HtmlContainer.PageSize.Height);
            }

            if (box.Words.Count > 0)
            {
                height = Math.Max(height, box.Words.Sum(w => w.Height));
            }

            // CSS 2.1 §10.1: percentages on an absolutely-positioned box resolve against its containing
            // block — the *nearest positioned ancestor's* padding box — not the nearest in-flow block
            // container that ContainingBlock returns. (Positioning already uses GetNearestPositionedAncestor;
            // this keeps % height resolution consistent, so e.g. a Charts.css pie slice `td { position:
            // absolute; height: 100% }` fills its position:relative tbody even though its parent <tr> is
            // static and zero-height.)
            var heightCb = PercentageBase(box);

            // CSS 2.1 §10.6.3: a definite (non-auto) `height` is the used height regardless of
            // content - content taller than it overflows past ActualBottom (clipped or not per
            // `overflow`), it does not grow the box. This must run BEFORE min-height, not after (as
            // the previous order here had it), so min-height can still win on conflict per §10.7.
            if (CssValueParser.IsValidLength(box.Height))
            {
                if (!heightCb.IsHeightCalculated && box.Height.EndsWith('%'))
                {
                    // An indefinite percentage height behaves as automatic (CSS Box Sizing 4 §5): a
                    // preferred aspect ratio sizes the height from the (definite) width if there is one;
                    // otherwise the height stays auto.
                    if (!TryGetAspectRatioHeight(box, out height))
                    {
                        return null;
                    }
                }
                else
                {
                    height = CssValueParser.ParseLength(box.Height, heightCb.Size.Height, box) + box.ActualBoxSizeIncludedHeight;
                }
            }
            else if (box.Position.Value is PositionMode.Absolute
                     && box.Top.Value.IsValue && box.Bottom.Value.IsValue
                     && heightCb.IsHeightCalculated)
            {
                // CSS 2.1 §10.6.4: an absolutely-positioned box with auto height but both `top` and `bottom`
                // set fills the space between them in the containing block. The counterpart of the §10.3.7
                // width rule above; sizes a Charts.css area/line `td::before` (auto height, `inset: 0`) to
                // its cell's height.
                var top = CssValueParser.ParseLength(box.Top.Value.Value!.Value, heightCb.Size.Height, box);
                var bottom = CssValueParser.ParseLength(box.Bottom.Value.Value!.Value, heightCb.Size.Height, box);
                // Over-constrained fill clamps to 0 (a used height is never negative), per CSS 2.1 §10.6.4.
                height = Math.Max(0, heightCb.Size.Height - top - bottom - box.ActualMarginTop - box.ActualMarginBottom);
            }
            else if (TryGetAspectRatioHeight(box, out var ratioHeight))
            {
                // CSS Box Sizing 4 §5: with a preferred aspect-ratio and an auto height, the height is
                // computed from the (definite) width via the ratio. This is what gives a Charts.css
                // `tbody { aspect-ratio: … }` its height, from which the bars take their percentage heights.
                height = ratioHeight;
            }

            if (CssValueParser.IsValidLength(box.MinHeight) &&
                (heightCb.IsHeightCalculated || !box.MinHeight.EndsWith('%')))
            {
                var minHeight = CssValueParser.ParseLength(box.MinHeight, heightCb.Size.Height, box) + box.ActualBoxSizeIncludedHeight;

                if (minHeight > height)
                {
                    height = minHeight;
                }
            }

            return height;
        }

        /// <summary>
        /// Computes a box's border-box height from its <c>aspect-ratio</c> and its (definite) used width, when
        /// the box has a usable preferred ratio and no definite height. The ratio applies to the box-sizing
        /// box, so dividing the content width by the ratio and adding the box-sizing-included height yields the
        /// border-box height for both <c>content-box</c> and <c>border-box</c> sizing (the included term is 0
        /// for <c>border-box</c>, where <c>Size.Width</c> is already the border-box width).
        /// </summary>
        internal static bool TryGetAspectRatioHeight(CssBox box, out double height)
        {
            height = 0;

            if (string.IsNullOrEmpty(box.AspectRatio) || box.AspectRatio == Keywords.Auto) return false;
            if (box.Size.Width <= 0) return false;

            var tokens = CssValueParser.GetCssTokens(box.AspectRatio);
            if (!AspectRatioGrammar.TryParse(tokens, out var ratio) || ratio is not (> 0)) return false;

            height = box.Size.Width / ratio.Value + box.ActualBoxSizeIncludedHeight;
            return true;
        }

        /// <summary>
        /// True when the box has a definite used height: a non-percentage length, or a percentage resolved
        /// against a height-calculated containing block. A percentage against an indefinite (auto-height)
        /// containing block is NOT definite — CSS Box Sizing 4 §5 treats it as automatic.
        /// </summary>
        internal static bool HasDefiniteHeight(CssBox box)
        {
            if (!CssValueParser.IsValidLength(box.Height)) return false;
            if (box.Height.EndsWith('%')) return PercentageBase(box).IsHeightCalculated;
            return true;
        }

        /// <summary>
        /// Computes a box's box-sizing-box width from its <c>aspect-ratio</c> and its definite used height —
        /// the height→width counterpart of <see cref="TryGetAspectRatioHeight"/>. Applies only where the width
        /// would otherwise be shrink-to-fit (an absolutely-positioned auto-width box): a normal-flow block's
        /// auto inline size is stretch-fit, which wins over the ratio (CSS Box Sizing 4 §5.1), so the caller
        /// must not use this on a stretch-fit box. The returned width is the box-sizing box's width (matching
        /// <see cref="GetBoxWidth"/>'s explicit-width branch), so the caller adds <c>ActualBoxSizeIncludedWidth</c>
        /// to reach the border-box edge — for both <c>content-box</c> (dividing/multiplying on the content box)
        /// and <c>border-box</c> (the included term is 0, so the ratio maps border-box height to border-box width).
        /// </summary>
        internal static bool TryGetAspectRatioWidth(CssBox box, out double width)
        {
            width = 0;

            if (string.IsNullOrEmpty(box.AspectRatio) || box.AspectRatio == Keywords.Auto) return false;
            // Only when the box has no explicit width (an auto/absent width the ratio can determine).
            if (box.Width != Keywords.Auto && !string.IsNullOrEmpty(box.Width)) return false;
            if (!HasDefiniteHeight(box)) return false;

            var tokens = CssValueParser.GetCssTokens(box.AspectRatio);
            if (!AspectRatioGrammar.TryParse(tokens, out var ratio) || ratio is not (> 0)) return false;

            var boxSizingHeight = CssValueParser.ParseLength(box.Height, PercentageBase(box).Size.Height, box);
            if (boxSizingHeight <= 0) return false;

            width = boxSizingHeight * ratio.Value;
            return true;
        }

        public static void ApplyParentHeight(CssBox box)
        {
            foreach (var childBox in box.Boxes)
            {
                // A table-cell/row/row-group's height is entirely owned by CssLayoutEngineTable's own
                // row-stretch algorithm (CSS2.1 17.5.3: every cell in a row stretches to the row's
                // tallest cell, which can be taller than the cell's own explicit `height` - that
                // property is only one candidate feeding INTO the row's height, not necessarily the
                // final used value). CssLayoutEngineTable.LayoutBodyRow already ran ApplyHeight for
                // this cell's own subtree once, via its cell.PerformLayout(g) call, BEFORE stretching
                // cell.ActualBottom up to match the row - re-running the generic ApplyHeight here
                // (reached because the table box itself flows through this same PerformLayoutImp height
                // pass afterward) would recompute the cell's height from its own explicit `height`
                // alone, discarding that stretch - exactly what made ".third-part" (Acid2's own
                // "gets stretched to fit row" test case) end up 4.5pt short of its row.
                if (childBox.DerivedStyle.ActualDisplay is Keywords.TableCell or Keywords.TableRow
                    or Keywords.TableRowGroup or Keywords.TableHeaderGroup or Keywords.TableFooterGroup)
                {
                    continue;
                }

                ApplyHeight(childBox);
                ApplyParentHeight(childBox);
            }
        }

        public static void ApplyHeight(CssBox box)
        {
            var boxHeight = GetBoxHeight(box);
            var height = boxHeight ?? 0;

            // GetBoxHeight already resolves the correct used height when it can (content height for
            // auto, clamped by min-height; the explicit height itself, also clamped by min-height,
            // when set) - assign it directly so a definite height smaller than content actually
            // shrinks ActualBottom instead of leaving it at the content-driven value. Null means
            // GetBoxHeight couldn't resolve a value at all (e.g. a percentage height against an
            // indefinite containing block) - keep the existing Math.Max fallback there so the box
            // keeps its content-driven height instead of collapsing to zero.
            //
            // A table cell is the one box kind §10.6.3's "definite height wins regardless of content"
            // rule does not apply to - CSS 2.1 §17.5.3 makes a cell's own height (before its row even
            // equalizes every cell to the row's tallest) already the greater of its specified height
            // and the minimum height its content requires, so an explicit height smaller than content
            // must never shrink it here. This runs directly on the cell itself (PerformLayoutEpilogue,
            // via cell.PerformLayout - see ApplyParentHeight's own comment on that call), strictly after
            // CreateLineBoxes has already set ActualBottom to the real content bottom, so Math.Max
            // against that value is exactly "at least the content's required height" - the table row
            // loop's later Math.Max across every cell's ActualBottom (CssLayoutEngineTable.LayoutBodyRow)
            // then reconciles the row height from correct per-cell values instead of ones already
            // clipped to their own too-small specified height (issue #729).
            box.ActualBottom = boxHeight is not null && !box.IsTableCell
                ? box.Location.Y + height
                : Math.Max(box.ActualBottom, box.Location.Y + height);

            // IsHeightCalculated drives whether a DESCENDANT's percentage height/min-height/max-height
            // resolves against this box, per CSS 2.1 §10.5: only true when this box's own height is
            // "specified explicitly" (a definite, non-auto length, or a percentage against a containing
            // block that is itself height-calculated), or this box is the root/initial containing block
            // (whose used height is the page height regardless of its `height` computed value). It must
            // NOT simply mirror "GetBoxHeight returned a usable number" - GetBoxHeight also returns a
            // real number for plain content-driven `height: auto` boxes (e.g. via ActualBoxSizingHeight/
            // Words height), and treating THAT as "calculated" incorrectly makes every percentage-height
            // descendant of an auto-height block resolve against that content-driven height instead of
            // being treated as auto (CSS2.1 Acid2 test's `.nose { height: 60% }` inside auto-height
            // `.picture` is exactly this trap - it must resolve to `auto`, not to a huge value derived
            // from `.picture`'s own content height).
            var isRootWithPageHeight = box == box.ContainingBlock && box.HtmlContainer is not null;
            var isDefiniteHeight = CssValueParser.IsValidLength(box.Height) &&
                (box.ContainingBlock.IsHeightCalculated || !box.Height.EndsWith('%'));
            // An aspect-ratio with a definite width and no definite height yields a definite (ratio-derived)
            // height too, so a percentage-height descendant resolves against it — this is what lets the
            // Charts.css bars take their height from the ratio-sized tbody. "No definite height" is exactly
            // !isDefiniteHeight, which covers both an auto height and an *indefinite* percentage height
            // (a `%` whose containing block isn't itself height-calculated — CSS Box Sizing 4 §5 treats
            // that as automatic, so the ratio applies there too).
            var isRatioHeight = !isDefiniteHeight && TryGetAspectRatioHeight(box, out _);
            box.IsHeightCalculated = isRootWithPageHeight || isDefiniteHeight || isRatioHeight;

            // Apply max-height constraint. Unlike min-height/explicit-height above (which only ever
            // grow ActualBottom), max-height must be able to shrink the box below its content's
            // natural extent — content simply overflows past ActualBottom, mirroring the existing
            // overflow:hidden clip elsewhere in this engine.
            if (CssValueParser.IsValidLength(box.MaxHeight) &&
                (box.ContainingBlock.IsHeightCalculated || !box.MaxHeight.EndsWith('%')))
            {
                var maxHeight = CssValueParser.ParseLength(box.MaxHeight, box.ContainingBlock.Size.Height, box) + box.ActualBoxSizeIncludedHeight;
                var maxBottom = box.Location.Y + maxHeight;

                if (box.ActualBottom > maxBottom)
                {
                    box.ActualBottom = maxBottom;

                    // min-height wins over max-height on conflict (CSS 2.1 §10.7)
                    if (CssValueParser.IsValidLength(box.MinHeight) &&
                        (box.ContainingBlock.IsHeightCalculated || !box.MinHeight.EndsWith('%')))
                    {
                        var minHeight = CssValueParser.ParseLength(box.MinHeight, box.ContainingBlock.Size.Height, box) + box.ActualBoxSizeIncludedHeight;
                        var minBottom = box.Location.Y + minHeight;

                        if (box.ActualBottom < minBottom)
                        {
                            box.ActualBottom = minBottom;
                        }
                    }
                }
            }
        }

        #region Private methods

        private static void ClearBox(CssBox box, int currentBoxIdx, CssBox containingBox)
        {
            var clearance = Math.Max(containingBox.ClientTop, box.Location.Y);

            for (var i = 0; i < currentBoxIdx; i++)
            {
                var siblingBox = containingBox.Boxes[i];

                clearance = Math.Max(clearance, GetClearance(siblingBox, box.Clear.Value));

                if (!siblingBox.IsFloated) continue;

                switch (siblingBox.Float.Value)
                {
                    case Floating.Left when box.Clear.Value is ClearMode.Right:
                    case Floating.Right when box.Clear.Value is ClearMode.Left:
                        continue;
                }

                // CSS 2.1 §9.5.2: clearance places the cleared box's top border edge even with the
                // float's bottom OUTER (margin) edge, not its border edge - a float with a negative
                // bottom margin (Acid2's ".nose { margin: -2em 2em -1em }") is cleared 1em higher
                // than its visible bottom. StaticBottom so a float that is ALSO position:relative
                // clears at its static position, not its visual offset (CSS 2.1 §9.4.3).
                clearance = Math.Max(clearance, siblingBox.StaticBottom + siblingBox.ActualMarginBottom);

            }

            box.Location = new RPoint(box.ClientLeft, clearance);
        }

        private static double GetClearance(CssBox box, ClearMode clearPropValue)
        {
            var clearance = 0d;

            foreach (var childBox in box.Boxes)
            {
                foreach (var childChildBox in childBox.Boxes)
                {
                    clearance = Math.Max(clearance, GetClearance(childChildBox, clearPropValue));
                }

                if (!childBox.IsFloated)
                {
                    continue;
                }

                // clearPropValue (the CLEARING box's own `clear` value, passed down through the
                // recursion) - not box.Clear, which is the container being searched and is usually
                // "none", never filtering anything.
                switch (childBox.Float.Value)
                {
                    case Floating.Left when clearPropValue is ClearMode.Right:
                    case Floating.Right when clearPropValue is ClearMode.Left:
                        continue;
                }

                // Bottom outer (margin) edge at the static position, same as ClearBox above
                // (CSS 2.1 §9.5.2 / §9.4.3).
                clearance = Math.Max(clearance, childBox.StaticBottom + childBox.ActualMarginBottom);
            }

            return clearance;
        }

        private static void FloatBoxLeft(CssBox box, double startX, double startY, double limitRight)
        {
            CssFloatCoordinates coordinates = new()
            {
                Left = startX + box.ActualMarginLeft,
                Right = limitRight,
                Top = startY,
                MaxBottom = startY,
                MarginLeft = box.ActualMarginLeft,
                MarginRight = box.ActualMarginRight,
                ReferenceWidth = box.ActualBoxSizingWidth
            };

            do
            {
                var intersectingFloat = DomUtils.GetFirstIntersectingFloatBox(box, coordinates, box.Float.Value);

                if (intersectingFloat is null) break;

                switch (intersectingFloat.Float.Value)
                {
                    case Floating.Left:
                        coordinates.Left = intersectingFloat.ActualRight + intersectingFloat.ActualMarginRight + box.ActualMarginLeft;
                        break;
                    case Floating.Right:
                        coordinates.Right = intersectingFloat.Location.X - intersectingFloat.ActualMarginLeft;
                        break;
                }

                if (intersectingFloat.ActualBottom > coordinates.MaxBottom)
                {
                    coordinates.MaxBottom = intersectingFloat.ActualBottom;
                }

                if (coordinates.Left + box.ActualWidth > coordinates.Right)
                {
                    coordinates.Top = coordinates.MaxBottom + box.ActualMarginTop;
                    coordinates.Left = startX + box.ActualMarginLeft;
                }
            } while (true);

            box.Location = new RPoint(coordinates.Left, coordinates.Top);

        }

        private static void FloatBoxRight(CssBox box, double startX, double startY, double limitRight)
        {
            CssFloatCoordinates coordinates = new()
            {
                Left = startX,
                Right = limitRight - box.ActualMarginRight,
                Top = startY,
                MaxBottom = startY,
                MarginLeft = box.ActualMarginLeft,
                MarginRight = box.ActualMarginRight,
                ReferenceWidth = box.ActualBoxSizingWidth
            };

            do
            {
                var intersectingFloat = DomUtils.GetFirstIntersectingFloatBox(box, coordinates, box.Float.Value);

                if (intersectingFloat is null) break;

                switch (intersectingFloat.Float.Value)
                {
                    case Floating.Left:
                        coordinates.Left = intersectingFloat.ActualRight;
                        break;
                    case Floating.Right:
                        coordinates.Right = intersectingFloat.Location.X;
                        break;
                }
                if (intersectingFloat.ActualBottom > coordinates.MaxBottom)
                {
                    coordinates.MaxBottom = intersectingFloat.ActualBottom;
                }

                if (coordinates.Left > coordinates.FloatRightStartX)
                {
                    coordinates.Right = limitRight - box.ActualMarginRight;
                    coordinates.Top = coordinates.MaxBottom;
                }
            } while (true);

            box.Location = new RPoint(coordinates.FloatRightStartX, coordinates.Top);
        }

        /// <summary>
        /// FlowBox's per-entry setup, before it walks box's children.
        /// </summary>
        private static bool PrepareFlowBoxEntry(CssBox box, CssBox blockBox, CssLineBoxCoordinates coordinates, int startOrdinal)
        {
            var opensHere = startOrdinal >= coordinates.ResumeOrdinal;

            // Only a box actually opening here may claim the current line as its first: §6.2 gives a
            // sliced box its leading border and padding once, at its true start, and
            // CssLineBox.UpdateRectangle reads this to decide which line gets them. A box whose own
            // first word is the resume word does open here - the line it was entered on in the earlier
            // pass is the one CreateLineBoxes discarded.
            if (opensHere) box.FirstHostingLineBox = coordinates.Line;

            // Inline elements (e.g. <b>/<span>) never get their own PerformLayoutImp call - that only
            // happens for block children (CssBox.PerformLayoutImp's childBox.PerformLayout loop), which
            // is exactly the path skipped when a block's children are ContainsInlinesOnly and flowed
            // here instead. FlowBox's own entry/exit (this method and FinalizeFlowBoxExit below) is the
            // one place that visits every inline box, in DOM order, exactly once - so it's where
            // string-set and named-page registration (normally done near the top/bottom of
            // PerformLayoutImp) have to happen for inline boxes. blockBox itself is excluded since its
            // own PerformLayoutImp already handles it correctly before CreateLineBoxes is even called.
            // Gated on opensHere for the same reason FirstHostingLineBox is above: a pass resuming this
            // box's flow into a later fragmentainer re-enters it from the top (its already-placed words
            // are only skipped by ordinal further down), so without this guard a resumed pass would
            // re-run ApplyStringSet using *this* pass's fresh coordinates - overwriting the box's true
            // position (set when it actually opened) with wherever the resumed fragmentainer happens to
            // start. string-set fires exactly once per box per opening, mirroring a block box's own
            // once-per-_prologueDone guarantee.
            if (opensHere && box != blockBox && !string.IsNullOrEmpty(box.StringSet) && box.StringSet != Keywords.None)
            {
                // Unlike a block box's PerformLayoutPrologue, nothing else withdraws this box's previous
                // registration before FlowBox re-opens it (a multicol measurement pass, a re-banding
                // re-layout, or a column-fill retry all re-run this) - so without unregistering first, a
                // stale entry from an earlier pass survives as an orphan, still eligible to match some
                // page's Y window in MarginBoxRenderer.ResolveNamedString.
                if (box.NamedStrings.Count > 0)
                {
                    box.HtmlContainer?.UnregisterNamedStrings(box.NamedStrings.Values);
                    box.NamedStrings.Clear();
                }

                CssNamedStringEngine.ApplyStringSet(box);
            }

            return opensHere;
        }

        /// <summary>
        /// FlowBox's per-exit bookkeeping, once box's content (if any, this pass) has actually been
        /// placed and coordinates reflects where it landed.
        /// </summary>
        private static void FinalizeFlowBoxExit(CssBox box, CssBox blockBox, CssLineBoxCoordinates coordinates, bool opensHere, int startOrdinal, double startX, double startY)
        {
            // FlowBox's per-child loop pre-shifts CurrentY by box's own border-top+padding-top before
            // recursing into an atomic inline-level (display:inline-block) box's content (issue #333),
            // so when this box was reached that way, `startY` (captured at this call's own entry) names
            // the CONTENT box's top, not the border box's - while ActualHeight below is a border-box
            // measurement. The two fallbacks that follow reconstruct this box's own border-box rectangle
            // from `startY`/`ActualHeight`, so they must undo that shift first, or they double-count the
            // top inset on top of what ActualHeight already carries. A zero inset (no border/padding, or
            // a display this box's own pre-shift never applies to) leaves this a no-op.
            var trueStartY = box.DerivedStyle.ActualDisplay is Keywords.InlineBlock
                ? startY - (box.ActualBorderTopWidth + box.ActualPaddingTop)
                : startY;

            // handle height setting: the flowed content came out shorter than the box's own
            // ActualHeight (e.g. an inline-block button whose vertical padding exceeds its one
            // small-font text line), so extend MaxBottom to cover the box's full height from
            // where it started. This must be trueStartY-anchored: the old
            // `MaxBottom = ActualHeight - (MaxBottom - startY)` form assigned the deficit as an
            // ABSOLUTE document Y (a tiny value near the page top), dragging MaxBottom above
            // startY - when such a box was a block's last/only inline content, the block's
            // resulting ActualBottom landed above its own top (negative height), and paint-time
            // visibility culling then dropped the block's whole subtree (buttons styled like the
            // showcase's themeable-card "Learn More" were never painted at all).
            //
            // Restricted to non-plain-inline boxes: per CSS2.1 §10.8.1, the vertical padding/
            // border of a non-replaced `display: inline` box does not influence line box height
            // at all (it paints, overflowing the line, without taking vertical space) - only an
            // atomic inline-level box (inline-block/inline-table) contributes its full box
            // height to the line it sits on, which is what ActualHeight approximates here (a
            // plain inline's ActualHeight is just its own padding+border, since it never gets a
            // Size of its own - extending the flow by that would grow the containing block in
            // violation of §10.8.1).
            //
            // Like the width branch below, this compares an advance measured in this pass against the
            // box's whole height, so it only means anything for a box this pass both opened and
            // finished. For one it merely walked through the deficit is the box's entire height, which
            // it would then add to the flow all over again.
            if (opensHere && box.DerivedStyle.ActualDisplay is not Keywords.Inline && coordinates.MaxBottom - trueStartY < box.ActualHeight)
            {
                coordinates.MaxBottom = trueStartY + box.ActualHeight;
            }

            // handle width setting
            //
            // Only for a box this pass both opened and finished. Whether a box's content came out
            // narrower than its declared width is a question about the whole box, and `CurrentX - startX`
            // only measures it when the whole box went through this pass: a box the pass merely walked
            // through has a delta of zero (its words are skipped, and its spacing is suppressed above),
            // and one that began pages ago has a delta covering only the part that landed here. Either
            // way the branch fires spuriously - registering a rectangle at this page's coordinates for a
            // box whose real extent is elsewhere, which AssignRectanglesToBoxes then merges into the
            // box's own rectangle for the line. A box that opens here but breaks again never reaches
            // this point, since FlowBox returns as soon as the break is recorded.
            if (opensHere && box.IsInline && 0 <= coordinates.CurrentX - startX && coordinates.CurrentX - startX < box.ActualWidth)
            {
                // hack for actual width handling
                coordinates.CurrentX += box.ActualWidth - (coordinates.CurrentX - startX);
                coordinates.Line.Rectangles.Add(box, new RRect(startX, trueStartY, box.ActualWidth, box.ActualHeight));
            }

            // handle box that is only a whitespace
            if (opensHere && box.Text is { Length: > 0 } && string.IsNullOrWhiteSpace(box.Text) && !box.IsImage && box.IsInline && box.Boxes.Count == 0 && box.Words.Count == 0)
            {
                coordinates.CurrentX += box.ActualWordSpacing;
            }

            // Finalize what was captured at entry, now that this box's content has actually been placed
            // and coordinates.CurrentY reflects where it landed - mirrors CssBox.PerformLayoutImp's own
            // late-stage Y-correction/named-page registration (done there once Location is final), which
            // a plain inline box never gets a Location for in the first place. Gated on opensHere for the
            // same reason as the entry-side guard in PrepareFlowBoxEntry: a pass merely resuming this
            // box's already-placed content into a later fragmentainer must not re-stamp its
            // NamedStrings/named-page Y to wherever *this* pass's cursor happens to sit (typically the
            // resumed fragmentainer's own top, since the walk hasn't advanced past already-placed words
            // yet) - that both discards the box's true position from when it actually opened and, for
            // named-page, would corrupt ActivePageName for content after it.
            if (opensHere && box != blockBox)
            {
                if (box.NamedStrings.Count > 0)
                {
                    foreach (var namedString in box.NamedStrings.Values)
                    {
                        namedString.Y = coordinates.CurrentY;
                    }
                }

                if (!string.IsNullOrEmpty(box.PageName) && box.PageName != "auto")
                {
                    // Same re-entry hazard as the string-set guard in PrepareFlowBoxEntry: without
                    // withdrawing a stale registration first, a re-banding re-layout or column-fill retry
                    // leaves an orphaned NamedPageElement behind, corrupting ActivePageName for whatever
                    // comes after it.
                    if (box.RegisteredNamedPageElement is { } stalePageElement)
                    {
                        box.HtmlContainer?.UnregisterNamedPageElement(stalePageElement);
                    }

                    box.RegisteredNamedPageElement = box.HtmlContainer?.RegisterNamedPageElement(box.PageName, coordinates.CurrentY);
                }
            }

            // The mirror of the entry guard: a box this pass placed nothing for closed in an earlier
            // fragmentainer, and its last hosting line is the one it closed on there. Nothing reads a
            // clobbered value today - BubbleRectangles never reaches such a box on this page's lines,
            // and earlier lines are not re-finalized - but that is a property of two other methods, not
            // an invariant of this pair, and #336's whole difficulty was that one half of it could not
            // be trusted.
            if (coordinates.PlacedSince(startOrdinal)) box.LastHostingLineBox = coordinates.Line;
        }

        /// <summary>
        /// Treats an inline-flex child as an atomic inline element: positions it, runs flex layout over
        /// its children, applies its own string-set/named-page-name, then advances the cursor by its
        /// outer size and registers it in the line so its border/background paints.
        /// </summary>
        private static async ValueTask FlowInlineFlexChild(RGraphics g, CssBox b, CssLineBoxCoordinates coordinates)
        {
            // coordinates.CurrentX is already past the left margin+border+padding (leftSpacing was added
            // by the caller), so Location.X sits at the border-left edge (after margin).
            b.Location = new RPoint(
                coordinates.CurrentX - b.ActualPaddingLeft - b.ActualBorderLeftWidth,
                coordinates.CurrentY);
            b.ActualBottom = b.Location.Y;
            b.FirstHostingLineBox = coordinates.Line;
            b.LastHostingLineBox = coordinates.Line;

            // Unlike a plain inline box, b.Location is already final here, so string-set/named-page can
            // be applied and finalized together rather than split across entry/exit like plain inlines.
            if (!string.IsNullOrEmpty(b.StringSet) && b.StringSet != Keywords.None)
            {
                if (b.NamedStrings.Count > 0)
                {
                    b.HtmlContainer?.UnregisterNamedStrings(b.NamedStrings.Values);
                    b.NamedStrings.Clear();
                }

                CssNamedStringEngine.ApplyStringSet(b);
                foreach (var namedString in b.NamedStrings.Values)
                {
                    namedString.Y = b.Location.Y;
                }
            }

            if (!string.IsNullOrEmpty(b.PageName) && b.PageName != "auto")
            {
                if (b.RegisteredNamedPageElement is { } staleFlexPageElement)
                {
                    b.HtmlContainer?.UnregisterNamedPageElement(staleFlexPageElement);
                }

                b.RegisteredNamedPageElement = b.HtmlContainer?.RegisterNamedPageElement(b.PageName, b.Location.Y);
            }

            await CssLayoutEngineFlex.PerformLayout(g, b);

            // Advance to content-right so that the outer rightSpacing addition lands correctly.
            coordinates.CurrentX = b.ClientRight;
            coordinates.MaxRight = Math.Max(coordinates.MaxRight, b.Location.X + b.ActualBoxSizingWidth);
            coordinates.MaxBottom = Math.Max(coordinates.MaxBottom, b.Location.Y + b.ActualBoxSizingHeight);

            // Register the box in the parent line so its border/background is painted.
            coordinates.Line.Rectangles[b] = new RRect(
                b.Location.X, b.Location.Y, b.ActualBoxSizingWidth, b.ActualBoxSizingHeight);
        }

        /// <summary>
        /// Recursively flows the content of the box using the inline model
        /// </summary>
        /// <param name="g">Device Info</param>
        /// <param name="blockBox">Blockbox that contains the text flow</param>
        /// <param name="box">Current box to flow its content</param>
        /// <param name="limitRight">Maximum reached right</param>
        /// <param name="lineSpacing">Space to use between rows of text</param>
        /// <param name="lineStartX">x starting coordinate for when breaking lines of text</param>
        /// <param name="coordinates">Current coordinates being used</param>
        /// <param name="clonedResumeStart">
        /// The leading spacing owed to the fragment this pass opens, summed over the ancestors of the
        /// resume point that were suppressed on the way down because they had already opened in an
        /// earlier fragmentainer, and that clone their decorations
        /// (<see href="https://www.w3.org/TR/css-break-3/#break-decoration">css-break-3 §6.2</see>). It
        /// is spent once, where the flow actually resumes. Always zero on a pass that is not resuming.
        /// </param>
        private static async ValueTask FlowBox(RGraphics g, CssBox blockBox, CssBox box, double limitRight, double lineSpacing, double lineStartX, CssLineBoxCoordinates coordinates, double clonedResumeStart = 0)
        {
            var startX = coordinates.CurrentX;
            var startY = coordinates.CurrentY;

            // text-indent's line-start side is physical-right under RTL (CSS Text 3 §3) - reserved here by
            // narrowing the wrap boundary for the currently-active line's own indent, rather than by an
            // added CurrentX offset (which is what LTR uses; see CreateLineBoxes'/this function's own
            // `coordinates.CurrentX += GetLineTextIndent(...)` sites, both skipped for RTL). Recomputed
            // per line rather than cached, since which line is "current" changes as this walk wraps.
            var isRtl = blockBox.Direction.Value == DirectionMode.Rtl;

            // Since #321 a pass fills one fragmentainer, so a resumed pass re-enters this box from the
            // top even when its content began pages ago - the words it already placed are skipped by
            // ordinal below. Read before any of them are visited, this names the ordinal of the box's
            // own first word, so it says whether this pass is the one opening the box.
            var startOrdinal = coordinates.WordOrdinal;
            var opensHere = PrepareFlowBoxEntry(box, blockBox, coordinates, startOrdinal);

            var boxes = box.Boxes;

            if (boxes.Count is 0 && box.Words.Count > 0)
            {
                boxes = [box];
            }

            foreach (var b in boxes)
            {
                // An "outside" ::marker (the CSS default) must not affect the layout of the rest of
                // the list item it belongs to (CSS2.1 12.5.1 / CSS Lists Level 3) - it's positioned
                // and sized entirely on its own (see CssBoxMarker.PerformLayoutImp), not as part of
                // this inline flow. An "inside" marker has no such exclusion - it's simply the first
                // ordinary inline child, flowed exactly like any other word/box below.
                if (CssBox.IsOutsideMarker(b)) continue;

                // The same question as `opensHere` above, asked of this child: has the walk reached the
                // resume point yet, or is it still fast-forwarding through content an earlier
                // fragmentainer placed?
                var childStartOrdinal = coordinates.WordOrdinal;
                var childOpensHere = childStartOrdinal >= coordinates.ResumeOrdinal;

                // CSS2.1 §8.1 box model: an atomic inline-level box's (display:inline-block) content sits
                // inside its padding box, border-top+padding-top below its own top edge. Applied here,
                // before any of b's content is placed, rather than shifting already-placed words
                // afterward (issue #333) - so the ordinary per-word WouldStraddleFragmentainer check
                // below sees the real final position from the start, for every line b's own content may
                // wrap onto, not just the first (CreateLineBoxes can only discard one in-progress line,
                // so a straddle discovered only on a later internal line would have no way to discard the
                // earlier ones). Guarded with !ReferenceEquals(b, box) for the self-iteration case
                // (boxes = [box], b == box) even though the two conditions that would have to coincide
                // for it to fire never do: self-iteration requires box.Words.Count > 0, but reaching this
                // loop as a nested FlowBox call at all requires the PARENT's own per-child dispatch to
                // have found b.Words.Count == 0 (a box holding words directly takes the loop's other,
                // non-recursive branch instead) - the same b can't satisfy both. Kept explicit rather than
                // assumed, since a display:inline-block box acquiring direct words in the future would
                // silently double the shift here rather than fail loudly. Only on the pass that opens b's
                // own content (childOpensHere): a continuation must not re-push a box's later lines down
                // by a padding it already paid on an earlier fragmentainer.
                var appliesAtomicInset = b.DerivedStyle.ActualDisplay is Keywords.InlineBlock && !ReferenceEquals(b, box);
                var atomicTopInset = appliesAtomicInset ? b.ActualBorderTopWidth + b.ActualPaddingTop : 0;
                var atomicBottomInset = appliesAtomicInset ? b.ActualBorderBottomWidth + b.ActualPaddingBottom : 0;
                var preShiftedForAtomicInset = childOpensHere && atomicTopInset > 0;
                var lineBeforeChild = coordinates.Line;

                if (preShiftedForAtomicInset)
                {
                    coordinates.CurrentY += atomicTopInset;
                }

                var leftSpacing = (b.Position.Value != PositionMode.Absolute && b.Position.Value != PositionMode.Fixed) ? b.ActualMarginLeft + b.ActualBorderLeftWidth + b.ActualPaddingLeft : 0;
                var rightSpacing = (b.Position.Value != PositionMode.Absolute && b.Position.Value != PositionMode.Fixed) ? b.ActualMarginRight + b.ActualBorderRightWidth + b.ActualPaddingRight : 0;

                // Room for the border and padding a `box-decoration-break: clone` ancestor re-inserts at each
                // line break (css-break-3 §6.2), which the wrap decision has to leave for the fragment it
                // closes and the flow has to skip past on the fragment it opens. b's own trailing spacing is
                // already reserved on every line by `rightSpacing` below, so only its ancestors are counted
                // here; its own leading spacing is handled at the wrap itself.
                var clonesDecorations = b.HtmlContainer is { HasCloneDecorations: true };
                var clonedTrailing = clonesDecorations ? DomUtils.ClonedInlineEnd(b.ParentBox, blockBox) : 0;

                // A resumed flow is continuing this block, so the per-line rectangles bubbled into
                // inline boxes by the fragmentainers already filled must survive - resetting them here
                // would blank out their backgrounds, borders and text decoration on those pages.
                if (coordinates.ResumeOrdinal == 0) b.RectanglesReset();
                await b.MeasureWordsSize(g);

                // Still on blockBox's first formatted line and a ::first-line rule applies to it -
                // measure b's words using the first-line font/spacing instead of its own. If b's
                // content turns out to straddle the line-1/2 boundary, the wrap-handling block below
                // corrects the words that actually end up on line 2+ back to b's own normal style.
                if (blockBox.ResolvedFirstLineStyle != null && coordinates.Line == blockBox.LineBoxes[0])
                {
                    b.ApplyFirstLineStyleOverride(g, blockBox.ResolvedFirstLineStyle);
                }

                // A box whose content began in an earlier fragmentainer is not re-opened here: under
                // `slice` a continuation carries no leading margin, border or padding at all (§6.2), so
                // re-adding it both indents this page's text and, with FirstHostingLineBox guarded
                // above, would leave the rectangle disagreeing with the words inside it. Under `clone`
                // the box genuinely does re-open - that spacing is carried down and applied once, at
                // the point the flow resumes, rather than at every box the walk passes through.
                var childClonedResumeStart = clonedResumeStart;

                if (childOpensHere)
                {
                    coordinates.CurrentX += leftSpacing;
                }
                else if (b.BoxDecorationBreak.Value == BoxDecorationBreakMode.Clone)
                {
                    childClonedResumeStart += leftSpacing;
                }

                // What the guard above actually put on the cursor. The float branches below re-establish
                // the line start from scratch rather than adjusting it, so they have to re-apply the same
                // amount - re-applying the raw `leftSpacing` would put back exactly what `slice` suppresses.
                var appliedLeftSpacing = childOpensHere || b.BoxDecorationBreak.Value == BoxDecorationBreakMode.Clone ? leftSpacing : 0;

                var lastLeftIntersectingFloatBox = DomUtils.GetLastLeftIntersectingFloatBox(box, coordinates);

                if (lastLeftIntersectingFloatBox is not null)
                {
                    coordinates.CurrentX = lastLeftIntersectingFloatBox.ActualRight + lastLeftIntersectingFloatBox.ActualMarginRight + appliedLeftSpacing;
                }

                if (b.Words.Count > 0)
                {
                    var wrapNoWrapBox = false;

                    if (b.WhiteSpace.Value == Whitespace.NoWrap && coordinates.CurrentX > lineStartX)
                    {
                        var boxRight = coordinates.CurrentX;

                        foreach (var word in b.Words)
                            boxRight += word.FullWidth;

                        var noWrapLimitRight = isRtl
                            ? limitRight - GetLineTextIndent(blockBox, coordinates.Line.Equals(blockBox.LineBoxes[0]), coordinates.Line.FollowsForcedBreak)
                            : limitRight;

                        if (boxRight > noWrapLimitRight)
                            wrapNoWrapBox = true;
                    }

                    // The space belongs before b's first word. If that word was placed in an earlier
                    // fragmentainer, so was the space.
                    if (childOpensHere && DomUtils.IsBoxHasWhitespace(b))
                        coordinates.CurrentX += box.ActualWordSpacing;

                    for (var wordIndex = 0; wordIndex < b.Words.Count; wordIndex++)
                    {
                        var word = b.Words[wordIndex];

                        // The walk order is fixed, so this ordinal names a position in it. Words below
                        // the resume point were placed in an earlier fragmentainer: their geometry and
                        // their line boxes already exist and this pass must not touch them.
                        var wordOrdinal = coordinates.WordOrdinal++;
                        if (wordOrdinal < coordinates.ResumeOrdinal) continue;

                        if (coordinates.MaxBottom - coordinates.CurrentY < box.ActualLineHeight)
                            coordinates.MaxBottom += box.ActualLineHeight - (coordinates.MaxBottom - coordinates.CurrentY);

                        var actualLimitRight = limitRight;
                        var lastRightIntersectingFloatBox = DomUtils.GetLastRightIntersectingFloatBox(box, coordinates);

                        if (lastRightIntersectingFloatBox is not null)
                        {
                            actualLimitRight = lastRightIntersectingFloatBox.Location.X -
                                               lastRightIntersectingFloatBox.ActualMarginLeft - rightSpacing;
                        }

                        // RTL reserves text-indent's line-start space on this (wrap-boundary) side instead
                        // of an added CurrentX offset - see this function's own `isRtl` comment.
                        if (isRtl)
                        {
                            actualLimitRight -= GetLineTextIndent(blockBox,
                                coordinates.Line.Equals(blockBox.LineBoxes[0]), coordinates.Line.FollowsForcedBreak);
                        }

                        var overflows = b.WhiteSpace.Value != Whitespace.NoWrap && b.WhiteSpace.Value != Whitespace.Pre
                                         && coordinates.CurrentX + word.Width + rightSpacing + clonedTrailing > actualLimitRight
                                         && (b.WhiteSpace.Value != Whitespace.PreWrap || !word.IsSpaces);

                        // hyphens:auto/manual: before giving up and wrapping the whole word, see if a
                        // cached candidate break point (from ParseToWords - either an explicit soft
                        // hyphen or an automatic HyphenationEngine suggestion) lets a hyphenated prefix
                        // fit in the space remaining on the current line instead - gated by
                        // hyphenate-limit-lines (no more than N consecutive hyphenated lines) and
                        // hyphenate-limit-zone (only bother when skipping the hyphen would otherwise
                        // leave more than the zone's worth of unfilled space on this line).
                        var availableWidth = actualLimitRight - coordinates.CurrentX - rightSpacing - clonedTrailing;

                        if (!word.SuppressWrapBefore && overflows && !word.IsLineBreak && !wrapNoWrapBox &&
                            word.HyphenationCandidates is { Count: > 0 } &&
                            IsWithinHyphenateLimitLines(blockBox, coordinates) &&
                            IsWithinHyphenateLimitZone(blockBox, availableWidth, actualLimitRight - lineStartX) &&
                            TryHyphenateWord(g, b, word, availableWidth, out var prefixWord, out var suffixWord))
                        {
                            b.Words[wordIndex] = prefixWord!;
                            b.Words.Insert(wordIndex + 1, suffixWord!);
                            word = prefixWord!;
                            overflows = false;
                            coordinates.CurrentLineHyphenated = true;
                        }

                        // A resumed flow's opening line is empty, so there is nothing to wrap away from;
                        // honouring the wrap would leave a blank line at the top of the fragmentainer.
                        var wrapping = !word.SuppressWrapBefore && (overflows || word.IsLineBreak || wrapNoWrapBox);

                        if (wrapping && coordinates is { SuppressLeadingWrap: true, Line.Words.Count: 0 })
                        {
                            wrapping = false;
                            wrapNoWrapBox = false;
                        }

                        coordinates.SuppressLeadingWrap = false;

                        if (wrapping)
                        {
                            // The line about to close is being abandoned for a new one - fold whether it
                            // ended in a hyphen into the running consecutive-hyphenated-lines count before
                            // that state resets for the line that's about to start.
                            coordinates.ConsecutiveHyphenatedLines =
                                coordinates.CurrentLineHyphenated ? coordinates.ConsecutiveHyphenatedLines + 1 : 0;
                            coordinates.CurrentLineHyphenated = false;

                            // b's content straddles the line-1/2 boundary: its words were measured
                            // using blockBox's first-line style (above), but word (and everything after
                            // it in b) is wrapping off line 1 right now, so it/they are no longer
                            // first-line content - correct their width back to b's own normal font/
                            // spacing before placing them on the new line. Must be read before
                            // coordinates.Line is reassigned just below.
                            if (coordinates.Line == blockBox.LineBoxes[0] && word.FirstLineStyle != null)
                            {
                                b.RemeasureWordsTail(g, wordIndex);
                            }

                            wrapNoWrapBox = false;
                            coordinates.CurrentX = lineStartX;
                            coordinates.CurrentY = coordinates.MaxBottom + lineSpacing;

                            lastLeftIntersectingFloatBox = DomUtils.GetLastLeftIntersectingFloatBox(b, coordinates);

                            if (lastLeftIntersectingFloatBox is not null)
                            {
                                coordinates.CurrentX = lastLeftIntersectingFloatBox.ActualRight + lastLeftIntersectingFloatBox.ActualMarginRight + leftSpacing;
                            }

                            coordinates.Line = new CssLineBox(blockBox) { FollowsForcedBreak = word.IsLineBreak };
                            coordinates.LineStartOrdinal = wordOrdinal;
                            coordinates.Line.StartOrdinal = wordOrdinal;

                            // Never the block's first formatted line (that's the seed line CreateLineBoxes
                            // creates) - only `each-line`/`hanging` (CSS Text 3 §3) can select this line.
                            // RTL reserves the same space on the opposite (wrap-boundary) side instead -
                            // see the seed line's own comment on `isRtl`.
                            if (!isRtl)
                            {
                                coordinates.CurrentX += GetLineTextIndent(blockBox, isFirstLine: false,
                                    coordinates.Line.FollowsForcedBreak);
                            }

                            if (word.IsImage || word.Equals(b.FirstWord))
                            {
                                // b's own box starts on this new line, so its full leading spacing applies
                                // however its decorations break; only its cloning ancestors re-open here.
                                coordinates.CurrentX += leftSpacing;

                                if (clonesDecorations)
                                    coordinates.CurrentX += DomUtils.ClonedInlineStart(b.ParentBox, blockBox);
                            }
                            else if (clonesDecorations)
                            {
                                // The break fell inside b: every cloning box it is part of, itself included,
                                // re-opens with its own leading border and padding on this line (§6.2).
                                coordinates.CurrentX += DomUtils.ClonedInlineStart(b, blockBox);
                            }
                        }
                        else if (coordinates.Line.Words.Count == 0)
                        {
                            // First word of the flow's opening line, which was created by the caller
                            // rather than by a wrap.
                            coordinates.LineStartOrdinal = wordOrdinal;
                            coordinates.Line.StartOrdinal = wordOrdinal;

                            // ...and on a resumed flow that word is where the fragment this pass opens
                            // begins, so it is where every cloning box the break fell inside re-opens
                            // with its own leading margin, border and padding (§6.2) - the same thing
                            // the wrap branch above does, for the same reason. The amount was
                            // accumulated on the way down, so it names exactly the boxes that
                            // straddle the break rather than every cloning ancestor.
                            if (wordOrdinal == coordinates.ResumeOrdinal)
                                coordinates.CurrentX += childClonedResumeStart;
                        }

                        coordinates.Line.ReportExistanceOf(word);

                        lastLeftIntersectingFloatBox = DomUtils.GetLastLeftIntersectingFloatBox(box, coordinates);

                        if (lastLeftIntersectingFloatBox is not null)
                        {
                            coordinates.CurrentX = lastLeftIntersectingFloatBox.ActualRight + lastLeftIntersectingFloatBox.ActualMarginRight + appliedLeftSpacing;
                        }

                        word.Left = coordinates.CurrentX;
                        word.Top = coordinates.CurrentY;

                        // A fixed box repeats at the same page-box position on every page (CSS 2.1
                        // §13.3.1), so a boundary means nothing to its words. A *table cell* used to be
                        // exempt here too, and that was a defect rather than a rule: a cell's own text
                        // then had no mechanism to stop a line straddling the boundary, so the emitter
                        // claimed that line in both bands and it painted sliced in half on each. The
                        // giveaway is that wrapping the identical text in a block inside the same cell
                        // was already correct - the nested box is not a cell, so it never took this arm.
                        //
                        // A null Fragmentainer means there is nothing here to ask (issue #400: "a
                        // measurement pass names no fragmentainer at all" - the question is meaningless
                        // there, not merely unanswerable), so this is simply skipped rather than falling
                        // back to a relocation - issue #333 retired the last caller of the pre-#321
                        // per-word CssRect.BreakPage mechanism that used to run here.
                        if (box is { IsFixed: false } && box.HtmlContainer?.SuppressWordPageBreaks != true
                            && coordinates.Fragmentainer is not null)
                        {
                            // The same question CssRect.BreakPage used to ask after the fact, answered
                            // here instead so this flow can stop before placing the word rather than
                            // relocating it and carrying on. The break is taken at the start of the line,
                            // not at this word: a line box is monolithic (css-break-3 §4.1), so the whole
                            // of it moves to the next fragmentainer rather than leaving its shorter words
                            // behind.
                            if (word.WouldStraddleFragmentainer())
                            {
                                // CompletedLineCount is filled in by CreateLineBoxes once the
                                // in-progress line has been discarded, since that is what fixes how
                                // many lines this fragmentainer actually kept.
                                coordinates.Break = new InlineBreakToken(
                                    blockBox,
                                    // Derived from where the break actually fell (the band this line
                                    // was trying to sit in), never assumed to be "the pass after this
                                    // one" - see ResumeSlotForBreakBefore's own remarks.
                                    word.ResumeSlotForBreakBefore(),
                                    [], coordinates.LineStartOrdinal, CompletedLineCount: 0,
                                    FollowsForcedBreak: coordinates.Line.FollowsForcedBreak,
                                    // The discarded line-in-progress never closed, so it hasn't been
                                    // folded in either way - this is exactly the run of already-closed
                                    // trailing hyphenated lines the resumed pass must keep counting from.
                                    ConsecutiveHyphenatedLines: coordinates.ConsecutiveHyphenatedLines);
                                return;
                            }

                            // A word too tall for any fragmentainer overflows the one it is in
                            // rather than breaking (css-break-3 §2) - so the words after it, on this
                            // same line or a later one, flow into whatever band follows without a
                            // break ever recording the crossing. Step the cursor there so any further
                            // fragmentation question this pass asks answers about the band flow has
                            // actually reached, not the one this overflowing word started in - #435.
                            if (word.OverflowsEveryFragmentainer())
                            {
                                coordinates.Fragmentainer.StepOverTo(box.HtmlContainer!.SlotEndingAt(word.Bottom));
                            }
                        }

                        coordinates.CurrentX = word.Left + word.FullWidth;

                        coordinates.MaxRight = Math.Max(coordinates.MaxRight, word.Right);
                        coordinates.MaxBottom = Math.Max(coordinates.MaxBottom, word.Bottom);

                        if (b.Position.Value != PositionMode.Absolute) continue;

                        word.Left += box.ActualMarginLeft;
                        word.Top += box.ActualMarginTop;
                    }

                    if (coordinates.Break is not null) return;

                    // A box holding its words directly (e.g. a ::before/::after pseudo-element,
                    // whose generated text lives on the box itself rather than an anonymous
                    // child) never goes through the FlowBox recursion below, so it gets its
                    // atomic-inline vertical insets (the bottomInset/MaxBottom bookkeeping - the
                    // topInset itself was already applied before this box's words above were placed)
                    // here. Skipped for the self-iteration case (boxes = [box], b == box): there the
                    // insets are applied by the PARENT's own recursion branch after this call returns -
                    // applying both would double the bottom inset.
                    //
                    // ...and neither does a box this pass placed nothing for: a box this pass placed
                    // none for has already had its bookkeeping done by the pass that placed it.
                    if (!ReferenceEquals(b, box) && coordinates.PlacedSince(childStartOrdinal))
                    {
                        ApplyAtomicInlineVerticalInsets(b, coordinates, atomicBottomInset);
                    }
                }
                else if (b.DerivedStyle.ActualDisplay == Keywords.InlineFlex)
                {
                    // Nothing at all for a box a resumed pass is only walking through. An inline-flex
                    // contributes no word ordinals, so nothing else here would notice that it belongs to
                    // an earlier fragmentainer - and this branch positions the box, re-runs the flex
                    // engine over its children and sets the cursor to the box's own right edge, so
                    // re-entering it moves an already-placed box onto this page and indents everything
                    // after it by its width. The dispatch stays here rather than falling through to the
                    // recursion below, which would walk the flex container's children as inline content
                    // and advance word ordinals this box never had on the pass that placed it - and the
                    // ordinals have to mean the same thing on every pass for any of these guards to work.
                    if (!childOpensHere) continue;

                    await FlowInlineFlexChild(g, b, coordinates);
                }
                else
                {
                    await FlowBox(g, blockBox, b, limitRight, lineSpacing, lineStartX, coordinates, childClonedResumeStart);
                    if (coordinates.Break is not null) return;

                    // Same as the branch above: an inset applies to the words this pass placed, and a
                    // box it placed none for has already had it.
                    if (coordinates.PlacedSince(childStartOrdinal))
                    {
                        ApplyAtomicInlineVerticalInsets(b, coordinates, atomicBottomInset);
                    }
                }

                // Undoes the pre-shift above for whatever comes after b on the same line: the shift must
                // only affect b's own content, never a sibling's. Left alone when b's own content wrapped
                // onto a new line internally (coordinates.Line changed) - that new CurrentY already comes
                // from the (correctly pre-shifted) MaxBottom the wrap computed it from, not from this
                // pre-shift, so there is nothing here to subtract back.
                if (preShiftedForAtomicInset && ReferenceEquals(lineBeforeChild, coordinates.Line))
                {
                    coordinates.CurrentY -= atomicTopInset;
                }

                // A box whose content was all placed in an earlier fragmentainer is not re-closed here
                // either: its trailing spacing was spent on the page it ended on, and charging it again
                // shifts everything after it to the right. A box straddling the break does end here,
                // and does get it.
                if (coordinates.PlacedSince(childStartOrdinal))
                {
                    coordinates.CurrentX += rightSpacing;
                }
            }

            FinalizeFlowBoxExit(box, blockBox, coordinates, opensHere, startOrdinal, startX, startY);
        }

        /// <summary>
        /// Undoes any hyphenation split (<see cref="TryHyphenateWord"/>) whose prefix ended up part of
        /// <paramref name="discardedLine"/> - a line <see cref="CreateLineBoxes"/> is throwing away
        /// because the flow it belongs to is resuming in the next fragmentainer. The split mutated its
        /// owning box's <see cref="CssBox.Words"/> list in place against this line's own (now
        /// irrelevant) remaining width, and a resumed pass re-walks that same list - so left alone, the
        /// split would survive unchanged into the resumed pass's fresh, undivided width, showing up as
        /// two words (with a needless hyphen) where the whole original word would have fit
        /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/344">#344</see>). Restoring the
        /// original word here instead lets the resumed pass decide afresh whether to hyphenate at all.
        /// </summary>
        /// <remarks>
        /// Only reachable for a prefix: the suffix half of a split that already wrapped onto its own,
        /// separately-kept line is never a member of <paramref name="discardedLine"/>'s own word list,
        /// so an ordinary hyphen straddling the fragmentainer break itself - suffix alone starting the
        /// next fragmentainer, prefix already committed to the one that just closed - is left untouched.
        /// </remarks>
        internal static void UndoAbandonedHyphenationSplits(CssLineBox discardedLine)
        {
            foreach (var word in discardedLine.Words)
            {
                if (word is not CssRectWord { PreSplitWord: { } original, HyphenationSuffix: { } suffix } prefix)
                    continue;

                TryRestoreHyphenationSplit(prefix, original, suffix);
            }
        }

        /// <summary>
        /// Merges a hyphenation split back into <paramref name="original"/>, in
        /// <paramref name="prefix"/>'s owner box's <see cref="CssBox.Words"/> list - the shared "undo"
        /// mechanics both <see cref="UndoAbandonedHyphenationSplits"/> and
        /// <see cref="EnforceHyphenateLimitLastBeforeBreak"/> need, differing only in which line they
        /// found the split's prefix on and why. Returns whether the merge happened -
        /// <paramref name="prefix"/> not being present in its own owner's word list is the one way it
        /// declines (should not happen given <see cref="TryHyphenateWord"/>'s own invariants, but callers
        /// hold a reference to <paramref name="prefix"/> from elsewhere and cannot assume it still does).
        /// </summary>
        private static bool TryRestoreHyphenationSplit(CssRectWord prefix, CssRect original, CssRectWord suffix)
        {
            var ownerWords = prefix.OwnerBox.Words;
            var prefixIndex = ownerWords.IndexOf(prefix);

            if (prefixIndex < 0) return false;

            ownerWords.Remove(suffix);
            ownerWords[prefixIndex] = original;

            // The restored word has never been positioned this pass, so its Top/Left are whatever
            // it last carried (nothing, for a word this split had never let reach placement) - not
            // the "unset" state FragmentEmitter's own AwaitsTheNextFragmentainer check relies on to
            // tell a genuinely-placed word from one whose position means nothing yet. Set explicitly
            // rather than left to default, since the loop that sets it for prefix/suffix's own
            // discarded-line siblings runs against the line's word list, which still names the
            // now-discarded prefix object, not this replacement.
            original.AwaitsTheNextFragmentainer = true;
            return true;
        }

        /// <summary>
        /// <c>hyphenate-limit-last</c> (CSS Text 4 §6.3.5, <c>none | always | column | page | spread</c>):
        /// undoes <paramref name="discardedLine"/>'s preceding line's trailing hyphenation split when the
        /// resolved value forbids hyphenating before this break, so the whole original word moves into
        /// the fragmentainer this break resumes in instead - the same "whole word moves on" outcome an
        /// ordinary overflowing (never-hyphenated) word already gets at a fragmentainer boundary.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Only reachable through the one shape <see cref="TryHyphenateWord"/> ever produces: a split's
        /// suffix always opens a fresh line of its own, so a hyphen a forbidding value would reject shows
        /// up as <paramref name="discardedLine"/>'s own first word carrying a
        /// <see cref="CssRectWord.HyphenationPrefix"/> link back to the prefix that ends the line
        /// <c>CreateLineBoxes</c> just kept - read off <c>blockBox.LineBoxes[^1]</c>, since the caller has
        /// already removed <paramref name="discardedLine"/> from that list.
        /// </para>
        /// <para>
        /// Never reached for the element's own true final line: a split's suffix always claims that
        /// position instead of the hyphenated prefix (it either fits right after the prefix - not a
        /// hyphenated line end at all - or opens a new line, which becomes the element's new final line),
        /// so <c>always</c>'s "last full line of the element" clause needs no separate handling here -
        /// only its "last line before a break" clause does.
        /// </para>
        /// <para>
        /// PeachPDF has no facing-page "spread" concept (css-text-4 borrows the term from paged media this
        /// engine does not model), so <c>spread</c> is treated the same as <c>page</c>.
        /// </para>
        /// </remarks>
        internal static InlineBreakToken EnforceHyphenateLimitLastBeforeBreak(
            CssBox blockBox, CssLineBox discardedLine, FragmentainerContext? context, InlineBreakToken stopped,
            int completedLines)
        {
            var limit = blockBox.HyphenateLimitLast.Value;

            if (discardedLine.Words is not [CssRectWord { HyphenationPrefix: { } prefix }, ..])
                return stopped;

            if (prefix is not { PreSplitWord: { } original, HyphenationSuffix: { } suffix })
                return stopped;

            var isColumnBreak = context?.HasOwnBand ?? false;

            // None is handled explicitly here rather than filtered out beforehand, so this switch is the
            // only place that decides what each value forbids. The `_` arm is unreachable for any value
            // the cascade can actually produce (HyphenateLimitLast has exactly these five members) - kept
            // only because the compiler cannot see that guarantee and requires the switch expression to
            // be exhaustive against the underlying byte's full range.
            var forbidden = limit switch
            {
                HyphenateLimitLast.None => false,
                HyphenateLimitLast.Always => true,
                HyphenateLimitLast.Column => isColumnBreak,
                HyphenateLimitLast.Page or HyphenateLimitLast.Spread => !isColumnBreak,
                _ => false
            };

            if (!forbidden) return stopped;

            // keptLine must be a line THIS pass produced. When the current pass's own seed line (the one
            // that resumed a split's suffix) is itself what just got discarded, blockBox.LineBoxes[^1] is
            // a line an EARLIER pass already finalized - possibly already emitted into a frozen fragment.
            // TakeEarlyBreak and TryRewindForWidows both call HtmlContainerInt.InvalidateEmittedFragmentsFor
            // before touching geometry that old; this is a synchronous, same-pass-only fixup with no such
            // machinery, so it declines instead of mutating a line paint may never re-read. blockBox.
            // DiscardLineBoxesFrom(completedLines) at the top of CreateLineBoxes guarantees indices below
            // completedLines all predate this pass, which is also what makes an empty LineBoxes list (no
            // kept line exists at all) fall out of the same check, since -1 < completedLines always.
            var keptLineIndex = blockBox.LineBoxes.Count - 1;
            if (keptLineIndex < completedLines) return stopped;

            var keptLine = blockBox.LineBoxes[keptLineIndex];

            // If the prefix is its line's only word, that line was already a fresh, full-width one - the
            // same width the next fragmentainer's opening line will offer. Undoing the split would only
            // recreate an identical too-narrow-for-the-whole-word line there, which hyphenates again,
            // whose prefix is again its line's only word, forever - a livelock this measurably produced
            // (100,000+ empty pages before the fragmentainer-pass cap forced a monolithic fallback) before
            // this guard existed. Declining leaves the hyphen in place, which is the CSS-conformant
            // fallback anyway: a value this engine cannot honor at all is better given up than looped on
            // (the same reasoning `orphans`/`widows` already applies when nothing above a box in its
            // fragmentainer means moving it cannot help either).
            if (keptLine.Words.Count <= 1) return stopped;

            if (!keptLine.Words.Remove(prefix)) return stopped;

            if (!TryRestoreHyphenationSplit(prefix, original, suffix))
            {
                // Should not happen (the prefix that just came off keptLine has to still be in its own
                // owner box's word list), but restoring what was removed leaves nothing worse off than
                // declining the whole rewrite would have.
                keptLine.Words.Add(prefix);
                return stopped;
            }

            // One lower than the suffix's own ordinal (what stopped.ResumeWordIndex already names) -
            // the resumed pass re-walks the owner list from scratch, now one entry shorter, so the
            // restored whole word lands exactly where the prefix - not the suffix - was counted.
            //
            // keptLine is exactly the line stopped.ConsecutiveHyphenatedLines counted as the run's most
            // recent member when it closed (hyphenate-limit-lines folds a line's hyphenation state into
            // the running count the moment it closes, before this pass ever reaches the break that lands
            // it here). Un-hyphenating it now breaks that run wherever it stood - a trailing run whose
            // last member no longer ends in a hyphen isn't a shorter run, it's no run at all - so the
            // resumed pass must start back at 0 rather than inherit a count this undo has just made stale.
            return stopped with { ResumeWordIndex = stopped.ResumeWordIndex - 1, ConsecutiveHyphenatedLines = 0 };
        }

        /// <summary>
        /// <c>hyphenate-limit-lines</c> (CSS Text 4 §6.3.5): whether the line about to be built is still
        /// allowed to end in a hyphen, given how many immediately preceding lines already did.
        /// </summary>
        private static bool IsWithinHyphenateLimitLines(CssBox blockBox, CssLineBoxCoordinates coordinates)
        {
            var limit = blockBox.HyphenateLimitLines.Value;
            return !limit.IsValue || coordinates.ConsecutiveHyphenatedLines < limit.Value!.Value;
        }

        /// <summary>
        /// <c>hyphenate-limit-zone</c> (CSS Text 4 §6.3.3): whether the space this line would otherwise
        /// leave unfilled - <paramref name="availableWidth"/>, the same remaining width the hyphenation
        /// attempt itself measures a candidate prefix against - is worth spending a hyphen to reclaim.
        /// The initial value 0 makes this always true, preserving the engine's pre-existing behavior of
        /// attempting a hyphenated break whenever there is a candidate and any overflow at all.
        /// </summary>
        private static bool IsWithinHyphenateLimitZone(CssBox blockBox, double availableWidth, double lineBoxLength)
        {
            var declared = blockBox.HyphenateLimitZone;
            if (string.IsNullOrEmpty(declared) || declared == "0") return true;

            var zone = CssValueParser.ParseLength(declared, lineBoxLength, blockBox);
            return availableWidth > zone;
        }

        /// <summary>
        /// Tries to split <paramref name="word"/> at the widest of its precomputed
        /// <see cref="CssRect.HyphenationCandidates"/> (set by <see cref="CssBox.ParseToWords"/> — either
        /// an explicit soft hyphen position or an automatic <c>HyphenationEngine</c> suggestion) whose
        /// hyphenated prefix (with a trailing <c>hyphenate-character</c> glyph, actually measured) still
        /// fits in <paramref name="availableWidth"/> and satisfies <c>hyphenate-limit-chars</c>'s word/
        /// before/after minimums. Candidates are tried from the last (rightmost, keeping the most text on
        /// the current line) to the first, so the result is the longest prefix that fits — not just the
        /// first candidate found. Returns false (leaving <paramref name="prefix"/>/<paramref name="suffix"/>
        /// null) if no candidate fits, in which case the caller falls back to wrapping the whole word as
        /// before.
        /// </summary>
        private static bool TryHyphenateWord(RGraphics g, CssBox b, CssRect word, double availableWidth, out CssRectWord? prefix, out CssRectWord? suffix)
        {
            prefix = null;
            suffix = null;

            var text = word.Text;
            var candidates = word.HyphenationCandidates;
            if (string.IsNullOrEmpty(text) || candidates is not { Count: > 0 })
                return false;

            // The bool result is intentionally unchecked: on a genuinely malformed stored value (never
            // produced by the validated cascade) TryParse already resets all three out params to null -
            // "no constraint", the same safe default as an explicit "auto" - so there is no distinct
            // failure behavior to branch on.
            HyphenateLimitCharsGrammar.TryParse(b.HyphenateLimitChars, out var wordMin, out var beforeMin, out var afterMin);
            if (wordMin.HasValue && text.Length < wordMin.Value)
                return false;

            var hyphenCharacter = ResolveHyphenateCharacter(b);

            for (var i = candidates.Count - 1; i >= 0; i--)
            {
                var breakAt = candidates[i];
                if (breakAt <= 0 || breakAt >= text.Length) continue;
                if (beforeMin.HasValue && breakAt < beforeMin.Value) continue;
                if (afterMin.HasValue && text.Length - breakAt < afterMin.Value) continue;

                var prefixText = text[..breakAt] + hyphenCharacter;
                var prefixWidth = g.MeasureString(prefixText, b.ActualFont, b.ActualTextShapingFeatures).Width;
                if (prefixWidth > availableWidth) continue;

                var suffixText = text[breakAt..];
                prefix = new CssRectWord(b, prefixText, word.HasSpaceBefore, false)
                {
                    Width = prefixWidth,
                    Height = b.ActualFont.Height
                };
                suffix = new CssRectWord(b, suffixText, false, word.HasSpaceAfter)
                {
                    Width = g.MeasureString(suffixText, b.ActualFont, b.ActualTextShapingFeatures).Width,
                    Height = b.ActualFont.Height
                };

                // Recorded so a discarded fragmentainer line can undo this split rather than carry it
                // into the resumed pass unchanged - see CssRectWord.HyphenationSuffix.
                prefix.PreSplitWord = word;
                prefix.HyphenationSuffix = suffix;
                suffix.HyphenationPrefix = prefix;

                return true;
            }

            return false;
        }

        /// <summary>
        /// Resolves <c>hyphenate-character</c> (CSS Text 4 §6.3.1) to the literal glyph(s)
        /// <see cref="TryHyphenateWord"/> appends to a hyphenated prefix. <c>b.HyphenateCharacter</c>
        /// stores the raw CSS-OM serialization of the declared value (the <c>list-style-type</c> <c>&lt;string&gt;</c>
        /// convention — a string value still carries its quotes), so <c>auto</c> is the literal text
        /// "auto" here, not a resolved character. There is no per-script "typographic convention" table
        /// behind <c>auto</c> — it always resolves to the U+002D HYPHEN-MINUS this engine has always
        /// hardcoded (see the accepted-gaps note); an explicit empty string is honored as specified
        /// (CSS Text 4 §6.3.1's note), breaking the line with no visible hyphen glyph at all.
        /// </summary>
        private static string ResolveHyphenateCharacter(CssBox b)
        {
            var declared = b.HyphenateCharacter;
            if (string.IsNullOrEmpty(declared) || declared.Equals(Keywords.Auto, StringComparison.OrdinalIgnoreCase))
                return "-";

            // GetFontFaceFamilyName is the existing "unwrap a stored CSS-OM <string> literal" helper
            // (the same list-style-type/font-family convention this property's own storage follows) -
            // its only possible no-match fallback here is Auto, already handled above, since Layer A
            // validation guarantees HyphenateCharacter is always either "auto" or a real string token.
            return CssValueParser.GetFontFaceFamilyName(declared);
        }

        /// <summary>
        /// CSS2.1 §8.1 box model: an atomic inline-level box's content is laid out inside its
        /// padding box, so its flowed words must sit border+padding-top BELOW the box's top
        /// edge. <see cref="FlowBox"/>'s per-child loop now applies that inset by pre-shifting
        /// <c>coordinates.CurrentY</c> before b's content is flowed (issue #333), so by the time
        /// this runs every word b has flowed already sits at its correct final <see cref="CssRect.Top"/> -
        /// what remains is purely bookkeeping. Without growing <see cref="CssLineBoxCoordinates.MaxBottom"/>
        /// by the bottom inset, <see cref="CssLineBox.UpdateRectangle"/>'s padding expansion would close
        /// the box's background/border rect at the last word's own bottom rather than below it (a
        /// button's label would hug its bottom border). Plain <c>display: inline</c> boxes are excluded
        /// per §10.8.1 by the caller (their vertical padding paints without taking vertical space, which
        /// the existing rect expansion alone already models correctly) - <paramref name="bottomInset"/>
        /// is precomputed as 0 for them. An absolutely/fixed-positioned box still gets its own insets
        /// (its content sits inside its own padding box regardless) but never grows the in-flow
        /// <see cref="CssLineBoxCoordinates.MaxBottom"/> - out-of-flow boxes must not affect ancestor flow
        /// height (CSS2.1 §9.6).
        /// </summary>
        /// <param name="b">the just-flowed inline-level box</param>
        /// <param name="coordinates">the current line coordinates</param>
        /// <param name="bottomInset">b's own border-bottom+padding-bottom, precomputed by the caller (0
        /// when b is not an atomic inline-level box)</param>
        private static void ApplyAtomicInlineVerticalInsets(CssBox b, CssLineBoxCoordinates coordinates, double bottomInset)
        {
            if (bottomInset <= 0 || b.Position.Value is PositionMode.Absolute or PositionMode.Fixed)
                return;

            var maxWordBottom = MaxFlowedWordBottom(b);

            if (maxWordBottom > double.MinValue)
            {
                coordinates.MaxBottom = Math.Max(coordinates.MaxBottom, maxWordBottom + bottomInset);
            }
        }

        /// <summary>
        /// The deepest bottom edge among every word already flowed inside <paramref name="box"/>'s
        /// subtree, or <see cref="double.MinValue"/> when it holds none. A read-only walk - unlike the
        /// shifting/relocating walk it replaced (issue #333), every word already sits at its correct
        /// <see cref="CssRect.Top"/> by the time <see cref="ApplyAtomicInlineVerticalInsets"/> runs.
        /// </summary>
        private static double MaxFlowedWordBottom(CssBox box)
        {
            var maxBottom = double.MinValue;

            foreach (var word in box.Words)
            {
                maxBottom = Math.Max(maxBottom, word.Bottom);
            }

            foreach (var child in box.Boxes)
            {
                maxBottom = Math.Max(maxBottom, MaxFlowedWordBottom(child));
            }

            return maxBottom;
        }

        /// <summary>
        /// Recursively creates the rectangles of the blockBox, by bubbling from deep to outside the boxes
        /// in the rectangle structure
        /// </summary>
        private static void BubbleRectangles(CssBox box, CssLineBox line)
        {
            if (box.Words.Count > 0)
            {
                double x = float.MaxValue, y = float.MaxValue, r = float.MinValue, b = float.MinValue;
                var words = line.WordsOf(box);

                if (words.Count <= 0) return;

                foreach (var word in words)
                {
                    // handle if line is wrapped for the first text element where parent has left margin\padding
                    //
                    // This exists for the one case FirstHostingLineBox cannot express: a parent entered
                    // on one line whose first word wraps to the next, so the leading spacing was applied
                    // on a line the parent does not really start on. Not when the parent genuinely opens
                    // on this line - there the spacing is real, it was applied to the words, and
                    // UpdateRectangle takes it off the rectangle itself. Doing both takes it off twice,
                    // which is what a resumed pass hit: its opening line is never LineBoxes[0], since
                    // earlier fragmentainers' lines are kept.
                    var left = word.Left;

                    if (box == box.ParentBox!.Boxes[0] && word == box.Words[0] && word == line.Words[0] && line != line.OwnerBox.LineBoxes[0] && !word.IsLineBreak
                        && !ReferenceEquals(box.ParentBox.FirstHostingLineBox, line))
                        left -= box.ParentBox.ActualMarginLeft + box.ParentBox.ActualBorderLeftWidth + box.ParentBox.ActualPaddingLeft;


                    x = Math.Min(x, left);
                    r = Math.Max(r, word.Right);
                    y = Math.Min(y, word.Top);
                    b = Math.Max(b, word.Bottom);
                }

                line.UpdateRectangle(box, x, y, r, b);
            }
            else
            {
                foreach (var b in box.Boxes)
                {
                    BubbleRectangles(b, line);
                }
            }
        }

        /// <summary>
        /// Applies vertical and horizontal alignment to words in line-boxes
        /// </summary>
        /// <param name="lineBox">the line to align</param>
        /// <param name="blockFinished">
        /// whether the flow reached the end of the owning block's content — see
        /// <see cref="FinalizeLineBoxes"/>, whose parameter this is.
        /// </param>
        private static void ApplyHorizontalAlignment(CssLineBox lineBox, bool blockFinished)
        {
            // text-align's initial/logical values, start/end (CSS Text 3 §7.1), resolve against the
            // owning box's own direction - the CSS-OM-visible value (box.TextAlign) stays exactly as
            // authored/defaulted; only this *used*-value resolution is direction-aware.
            var isRtl = lineBox.OwnerBox.Direction.Value == DirectionMode.Rtl;
            var textAlign = lineBox.OwnerBox.TextAlign.Value switch
            {
                HorizontalAlignment.Start => isRtl ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                HorizontalAlignment.End => isRtl ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                var other => other
            };

            switch (textAlign)
            {
                case HorizontalAlignment.Right:
                    ApplyRightAlignment(lineBox);
                    break;
                case HorizontalAlignment.Center:
                    ApplyCenterAlignment(lineBox);
                    break;
                case HorizontalAlignment.Justify:
                    ApplyJustifyAlignment(lineBox, blockFinished);
                    break;
            }
        }

        /// <summary>
        /// UAX#9 L2 (visual reordering) + L4 (mirroring), applied at word granularity - each word is one
        /// homogeneous-level unit by construction (<see cref="CssBox.ParseToWords"/> splits at a bidi
        /// level boundary the same way it already splits at whitespace/hyphen/CJK boundaries, see
        /// <see cref="CssBox.BidiLevels"/>). Replaces the old whole-word-only mirroring
        /// (<c>ApplyRightToLeft</c>/<c>ApplyRightToLeftOnLine</c>/<c>ApplyRightToLeftOnSingleBox</c>),
        /// which only ever repositioned whole words and never reordered or mirrored their own characters.
        /// </summary>
        /// <remarks>
        /// Repositions one run at a time (a run is a maximal same-level span - the common case is one run
        /// spanning the whole line) by <b>reflecting it about its own span</b> rather than reusing the
        /// <c>Left</c> "slots" <see cref="ApplyHorizontalAlignment"/> already assigned in logical order:
        /// an earlier version reused those slots positionally (the word that used to occupy slot <c>i</c>
        /// handed its <c>Left</c> to whichever word the reordered sequence put there), which corrupted
        /// layout as soon as two words in a run had different widths - a slot sized for a narrow word
        /// (say, a lone comma) now had to hold a much wider one, overlapping its neighbor. Reflection
        /// avoids this because it only ever repositions a word using <i>its own</i> width and <i>its
        /// own</i> original offset from the run's start - <c>newLeft = runNewStart + runWidth -
        /// (offsetFromRunStart + word.Width)</c> for an RTL run, or a straight carry-over of
        /// <c>offsetFromRunStart</c> for an LTR one - so every gap the original logical-order layout
        /// already established between adjacent words (plain inter-word spacing, justify's extra
        /// per-word budget, whatever it is) reappears at the mirrored position instead of being
        /// recomputed, and a line with no actual reordering to do (a plain LTR line, one run spanning the
        /// whole line) is a true no-op - confirmed by the early return below, not just by coincidence.
        /// </remarks>
        /// <param name="lineBox">the line to reorder</param>
        private static void ApplyBidiReordering(CssLineBox lineBox)
        {
            if (lineBox.Words.Count == 0) return;

            var levels = new byte[lineBox.Words.Count];
            var slots = new double[lineBox.Words.Count];
            for (var i = 0; i < levels.Length; i++)
            {
                levels[i] = lineBox.Words[i].BidiLevel;
                slots[i] = lineBox.Words[i].Left;
            }

            // L1 clause 4: a trailing run of whitespace/line-break words at the very end of the line
            // resets to the paragraph's base level, so trailing space never visually detaches from the
            // line's dominant direction - CssBidiParagraphResolver.ResolveParagraph already applies
            // clauses 1-3 (which don't depend on where a line actually breaks), leaving this one for
            // here, where the line's own end is finally known.
            var baseLevel = lineBox.OwnerBox.Direction.Value == DirectionMode.Rtl ? (byte)1 : (byte)0;
            var j = levels.Length - 1;
            while (j >= 0 && lineBox.Words[j].IsSpaces)
            {
                levels[j] = baseLevel;
                j--;
            }

            var runs = BidiResolver.ReorderLine(levels, 0, levels.Length);

            if (runs.Count == 1 && !runs[0].IsRtl) return;

            // The original Left of the word right after `index` in document order - or, for the line's
            // very last word, its own spacing-inclusive right edge, since there is no following slot to
            // read a boundary from. This is the boundary between whatever run `index` belongs to and
            // whatever comes next - never reflected, since it's spacing between two runs, not content
            // that moves when one of them does.
            double SlotBoundaryAfter(int index) =>
                index + 1 < lineBox.Words.Count
                    ? slots[index + 1]
                    : slots[index] + lineBox.Words[index].FullWidth;

            var runNewStart = slots[0];

            foreach (var run in runs)
            {
                // BidiRun.Start/Length here index into `levels`/lineBox.Words (word ordinals, one
                // homogeneous-level unit each) rather than characters - ReorderLine only ever compares
                // and copies the levels array, so it works unchanged at this granularity. `runs` is
                // already in final visual (left-to-right) order, so each run's new span simply follows
                // the previous one's.
                var runOldStart = slots[run.Start];
                var lastIndexInRun = run.Start + run.Length - 1;

                // The run's own content span - first word's Left to last word's own Right, i.e. every
                // word's width and every *internal* gap between this run's own words, but NOT the
                // trailing gap to whatever comes after the run. That gap is spacing *between* runs, not
                // part of either one's content, so it must never take part in the reflection below - an
                // earlier version folded it into the run's own width, which reflected it onto the run's
                // *leading* edge instead of leaving it trailing, doubling up the gap on one side of an
                // RTL run and erasing it on the other.
                var runContentWidth = slots[lastIndexInRun] + lineBox.Words[lastIndexInRun].Width - runOldStart;
                var trailingGap = SlotBoundaryAfter(lastIndexInRun) - (runOldStart + runContentWidth);

                // Within an RTL run both the word order and each word's own text reverse (mirroring
                // characters where they have a mirror image), matching how BidiResolver's own
                // conformance reconstruction walks a character-granularity RTL run backwards.
                for (var k = 0; k < run.Length; k++)
                {
                    var idx = run.Start + k;
                    var word = lineBox.Words[idx];
                    var offsetFromRunStart = slots[idx] - runOldStart;

                    var newLeft = run.IsRtl
                        ? runNewStart + runContentWidth - (offsetFromRunStart + word.Width)
                        : runNewStart + offsetFromRunStart;

                    PlaceBidiRunWord(word, newLeft, run.Level, mirror: run.IsRtl);
                }

                runNewStart += runContentWidth + trailingGap;
            }
        }

        private static void PlaceBidiRunWord(CssRect word, double slotLeft, byte level, bool mirror)
        {
            if (mirror && word is CssRectWord { IsSpaces: false, IsLineBreak: false } rectWord)
            {
                // Mirrors from the word's own stable pre-mirror text, not its current (possibly already
                // mirrored) Text - mirroring is an involution, so a box tree laid out more than once
                // against the same word objects (HtmlContainerInt's variable-page-width reflow re-runs
                // LayoutDocument, re-deriving line boxes and re-applying this on every pass) would
                // otherwise toggle back to unmirrored on every second application.
                rectWord.ReplaceText(BidiMirrorResolver.ApplyMirroring(rectWord.PreMirrorText, level));
            }

            word.Left = slotLeft;
        }

        /// <summary>
        /// Applies vertical alignment to the linebox
        /// </summary>
        /// <param name="lineBox"></param>
        private static void ApplyVerticalAlignment(CssLineBox lineBox)
        {
            var baseline = double.MinValue;

            foreach (var box in lineBox.Rectangles.Keys)
            {
                baseline = Math.Max(baseline, lineBox.Rectangles[box].Top);
            }

            // A ::first-line rule's vertical-align (if it sets one) applies to everything on the
            // target's first formatted line, overriding each box's own value - ::first-line has no
            // synthesized box of its own to carry a per-box override, and unlike font/color/spacing
            // (resolved per-word in FlowBox/PaintWords), vertical-align is inherently a whole-line
            // concept already (this method itself runs once per line), so the simplest, most direct
            // application is a single line-wide override rather than per-word plumbing.
            //
            // ResolvedFirstLineStyle.VerticalAlign is NOT a reliable "did some ::first-line rule
            // actually declare vertical-align" signal by itself: vertical-align is CSS-spec
            // Inherited: no, so InheritStyle no longer carries it onto the shadow box - instead,
            // DomParser.ResolveFirstLineStyle explicitly re-seeds shadowBox.VerticalAlign from the
            // owner box's own resolved value right after InheritStyle runs, specifically so the shadow
            // box starts out matching the block's own value even when no matched rule ever mentions
            // vertical-align. Comparing against the block's own value is a cheap, good-enough proxy for
            // "was this actually declared" (it only under-detects the harmless edge case of a rule
            // re-declaring the same value the block already had).
            var ownerBox = lineBox.OwnerBox;
            var firstLineVerticalAlign = lineBox == ownerBox.LineBoxes.FirstOrDefault()
                                         && ownerBox.ResolvedFirstLineStyle is { } firstLineStyle
                                         && firstLineStyle.VerticalAlign != ownerBox.VerticalAlign
                ? firstLineStyle.VerticalAlign
                : null;

            var boxes = new List<CssBox>(lineBox.Rectangles.Keys);

            // Snapshot the line's own top/bottom extents up front, from the original (pre-alignment)
            // rectangles - same convention this method already uses for "baseline" above, so every
            // case below aligns against the line's original geometry rather than a value some earlier
            // box in this loop already shifted.
            var lineTop = double.MaxValue;
            var lineBottom = double.MinValue;
            foreach (var box in boxes)
            {
                var r = lineBox.Rectangles[box];
                lineTop = Math.Min(lineTop, r.Top);
                lineBottom = Math.Max(lineBottom, r.Bottom);
            }

            foreach (var box in boxes)
            {
                var rect = lineBox.Rectangles[box];
                // "box" here may be an anonymous text-node box rather than the real element
                // vertical-align was declared on (e.g. for "<span style='vertical-align:top'>text
                // </span>", the word's owner is an anonymous child of the span, which never goes
                // through its own CSS cascade and so never gets its own vertical-align declaration -
                // since vertical-align is CSS-spec Inherited: no (CSS 2.1 §10.8.1), an anonymous box's
                // own VerticalAlign always stays at the initial "baseline" regardless of what its
                // originating element declared. Walk up to the nearest ancestor that has an HtmlTag
                // (the same walk TextTop/TextBottom below already needs, for the same reason) to read
                // the value the author actually declared.
                var styledBoxForVerticalAlign = box;
                while (styledBoxForVerticalAlign.HtmlTag is null && styledBoxForVerticalAlign.ParentBox is not null)
                    styledBoxForVerticalAlign = styledBoxForVerticalAlign.ParentBox;
                var effectiveVerticalAlign = firstLineVerticalAlign ?? styledBoxForVerticalAlign.VerticalAlign;

                // A length/percentage (CSS 2.1 §10.8.1, issue #603) raises (positive) or lowers
                // (negative) the box by this distance from its own baseline - a percentage resolves
                // against the styled box's own line-height, mirroring sub/super's own baseline-relative
                // offset below rather than any of the line-extent-relative cases.
                if (effectiveVerticalAlign.Value is { IsValue: true, Value: { } lengthOrCalc })
                {
                    var offset = CssValueParser.ParseLength(lengthOrCalc, styledBoxForVerticalAlign.ActualLineHeight, styledBoxForVerticalAlign);
                    lineBox.SetBaseLine(box, baseline - offset);
                    continue;
                }

                //Important notes on http://www.w3.org/TR/CSS21/tables.html#height-layout
                switch (effectiveVerticalAlign.Value.Keyword)
                {
                    case VerticalAlignment.Sub:
                        lineBox.SetBaseLine(box, baseline + rect.Height * .5f);
                        break;
                    case VerticalAlignment.Super:
                        lineBox.SetBaseLine(box, baseline - rect.Height * .2f);
                        break;
                    case VerticalAlignment.Top:
                        OffsetBoxWithinLine(lineBox, box, lineTop - rect.Top);
                        break;
                    case VerticalAlignment.Bottom:
                        OffsetBoxWithinLine(lineBox, box, lineBottom - rect.Bottom);
                        break;
                    case VerticalAlignment.Middle:
                        var lineMiddleTop = lineTop + (lineBottom - lineTop - rect.Height) / 2;
                        OffsetBoxWithinLine(lineBox, box, lineMiddleTop - rect.Top);
                        break;
                    case VerticalAlignment.TextTop:
                    case VerticalAlignment.TextBottom:
                        // Align with the top/bottom of the parent's font box, per CSS1 §5.6.11 - not
                        // the line's own extents (that's top/bottom above), so this references the
                        // parent element's own ActualFont rather than lineTop/lineBottom. Reuses the
                        // same tagged-ancestor walk computed above for effectiveVerticalAlign.
                        var styledBox = styledBoxForVerticalAlign;
                        var referenceFont = (styledBox.ParentBox ?? styledBox).ActualFont;
                        var fontTop = baseline - referenceFont.Ascent;
                        var target = effectiveVerticalAlign.Value.Keyword == VerticalAlignment.TextTop
                            ? fontTop
                            : fontTop + referenceFont.Height - rect.Height;
                        OffsetBoxWithinLine(lineBox, box, target - rect.Top);
                        break;
                    default:
                        // baseline, and PeachBaselineMiddle (the deprecated img align=middle sentinel -
                        // see Keywords.PeachBaselineMiddle - has no distinct inline-layout effect,
                        // matching its pre-existing behavior before this typed-storage conversion).
                        lineBox.SetBaseLine(box, baseline);
                        break;
                }
            }
        }

        /// <summary>
        /// Shifts a single box's rectangle and words within one specific line box by <paramref name="delta"/>.
        /// Scoped to this <paramref name="lineBox"/> only (via <see cref="CssLineBox.WordsOf"/>/
        /// <see cref="CssLineBox.Rectangles"/>) rather than <see cref="CssBox.OffsetTop(double)"/>'s all-lines,
        /// all-descendants offset - necessary since a single inline box can participate in multiple
        /// line boxes (when its content wraps), each needing independent vertical alignment.
        /// </summary>
        private static void OffsetBoxWithinLine(CssLineBox lineBox, CssBox box, double delta)
        {
            if (delta == 0) return;

            if (lineBox.Rectangles.TryGetValue(box, out var r))
                lineBox.Rectangles[box] = new RRect(r.X, r.Y + delta, r.Width, r.Height);

            // An image word's own Rectangle (not lineBox.Rectangles/box.Location) is exactly what
            // CssBoxImage/CssBoxObject.PaintImpCore reads to position the drawn image
            // ("var r = _imageWord.Rectangle; ...; g.DrawImage(_imageWord.Image, r)") - so it must move
            // with everything else here. Previously excluded, which made `vertical-align: top/bottom/
            // middle` a complete no-op for inline replaced elements (e.g. an <object>/<img>): the word
            // kept its original flow-assigned Top forever, regardless of the declared alignment.
            foreach (var word in lineBox.WordsOf(box))
            {
                word.Top += delta;
            }
        }

        /// <summary>
        /// Resolves the used <c>text-indent</c> for one line (CSS Text 3 §3): the plain
        /// <c>&lt;length-percentage&gt;</c> applies to the block's first formatted line only; <c>each-line</c>
        /// additionally applies it to the line following every forced break; <c>hanging</c> inverts the
        /// selection (every line *except* the ones above). <paramref name="isFirstLine"/> is the block's true
        /// first formatted line, not merely the first line of a page fragment - a resumed fragment's opening
        /// line passes <c>false</c> here, so under <c>hanging</c> it is (correctly) still indented, being a
        /// non-first line. Its <paramref name="followsForcedBreak"/> is carried over from the line-in-progress
        /// the break discarded (<see cref="Fragmentation.InlineBreakToken.FollowsForcedBreak"/>) rather than
        /// assumed false, so <c>each-line</c> still recognizes a resumed line that follows a forced break in
        /// the source.
        /// </summary>
        private static double GetLineTextIndent(CssBox blockBox, bool isFirstLine, bool followsForcedBreak)
        {
            var selected = isFirstLine || (blockBox.ActualTextIndentEachLine && followsForcedBreak);
            return (blockBox.ActualTextIndentHanging ? !selected : selected) ? blockBox.ActualTextIndent : 0d;
        }

        /// <summary>
        /// Spreads the words of <paramref name="lineBox"/> to fill its measure, per
        /// <see href="https://www.w3.org/TR/css-text-3/#text-align-property">CSS Text §7.3</see>'s
        /// <c>justify</c>.
        /// </summary>
        /// <param name="lineBox">the line to justify</param>
        /// <param name="blockFinished">
        /// whether the flow reached the end of the owning block's content, which is what decides whether
        /// the last line in the list is §7.3's exempt <i>last line of the block</i> — see
        /// <see cref="FinalizeLineBoxes"/>.
        /// </param>
        private static void ApplyJustifyAlignment(CssLineBox lineBox, bool blockFinished)
        {
            // The block's last line is exempt (CSS Text §7.3) - but only a block whose flow actually
            // finished has one. A pass that stopped at a fragmentation break leaves the line it stopped on
            // at the end of the list without it being the end of the block.
            if (blockFinished && lineBox.Equals(lineBox.OwnerBox.LineBoxes[^1]))
                return;

            var indent = GetLineTextIndent(lineBox.OwnerBox, lineBox.Equals(lineBox.OwnerBox.LineBoxes[0]),
                lineBox.FollowsForcedBreak);
            var textSum = 0d;
            var words = 0d;
            var availWidth = lineBox.OwnerBox.ClientRectangle.Width - indent;

            // Gather text sum
            foreach (var w in lineBox.Words)
            {
                textSum += w.Width;
                words += 1d;
            }

            if (words <= 0d)
                return; //Avoid Zero division

            var spacing = (availWidth - textSum) / words; //Spacing that will be used

            // text-indent belongs on the line-start side (CSS Text 3 §3), which is the physical right
            // under RTL - so the indent moves from the leading `currentX` offset to the trailing forced-
            // flush edge instead (mirroring ApplyRightAlignment's own direction handling below, including
            // why FlowBox's RTL wrap-boundary narrowing has to agree with this: the words this line holds
            // were already wrapped assuming the reduced measure `availWidth` computes above). This
            // runs before ApplyBidiReordering, which only reflects positions within the span these two
            // edges bound - it never moves the span itself - so which edge carries the indent here is
            // what decides which physical side it ends up on after mirroring.
            var isRtl = lineBox.OwnerBox.Direction.Value == DirectionMode.Rtl;
            var currentX = lineBox.OwnerBox.ClientLeft + (isRtl ? 0 : indent);

            foreach (var word in lineBox.Words)
            {
                word.Left = currentX;
                currentX = word.Right + spacing;

                if (word == lineBox.Words[^1])
                {
                    word.Left = lineBox.OwnerBox.ClientRight - word.Width - (isRtl ? indent : 0);
                }
            }
        }

        /// <summary>
        /// Applies centered alignment to the text on the line-box
        /// </summary>
        /// <param name="line"></param>
        private static void ApplyCenterAlignment(CssLineBox line)
        {
            if (line.Words.Count == 0)
                return;

            // text-indent's line-start side is physical-left under LTR, where the flow-time CurrentX
            // offset (CreateLineBoxes/FlowBox) already starts every word `indent` further right - so the
            // ragged slack this function centers (`diff` below) already excludes the indent, and halving
            // it evenly keeps the full indent as a fixed left-side offset while only splitting the actual
            // leftover space (verified empirically: a 40pt indent measures as a full 40pt leftGap-minus-
            // rightGap difference with no change here). Physical-right under RTL is different: FlowBox
            // reserves RTL's indent by narrowing the *wrap boundary*, not by moving the start position, so
            // nothing here excludes it from `diff` on its own - insetting the flush target by `indent`,
            // mirroring ApplyRightAlignment's/ApplyJustifyAlignment's identical `isRtl` handling, is what
            // moves it onto the line-start (physical right) side instead of splitting it away to nothing
            // (issue #623).
            var isFirstLine = line.Equals(line.OwnerBox.LineBoxes[0]);
            var indent = GetLineTextIndent(line.OwnerBox, isFirstLine, line.FollowsForcedBreak);
            var isRtl = line.OwnerBox.Direction.Value == DirectionMode.Rtl;

            var lastWord = line.Words[^1];
            var right = line.OwnerBox.ActualRight - line.OwnerBox.ActualPaddingRight - line.OwnerBox.ActualBorderRightWidth
                        - (isRtl ? indent : 0);
            var diff = right - lastWord.Right - lastWord.OwnerBox.ActualBorderRightWidth - lastWord.OwnerBox.ActualPaddingRight;
            diff /= 2;

            if (!(diff > 0)) return;

            foreach (var word in line.Words)
            {
                word.Left += diff;
            }

            if (line.Rectangles.Count <= 0) return;

            foreach (var b in line.Rectangles.Keys.ToList())
            {
                var r = line.Rectangles[b];
                line.Rectangles[b] = new RRect(r.X + diff, r.Y, r.Width, r.Height);
            }
        }

        /// <summary>
        /// Applies right alignment to the text on the line-box
        /// </summary>
        /// <param name="line"></param>
        private static void ApplyRightAlignment(CssLineBox line)
        {
            if (line.Words.Count == 0)
                return;

            // ApplyRightAlignment runs for every plain RTL paragraph (RTL's default `text-align: start`
            // resolves to `right`), and it runs before ApplyBidiReordering, which only mirrors positions
            // within the span this flush-right shift establishes - it never moves the span's own edges.
            // text-indent's line-start-side space (CSS Text 3 §3) is what puts it on the physical right
            // after mirroring, instead of stranding it on the left next to the reading-order-last
            // character - but insetting the flush target here is only half of that: FlowBox already
            // narrowed the wrap boundary by this same amount for RTL (its own `isRtl` handling), so
            // content never naturally reaches past this reduced target in the first place. Insetting only
            // here, while still adding the indent to the flow-time CurrentX offset (what an earlier version
            // of this fix did), double-reserves the space and can leave `diff` negative - the guard below
            // then leaves the line exactly where flow put it, un-aligned, whenever a line's natural
            // slack is smaller than the indent.
            var isFirstLine = line.Equals(line.OwnerBox.LineBoxes[0]);
            var indent = GetLineTextIndent(line.OwnerBox, isFirstLine, line.FollowsForcedBreak);
            var isRtl = line.OwnerBox.Direction.Value == DirectionMode.Rtl;

            var lastWord = line.Words[^1];
            var right = line.OwnerBox.ActualRight - line.OwnerBox.ActualPaddingRight - line.OwnerBox.ActualBorderRightWidth
                        - (isRtl ? indent : 0);
            var diff = right - lastWord.Right - lastWord.OwnerBox.ActualBorderRightWidth - lastWord.OwnerBox.ActualPaddingRight;

            if (!(diff > 0)) return;

            foreach (var word in line.Words)
            {
                word.Left += diff;
            }

            if (line.Rectangles.Count <= 0) return;

            foreach (var b in line.Rectangles.Keys.ToList())
            {
                var r = line.Rectangles[b];
                line.Rectangles[b] = new RRect(r.X + diff, r.Y, r.Width, r.Height);
            }
        }

        private static async ValueTask<double> GetLargestChildWidth(RGraphics g, CssBox box, double currentSize)
        {
            foreach (var childBox in box.Boxes)
            {
                var childBoxWidth = await GetBoxWidth(g, childBox);
                childBoxWidth = await GetLargestChildWidth(g, childBox, Math.Max(currentSize, childBoxWidth));
                currentSize = childBoxWidth > currentSize ? childBoxWidth : currentSize;
            }

            return currentSize;
        }


        #endregion
    }
}