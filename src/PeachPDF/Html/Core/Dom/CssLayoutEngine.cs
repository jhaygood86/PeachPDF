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

using PeachDrawing.Text.Shaping;
using PeachDrawing.Text.Unicode;
using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Fragmentation;
using PeachPDF.Html.Core.Paint;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// Helps on CSS Layout.
    /// </summary>
    internal static class CssLayoutEngine
    {
        /// <summary>
        /// Slack, in layout units, a word (or a whole <c>nowrap</c> run) may overshoot the line's limit by
        /// and still count as fitting. The same idiom as the existing <c>+ 0.01</c> fit tests in this file,
        /// and the same order of magnitude as Chrome's 1/64px <c>LayoutUnit</c> (not numerically identical:
        /// one layout unit is a point at a pixel scale of 1). A shrink-wrapped item is exactly its text's
        /// natural width, and the commit pass re-accumulates that line's <c>CurrentX</c> word by word at
        /// a shifted X, so the running sum can land one floating-point ULP past a limit the item was sized
        /// to exactly - which a strict compare would read as overflow and wrap the last word.
        /// </summary>
        private const double LineFitTolerance = 0.01;

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

            // Every absolute-pixel value this method resolves must land in the box's own internal
            // layout coordinate space, which PixelsPerPoint (PdfGenerateConfig.PixelsPerInch / 72)
            // inflates relative to a true PDF point whenever PixelsPerInch != 72 - see
            // CssValueParser.PixelsPerPointOf's own doc comment (issue #814).
            var pixelsPerPoint = (word.OwnerBox.HtmlContainer?.Adapter as PdfSharpAdapter)?.PixelsPerPoint ?? 1.0;

            // Intrinsic sizes arrive in CSS-pixel space (a raster's device pixels / an SVG's user
            // units, both 1px = 1/96in per spec), while word.Width/Height are layout units (points).
            // Convert once here; every px-unit branch below applies the same shared factor.
            intrinsicWidth *= Length.PointsPerPx * pixelsPerPoint;
            intrinsicHeight *= Length.PointsPerPx * pixelsPerPoint;

            var width = new CssLength(word.OwnerBox.Width);
            var height = new CssLength(word.OwnerBox.Height);

            var hasImageTagWidth = TryResolveAbsolute(width, pixelsPerPoint, out var widthUnits);
            var hasImageTagHeight = TryResolveAbsolute(height, pixelsPerPoint, out var heightUnits);
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
                word.Width = hasImageTagHeight ? heightUnits / 1.14f : 20 * Length.PointsPerPx * pixelsPerPoint;
            }

            var maxWidth = new CssLength(word.OwnerBox.MaxWidth);
            if (maxWidth.Number > 0)
            {
                double maxWidthVal = -1;
                if (TryResolveAbsolute(maxWidth, pixelsPerPoint, out var maxWidthUnits))
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
                if (TryResolveAbsolute(minWidth, pixelsPerPoint, out var minWidthUnits))
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
                word.Height = word.Width > 0 ? word.Width * 1.14f : 22.8f * Length.PointsPerPx * pixelsPerPoint;
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
                if (TryResolveAbsolute(maxHeight, pixelsPerPoint, out var maxHeightUnits))
                {
                    maxHeightVal = maxHeightUnits;
                }
                else if (maxHeight.IsPercentage && IsHeightDefinite(word.OwnerBox.ContainingBlock))
                {
                    maxHeightVal = maxHeight.Number * (ResolveDefiniteHeightValue(word.OwnerBox.ContainingBlock) ?? word.OwnerBox.ContainingBlock.Size.Height);
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
                if (TryResolveAbsolute(minHeight, pixelsPerPoint, out var minHeightUnits))
                {
                    minHeightVal = minHeightUnits;
                }
                else if (minHeight.IsPercentage && IsHeightDefinite(word.OwnerBox.ContainingBlock))
                {
                    minHeightVal = minHeight.Number * (ResolveDefiniteHeightValue(word.OwnerBox.ContainingBlock) ?? word.OwnerBox.ContainingBlock.Size.Height);
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

            bool parsed;
            double? ratio;
            bool hasAuto;
            using (var pooledTokens = CssValueParser.GetCssTokensPooled(aspectRatio))
            {
                List<Token> tokens = pooledTokens;
                parsed = AspectRatioGrammar.TryParse(tokens, out ratio, out hasAuto);
            }

            if (!parsed || ratio is not (> 0))
                return intrinsicRatio; // bare `auto`, or a zero-term ratio: natural ratio only

            // `auto <ratio>` prefers the natural ratio; a bare `<ratio>` overrides it.
            return hasAuto ? intrinsicRatio ?? ratio.Value : ratio.Value;
        }

        /// <summary>
        /// Resolves an explicit absolute width/height/min/max on a replaced element to layout units
        /// through the shared CSS-OM <see cref="Length"/> conversion — any absolute unit, spec-correct px
        /// (1px = 0.75pt) included — then, since that conversion assumes the internal layout unit is a
        /// true PDF point, scales by <paramref name="pixelsPerPoint"/> to land correctly in the box's real
        /// (possibly <c>PixelsPerInch</c>-inflated) internal coordinate space, the same catch-up
        /// <see cref="Parse.CssValueParser.ParseLength(Length, double, CssBox)"/> applies (issue #814).
        /// </summary>
        internal static bool TryResolveAbsolute(CssLength length, double pixelsPerPoint, out double layoutUnits)
        {
            layoutUnits = 0;
            if (length.HasError || length.IsPercentage || !(length.Number > 0))
                return false;

            if (!Length.TryParse(length.Length.Trim().ToLowerInvariant(), out var parsed) || !parsed.IsAbsolute)
                return false;

            layoutUnits = parsed.ToPixels(0, 0, 0) * pixelsPerPoint;
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
                RestoreOverflowWrapSplits(blockBox);
                RestoreLineClampMutations(blockBox);
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

            //Get the start x and y of the blockBox
            var startX = blockBox.ClientLeft;

            // Resumed content starts at the new fragmentainer's own content edge, below any border and padding
            // a `box-decoration-break: clone` box re-opens with there (css-break-3 §6.2). `text-indent`
            // applies to the first formatted line only (CSS Text §5), so it is not re-applied here.
            //
            // Below the room a page float pinned to this fragmentainer's head (css-page-floats `float: top`)
            // has claimed, too. A block-level box is kept off that strip by ResolveBlockChildOffset's floor,
            // but a paragraph that started on an earlier page is not placed by it: its lines continue from
            // the content edge, which is inside the strip (issue #1273). The same floor, taken the same way,
            // so a document with no page float reads zero here.
            var startY = resume is not null && context is not null
                ? Math.Max(
                    context.ResumeContentTop + (blockBox.HtmlContainer is { HasCloneDecorations: true }
                        ? DomUtils.ClonedBlockStart(blockBox, stopAt: null)
                        : 0),
                    context.BandTop + context.BandStartInsetOf(context.SlotIndex))
                : blockBox.ClientTop;

            // The last line an earlier fragmentainer kept, read before the seed line joins the list.
            var lineBeforeResume = resume is not null && blockBox.LineBoxes.Count > 0 ? blockBox.LineBoxes[^1] : null;

            // A resumed pass's seed line rebuilds whole the line-in-progress the previous pass discarded
            // (a line box is monolithic, css-break-3 §4.1) - carrying its FollowsForcedBreak bit forward
            // is what lets `text-indent: each-line` (CSS Text 3 §3) still recognize a resumed line that
            // follows a forced break in the source, not just one born mid-fragmentainer.
            var seedLine = new CssLineBox(blockBox)
            {
                FollowsForcedBreak = resume?.FollowsForcedBreak ?? false,
                // css-break-3 §5.1: this line begins in whatever fragmentainer startY falls in, and a
                // stretch-fit/auto-width main-column box recalculates its inline size per fragmentainer -
                // asked here, at the one moment the line's own Y is known and before a single word of it
                // has been measured against a boundary, which is why no retry loop is needed for text:
                // line-breaking is already Y-sequential. Falls back to blockBox.ClientRight (the box's own,
                // start-page-fixed measure) for a box not eligible for per-fragmentainer re-wrap - see
                // LineContentRightOf's own remarks.
                ContentRight = LineContentRightOf(blockBox, startY),
                ContentLeft = startX
            };

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
                EmptyInlinesBeforeResume = lineBeforeResume?.EmptyInlines,
                SuppressLeadingWrap = resume is not null,
                // hyphenate-limit-lines (CSS Text 4 §6.3.5): a run of consecutive hyphenated lines that
                // straddles this boundary keeps counting rather than restarting at 0 - see
                // InlineBreakToken.ConsecutiveHyphenatedLines.
                ConsecutiveHyphenatedLines = resume?.ConsecutiveHyphenatedLines ?? 0
            };

            //Flow words and boxes
            var activeFlows = blockBox.HtmlContainer?.ActiveInlineFlows;
            activeFlows?.Add(coordinates);

            try
            {
                await FlowBox(g, blockBox, blockBox, 0, startX, coordinates);
            }
            finally
            {
                activeFlows?.RemoveAt(activeFlows.Count - 1);
            }

            // Empty inlines still held for a word that never came sit on the flow's last line. A break
            // discards the line being built and a line-clamp stop hides what follows, so then they are on no
            // line this pass keeps.
            if (coordinates is { Break: null, ClampedStop: false, PendingEmptyInlineExtent: { } trailing })
            {
                HandHeldEmptyInlinesToTheLine(coordinates);
                GrowLineForEmptyInlines(blockBox, coordinates, trailing);
            }

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
                UndoAbandonedOverflowWrapSplits(coordinates.Line);

                foreach (var word in coordinates.Line.Words)
                {
                    word.AwaitsTheNextFragmentainer = true;
                }

                AnchorSetAsideBoxes(coordinates);
                FinalizeLineBoxes(blockBox, completedLines, blockFinished: false);

                // The ones on the discarded line are reached again by the resumed pass.
                await LayoutAbsolutelyPositioned(g, coordinates, stopped.ResumeWordIndex);

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

            DropATrailingForcedBreaksOwnLine(blockBox, coordinates);

            AnchorSetAsideBoxes(coordinates);
            FinalizeLineBoxes(blockBox, completedLines);

            blockBox.ActualBottom = coordinates.MaxBottom + blockBox.ActualPaddingBottom + blockBox.ActualBorderBottomWidth;

            // handle limiting block height when overflow is hidden
            if (blockBox.Height != Keywords.Auto && blockBox.Overflow.Value == Overflow.Hidden && blockBox.ActualBottom - blockBox.Location.Y > blockBox.ActualHeight)
            {
                blockBox.ActualBottom = blockBox.Location.Y + blockBox.ActualHeight + blockBox.ActualPaddingBottom + blockBox.ActualPaddingTop;
            }

            await LayoutAbsolutelyPositioned(g, coordinates, int.MaxValue);

            return null;
        }

        /// <summary>
        /// Lays out the absolutely positioned boxes <see cref="FlowBox"/> set aside among the block's inline
        /// content, through the same frame entry a block-level child takes, so the containing block
        /// resolution and positioning are the ones <see cref="CssBox.LayoutBlockChild"/> gives any
        /// absolutely positioned box.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Run after the lines are final: the box's containing block can be an inline ancestor, which
        /// CSS Positioned Layout 3 §2.1 forms from that inline's first and last fragments (CSS 2.1 §10.1
        /// for one on a single line), and those only exist once every line holding the inline has been
        /// built and aligned.
        /// </para>
        /// <para>
        /// Laid out with breaking detached, as <c>CssBox.LayoutEngineContent</c> lays out an engine
        /// container's absolutely positioned children: nothing up this inline flow resumes a record such a
        /// box leaves behind, so a break inside it would drop the content after the break. Its lines are
        /// still distributed over the pages they reach, as the block-flow path's are.
        /// </para>
        /// </remarks>
        /// <param name="g">the device context</param>
        /// <param name="coordinates">the flow that set the boxes aside</param>
        /// <param name="beforeOrdinal">only boxes the flow reached before this word ordinal are laid out</param>
        private static async ValueTask LayoutAbsolutelyPositioned(RGraphics g, CssLineBoxCoordinates coordinates, int beforeOrdinal)
        {
            if (coordinates.AbsolutelyPositioned is not { } boxes) return;

            foreach (var setAside in boxes)
            {
                if (setAside.Ordinal >= beforeOrdinal) continue;

                var box = setAside.Box;
                var emptyInline = EmptyInlineContainingBlockFor(setAside);
                var previous = box.HtmlContainer?.DetachFragmentainer();

                try
                {
                    await box.ParentBox!.LayoutBlockChild(g, box);
                }
                finally
                {
                    box.HtmlContainer?.RestoreFragmentainer(previous);
                    if (emptyInline is not null) emptyInline.EmptyInlineContainingBlock = null;
                }
            }
        }

        /// <summary>
        /// Records <paramref name="box"/> as passed at the flow's cursor, to be laid out once the lines are
        /// final. Held once: a box a float's or inline-block's layout reaches on several passes (a
        /// measurement, then the real one) replaces its earlier entry rather than being laid out twice.
        /// </summary>
        private static void SetAsideAtTheCursor(CssLineBoxCoordinates coordinates, CssBox box, int ordinal)
        {
            var words = coordinates.Line.Words;
            var preceding = words.Count > 0 ? words[^1] : null;
            var setAside = new SetAsideBox(
                box, ordinal, coordinates.Line, coordinates.CurrentX, coordinates.CurrentY,
                words.Count, preceding, preceding?.Left ?? 0);

            var boxes = coordinates.AbsolutelyPositioned ??= [];
            var existing = boxes.FindIndex(s => ReferenceEquals(s.Box, box));

            if (existing >= 0) boxes[existing] = setAside;
            else boxes.Add(setAside);
        }

        /// <summary>
        /// Hands an absolutely positioned <paramref name="box"/> to the inline flow that owns its containing
        /// block when that block is a positioned <i>inline</i> whose lines the flow is still building, and
        /// says so; false when the box can be laid out now.
        /// </summary>
        /// <remarks>
        /// A box inside a float, or inside an inline-block holding block-level content, is laid out as part
        /// of that box's own content, mid-walk of the flow around it. Its nearest positioned ancestor can be
        /// an inline outside that content, and an inline's containing block is formed from its first and last
        /// line fragments (<see href="https://www.w3.org/TR/css-position-3/#def-cb">CSS Positioned Layout 3
        /// §2.1</see>, <see href="https://www.w3.org/TR/CSS21/visudet.html#containing-block-details">CSS 2.1
        /// §10.1</see>) - which do not exist until the flow has finished and aligned every line. Read
        /// early, they gave nothing and the box was placed at the sheet's origin (issue #1304). Where the box
        /// sits inside the inline does not change its containing block, so the flow holding the inline is
        /// the one to lay it out, as it does a box directly among its content. The flows are found by
        /// box-tree ancestry, never by nesting depth: the one owned by the block the inline's run sits in.
        /// </remarks>
        internal static bool TryDeferToEnclosingInlineFlow(CssBox box)
        {
            // A fixed box's containing block is the page (or a transformed ancestor), never a positioned inline.
            if (box.Position.Value != PositionMode.Absolute) return false;
            if (box.HtmlContainer?.ActiveInlineFlows is not { Count: > 0 } flows) return false;

            var ancestor = DomUtils.GetNearestPositionedAncestor(box);
            if (!ancestor.IsInline || DomUtils.IsAtomicInline(ancestor)) return false;

            // The flow that lays the inline's own lines out is the one owned by the block its inline run sits
            // in. Any flow further out merely contains that block (a float inside an outer flow holds a whole
            // inline flow of its own), and handing the box to it would lay the box out before the inline's
            // fragments exist there too.
            var linesOwner = ancestor.ParentBox;
            while (linesOwner is { IsInline: true } && !DomUtils.IsAtomicInline(linesOwner))
            {
                linesOwner = linesOwner.ParentBox;
            }

            for (var i = flows.Count - 1; i >= 0; i--)
            {
                var flow = flows[i];
                if (!ReferenceEquals(flow.Line.OwnerBox, linesOwner)) continue;

                SetAsideAtTheCursor(flow, box, flow.WordOrdinal);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gives <paramref name="setAside"/>'s nearest positioned ancestor a zero-width containing block at
        /// the box's place on the line when that ancestor is an inline with no fragment of its own, and
        /// returns it so the caller can take it back; null for any other ancestor.
        /// </summary>
        /// <remarks>
        /// <para>
        /// An inline holding nothing but absolutely positioned boxes places no word, so it has no line
        /// fragments to form a containing block from (<see cref="DomUtils.InlineContainingBlockOf"/>), and
        /// its descendants fell back to its line-local <see cref="CssBox.Location"/>: the sheet origin. An
        /// empty inline element still generates an inline box in its line box
        /// (<see href="https://www.w3.org/TR/CSS21/visuren.html#inline-formatting">CSS 2.1 §9.4.2</see>,
        /// CSS Inline 3), and browsers anchor the descendants to that zero-width box.
        /// </para>
        /// <para>
        /// Its inline position is where the walk passed the box, carried through alignment and bidi
        /// reordering by an adjacent word (<see cref="PlaceOnFinalLine"/>, which is right within a bidi run
        /// but not at a run boundary). Its block extent is the content
        /// area a word in the inline's own font has on the line (baseline less ascent, one font height),
        /// plus the inline's padding; on a line with no word, it starts at the line's top. The inline's own
        /// <c>vertical-align</c> is not applied, so a raised empty inline sits on the line's baseline. Held
        /// only for the duration of the box's layout: nothing later reads it, and the line it describes is
        /// moved afterwards (a table cell's alignment, say) without it.
        /// </para>
        /// </remarks>
        /// <param name="setAside">the set-aside box about to be laid out</param>
        /// <returns>the inline given the containing block, or null</returns>
        private static CssBox? EmptyInlineContainingBlockFor(SetAsideBox setAside)
        {
            var ancestor = DomUtils.GetNearestPositionedAncestor(setAside.Box);
            var line = setAside.Line;

            // An atomic inline is let through: DomUtils.InlineContainingBlockOf never reads this off one.
            if (!ancestor.IsInline || ancestor.Rectangles.Count > 0) return null;

            // Only an inline in this flow is known to be empty. One outside it - the box sits in a float or
            // inline-block inside that inline - merely has not got its fragments yet, since its own line is
            // still being built, and the box's place in the inner flow is not a place on that line.
            if (!DomUtils.IsSelfOrDescendantOf(ancestor, line.OwnerBox)) return null;

            var x = PlaceOnFinalLine(setAside);
            var font = ancestor.ActualFont;
            var contentTop = line.Words.Count > 0 && line.BaselineY is { } baseline
                ? baseline - font.Ascent
                : setAside.Y;

            ancestor.EmptyInlineContainingBlock = RRect.FromLTRB(
                x - ancestor.ActualPaddingLeft,
                contentTop - ancestor.ActualPaddingTop,
                x + ancestor.ActualPaddingRight,
                contentTop + font.Height + ancestor.ActualPaddingBottom);

            return ancestor;
        }

        /// <summary>
        /// Gives each set-aside box that had no word before it on its line the word after it as its anchor,
        /// with that word's position before <see cref="FinalizeLineBoxes"/> moves it.
        /// </summary>
        /// <remarks>
        /// Run just before the lines are finalized: the word after a box is only placed once the walk has
        /// passed it, and alignment and bidi reordering move it straight after. Without it, a badge at the
        /// start of a centred line stayed at the line's left edge while the words moved to the middle. The
        /// word after is also preferred when a space separates the word before from the place, since
        /// <c>text-align: justify</c> widens that space and so moves the place as far as the word after.
        /// </remarks>
        /// <param name="coordinates">the flow that set the boxes aside</param>
        private static void AnchorSetAsideBoxes(CssLineBoxCoordinates coordinates)
        {
            if (coordinates.AbsolutelyPositioned is not { } boxes) return;

            for (var i = 0; i < boxes.Count; i++)
            {
                var setAside = boxes[i];

                // A space between the word before and the place is widened by justification, which moves
                // the place with the word after it, not with the word before.
                var spaceBefore = setAside.Anchor is { } preceding
                                  && setAside.X - (setAside.AnchorLeft + preceding.Width) > 0.001;

                if ((setAside.Anchor is not null && !spaceBefore) || setAside.WordIndex >= setAside.Line.Words.Count) continue;

                var following = setAside.Line.Words[setAside.WordIndex];
                boxes[i] = setAside with { Anchor = following, AnchorLeft = following.Left };
            }
        }

        /// <summary>
        /// Where the place <paramref name="setAside"/> was passed at on its line ends up once the line is
        /// final: moved as its anchor word was, which is a translation for text-align and a left-to-right
        /// run, and a reflection within the run for a right-to-left one (<see cref="ApplyBidiReordering"/>
        /// reverses such a run within its span). So a place just after a word in an <c>rtl</c> run lands on
        /// that word's left edge, where the next word in reading order begins.
        /// </summary>
        /// <remarks>
        /// Right within a run only. The place has no bidi level of its own, so at the boundary between two
        /// runs of opposite direction it goes with its anchor's run, and lands at that run's far end rather
        /// than between the runs (#1309).
        /// </remarks>
        /// <param name="setAside">the set-aside box</param>
        /// <returns>the place's inline position on the final line</returns>
        private static double PlaceOnFinalLine(SetAsideBox setAside)
        {
            if (setAside.Anchor is not { } word) return PlaceOnWordlessLine(setAside);

            var offset = setAside.X - setAside.AnchorLeft;

            return ReorderedLevelOf(word, setAside.Line) % 2 == 1
                ? word.Left + word.Width - offset
                : word.Left + offset;
        }

        /// <summary>
        /// Where the place <paramref name="setAside"/> was passed at ends up on a line holding no word, which
        /// nothing in <see cref="FinalizeLineBoxes"/> moves: the walk's cursor starts at the left edge
        /// whatever the line's direction and alignment. The line's content is taken as the zero-width place
        /// plus whatever the walk put before it (a <c>text-indent</c>, the inline's own start spacing), on
        /// the start side, and aligned as the line would align words: flush left, flush right or centred.
        /// </summary>
        /// <param name="setAside">the set-aside box, on a line with no word</param>
        /// <returns>the place's inline position on the line</returns>
        private static double PlaceOnWordlessLine(SetAsideBox setAside)
        {
            var line = setAside.Line;
            var isRtl = line.OwnerBox.Direction.Value == DirectionMode.Rtl;
            var towardStart = isRtl ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            var towardEnd = isRtl ? HorizontalAlignment.Left : HorizontalAlignment.Right;

            // A line with no word has no justification opportunity, so it takes text-align-last, as the
            // last line of a paragraph does.
            var declared = ResolveLogicalAlignment(line.OwnerBox.ActualTextAlignAll, towardStart, towardEnd);
            var (alignment, _) = ResolveUsedAlignment(line, endsAParagraph: true, declared, towardStart, towardEnd);

            // The walk put an ltr line's text-indent into the cursor, but reserves an rtl one on the right
            // edge instead (see ApplyJustifyAlignment), so it is added back there.
            var lead = setAside.X - line.ContentLeft;
            var rtlIndent = isRtl
                ? GetLineTextIndent(line.OwnerBox, line.Equals(line.OwnerBox.LineBoxes[0]), line.FollowsForcedBreak)
                : 0d;
            var flushLeft = isRtl ? line.ContentLeft : setAside.X;
            var flushRight = isRtl ? line.ContentRight - lead - rtlIndent : line.ContentRight;

            return alignment switch
            {
                HorizontalAlignment.Left => flushLeft,
                HorizontalAlignment.Right => flushRight,
                HorizontalAlignment.Center => (flushLeft + flushRight) / 2,
                _ => isRtl ? flushRight : flushLeft
            };
        }

        /// <summary>
        /// The bidi level <see cref="ApplyBidiReordering"/> reordered <paramref name="word"/> at: its own,
        /// except in the run of space words that ends the line, which UAX #9 L1 resets to the paragraph's
        /// level. That reset is applied to the reordering's local copy of the levels only, so the word's
        /// own <see cref="CssRect.BidiLevel"/> still says rtl for a trailing space after rtl text.
        /// </summary>
        /// <param name="word">a word on <paramref name="line"/></param>
        /// <param name="line">the line holding it</param>
        /// <returns>the level the word was placed at</returns>
        private static byte ReorderedLevelOf(CssRect word, CssLineBox line)
        {
            for (var i = line.Words.Count - 1; i >= 0 && line.Words[i].IsSpaces; i--)
            {
                if (ReferenceEquals(line.Words[i], word))
                    return line.OwnerBox.Direction.Value == DirectionMode.Rtl ? (byte)1 : (byte)0;
            }

            return word.BidiLevel;
        }

        /// <summary>
        /// Real <c>vertical-rl</c>/<c>vertical-lr</c> line flow for a block box holding only inline
        /// content: lines ("columns") stack along the block axis, text within a line runs along the
        /// inline axis, using <see cref="WritingModeFrame"/> to convert the logical placement to physical
        /// coordinates. <see cref="FlowBox"/>'s counterpart, written fresh rather than made
        /// writing-mode-aware in place, because a vertical box is
        /// <see cref="MonolithicContent.IsUnresumableOrthogonalFlow">monolithic w.r.t. its parent's
        /// fragmentation</see> and so never needs <see cref="CssLineBoxCoordinates"/>'s fragmentation-resume
        /// machinery (word ordinals, a fragmentainer, an in-progress break) at all - this lays out the
        /// whole box in one pass, always.
        /// </summary>
        /// <remarks>
        /// Real vertical line flow with real feature parity to <see cref="FlowBox"/> (issue #768): floats,
        /// absolute/fixed positioning, <c>hyphens</c>, Unicode Bidi Algorithm reordering, and
        /// <c>text-align</c> are all honored now. Glyphs are painted rotated 90° regardless of
        /// <c>text-orientation</c> (the "everything rotates" interim simplification - real per-character
        /// upright/rotated splitting is a later phase), so each word's own natural (horizontal)
        /// <see cref="CssRect.Width"/>/<see cref="CssRect.Height"/> already are its inline-size/block-size;
        /// only their arrangement, not their own measurement, needs to change here. Width auto-sizing
        /// (fill-available, unaware of writing-mode) is unchanged; only auto height becomes genuinely
        /// content-driven, mirroring <see cref="CreateLineBoxes"/>'s own auto-height growth. Still scoped
        /// down: no leading/trailing inline spacing from a nested inline box's own border/padding/margin, no
        /// <c>box-decoration-break: clone</c> - see the no-vertical-writing-mode-layout accepted gap. A float
        /// among the box's own inline content is placed by <see cref="CssBox.PlaceVerticalFloat"/> at the
        /// block-axis position of the column the word stream has reached (issue #796); one in its
        /// <em>block-level</em> content is placed the same way by <c>CssBox.LayoutVerticalBlockChildren</c>.
        /// </remarks>
        internal static async ValueTask CreateVerticalLineBoxes(RGraphics g, CssBox blockBox)
        {
            RestoreOverflowWrapSplits(blockBox);
            RestoreLineClampMutations(blockBox);
            blockBox.LineBoxes.Clear();

            var words = new List<CssRect>();
            var outOfFlowDescendants = new List<(CssBox Box, int WordIndex)>();
            await MeasureAndCollectWordsInDocumentOrder(g, blockBox, words, outOfFlowDescendants);

            var clientTop = blockBox.ClientTop;
            var clientLeft = blockBox.ClientLeft;
            var clientRight = blockBox.ClientRight;

            // The inline axis's own extent (physical height, for vertical-rl/vertical-lr) is what content
            // wraps against. A definite height is resolved directly from the box's own CSS Height string
            // via DefiniteContentHeight rather than IsHeightDefinite/ResolveDefiniteHeightValue - those
            // exist for a DESCENDANT's percentage resolution against this box, a different question from
            // this box's own wrap limit, and DefiniteContentHeight's own percentage basis is a pre-existing,
            // narrower approximation this change does not extend. An auto height has nothing else to
            // consult (height is resolved bottom-up), so it falls back to one full page's own depth -
            // deliberately NOT "whatever
            // remains between this box's own (document-continuous, not page-relative) ClientTop and the
            // bottom of whichever page it currently lands on": that would make an auto-height box near the
            // bottom of a page self-limit to a sliver of remaining space instead of the fresh page's worth
            // the monolithic-content mover (CssBox.PerformLayoutEpilogue) would otherwise relocate it to.
            // A position-independent fallback keeps sizing consistent regardless of where the box lands,
            // so that mover's own "does this fit here, else move to the next page" check still gets a
            // real chance to fire.
            var heightIsAuto = !CssValueParser.IsValidLength(blockBox.Height);
            var wrapLimit = heightIsAuto
                ? Math.Max(1, blockBox.HtmlContainer?.PageSize.Height ?? 1)
                : Math.Max(1, DefiniteContentHeight(blockBox));

            var frame = WritingModeFrame.ForContentBox(
                clientLeft, clientTop, clientRight, clientTop + wrapLimit, blockBox.WritingMode.Value, blockBox.Direction.Value);

            // Auto width (block axis) shrinks to the content's own extent, the direct counterpart of
            // auto height (inline axis) shrinking above - CSS Writing Modes doesn't special-case either
            // axis, so a vertical box's auto block-size shrinking to content is exactly as spec-required
            // as horizontal-tb's auto height already shrinking is (issue #761). Block-start (physical
            // Right for vertical-rl, physical Left for vertical-lr) is the edge every word position below
            // is already anchored to (frame.ToPhysical's own BlockStartIsRight branch), so it stays fixed
            // exactly the way ClientTop stays fixed for auto height, and only the block-end edge moves -
            // Location.X for vertical-rl (mirroring ActualBottom's own "move the end edge" shape flipped
            // onto the other physical axis), ActualRight for vertical-lr (identical shape to the height
            // case, just on X). Reads frame.BlockStartIsRight rather than re-deriving
            // LogicalPropertyResolver.BlockStart a second time for the same box.
            var widthIsAuto = !CssValueParser.IsValidLength(blockBox.Width);

            var placedFloats = new List<CssBox.VerticalFloatPlacement>();
            var bottomFloats = new List<(CssBox Box, double InlineFromBottom)>();
            var nextFloat = 0;
            var floatsInOrder = outOfFlowDescendants.Where(o => o.Box.IsFloated
                && o.Box.DerivedStyle.ActualDisplay != Keywords.None).ToList();

            // Places every float the word stream has reached by <paramref name="wordIndex"/>, at the block-axis
            // position of the column being built (or of the box's block-start, before any column exists).
            async ValueTask<bool> PlaceFloatsUpTo(int wordIndex, double atBlockOffset)
            {
                var placedAny = false;

                while (nextFloat < floatsInOrder.Count && floatsInOrder[nextFloat].WordIndex <= wordIndex)
                {
                    await CssBox.PlaceVerticalFloat(g, floatsInOrder[nextFloat].Box, frame, clientTop, atBlockOffset,
                        placedFloats, bottomFloats, heightIsAuto ? null : wrapLimit);
                    nextFloat++;
                    placedAny = true;
                }

                return placedAny;
            }

            // A word split in two (hyphenation, overflow-wrap) puts one more word in the stream, so the floats that
            // follow it in the source now follow one word further on.
            void ShiftFloatsAfter(int splitIndex)
            {
                for (var f = nextFloat; f < floatsInOrder.Count; f++)
                {
                    if (floatsInOrder[f].WordIndex > splitIndex)
                        floatsInOrder[f] = (floatsInOrder[f].Box, floatsInOrder[f].WordIndex + 1);
                }
            }

            if (words.Count == 0)
            {
                await PlaceFloatsUpTo(int.MaxValue, 0);
                blockBox.SetPendingBottomFloats(bottomFloats);

                if (heightIsAuto) blockBox.ActualBottom = clientTop;
                if (widthIsAuto) ShrinkAutoWidthTo(blockBox, frame, 0);
                await LayoutOutOfFlowDescendants(g, blockBox, outOfFlowDescendants);
                return;
            }

            var line = new CssLineBox(blockBox);
            double inlineOffset = 0;
            double blockOffset = 0;
            double lineThickness = 0;
            double maxInlineExtentUsed = 0;
            var consecutiveHyphenatedColumns = 0;
            var currentColumnHyphenated = false;
            var trailingRegionalIndicatorCount = 0;
            var trailingGraphemeContext = string.Empty;
            // Where the current column's content starts along the inline axis and how far it may run: a float
            // pinned to the line-left or line-right side (physical top or bottom) takes that end of the column
            // (CSS Writing Modes 4 section 7.5), so words start after it and stop before the one on the other side.
            await PlaceFloatsUpTo(0, 0);

            var (columnStartInset, effectiveWrapLimit, columnTopInset, columnBottomInset) =
                ComputeColumnInlineSpan(blockBox, frame, clientTop, wrapLimit, 0, placedFloats);
            inlineOffset = columnStartInset;
            line.VerticalTopInset = columnTopInset;
            line.VerticalBottomInset = columnBottomInset;

            // Whether blockBox's own white-space permits a column break anywhere in its content at all -
            // FlowBox's own `blockBoxPermitsWrap` counterpart (issue #841). Read once: if blockBox itself
            // is nowrap/pre, no line break is legal anywhere in its content, so the nested-nowrap-run
            // atomic-move check below (mirroring FlowBox's own wrapNoWrapBox) must never fire - it exists
            // only for a *nested* nowrap run inside otherwise-wrapping content.
            var blockBoxPermitsWrap = blockBox.WhiteSpace.Value != Whitespace.NoWrap
                                       && blockBox.WhiteSpace.Value != Whitespace.Pre;

            var currentWordIndex = 0;

            async ValueTask StartNewLine()
            {
                // The column about to close is being abandoned for a new one - fold whether it ended in a
                // hyphen into the running consecutive-hyphenated-columns count before that state resets.
                consecutiveHyphenatedColumns = currentColumnHyphenated ? consecutiveHyphenatedColumns + 1 : 0;
                currentColumnHyphenated = false;

                maxInlineExtentUsed = Math.Max(maxInlineExtentUsed, inlineOffset);
                blockOffset += lineThickness;
                lineThickness = 0;
                trailingRegionalIndicatorCount = 0;
                trailingGraphemeContext = string.Empty;

                // A float the word stream reached while the closing column held words goes beside the next one,
                // placed now that this column's thickness is final: a taller word arriving mid-column could
                // otherwise have reached into a float placed against the thickness so far.
                await PlaceFloatsUpTo(currentWordIndex, blockOffset);

                line = new CssLineBox(blockBox);
                (columnStartInset, effectiveWrapLimit, columnTopInset, columnBottomInset) =
                    ComputeColumnInlineSpan(blockBox, frame, clientTop, wrapLimit, blockOffset, placedFloats);
                inlineOffset = columnStartInset;
                line.VerticalTopInset = columnTopInset;
                line.VerticalBottomInset = columnBottomInset;
            }

            var pendingWordSeparator = false;

            for (var i = 0; i < words.Count; i++)
            {
                currentWordIndex = i;

                // A float the word stream has just reached is no higher than the column being built (CSS 2.1
                // section 9.5.1 rule 6, axes swapped): at its block-axis position while the column is still empty.
                // Once words are on the column it waits for the next one (StartNewLine), so it never overlaps words
                // already placed or yet to come.
                if (nextFloat < floatsInOrder.Count && floatsInOrder[nextFloat].WordIndex <= i
                    && line.Words.Count == 0
                    && await PlaceFloatsUpTo(i, blockOffset))
                {
                    (columnStartInset, effectiveWrapLimit, columnTopInset, columnBottomInset) =
                        ComputeColumnInlineSpan(blockBox, frame, clientTop, wrapLimit, blockOffset, placedFloats);
                    inlineOffset = columnStartInset;
                    line.VerticalTopInset = columnTopInset;
                    line.VerticalBottomInset = columnBottomInset;
                }

                var word = words[i];

                if (word.IsLineBreak)
                {
                    // See CssLineBox.PrecedesForcedBreak: the column this break closes is the last one of
                    // its paragraph, so text-align-last governs it (css-text-3 §6.1/§6.3).
                    line.PrecedesForcedBreak = true;
                    await StartNewLine();
                    continue;
                }

                // Re-measured fresh, never read off word.Width/Height directly: this box's own content
                // layout can run more than once (a flex/table/shrink-to-fit ancestor's provisional sizing
                // pass, a monolithic relocation to a later page) and the geometry commit below overwrites
                // word.Width/Height with the *physical* (rotated) footprint - trusting them as "natural"
                // on a second entry would compound that rotation instead of redoing it from source.
                var (naturalWidth, naturalHeight) = NaturalWordSize(g, word);
                var wordRectInline = naturalWidth; // the word's own glyph footprint, no trailing space
                var wordAdvance = naturalWidth + word.ActualWordSpacing; // + spacing, what the next word's placement clears
                var wordBlock = naturalHeight; // natural line-height - the line's own cross-axis thickness
                var previousWord = line.Words.Count > 0 ? line.Words[^1] : null;
                var isGraphemeBoundary = IsGraphemeBoundaryBefore(
                    previousWord, word, trailingRegionalIndicatorCount, trailingGraphemeContext);
                var hasOrdinaryWrapBefore = HasOrdinaryWrapOpportunityBefore(
                    previousWord, word, precedingRegionalIndicatorCount: trailingRegionalIndicatorCount,
                    precedingGraphemeContext: trailingGraphemeContext);

                // white-space: nowrap/pre on word's own owning box must never register as "doesn't fit" -
                // the direct counterpart of FlowBox's own `overflows` exclusion (issue #844: this exclusion
                // was missing entirely, so nowrap had no effect at all on vertical column-breaking). A
                // pre-wrap run doesn't wrap on its own trailing whitespace either, mirroring FlowBox's
                // identical carve-out.
                var permitsOverflowWrap = word.OwnerBox.WhiteSpace.Value != Whitespace.NoWrap
                                           && word.OwnerBox.WhiteSpace.Value != Whitespace.Pre
                                           && (word.OwnerBox.WhiteSpace.Value != Whitespace.PreWrap || !word.IsSpaces);

                // Whether word, placed at its current inlineOffset, fits this column's real usable extent
                // (effectiveWrapLimit, narrowed from the box's own wrapLimit by a floated sibling, if any) -
                // checked unconditionally, independent of column position, the same way FlowBox's own
                // `overflows` is: a word that alone is too long for even an empty column must still get a
                // real hyphenation attempt, not just an unavoidable-overflow pass-through.
                var wordDoesNotFit = permitsOverflowWrap && inlineOffset + wordAdvance > effectiveWrapLimit + LineFitTolerance;

                // hyphens:auto/manual: before giving up and wrapping the whole word, see if a cached
                // candidate break point (from CssBox.ParseToWords - either an explicit soft hyphen or an
                // automatic Hyphenator suggestion) lets a hyphenated prefix fit in the space
                // remaining in this column instead - gated by hyphenate-limit-lines/-zone exactly like
                // FlowBox's own attempt, just against this column's own local hyphenation-streak counter
                // rather than CssLineBoxCoordinates's (this method is always a single monolithic pass, so
                // there is no fragmentainer-resume state to seed that counter from, and hyphenate-limit-last
                // - defined relative to a fragmentainer break - has nothing to apply to here).
                if (wordDoesNotFit && !word.SuppressWrapBefore &&
                    word.HyphenationCandidates is { Count: > 0 } &&
                    IsWithinHyphenateLimitLines(blockBox, consecutiveHyphenatedColumns) &&
                    IsWithinHyphenateLimitZone(blockBox, effectiveWrapLimit - inlineOffset, effectiveWrapLimit) &&
                    TryHyphenateWord(g, word.OwnerBox, word, effectiveWrapLimit - inlineOffset, out var prefixWord, out var suffixWord))
                {
                    // Splices into both the owning box's own word list (the real source of truth
                    // TryRestoreHyphenationSplit later reads) and this method's own locally-flattened list,
                    // at this word's current position in each.
                    var ownerWords = word.OwnerBox.Words;
                    var ownerIndex = ownerWords.IndexOf(word);
                    if (ownerIndex >= 0)
                    {
                        ownerWords[ownerIndex] = prefixWord!;
                        ownerWords.Insert(ownerIndex + 1, suffixWord!);
                    }

                    words[i] = prefixWord!;
                    words.Insert(i + 1, suffixWord!);
                    ShiftFloatsAfter(i);

                    // TryHyphenateWord never sets this - without it, ApplyVerticalBidiReordering would
                    // treat a hyphenated fragment as base-level LTR regardless of its true embedding level.
                    prefixWord!.BidiLevel = word.BidiLevel;
                    suffixWord!.BidiLevel = word.BidiLevel;

                    word = prefixWord;
                    (naturalWidth, naturalHeight) = NaturalWordSize(g, word);
                    wordRectInline = naturalWidth;
                    wordAdvance = naturalWidth + word.ActualWordSpacing;
                    wordBlock = naturalHeight;

                    wordDoesNotFit = false;
                    currentColumnHyphenated = true;
                }

                if (wordDoesNotFit && (inlineOffset <= columnStartInset || !hasOrdinaryWrapBefore) &&
                    TryOverflowWrapWord(g, word, effectiveWrapLimit - inlineOffset, out var overflowPrefix,
                        out var overflowSuffix))
                {
                    ReplaceCollectedWordWithOverflowWrapSplit(words, i, word, overflowPrefix!, overflowSuffix!);
                    ShiftFloatsAfter(i);
                    word = overflowPrefix!;
                    (naturalWidth, naturalHeight) = NaturalWordSize(g, word);
                    wordRectInline = naturalWidth;
                    wordAdvance = naturalWidth + word.ActualWordSpacing;
                    wordBlock = naturalHeight;
                    wordDoesNotFit = false;
                }

                // A nested nowrap run (a box whose own white-space overrides an otherwise-wrapping
                // ancestor's, e.g. a <span style="white-space:nowrap"> inside a normally-wrapping vertical
                // block) must still be able to move to a fresh column as a whole unit if it doesn't fit -
                // the vertical counterpart of FlowBox's own wrapNoWrapBox (issue #844). Evaluated once per
                // run (entering a new owning box whose own white-space is nowrap) rather than per word, and
                // gated on blockBoxPermitsWrap: when blockBox itself is nowrap, every word already shares
                // that nowrap and permitsOverflowWrap above already suppresses the ordinary wrap check for
                // all of them, so forcing a break here too would reintroduce #841's bug on this axis.
                var entersNewNoWrapRun = blockBoxPermitsWrap
                                          && word.OwnerBox.WhiteSpace.Value == Whitespace.NoWrap
                                          && (i == 0 || words[i - 1].OwnerBox != word.OwnerBox);

                var wrapsWholeNoWrapRun = false;
                if (entersNewNoWrapRun && inlineOffset > columnStartInset)
                {
                    // The run's own total *inline-axis* (along-the-column) extent - NaturalWordSize's
                    // Width, the same quantity wordAdvance above is built from, not Height (the cross-axis
                    // line-thickness FlowBox's own horizontal `FullWidth` has no counterpart for).
                    var runExtent = 0d;
                    foreach (var runWord in word.OwnerBox.Words)
                    {
                        var (runWidth, _) = NaturalWordSize(g, runWord);
                        runExtent += runWidth + runWord.ActualWordSpacing;
                    }

                    wrapsWholeNoWrapRun = inlineOffset + runExtent > effectiveWrapLimit + LineFitTolerance;
                }

                // A fragment split from what was one word - a per-codepoint-font run, or a
                // Vertical_Orientation run - must never itself be treated as a wrap opportunity, the same
                // guard FlowBox's own wrap check already applies. inlineOffset > 0 (a real word already
                // placed in this column) avoids wrapping a column that is still empty - the same
                // unavoidable-overflow fallback FlowBox's own first-word-of-a-line case gets.
                var startedNewLine = inlineOffset > columnStartInset
                    && ((hasOrdinaryWrapBefore && wordDoesNotFit)
                        || (!hasOrdinaryWrapBefore && wordDoesNotFit
                            && isGraphemeBoundary && AllowsOverflowWrapAtBoundary(previousWord, word))
                        || (!word.SuppressWrapBefore && wrapsWholeNoWrapRun));
                if (startedNewLine)
                    await StartNewLine();

                if (startedNewLine && naturalWidth > effectiveWrapLimit - columnStartInset &&
                    TryOverflowWrapWord(g, word, effectiveWrapLimit - columnStartInset, out overflowPrefix,
                        out overflowSuffix))
                {
                    ReplaceCollectedWordWithOverflowWrapSplit(words, i, word, overflowPrefix!, overflowSuffix!);
                    ShiftFloatsAfter(i);
                    word = overflowPrefix!;
                    (naturalWidth, naturalHeight) = NaturalWordSize(g, word);
                    wordRectInline = naturalWidth;
                    wordAdvance = naturalWidth + word.ActualWordSpacing;
                    wordBlock = naturalHeight;
                }

                var physical = frame.ToPhysical(new RRect(inlineOffset, blockOffset, wordRectInline, wordBlock));
                word.Left = physical.X;
                word.Top = physical.Y;
                word.Width = physical.Width;
                word.Height = physical.Height;

                // The horizontal counterpart of FlowBox's own assignment - see
                // CssRect.PrecededByWordSeparator. This flow walks one flat word list rather than
                // recursing through inline boxes, so it never adds a whitespace-only box's own advance
                // and the two word-level sources are all there is.
                word.PrecededByWordSeparator = pendingWordSeparator || word.HasSpaceBefore;
                pendingWordSeparator = word.HasSpaceAfter;

                line.ReportExistanceOf(word);
                (trailingRegionalIndicatorCount, trailingGraphemeContext) = UpdateTrailingTextState(
                    word, trailingRegionalIndicatorCount, trailingGraphemeContext);

                inlineOffset += wordAdvance;

                // The column's cross-axis thickness is the line box's own extent (CSS 2.1 §10.8.1), not the
                // word's glyph footprint - `wordBlock` above stays the glyph content area, because that is
                // what the word's own rectangle and its inline box's background/border area are sized from.
                // Same rule and same shared helper as FlowBox's horizontal counterpart, so a declared
                // line-height means the same thing in both writing modes; replaced content (IsImage) is
                // still sized from its own box, exactly as it is there.
                lineThickness = Math.Max(lineThickness,
                    word.IsImage ? wordBlock : LineBoxExtentOf(word, blockBox));
            }

            maxInlineExtentUsed = Math.Max(maxInlineExtentUsed, inlineOffset);

            // Floats after the last word, and the ones pinned to the bottom edge, which
            // CssBox.PerformLayoutEpilogue moves there once that edge is final. Placed before the auto width
            // settles, as the ones in the loop above were, so shrinking moves them all together.
            await PlaceFloatsUpTo(int.MaxValue, blockOffset + lineThickness);
            blockBox.SetPendingBottomFloats(bottomFloats);

            // Auto height/width must settle before text-align/bidi below - both read the box's own final
            // ClientTop/ClientBottom, which ApplyHeight would otherwise not have resolved yet. This also
            // moves BubbleRectangles/AssignRectanglesToBoxes (formerly run immediately after the word loop)
            // to after settling - safe, since neither ever reads blockBox.ActualBottom/ActualRight.
            if (heightIsAuto)
            {
                blockBox.ActualBottom = clientTop + Math.Min(maxInlineExtentUsed, wrapLimit)
                    + blockBox.ActualPaddingBottom + blockBox.ActualBorderBottomWidth;
            }

            if (widthIsAuto)
            {
                // blockOffset only folds in a *finished* line's own thickness (StartNewLine's job) - the
                // last line's is still sitting in lineThickness, unmoved, when the loop above ends.
                var contentBlockExtent = blockOffset + lineThickness;
                ShrinkAutoWidthTo(blockBox, frame, contentBlockExtent);
            }

            // The box's own real final inline-axis bottom edge - NOT blockBox.ClientBottom: for a box with
            // a definite (non-auto) height, ActualBottom (and so ClientBottom) isn't actually settled until
            // ApplyHeight runs in the layout epilogue, well after this method returns. clientTop needs no
            // equivalent care - it never moves once this method starts. Reuses the exact same formula the
            // heightIsAuto branch above just used to set ActualBottom, minus the padding/border it folds in
            // (ClientBottom is a content-box edge; clientTop already is too).
            var finalClientBottom = heightIsAuto
                ? clientTop + Math.Min(maxInlineExtentUsed, wrapLimit)
                : clientTop + wrapLimit;

            // Rebuilt against the box's now-final Client edges - frame (above) was built against
            // heightIsAuto's page-height wrap-limit fallback, which is not a real edge for the
            // logical<->physical conversions text-align/bidi need below.
            var finalFrame = WritingModeFrame.ForContentBox(blockBox.ClientLeft, clientTop,
                blockBox.ClientRight, finalClientBottom, blockBox.WritingMode.Value, blockBox.Direction.Value);

            // A direction:rtl vertical box's own natural word placement (the loop above) is anchored against
            // clientTop + wrapLimit - for an auto-height box that is only a provisional placeholder edge, not
            // this box's real final bottom. Worse, finalClientBottom (computed locally just above) is itself
            // not fully final for ANY height (auto or definite): min-height/max-height clamping
            // (CssLayoutEngine.ApplyHeight/GetBoxHeight) only runs later, in the layout epilogue, and applies
            // regardless of whether this box's own `height` is auto or an explicit length - reusing this
            // method's own local edge here would reintroduce a version of issue #778's own symptom for text
            // whenever min-height/max-height moves the box's real bottom away from that local value. So for
            // every direction:rtl vertical box, finalize is deferred to CssBox.PerformLayoutEpilogue instead
            // of running immediately - see CssBox._pendingVerticalInlineFinalize's own remarks. LTR vertical
            // is unaffected either way, since its natural placement never depends on clientBottom at all, so
            // deferring would have nothing to gain there.
            if (frame.InlineStartIsBottom)
            {
                blockBox._pendingVerticalInlineFinalize = true;
            }
            else
            {
                FinalizeVerticalLineBoxes(blockBox, finalFrame, clientTop, finalClientBottom);
            }

            await LayoutOutOfFlowDescendants(g, blockBox, outOfFlowDescendants);
        }

        private static void ReplaceCollectedWordWithOverflowWrapSplit(List<CssRect> collectedWords, int index,
            CssRect original, CssRectWord prefix, CssRectWord suffix)
        {
            var ownerWords = original.OwnerBox.Words;
            var ownerIndex = ownerWords.IndexOf(original);
            if (ownerIndex >= 0)
            {
                ownerWords[ownerIndex] = prefix;
                ownerWords.Insert(ownerIndex + 1, suffix);
            }

            collectedWords[index] = prefix;
            collectedWords.Insert(index + 1, suffix);
        }

        /// <summary>
        /// The per-column text-align/bidi finalize pass for <see cref="CreateVerticalLineBoxes"/>'s own line
        /// boxes, against the box's real final inline-axis edges. Takes <paramref name="finalFrame"/>/
        /// <paramref name="clientTop"/>/<paramref name="clientBottom"/> as parameters rather than reading them
        /// live off <paramref name="blockBox"/> - the immediate call site (a definite-height box, or an
        /// auto-height LTR vertical one) still needs its own locally-computed edges, since
        /// <paramref name="blockBox"/>'s own <c>ClientBottom</c> is not genuinely final until
        /// <c>ApplyHeight</c> runs in the layout epilogue; only the deferred call site
        /// (<see cref="CssBox.PerformLayoutEpilogue"/>, after <c>ApplyHeight</c> has run) can safely pass the
        /// box's own live Client edges.
        /// </summary>
        internal static void FinalizeVerticalLineBoxes(CssBox blockBox, WritingModeFrame finalFrame,
            double clientTop, double clientBottom)
        {
            for (var i = 0; i < blockBox.LineBoxes.Count; i++)
            {
                var lineBox = blockBox.LineBoxes[i];
                var isLastColumn = i == blockBox.LineBoxes.Count - 1;

                // text-align before bidi, for the same reason FinalizeLineBoxes orders them this way for
                // horizontal flow: alignment establishes the column's own outer span, and bidi only ever
                // reflects positions within that span - it never moves the span's own edges.
                //
                // The column's own span, not the box's: a float pinned to the physical top or bottom took that
                // end of it (VerticalTopInset/VerticalBottomInset), and alignment is within what is left.
                var spanTop = clientTop + lineBox.VerticalTopInset;
                var spanBottom = clientBottom - lineBox.VerticalBottomInset;
                ApplyVerticalTextAlignment(lineBox, finalFrame, isLastColumn, spanTop, spanBottom);
                ApplyVerticalBidiReordering(lineBox, finalFrame, spanTop, spanBottom);

                BubbleRectangles(blockBox, lineBox);
                lineBox.AssignRectanglesToBoxes();
            }
        }

        /// <summary>
        /// The inline-axis span a column starting at block-axis position <paramref name="blockOffset"/> can
        /// actually use: where its content starts (the room a float pinned to the physical top or bottom takes at
        /// the column's inline-start end) and how far it may run (<paramref name="wrapLimit"/>, less what a float
        /// takes at the other end). Both come from the floated siblings found via
        /// <see cref="DomUtils.GetVerticalFloatInsets"/>. Computed once per column (called from <c>StartNewLine</c>,
        /// not per word) since a column's own block-axis position, unlike a horizontal line's right-float wrap
        /// boundary, never changes mid-column.
        /// </summary>
        private static (double StartInset, double WrapLimit, double TopInset, double BottomInset) ComputeColumnInlineSpan(
            CssBox blockBox, WritingModeFrame frame,
            double clientTop, double wrapLimit, double blockOffset, List<CssBox.VerticalFloatPlacement>? ownFloats = null)
        {
            var columnBlockAxisPoint = frame.ToPhysical(0, blockOffset).X;

            // The same provisional bottom edge frame itself was built from (clientTop + wrapLimit), not
            // blockBox.ClientBottom - which, for an auto-height box, is not yet resolved at this point.
            var (topInset, bottomInset) = DomUtils.GetVerticalFloatInsets(
                blockBox, columnBlockAxisPoint, frame.BlockStartIsRight, clientTop, clientTop + wrapLimit);

            // Floats this box's own inline flow placed are its children, which the scan of preceding siblings
            // does not reach.
            if (ownFloats is not null)
            {
                foreach (var own in ownFloats)
                {
                    if (own.Box.VerticalFloatOccupancy is not { } reach) continue;

                    if (DomUtils.VerticalFloatCoversBlockPoint(own.Box, columnBlockAxisPoint, frame.BlockStartIsRight))
                    {
                        reach.GrowInsets(clientTop, clientTop + wrapLimit, ref topInset, ref bottomInset);
                    }
                }
            }

            // The line-left float (physical top) is at the column's start when inline-start is the top and at
            // its end when inline-start is the bottom, and the reverse for the line-right one.
            var (startInset, endInset) = frame.InlineStartIsBottom ? (bottomInset, topInset) : (topInset, bottomInset);

            // A float covering the whole column leaves no usable extent, but a column still holds one word.
            return (startInset, Math.Max(startInset, wrapLimit - endInset), topInset, bottomInset);
        }

        /// <summary>
        /// Shrinks <paramref name="blockBox"/>'s auto width to <paramref name="contentBlockExtent"/> (the
        /// content's own block-axis extent under a vertical writing mode - see the caller's own remarks on
        /// why block-start stays fixed and only the block-end edge moves). Reuses <paramref name="frame"/>'s
        /// own <see cref="WritingModeFrame.ToPhysical(double, double)"/>/<see cref="WritingModeFrame.BlockStartIsRight"/>
        /// rather than re-deriving the rl/lr edge arithmetic a second time - the exact edge every word
        /// position was already placed against. <see cref="CssBox.ActualRight"/> is a <em>written</em>
        /// property whose getter is <c>Location.X + Size.Width</c> - moving <see cref="CssBox.Location"/>
        /// alone (the vertical-rl branch) would silently drag ActualRight along with it, since Size.Width
        /// doesn't change on its own, so the true (pre-shrink) right edge has to be captured and re-applied
        /// *after* Location moves, which re-derives Size.Width against the new Location.X through the
        /// normal setter rather than leaving it stale.
        /// </summary>
        /// <remarks>
        /// Internal rather than private: <see cref="CssBox.LayoutVerticalBlockChildren"/> reuses this
        /// directly for its own auto-width shrink (issue #760) rather than duplicating the
        /// <see cref="WritingModeFrame.BlockStartIsRight"/>-vs-<c>Location.X</c> subtlety above.
        /// <para>
        /// For an absolutely/fixed-positioned box under <c>vertical-rl</c>, <c>Location.X</c> is not a
        /// block-start edge free to move - it was already set by <see cref="CssBox.CommitBlockChildOffset"/>
        /// from the box's own CSS <c>left</c> offset before this box's own content layout ran, so the
        /// vertical-rl branch below pins <c>Location.X</c> and shifts the already-laid-out content instead
        /// (issue #798), via <see cref="CssBox.OffsetContentLeft"/>. This deliberately checks
        /// <c>Position.Value</c> rather than the broader <see cref="CssBox.IsOutOfFlow"/> (which also
        /// covers floats): a float reaches <see cref="CssBox.CommitBlockChildOffset"/>'s flow-stacking
        /// branch instead, so its <c>Location.X</c> is a computed avoidance position, not a CSS offset -
        /// exactly as free to move as an ordinary in-flow box's, and correctly takes the ordinary branch
        /// below.
        /// </para>
        /// </remarks>
        internal static void ShrinkAutoWidthTo(CssBox blockBox, WritingModeFrame frame, double contentBlockExtent)
        {
            // The content-box edge the block axis has reached, in the same physical coordinate
            // ToPhysical already anchors every word to - independent of which side is block-start.
            var contentFarEdgeX = frame.ToPhysical(0, contentBlockExtent).X;

            if (frame.BlockStartIsRight && blockBox.Position.Value is PositionMode.Absolute or PositionMode.Fixed)
            {
                // Content was laid out against a placeholder block-start anchor (this box's pre-shrink
                // ActualRight) before its true extent was known, but Location.X is pinned to a CSS `left`
                // offset that must not move. ToPhysical is a pure translation for a fixed frame, so the
                // gap between where the content's block-end edge landed and where Location.X actually is
                // is a uniform shift - move the content to close it, not Location.
                var trueRight = blockBox.ActualRight;
                var contentLeftEdge = contentFarEdgeX - blockBox.ActualPaddingLeft - blockBox.ActualBorderLeftWidth;
                var delta = blockBox.Location.X - contentLeftEdge;

                if (delta != 0) blockBox.OffsetContentLeft(delta);

                blockBox.ActualRight = blockBox.Location.X + (trueRight - contentLeftEdge);
                return;
            }

            if (frame.BlockStartIsRight)
            {
                var trueRight = blockBox.ActualRight;
                blockBox.Location = blockBox.Location with
                {
                    X = contentFarEdgeX - blockBox.ActualPaddingLeft - blockBox.ActualBorderLeftWidth
                };
                blockBox.ActualRight = trueRight;
            }
            else
            {
                blockBox.ActualRight = contentFarEdgeX
                    + blockBox.ActualPaddingRight + blockBox.ActualBorderRightWidth;
            }
        }

        /// <summary>
        /// A word's natural (horizontal, pre-rotation) size, re-derived the same way
        /// <see cref="CssBox.MeasureWordsSize"/> itself computes it - deliberately not read off
        /// <see cref="CssRect.Width"/>/<see cref="CssRect.Height"/>, which <see cref="CreateVerticalLineBoxes"/>
        /// overwrites with the word's *physical* (rotated) footprint once placed.
        /// </summary>
        private static (double Width, double Height) NaturalWordSize(RGraphics g, CssRect word)
        {
            // Cached once per word (see CssRect.NaturalSize's own remarks): both to avoid re-shaping the
            // same text on every repeated layout pass, and because an image/leader word's Width/Height -
            // read as-is below, the same as MeasureWordsSize itself does for them - are only trustworthy
            // as "natural" the first time this runs, before this box's own placement loop below has had a
            // chance to overwrite them with the physical (rotated) footprint.
            if (word.NaturalSize is { } cached) return cached;

            (double Width, double Height) natural;
            if (word.IsImage || word is CssRectLeader)
            {
                natural = (word.Width, word.Height);
            }
            else
            {
                // Mirrors FragmentPainter.Text.cs's PaintWords isUpright decision exactly: upright/
                // sideways force one answer for every word on the box; mixed (the default) defers to
                // the word's own per-fragment IsUprightOrientation, set at word-split time (CssBox
                // .AddWord/EmitPerCodepointFragments). Branching on word.IsUprightOrientation alone -
                // which CssBox only ever sets true under mixed orientation - would size an
                // upright-forced box's words with the rotated-run formula below while paint always
                // takes the per-character upright path for that box, reserving the wrong extent for
                // what's actually painted (the same class of layout/paint desync issue #770's
                // per-character advance fix addressed on the other axis).
                var isUpright = word.OwnerBox.TextOrientation.Value switch
                {
                    TextOrientation.Upright => true,
                    TextOrientation.Sideways => false,
                    _ => word.IsUprightOrientation
                };

                if (isUpright)
                {
                    // An upright run paints each character individually, stacked down the column
                    // (FragmentPainter.Text.cs's PaintUprightVerticalRun) rather than as one natural
                    // horizontal glyph run reoriented as a whole (the rotated case below) - so the
                    // down-the-column extent reserved for it here has to match what paint actually steps
                    // by. When the resolved font carries real OpenType vertical metrics (vhea/vmtx -
                    // RFont.HasVerticalMetrics), that step is each character's own real vmtx advance
                    // height (issue #770). Otherwise it falls back to the font's own line height
                    // (ascender + descender), not each character's individually-measured horizontal
                    // advance width: RGraphics.DrawString always renders a glyph across the font's full
                    // line-height span from its anchor (XGraphicsPdfRenderer.DrawString's
                    // XLineAlignment.Near branch adds cyAscent above the baseline and leaves cyDescent
                    // below it - a fixed span, independent of the specific glyph's own ink), while a
                    // codepoint's own hmtx advance width can be narrower (a real subsetted CJK font
                    // measured ~9pt advance against a ~13pt line height at the same size) - stepping by
                    // the narrower value then visibly overlapped each character with the next, and
                    // overran into whatever followed once the run finished. This fallback is the closest
                    // available per-character constant that can never under-advance for the large
                    // majority of fonts, which carry no real vertical metrics at all.
                    var styleSource = word.FirstLineStyle ?? word.OwnerBox;
                    var font = CssBox.ResolveWordFont(word, styleSource);
                    var text = word.Text ?? "";
                    double width;

                    if (font.HasVerticalMetrics)
                    {
                        width = 0;
                        foreach (var rune in text.EnumerateRunes())
                            width += font.GetVerticalAdvance(rune) + styleSource.ActualLetterSpacing;
                    }
                    else
                    {
                        // A plain rune count, not MeasureUprightRunCharacters: the per-character advance
                        // here is a flat font.Height step, independent of each character's own measured
                        // size, so running every character through real glyph shaping here just to
                        // discard the result would be pure waste - MeasureUprightRunCharacters stays
                        // reserved for PaintUprightVerticalRun, which does need each character's own
                        // width for centering.
                        var runeCount = text.EnumerateRunes().Count();
                        width = runeCount * (font.Height + styleSource.ActualLetterSpacing);
                    }

                    // The cross-axis (column-thickness) reservation has to come from this same resolved
                    // font, not styleSource.ActualFont: PaintUprightVerticalRun centers each character
                    // using widths measured through this exact font (FragmentPainter.Text.cs resolves the
                    // identical CssBox.ResolveWordFont(word, styleSource) before calling it), and a
                    // per-codepoint fallback face (UsesPerCodepointFont) can have a materially different
                    // line height than the box's own default font. vmtx/VORG govern only the down-the-
                    // column advance above, not this cell-width sizing.
                    natural = (width, font.Height);
                }
                else
                {
                    var styleSource = word.FirstLineStyle ?? word.OwnerBox;
                    var font = CssBox.ResolveWordFont(word, styleSource);
                    // Per-word ScriptTag/JoiningForms, not the box-level ActualTextShapingFeatures alone
                    // (see ResolveWordShapingFeatures' own remarks) - a rotated (sideways) run still
                    // shapes as one horizontal glyph run before being reoriented as a whole, so it needs
                    // the same per-word script/joining-form-aware shaping request FragmentPainter.Text.cs's
                    // PaintWords resolves for the matching upright-vs-rotated paint path; measuring with
                    // only the box-level default here would under/over-reserve width for a script whose
                    // GSUB joining substitution changes glyph count or advances (e.g. Arabic-family
                    // joining), the same class of layout/paint desync issue #770's per-character advance
                    // fix addressed on the other axis.
                    var wordFeatures = styleSource.ResolveWordShapingFeatures(word);
                    var width = word.Text != "\n" ? g.MeasureString(word.Text!, font, wordFeatures).Width : 0;
                    if (word.Text != "\n" && styleSource.ActualLetterSpacing != 0)
                        width += g.CountShapedGlyphs(word.Text!, font, wordFeatures) * styleSource.ActualLetterSpacing;

                    natural = (width, styleSource.ActualFont.Height);
                }
            }

            word.NaturalSize = natural;
            return natural;
        }

        /// <summary>
        /// Measures each codepoint of an upright vertical-writing-mode run individually - the single
        /// shared basis both this file's own <see cref="NaturalWordSize"/> and
        /// <see cref="Paint.FragmentPainter.PaintUprightVerticalRun">FragmentPainter.PaintUprightVerticalRun</see>
        /// iterate to agree on the run's character sequence and count. The down-the-column *advance*
        /// per character is not this measurement's own <c>Size.Width</c> (see <see cref="NaturalWordSize"/>'s
        /// remarks for why) - each yielded <c>Size</c> is used only for the glyph's cross-axis
        /// (column-thickness) centering, which legitimately does vary per character.
        /// </summary>
        internal static IEnumerable<(string Text, Rune Rune, RSize Size)> MeasureUprightRunCharacters(
            RGraphics g, string text, RFont font, ShapeSettings? features)
        {
            foreach (var rune in text.EnumerateRunes())
            {
                var charText = rune.ToString();
                yield return (charText, rune, g.MeasureString(charText, font, features));
            }
        }

        /// <summary>
        /// A box's own definite (non-auto) CSS <c>height</c>, resolved to a real content-height number -
        /// mirroring <see cref="GetBoxHeight"/>'s own definite-length branch (percentage-against-containing-
        /// block resolution, then the box-sizing conversion) rather than a second, independently-derived
        /// formula, then converted from that branch's border-box result down to the content height
        /// <see cref="CreateVerticalLineBoxes"/>'s wrap limit actually needs.
        /// </summary>
        internal static double DefiniteContentHeight(CssBox box)
        {
            var borderBoxHeight = CssValueParser.ParseLength(box.Height, box.ContainingBlock.Size.Height, box) + box.ActualBoxSizeIncludedHeight;

            return borderBoxHeight - box.ActualPaddingTop - box.ActualPaddingBottom
                - box.ActualBorderTopWidth - box.ActualBorderBottomWidth;
        }

        /// <summary>
        /// Words are parsed into placeholder <see cref="CssRect"/>s during DOM construction, but only
        /// measured (given a real <see cref="CssRect.Width"/>/<see cref="CssRect.Height"/>) lazily, once
        /// per box, just before use - the same on-demand measurement <see cref="FlowBox"/> itself performs
        /// per child as it recurses (see its own <c>await b.MeasureWordsSize(g)</c> call), since a block
        /// box's own <see cref="CssBox.MeasureWordsSize"/> call in its prologue only measures its own
        /// direct words, not its descendants'.
        /// </summary>
        private static async ValueTask MeasureAndCollectWordsInDocumentOrder(RGraphics g, CssBox box,
            List<CssRect> words, List<(CssBox Box, int WordIndex)> outOfFlowDescendants)
        {
            if (box.Words.Count > 0)
            {
                box.RectanglesReset();
                await box.MeasureWordsSize(g);
                words.AddRange(box.Words);
            }

            foreach (var child in box.Boxes)
            {
                // A floated or absolutely/fixed-positioned descendant establishes its own formatting/
                // positioning context (CSS2.1 §9.7/§10.1) - its whole subtree is excluded from this box's
                // own word stream, not recursed into at all, and laid out separately once this box's own
                // auto sizing has settled (LayoutOutOfFlowDescendants, called from CreateVerticalLineBoxes's
                // own tail) rather than here, since a same-box positioned ancestor must not resolve its
                // offsets against this box's not-yet-final ClientLeft/Top/Width/Height.
                //
                // Reached by real content: DomParser leaves a float (#1038) or an absolutely positioned box
                // (#1299) among the inline content it sits in rather than splitting that content around it,
                // so one can be a descendant of a box this method is called on. Folding its words into the
                // column flow would place its content inline.
                if (child.IsOutOfFlow)
                {
                    // Where in the word stream it sits, for a float: it is placed at the block-axis position
                    // the column flow has reached by then (CSS 2.1 section 9.5.1 rule 6, axes swapped).
                    outOfFlowDescendants.Add((child, words.Count));
                    child.ResetVerticalFloatOccupancy();
                    continue;
                }

                await MeasureAndCollectWordsInDocumentOrder(g, child, words, outOfFlowDescendants);
            }
        }

        /// <summary>
        /// Lays out every box <see cref="MeasureAndCollectWordsInDocumentOrder"/> pulled out of a vertical
        /// box's own word stream (a floated or absolutely/fixed-positioned inline-nested descendant) via the
        /// same general, physical-coordinate <see cref="CssBox.LayoutBlockChild"/> entry point every other
        /// out-of-flow child already uses (<see cref="CssBox.LayoutOutOfFlowChildren"/>, <see cref="CssBox.LayoutVerticalBlockChildren"/>'s
        /// own <c>IsOutOfFlow</c> branch) - reused as-is, since a float's own physical `left`/`top`/`right`/
        /// `bottom` (position:absolute/fixed) or float placement (CssLayoutEngine.FloatBox, called from
        /// CommitBlockChildOffset) needs no writing-mode awareness of its own. Called only after
        /// <paramref name="blockBox"/>'s own auto width/height have settled, so a same-box positioned
        /// ancestor resolves against final geometry.
        /// </summary>
        private static async ValueTask LayoutOutOfFlowDescendants(RGraphics g, CssBox blockBox,
            List<(CssBox Box, int WordIndex)> outOfFlowDescendants)
        {
            foreach (var (child, _) in outOfFlowDescendants)
            {
                if (child.DerivedStyle.ActualDisplay == Keywords.None) continue;

                // A float the column flow already placed (CreateVerticalLineBoxes) is done.
                if (child.IsFloated && child.VerticalFloatOccupancy is not null) continue;

                await blockBox.LayoutBlockChild(g, child);
            }
        }

        /// <summary>
        /// Takes back the line a forced break at the very end of a block's content opened, which
        /// CSS 2.1 <see href="https://www.w3.org/TR/CSS21/visuren.html#inline-formatting">§9.4.2</see>
        /// says is not there.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A forced line break (<see href="https://www.w3.org/TR/css-text-3/#forced-line-break">css-text-3
        /// §5.5</see>) ends the line it falls on. This engine instead closes that line and places the
        /// break's own word at the start of the next one, so a break is always <i>followed</i> by a
        /// line — including where nothing follows it in the block, and that line holds nothing.
        /// §9.4.2 is explicit about such a line: one with "no text, no preserved white space, no
        /// inline elements with non-zero margins, padding, or borders, and no other in-flow content"
        /// must "be treated as not existing for any other purpose". A <c>&lt;br&gt;</c> is not the
        /// preserved newline that section carves out. The block measured one line taller than a
        /// browser makes it, for <c>x&lt;br&gt;</c> as much as for a bare <c>&lt;br&gt;</c>.
        /// </para>
        /// <para>
        /// Only ever the last line, and only when it holds nothing but breaks: <c>a&lt;br&gt;&lt;br&gt;</c>
        /// is genuinely two lines and <c>a&lt;br&gt;&lt;br&gt;c</c> three, so each break but the final
        /// one keeps the line it opened. A line with no words at all is left alone too — that is a
        /// block with no inline content, not a break's leftover.
        /// </para>
        /// </remarks>
        private static void DropATrailingForcedBreaksOwnLine(CssBox blockBox, CssLineBoxCoordinates coordinates)
        {
            if (blockBox.LineBoxes.Count <= 1) return;

            var last = blockBox.LineBoxes[^1];

            if (last.Words.Count == 0) return;

            foreach (var word in last.Words)
            {
                if (!word.IsLineBreak) return;
            }

            blockBox.LineBoxes.Remove(last);

            // The block now ends where the line it just lost began, which is the bottom of the line
            // the break actually terminated.
            // The line box's own top, not its first word's - a word sits half a leading below the line
            // it is on (CSS 2.1 §10.8.1), and reading it here made the block that much too tall.
            coordinates.MaxBottom = last.LineTop;
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
        /// fragmentation break, and <see cref="EndsAParagraph"/> is the one thing that has to know: a line
        /// that ends at a break is <b>not</b> the block's last line — the block continues in the next
        /// fragmentainer — so
        /// <see href="https://www.w3.org/TR/css-text-3/#text-align-property">css-text-3 §6.1</see>'s
        /// "except the last line" rule does not hand it to <c>text-align-last</c>. Reading it off the list
        /// alone justified nothing at a page boundary, because the line the pass stopped on is the last one
        /// the list holds.
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
                WidenAtomicInlineRectangles(lineBox);
                ApplyVerticalAlignment(lineBox);
                HeightenAtomicInlineRectangles(lineBox);
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
            var availWidth = lineBox.ContentRight - lineBox.ContentLeft - indent;

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
        /// <param name="isVertical">
        /// whether <paramref name="cell"/> belongs to a <c>vertical-rl</c>/<c>vertical-lr</c> table, whose
        /// row axis (what css-tables-3 aligns a cell's content along) is physical X rather than physical Y.
        /// A cell's own <c>ClientLeft</c>/<c>ClientRight</c> already describe its final, post-reflection
        /// physical position by the time this runs (table layout settles cell geometry, including the
        /// <c>vertical-rl</c> mirror pass, before ever calling this), so - unlike some other axis-aware
        /// sites in <see cref="Dom.CssLayoutEngineTable"/> - no separate "which physical edge is
        /// row-axis-start" flag is needed here: <c>top</c>/<c>baseline</c> stay a no-op (content already
        /// lays out from the cell's own block-start edge forward) and <c>bottom</c>/<c>middle</c> push
        /// toward <c>ClientRight</c>, uniformly for both <c>vertical-rl</c> and <c>vertical-lr</c>.
        /// </param>
        /// <returns>the distance every child of <paramref name="cell"/> was offset by, zero where none was</returns>
        public static double ApplyCellVerticalAlignment(RGraphics g, CssBox cell, bool isVertical = false)
        {
            ArgumentNullException.ThrowIfNull(g);
            ArgumentNullException.ThrowIfNull(cell);

            // CSS 2.1 §17.5.3: a table cell's vertical-align only recognizes this keyword subset - a
            // length/percentage (issue #603's own grammar addition) has no meaning here and falls to the
            // same no-op default as sub/super/text-top/text-bottom/PeachBaselineMiddle already do.
            var cellKeyword = cell.VerticalAlign.Value.Keyword;

            if (cellKeyword is VerticalAlignment.Top or VerticalAlignment.Baseline)
                return 0d;

            var cellFarEdge = isVertical ? cell.ClientRight : cell.ClientBottom;

            // Measure over the cell's children, not the cell itself: GetMaximumBottom/GetMaximumRight's own
            // explicit-size fallback exists for a childless box with an explicit height/width, but the cell
            // always has an explicit ActualBottom/ActualRight by this point (the row just stretched it), so
            // passing the cell as startBox would clamp the measured edge to cellFarEdge and always compute
            // a zero offset whenever the cell also carries a CSS height/width (issue found via `height` +
            // `vertical-align: middle`).
            //
            // An absolutely positioned child is never measured: it is not content of the cell (CSS 2.1
            // §10.6.3). Measured, a `top: 100%` child below the text pushed that text above the cell.
            // GetMaximumBottom/GetMaximumRight skip such boxes further down the same way.
            var farEdge = isVertical ? cell.ClientLeft : cell.ClientTop;
            foreach (var b in cell.Boxes)
            {
                if (b.IsAbsolutelyPositioned) continue;

                farEdge = isVertical ? CssBox.GetMaximumRight(b, farEdge) : CssBox.GetMaximumBottom(b, farEdge);
            }

            var dist = cellKeyword switch
            {
                VerticalAlignment.Bottom => cellFarEdge - farEdge,
                VerticalAlignment.Middle => (cellFarEdge - farEdge) / 2,
                _ => 0d
            };

            OffsetCellContent(cell, dist, isVertical);

            return dist;
        }

        /// <summary>
        /// Moves <paramref name="cell"/>'s content along its alignment axis by <paramref name="dist"/>, the
        /// way <see cref="ApplyCellVerticalAlignment"/> aligns it: every child, and the absolutely positioned
        /// boxes that belong at a static position in that content, but not a box its containing block places
        /// on that axis. Also what undoes an alignment (<c>TableRowCursor.Retract</c>, with the distance
        /// negated), which has to move exactly the same boxes.
        /// </summary>
        /// <param name="cell">the table cell whose content moves</param>
        /// <param name="dist">the distance, positive towards the cell's far edge</param>
        /// <param name="isVertical">whether the cell's table is vertical, which makes X the alignment axis</param>
        internal static void OffsetCellContent(CssBox cell, double dist, bool isVertical)
        {
            if (dist == 0d) return;

            foreach (var b in cell.Boxes)
            {
                if (IsPlacedByItsContainingBlockAlong(b, isVertical)) continue;

                OffsetAlongCellAxis(b, dist, isVertical);
                MoveStaticallyPlacedDescendants(b, b, dist, isVertical);
            }
        }

        private static void OffsetAlongCellAxis(CssBox box, double dist, bool isVertical)
        {
            if (isVertical) box.OffsetLeft(dist);
            else box.OffsetTop(dist);
        }

        /// <summary>
        /// Moves the absolutely positioned descendants of <paramref name="box"/> that the translation rooted at
        /// <paramref name="translationRoot"/> skipped, because their containing block lies outside it, but
        /// whose offsets on the alignment axis are both `auto`.
        /// </summary>
        /// <remarks>
        /// Such a box belongs at its static position (CSS 2.1 §10.6.4), which is in the content being moved,
        /// just as for a direct child of the cell (<see cref="IsPlacedByItsContainingBlockAlong"/>). Without
        /// this, one nested in an inline - <c>&lt;td&gt;text &lt;span&gt;x&lt;span style="position:
        /// absolute"&gt;</c> - stayed at its containing block's corner while a middle-aligned cell moved the
        /// text down around it.
        /// </remarks>
        /// <param name="box">the box whose descendants are walked</param>
        /// <param name="translationRoot">the box the translation that reached <paramref name="box"/> was rooted at</param>
        /// <param name="dist">the distance the cell's content was moved by</param>
        /// <param name="isVertical">whether the cell's table is vertical, which makes X the alignment axis</param>
        private static void MoveStaticallyPlacedDescendants(CssBox box, CssBox translationRoot, double dist, bool isVertical)
        {
            foreach (var child in box.Boxes)
            {
                if (!child.EscapesTranslationOf(translationRoot))
                {
                    MoveStaticallyPlacedDescendants(child, translationRoot, dist, isVertical);
                    continue;
                }

                // One placed by its containing block stays, and so does everything placed against it.
                if (IsPlacedByItsContainingBlockAlong(child, isVertical)) continue;

                OffsetAlongCellAxis(child, dist, isVertical);
                MoveStaticallyPlacedDescendants(child, child, dist, isVertical);
            }
        }

        /// <summary>
        /// Whether <paramref name="box"/> is absolutely positioned with an offset set on the axis a table cell
        /// aligns its content along (`top`/`bottom`, or `left`/`right` in a vertical table), so its place
        /// on that axis comes from its containing block alone.
        /// </summary>
        /// <remarks>
        /// Such a box does not move with the cell's aligned content: its containing block is the cell or
        /// something outside it, which the alignment does not move. One with both offsets `auto` belongs at
        /// its static position (CSS 2.1 §10.6.4), which is in the content and moves with it. The static
        /// position itself is not computed (#1303), so the box starts at its containing block's corner, and
        /// moving it with the content keeps it nearer where it belongs.
        /// </remarks>
        /// <param name="box">a child of the cell being aligned, or an absolutely positioned box nested in one</param>
        /// <param name="isVertical">whether the cell's table is vertical, which makes X the alignment axis</param>
        /// <returns>true to leave the box where it is</returns>
        private static bool IsPlacedByItsContainingBlockAlong(CssBox box, bool isVertical) =>
            box.IsAbsolutelyPositioned
            && (isVertical
                ? box.Left.Value.IsValue || box.Right.Value.IsValue
                : box.Top.Value.IsValue || box.Bottom.Value.IsValue);

        public static void FloatBox(CssBox box)
        {
            if (box is { Float.Value: Floating.None, Clear.Value: ClearMode.None })
            {
                return;
            }

            var containingBox = box.ContainingBlock!;

            //Get the start x and y of the blockBox
            var startX = containingBox.ClientLeft;
            var startY = box.Location.Y;

            var currentBoxIdx = containingBox.Boxes.IndexOf(box);

            switch (box.Float.Value)
            {
                case Floating.Left or Floating.Right or Floating.Inside or Floating.Outside
                    or Floating.InlineStart or Floating.InlineEnd:
                    // CSS Page Floats' inside/outside (and css-logical-1's inline-start/inline-end) resolve to an effective left/right based on which
                    // physical side of a two-page spread the float's landing page is (CssBox.EffectiveFloatSide);
                    // left/right pass through unchanged. Once resolved, every left/right code path
                    // (collision scanning, line wrapping, shrink-to-fit, "floats share the line") reads
                    // EffectiveFloatSide rather than the raw Float value, so it applies to all four the
                    // same way.
                    if (box.EffectiveFloatSide == Floating.Right) FloatBoxRight(box, containingBox, startX, startY);
                    else FloatBoxLeft(box, containingBox, startX, startY);
                    break;
                case Floating.Top or Floating.Bottom or Floating.TopBottom or Floating.Snap:
                    FloatBoxPageArea(box);
                    break;
            }

            // `clear` has no meaning for a page float (css-page-floats-3): it never wraps inline content
            // or shares a line, so there is nothing for it to clear past, and ClearBox's containing-block-
            // relative clearance would clobber the page-edge position FloatBoxPageArea just resolved with
            // nothing to re-apply it afterward (unlike left/right/inside/outside below, which handles
            // exactly that).
            if (box.Clear.Value is not ClearMode.None && !box.IsPageFloated)
            {
                ClearBox(box, currentBoxIdx, containingBox);

                // css-break-3 §5.1: clearance can carry the float several bands down, onto a
                // fragmentainer whose inline-end edge differs from the one the scan above placed it
                // against - and ClearBox rewrites Location wholesale, so a float (including inside/outside,
                // whose EffectiveFloatSide is re-resolved against the clearance's own, possibly different,
                // page) has lost its inline-end placement entirely by this point. Re-place it, once, at the
                // clearance it landed on. Bounded by construction: ClearBox itself is not re-run.
                if (box.EffectiveFloatSide == Floating.Right)
                    FloatBoxRight(box, containingBox, startX, box.Location.Y);
                else if (box.EffectiveFloatSide == Floating.Left)
                    FloatBoxLeft(box, containingBox, startX, box.Location.Y);
            }
        }

        /// <summary>
        /// CSS Page Floats (<c>float: top/bottom/top-bottom/snap</c>): moves <paramref name="box"/> to the
        /// block-start/block-end edge of the page its natural (ordinary block-flow) position landed it on,
        /// once <see cref="HtmlContainerInt.ResolvePageFloatsForThisAttempt"/> has decided where that is.
        /// </summary>
        /// <remarks>
        /// This is deliberately a two-attempt convergence, the same shape <c>float: footnote</c> already
        /// uses (<c>HtmlContainerInt.ResolveFootnotesForThisAttempt</c>): which edge a box belongs on
        /// (<c>top-bottom</c>/<c>snap</c>) and how much room every page float on the same edge of the same
        /// page needs both depend on every page float's own measured height, which is only known after a
        /// full layout attempt has already run. So the <b>first</b> attempt leaves <paramref name="box"/>
        /// at the ordinary position <see cref="FloatBox"/> already resolved for it (the position it would
        /// have if <c>Float</c> were <c>none</c>) - which is exactly what
        /// <see cref="HtmlContainerInt.ResolvePageFloatsForThisAttempt"/> reads afterward to discover which
        /// page it lands on. Once that attempt records a decision in
        /// <see cref="HtmlContainerInt.PageFloatPlacements"/> and the document is laid out again, this
        /// method moves the box to its final position before its own content
        /// (<see cref="CssBox.LayoutContents"/>, which runs after <see cref="CssBox.PerformLayoutImp"/>'s
        /// placement phase) lays out - so content lays out directly at the reserved position, with no
        /// separate detached re-layout pass needed the way a footnote body's does.
        /// </remarks>
        private static void FloatBoxPageArea(CssBox box)
        {
            if (box.HtmlContainer is not { HasRealPageGrid: true } container) return;

            container.NotePageFloatColumn(box);

            if (!container.PageFloatPlacements.TryGetValue(box, out var top)) return;

            box.Location = new RPoint(box.Location.X, top);
            box.ActualBottom = top;
        }

        public static double GetActualMarginLeft(CssBox box, double? boxWidth = null)
        {
            var marginLeft = box.MarginLeft.Value;
            if (marginLeft.IsValue)
            {
                return CssValueParser.ParseLength(marginLeft.Value!.Value, box.ContainingBlock.AvailableWidth, box);
            }

            // Exactly one auto margin: it takes whatever the other margin, the borders, the padding and
            // the width leave over (CSS 2.1 §10.3.3, "its used value follows from the equality"). Left at 0
            // this pinned `margin-left: auto` boxes - and so `<hr align=right>`'s mapping - to the start edge.
            if (box.MarginRight.Value.IsValue)
            {
                return IsInFlowBlockLevel(box) ? ResolveSingleAutoHorizontalMargin(box, box.ActualMarginRight) : 0;
            }

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
                return (box.ContainingBlock.AvailableWidth - boxWidth.Value) / 2;
            }

            return ResolveAutoHorizontalMargin(box);
        }

        public static double GetActualMarginRight(CssBox box, double? boxWidth = null)
        {
            var marginRight = box.MarginRight.Value;
            if (marginRight.IsValue)
            {
                return CssValueParser.ParseLength(marginRight.Value!.Value, box.ContainingBlock.AvailableWidth, box);
            }

            if (box.MarginLeft.Value.IsValue)
            {
                return IsInFlowBlockLevel(box) ? ResolveSingleAutoHorizontalMargin(box, box.ActualMarginLeft) : 0;
            }

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
                return (box.ContainingBlock.AvailableWidth - boxWidth.Value) / 2;
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
        private static double ResolveAutoHorizontalMargin(CssBox box) =>
            Math.Max(0, FreeInlineSpace(box)) / 2;

        /// <summary>
        /// The used value of a horizontal <c>margin: auto</c> when <b>exactly one</b> of the two margins is
        /// <c>auto</c> (CSS 2.1 §10.3.3): it takes everything the box's other margin, borders, padding and
        /// width leave over, which is what pushes a <c>margin-left: auto</c> box to the end edge.
        /// </summary>
        /// <param name="box">the box whose <c>auto</c> margin is being resolved</param>
        /// <param name="otherMargin">the used value of its other, non-<c>auto</c> margin</param>
        /// <remarks>
        /// Clamped at 0: an over-constrained box has no negative slack for an <c>auto</c> margin to take, and
        /// browsers resolve it to 0 rather than shifting the box. An <c>auto</c> width fills the containing
        /// block, so its slack is 0 unless a <c>max-width</c> narrows it - the same "definite" test the
        /// both-<c>auto</c> case makes.
        /// </remarks>
        private static double ResolveSingleAutoHorizontalMargin(CssBox box, double otherMargin) =>
            Math.Max(0, FreeInlineSpace(box) - otherMargin);

        /// <summary>
        /// Whether <paramref name="box"/> is an ordinary in-flow, block-level box - the only kind CSS 2.1
        /// §10.3.3's margin resolution applies to as written. A float, an absolutely-positioned box, a table,
        /// a flex/grid item or an inline-level box has margin rules of its own.
        /// </summary>
        internal static bool IsInFlowBlockLevel(CssBox box) =>
            FillsContainingBlockWidth(box)
            && box.DerivedStyle.ActualDisplay is Keywords.Block or Keywords.ListItem;

        /// <summary>
        /// The room left in <paramref name="box"/>'s containing block once its own border box is placed in
        /// it, ignoring its margins: the quantity <c>auto</c> margins are resolved from. Negative when the
        /// box is wider than the block, which callers clamp.
        /// </summary>
        private static double FreeInlineSpace(CssBox box)
        {
            // The containing block's CONTENT width, which is what §10.3.3's constraint is stated over
            // (§10.1 puts the containing block at the content edge of the nearest block container
            // ancestor). Size.Width is that only under `content-box`; under `border-box` it is the border
            // box, so splitting it pushed the box toward the end edge by half the container's own padding
            // and border. AvailableWidth means "content width" under either.
            var containingWidth = box.ContainingBlock.AvailableWidth;

            // A display:block image/SVG's synthetic wrapper (IsReplacedBlockWrapper) forces this box's
            // own Display back to inline purely so it can be sized as an atomic inline word (see the
            // flag's own remarks) - it never goes through GetBoxWidth's own block-width resolution, so
            // its Size.Width (and so ActualBoxSizingWidth, which the branches below read) is never
            // populated. CSS 2.1 §10.3.4 defers a replaced element's width to §10.3.2 - never to "fills
            // the containing block" like the non-replaced-box branch just below - so a declared definite
            // width is read directly here rather than through either branch (issue #1176). Read from
            // `box.Width` itself, not `box.FirstWord.Width`, whenever a declared width exists: FlowBox
            // resolves this same auto-margin question (via leftSpacing) before its own ordinary
            // MeasureWordsSize call for this box, later in the same loop iteration. An intrinsically-sized
            // (no declared length) replaced element still has to fall back to `box.FirstWord.Width`, but
            // FlowBox pre-measures this box (see its own `IsReplacedBlockWrapper` check right before
            // computing leftSpacing) before reaching this method, so the fallback reads a fully-resolved
            // value too, not a stale one (issue #1178).
            if (box.ParentBox is { IsReplacedBlockWrapper: true })
            {
                var replacedContentWidth = CssValueParser.IsValidLength(box.Width)
                    ? CssValueParser.ParseLength(box.Width, containingWidth, box)
                    : box.FirstWord.Width;

                // Mirror the auto-width branch's own max-then-min clamp (CSS 2.1 §10.4) just below -
                // GetBoxWidth would apply this same clamp for a box that went through ordinary block-width
                // resolution, which this one never does (see the remarks above).
                if (CssValueParser.IsValidLength(box.MaxWidth))
                {
                    replacedContentWidth = Math.Min(replacedContentWidth,
                        CssValueParser.ParseLength(box.MaxWidth, containingWidth, box));
                }

                if (box.MinWidth != "0" && CssValueParser.IsValidLength(box.MinWidth))
                {
                    replacedContentWidth = Math.Max(replacedContentWidth,
                        CssValueParser.ParseLength(box.MinWidth, containingWidth, box));
                }

                var replacedUsedWidth = replacedContentWidth + box.ActualBoxSizeIncludedWidth;
                return containingWidth - replacedUsedWidth;
            }

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

                return containingWidth - (usedContentWidth + box.ActualBoxSizeIncludedWidth);
            }

            // Definite width: split the genuine free space, accounting for the box's own
            // border/padding (ActualBoxSizingWidth is the used border-box width, already min/max-clamped).
            return containingWidth - box.ActualBoxSizingWidth;
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

            box.GetMinMaxWidth(g, out var minIntrinsicWidth, out _);

            return minIntrinsicWidth;
        }

        public static async ValueTask<double> GetMaxContentWidth(RGraphics g, CssBox box)
        {
            await MeasureWords(box, g);

            box.GetMinMaxWidth(g, out _, out var maxIntrinsicWidth);

            return maxIntrinsicWidth;
        }

        /// <summary>
        /// Whether <paramref name="box"/> is, on its own, a valid link in an unconstrained per-page-reflow
        /// chain (issues #143/#200): the synthetic root, or an ordinary in-flow block-level box - not a
        /// float, not out-of-flow, not a table/table-cell/flex/grid participant (those are Layers H/I/J's
        /// own concern, tracked as #196-#198) - with an auto width and no <c>max-width</c> clamp. Formerly
        /// restricted to the root/<c>&lt;html&gt;</c>/<c>&lt;body&gt;</c> element by tag name; any plain,
        /// unconstrained block-level wrapper now qualifies too, since nothing about spanning the page area
        /// (CSS Paged Media 3 §5) actually depends on element identity - only on the box genuinely filling
        /// its own containing block's width with no length/percentage cap at this level.
        /// </summary>
        private static bool IsOrdinaryUnconstrainedBlock(CssBox box)
        {
            if (box.IsRoot) return true;

            if (box.DerivedStyle.ActualDisplay is not (Keywords.Block or Keywords.ListItem)) return false;
            if (!FillsContainingBlockWidth(box)) return false;
            if (!string.IsNullOrEmpty(box.Width) && box.Width != Keywords.Auto) return false;
            if (CssValueParser.IsValidLength(box.MaxWidth)) return false;

            return true;
        }

        /// <summary>
        /// Whether <paramref name="box"/> (a candidate containing block) is an <b>unconstrained</b> main
        /// column: it and every ancestor up to the root are <see cref="IsOrdinaryUnconstrainedBlock"/>, so
        /// the chain genuinely spans the page area and a child can safely adopt its own page's content
        /// width (issue #143). If any level carries an explicit/percentage <c>width</c>, a <c>max-width</c>
        /// clamp, or isn't an ordinary in-flow block at all, the chain no longer provably spans the page
        /// area, so per-page reflow is not applied there.
        /// </summary>
        /// <remarks>
        /// <c>internal</c> rather than <c>private</c> so <see cref="Fragmentation.FragmentEmitter"/> can
        /// ask the same question when deciding whether a box's own OUTER frame (as opposed to the text
        /// inside it, which this method's callers already reflow) is eligible for a per-fragment inline
        /// extent (issue #876) — the two must agree on eligibility exactly, so this is shared rather than
        /// re-derived.
        /// </remarks>
        internal static bool IsUnconstrainedMainColumn(CssBox box)
        {
            for (var b = box; b is not null; b = b.ParentBox)
            {
                if (!IsOrdinaryUnconstrainedBlock(b)) return false;
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
        /// <remarks><c>internal</c> for the same reason as <see cref="IsUnconstrainedMainColumn"/>.</remarks>
        internal static double MainColumnRightInset(CssBox box)
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
        /// The inline-end content edge of <paramref name="containingBlock"/> on the fragmentainer whose
        /// band contains document Y <paramref name="blockTop"/> - per-page horizontal reflow (issue
        /// #143): when a per-page <c>@page</c> rule overrides left/right margins (or, for a mixed
        /// page-size document, the sheet width itself), content laid out on a page whose measure differs
        /// from the base uses THAT page's own content-box width (CSS Paged Media 3: "the edges of the
        /// page area act as a containing block for layout that occurs between page breaks" / css-break-3
        /// §5.1: "recalculating sizes and positions using its own size"). Falls back to
        /// <paramref name="containingBlock"/>'s own <see cref="CssBox.ClientRight"/> wherever per-page
        /// measure does not apply (no <see cref="HtmlContainerInt"/>, <see cref="HtmlContainerInt.UseVariableInlineMeasure"/>
        /// is off, or the containing block isn't an unconstrained main column), so callers may invoke it
        /// unconditionally. Shared by <see cref="GetBoxWidth(RGraphics, CssBox, double?)"/>'s own box-width resolution and
        /// <see cref="FloatBox"/>'s displacement scan, rather than each re-deriving the same
        /// page-area-minus-inset expression independently.
        /// </summary>
        internal static double ContentRightOf(CssBox containingBlock, double blockTop)
        {
            if (containingBlock.HtmlContainer is { UseVariableInlineMeasure: true } htmlContainer
                && IsUnconstrainedMainColumn(containingBlock))
            {
                return htmlContainer.PageContentRightOf(blockTop) - MainColumnRightInset(containingBlock);
            }

            return containingBlock.ClientRight;
        }

        /// <summary>
        /// <paramref name="containingBox"/>'s own content width to use as a percentage/<c>min-width</c>/
        /// <c>max-width</c> basis at document Y <paramref name="blockTop"/> — the explicit-length/
        /// percentage counterpart of <see cref="ContentRightOf"/> (which answers the same "what's this
        /// containing block's own page-aware edge" question for the auto-width branch). Reduces to
        /// <paramref name="containingBox"/>'s ordinary <c>ClientRight - ClientLeft</c> (equivalently,
        /// its <see cref="CssBox.Size"/>.Width) — the exact same static basis every caller used before
        /// this existed — wherever per-page measure does not apply, so it never yields a value the
        /// pre-#199/#200/#201 basis wouldn't have. Where it does apply (an unconstrained main-column
        /// containing block under per-page horizontal reflow), a percentage/min/max-width sibling of an
        /// auto-width block now tracks that SAME page-aware measure instead of the single value
        /// <see cref="CssBox.Size"/> happened to hold from an earlier layout generation (issue #199), and
        /// — because <see cref="ContentRightOf"/> unconditionally treats the synthetic root as eligible —
        /// a percentage width resolving against the initial containing block itself now tracks the FIRST
        /// page's own (possibly <c>:first</c>-overridden) area rather than the document's base configured
        /// width (issue #201).
        /// </summary>
        /// <remarks>
        /// <c>internal</c> rather than <c>private</c> so <see cref="CssLayoutEngineTable"/> can resolve
        /// its own table's width against the same page-aware basis (issue #197) — its own
        /// <c>GetAvailableTableWidth</c> bypasses <see cref="GetBoxWidth(RGraphics, CssBox, double?)"/> entirely (a table's width has
        /// its own column-width-driven resolution), so it needs this basis directly rather than through
        /// that method.
        /// </remarks>
        internal static double PageAwareWidthBasis(CssBox containingBox, double blockTop) =>
            ContentRightOf(containingBox, blockTop) - containingBox.ClientLeft;

        /// <summary>
        /// <paramref name="blockBox"/>'s own content-right edge for wrapping the line starting at document
        /// Y <paramref name="y"/> - the per-line counterpart of <see cref="ContentRightOf"/> (which answers
        /// the same question for a box being <i>placed inside</i> a containing block) for the box whose own
        /// children are being flowed. Mirrors <see cref="GetBoxWidth(RGraphics, CssBox, double?)"/>'s own auto-width branch: an
        /// explicit-length or percentage <c>width</c> keeps <paramref name="blockBox"/>'s one already-
        /// resolved <see cref="CssBox.ClientRight"/> - re-sizing the block's own border box per fragment is
        /// <c>Draft.InlineExtent</c>, a separate, not-yet-built fragment-tree contract change - while an
        /// auto-width block re-derives its content edge fresh from whichever page <paramref name="y"/> falls
        /// in, exactly as it would if <see cref="GetBoxWidth(RGraphics, CssBox, double?)"/> were resolving it for the first time there
        /// (css-break-3 §5.1).
        /// </summary>
        private static double LineContentRightOf(CssBox blockBox, double y)
        {
            if (blockBox.Width != Keywords.Auto && !string.IsNullOrEmpty(blockBox.Width))
            {
                return blockBox.ClientRight;
            }

            // ContentRightOf(blockBox.ContainingBlock, y) names the CONTAINING block's own content edge -
            // converting that into blockBox's OWN content-right by subtracting only its margin/border/
            // padding below is only valid when blockBox actually spans its containing block's full width
            // (CSS2.1 §10.3.3's auto-width-fills-container rule). A table cell (sized by the column-width
            // algorithm, §17.5) or a flex/grid item (sized by its own engine) generally does not - each
            // shares its containing block with sibling cells/items that also claim part of the same width,
            // so blindly applying the ordinary-block arithmetic there substituted the *table's* or the
            // *flex container's* width for the cell's/item's own (a multi-column table's rows collapsed to
            // roughly the whole table's width instead of one column). Tables/flex/grid are Layers H/I, not
            // this one, so their own children keep blockBox.ClientRight unchanged, exactly as before this
            // layer - only an ordinary in-flow block genuinely re-derives per page.
            if (!FillsContainingBlockWidth(blockBox))
            {
                return blockBox.ClientRight;
            }

            return ContentRightOf(blockBox.ContainingBlock, y)
                   - blockBox.ActualMarginRight - blockBox.ActualBorderRightWidth - blockBox.ActualPaddingRight;
        }

        /// <summary>
        /// Whether <paramref name="blockBox"/> is an ordinary in-flow block that spans its containing
        /// block's full content width (CSS2.1 §10.3.3) - the precondition <see cref="LineContentRightOf"/>
        /// needs before it may derive blockBox's own edge from its containing block's. False for a float or
        /// an absolutely/fixed-positioned box (sized against its own placement, not its container's
        /// measure), a table cell/table/inline-table/table-caption (CSS2.1 §17.5's column-width algorithm),
        /// or a flex/grid item (sized by its own engine) - each shares its containing block's width with
        /// siblings, rather than claiming all of it alone.
        /// </summary>
        private static bool FillsContainingBlockWidth(CssBox blockBox)
        {
            if (blockBox.IsOutOfFlow) return false;

            if (blockBox.DerivedStyle.ActualDisplay is Keywords.TableCell or Keywords.Table
                or Keywords.InlineTable or Keywords.TableCaption)
            {
                return false;
            }

            var parentDisplay = blockBox.ParentBox?.DerivedStyle.ActualDisplay;
            return parentDisplay is not (Keywords.Flex or Keywords.InlineFlex or Keywords.Grid or Keywords.InlineGrid);
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
            // Keyed off `blockTop` - the border-box top the frame placing this box has just decided on, not
            // the box's own Location.Y, which on the pass that places it still holds whatever position an
            // earlier layout generation left there (page 0's measure, on the first). The frame's offset is
            // settled before this runs precisely so the measure can come from the page the box lands on;
            // where no frame is placing the box (an engine measuring its own container), the box's
            // Location.Y has already been written and is the same answer.
            var availableRight = ContentRightOf(box.ContainingBlock, blockTop ?? box.Location.Y);

            var width = availableRight - box.ContainingBlock.ClientLeft - box.ActualMarginLeft - box.ActualMarginRight;

            if (box.Words.Count > 0)
            {
                // FullWidth carries each word's own trailing inter-word gap, which is right for
                // every word but the last: nothing of this box follows it, so that gap is the space
                // that ends the box's content and it hangs (css-text-3 §4.1.2) rather than widening
                // the box. Counted, an inline run measured one space wider than it draws, and
                // because GetLargestChildWidth folds this into a shrink-to-fit box's fit-content
                // size, every float/inline-block/absolute over a run ending in white space inherited
                // the extra space - the other half of issue #1014.
                width = box.Words.Sum(x => x.FullWidth) - box.Words[^1].ActualWordSpacing;
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
                // (CSS 2.1 §10.1), consistent with GetBoxHeight and the left/top positioning code. A
                // fixed box's containing block is the page area itself (CSS2.1 §10.1: the initial
                // containing block) - resolved directly against ITS OWN page's content-right edge, the
                // fixed-position analogue of CommitBlockChildOffset's own left/top basis for a fixed box,
                // rather than the document's base PageSize.Width (issue #201). An ordinary (or absolute)
                // box's percentage/explicit-length width resolves against its basis box's own page-aware
                // content width via PageAwareWidthBasis (issue #199), which falls back to the exact
                // previous static basis wherever per-page measure doesn't apply.
                var resolvedBlockTop = blockTop ?? box.Location.Y;
                var widthBasis = box.Position.Value is PositionMode.Fixed && box.HtmlContainer is { } wfc
                    ? wfc.PageContentRightOf(resolvedBlockTop) - wfc.MarginLeft
                    : InlinePercentageBase(box)?.Width ?? PageAwareWidthBasis(PercentageBase(box), resolvedBlockTop);
                width = CssValueParser.ParseLength(box.Width, widthBasis, box);
            }

            // CSS 2.1 §10.3.5: a FLOATING box's auto width is shrink-to-fit, not
            // stretch-to-containing-block. Given the stretch width, a float fills its containing
            // block, which leaves `float: right` nowhere to go — it lands at the left as a
            // full-width bar and the content that belongs beside it is pushed onto its own line.
            //
            // Float PLACEMENT was never the problem, which is what makes this hard to see: a float
            // with a DECLARED width lands on a browser's x to the point. Only the auto measurement
            // was wrong.
            //
            // min(max-content, max(min-content, available)) is §10.3.5's own formula, and the same
            // fit-content/min-content pair the orthogonal-flow shrink already uses. Both are outer
            // widths, so they compare against the stretch width as it stands and the box's own
            // decoration comes back off at the end — the same subtraction the plain auto branch
            // makes, and a no-op under box-sizing: border-box.
            if (box is { Width: Keywords.Auto, Position.Value: not (PositionMode.Absolute or PositionMode.Fixed) }
                && box.IsFloated)
            {
                var fit = Math.Max(await GetFitContentWidth(g, box, width), await GetMinContentWidth(g, box));
                width = fit - box.ActualBoxSizeIncludedWidth;
            }
            else if (box is { Width: Keywords.Auto, Position.Value: not PositionMode.Absolute })
            {
                width -= box.ActualBoxSizeIncludedWidth;
            }

            if (box is { Width: Keywords.Auto, Position.Value: PositionMode.Absolute })
            {
                var absCbWidth = InlinePercentageBase(box)?.Width ?? PercentageBase(box).Size.Width;
                if (box.Left.Value.IsValue && box.Right.Value.IsValue)
                {
                    // CSS 2.1 §10.3.7: an absolutely-positioned box with auto width but both `left` and
                    // `right` set fills the space between them in the containing block (rather than shrinking
                    // to fit its content). This is what sizes a Charts.css area/line `td::before` (auto width,
                    // `inset: 0`) to cover its cell.
                    var left = CssValueParser.ParseLength(box.Left.Value.Value!.Value, absCbWidth, box);
                    var right = CssValueParser.ParseLength(box.Right.Value.Value!.Value, absCbWidth, box);
                    // An over-constrained fill (left + right wider than the containing block) clamps to 0, per
                    // CSS 2.1 §10.3.7 (a used width is never negative).
                    width = Math.Max(0, absCbWidth - left - right - box.ActualMarginLeft - box.ActualMarginRight - box.ActualBoxSizeIncludedWidth);
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
                    // CSS 2.1 §10.3.7's shrink-to-fit is §10.3.5's formula verbatim -
                    // min(max(preferred minimum, available), preferred) - so this is the float branch
                    // above, with the same two corrections it already makes and this one used to skip:
                    // the min-content floor (GetFitContentWidth alone only ever narrows toward the
                    // available width, with nothing to stop it going below what the content needs), and
                    // subtracting this box's own decoration, because GetMinMaxWidth returns an OUTER
                    // width - its result already has the box's border and padding folded in - while what
                    // is returned here is a CONTENT width the caller adds them back onto.
                    //
                    // Without the subtraction an absolutely-positioned box counted its own border twice:
                    // Acid2's `blockquote.first.one`, 2em of black border either side of a 48px float,
                    // measured 144px instead of 96px, putting the face's second row an em and a half too
                    // wide on each side. A no-op under `box-sizing: border-box`, as it is there.
                    var fit = Math.Max(
                        await GetFitContentWidth(g, box, absCbWidth),
                        await GetMinContentWidth(g, box));

                    width = fit - box.ActualBoxSizeIncludedWidth;
                }
            }

            // Apply max-width constraint (before min-width, so min wins on conflict per CSS 2.1 §10.4).
            // Basis is page-aware (issue #199) via PageAwareWidthBasis, same as the explicit-width branch
            // above.
            if (CssValueParser.IsValidLength(box.MaxWidth))
            {
                var maxW = CssValueParser.ParseLength(box.MaxWidth, PageAwareWidthBasis(box.ContainingBlock, blockTop ?? box.Location.Y), box);
                width = Math.Min(width, maxW);
            }

            // Apply min-width constraint
            if (box.MinWidth != "0" && CssValueParser.IsValidLength(box.MinWidth))
            {
                var minW = CssValueParser.ParseLength(box.MinWidth, PageAwareWidthBasis(box.ContainingBlock, blockTop ?? box.Location.Y), box);
                width = Math.Max(width, minW);
            }

            // A used width can never leave the box's own content narrower than nothing (CSS 2.1 §10.2), and
            // under `border-box` that means the border box cannot go below the box's own padding and border
            // (css-sizing-3 §3 derives the content box by subtracting them from the specified size). Both
            // are the same floor on the content box, spelled in whichever box `width` names: everything
            // above works in content-box terms - the auto branch subtracts ActualBoxSizeIncludedWidth,
            // which is ZERO under border-box, and under content-box goes negative in a container with no
            // width to give - so a narrow enough container reached here at 0 (or below) with nothing to
            // stop it. Last, so it also outranks a `max-width` that would otherwise squeeze the edges out.
            var minimumUsedWidth = box.BoxSizing.Value is BoxSizingMode.BorderBox
                ? box.ActualPaddingLeft + box.ActualPaddingRight + box.ActualBorderLeftWidth + box.ActualBorderRightWidth
                : 0;

            width = Math.Max(width, minimumUsedWidth);

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

        /// <summary>
        /// The containing block of an absolutely positioned <paramref name="box"/> whose nearest positioned
        /// ancestor is an inline (<see cref="DomUtils.InlineContainingBlockOf"/>), or null. Consulted ahead of
        /// <see cref="PercentageBase"/>, whose box an inline's sizes would be read off meaninglessly.
        /// </summary>
        private static RRect? InlinePercentageBase(CssBox box) =>
            box.Position.Value is PositionMode.Absolute
                ? DomUtils.InlineContainingBlockOf(DomUtils.GetNearestPositionedAncestor(box))
                : null;

        public static double? GetBoxHeight(CssBox box)
        {
            var height = box.ActualBoxSizingHeight;

            if (box == box.ContainingBlock && box.HtmlContainer is not null)
            {
                // Deliberately pinned to the FIRST page's own content band, not a per-page one, even
                // when per-page @page margin (or, for a mixed page-size document, size) overrides give
                // individual pages taller/shorter/differently-sized bands: css-page-3 §3 is explicit that
                // "the edges of the page area on the first page establish the rectangle that is the
                // initial containing block of the document" - singular, page 1 only. This is NOT the same
                // question as an ordinary percentage-height box, which already resolves correctly against
                // its own (already page-aware) ancestor's computed height with no special-casing needed
                // here - css-break-3 §5.1's per-fragmentainer recalculation applies to that ordinary case,
                // not to the true ICB itself. Reads page 0's own resolved band (narrower than the
                // document's base configured size whenever `@page :first`/a first-page-targeting named
                // rule overrides margins - issue #201's vertical dimension) rather than the base
                // configured size directly, so a `:first`-overridden first page's own band is what a
                // root-relative percentage resolves against - still always page 1, never a later page.
                height = Math.Max(height, box.HtmlContainer.PageGeometry.GetPage(0).BandHeight);
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
            // static and zero-height.) A fixed box resolves against the page area instead (CSS2.1 §10.1:
            // the initial containing block, the same basis CommitBlockChildOffset already uses for a fixed
            // box's left/top) - always definite, since the page always has a real height. Like the true
            // ICB itself (this method's own box == box.ContainingBlock branch above), this is pinned to
            // page 1's own resolved band, not the document's base configured PageSize - a `@page :first`
            // margin/size override changes what a fixed box's own canonical height resolves against (issue
            // #146); FragmentEmitter.ComputeFixedSizeOverride corrects the delta for every LATER page
            // relative to whatever this establishes here.
            var heightCb = PercentageBase(box);
            var isFixedToPage = box.Position.Value is PositionMode.Fixed && box.HtmlContainer is not null;
            // CSS Sizing 3 makes an absolutely-positioned element's containing-block size definite with
            // respect to that element even when the positioned ancestor's own height is content-driven.
            // The first pass may see that ancestor before its height settles; ApplyParentHeight reruns this
            // after the ancestor's epilogue and supplies the authoritative used size.
            var heightBasisIsCalculated = isFixedToPage
                || box.Position.Value is PositionMode.Absolute
                || IsHeightDefinite(heightCb);
            var heightBasis = isFixedToPage
                ? box.HtmlContainer!.PageGeometry.GetPage(0).BandHeight
                : InlinePercentageBase(box)?.Height ?? ResolveDefiniteHeightValue(heightCb) ?? heightCb.Size.Height;

            // CSS 2.1 §10.6.3: a definite (non-auto) `height` is the used height regardless of
            // content - content taller than it overflows past ActualBottom (clipped or not per
            // `overflow`), it does not grow the box. This must run BEFORE min-height, not after (as
            // the previous order here had it), so min-height can still win on conflict per §10.7.
            if (CssValueParser.IsValidLength(box.Height))
            {
                if (!heightBasisIsCalculated && CssValueParser.DependsOnPercentage(box.Height))
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
                    height = CssValueParser.ParseLength(box.Height, heightBasis, box) + box.ActualBoxSizeIncludedHeight;
                }
            }
            else if (box.Position.Value is PositionMode.Absolute
                     && box.Top.Value.IsValue && box.Bottom.Value.IsValue
                     && heightBasisIsCalculated)
            {
                // CSS 2.1 §10.6.4: an absolutely-positioned box with auto height but both `top` and `bottom`
                // set fills the space between them in the containing block. The counterpart of the §10.3.7
                // width rule above; sizes a Charts.css area/line `td::before` (auto height, `inset: 0`) to
                // its cell's height.
                var top = CssValueParser.ParseLength(box.Top.Value.Value!.Value, heightBasis, box);
                var bottom = CssValueParser.ParseLength(box.Bottom.Value.Value!.Value, heightBasis, box);
                // Over-constrained fill clamps to 0 (a used height is never negative), per CSS 2.1 §10.6.4.
                height = Math.Max(0, heightBasis - top - bottom - box.ActualMarginTop - box.ActualMarginBottom);
            }
            else if (TryGetAspectRatioHeight(box, out var ratioHeight))
            {
                // CSS Box Sizing 4 §5: with a preferred aspect-ratio and an auto height, the height is
                // computed from the (definite) width via the ratio. This is what gives a Charts.css
                // `tbody { aspect-ratio: … }` its height, from which the bars take their percentage heights.
                height = ratioHeight;
            }

            if (CssValueParser.IsValidLength(box.MinHeight) &&
                (heightBasisIsCalculated || !CssValueParser.DependsOnPercentage(box.MinHeight)))
            {
                var minHeight = CssValueParser.ParseLength(box.MinHeight, heightBasis, box) + box.ActualBoxSizeIncludedHeight;

                if (minHeight > height)
                {
                    height = minHeight;
                }
            }

            return height;
        }

        /// <summary>
        /// A narrow row-axis counterpart to <see cref="GetBoxHeight"/>, added for
        /// <see cref="CssLayoutEngineTable"/>'s own row-height enforcement (CSS 2.1 §17.5.3) on a
        /// <c>writing-mode: vertical-rl</c>/<c>vertical-lr</c> table, where the row axis is physical
        /// <c>width</c> rather than physical <c>height</c> - see the writing-mode remarks at the top of
        /// that class. Deliberately scoped to only what
        /// <see cref="CssLayoutEngineTable.TryComputeRowHeightRedistribution"/> and its own
        /// <c>RowHeightFloor</c> need: a definite <c>width</c>/<c>min-width</c> parsed against
        /// <paramref name="box"/>'s percentage base, clamped by <c>max-width</c>.
        /// </summary>
        /// <remarks>
        /// Deliberately does NOT port two of <see cref="GetBoxHeight"/>'s branches:
        /// <list type="bullet">
        /// <item>the initial-containing-block pinning (<c>box == box.ContainingBlock</c>), meaningful only
        /// for the true document root - never a table or table-row box;</item>
        /// <item>the absolutely-positioned <c>top</c>/<c>bottom</c>-fill rule (CSS 2.1 §10.6.4's width
        /// counterpart), unreachable here since a table/row box is always in-flow or floated, never
        /// absolutely positioned, under CSS 2.1's table box model.</item>
        /// </list>
        /// A future generalization of this method to other box kinds needs to add those back
        /// deliberately, rather than inherit them silently from this narrower purpose.
        /// </remarks>
        /// <returns>
        /// The resolved border-box width, or null when <paramref name="box"/> has neither a definite
        /// <c>width</c> nor a definite <c>min-width</c> - mirroring <see cref="GetBoxHeight"/>'s own
        /// null-for-auto contract for its own explicit-height branch.
        /// </returns>
        public static double? GetBoxWidth(CssBox box)
        {
            var basis = PercentageBase(box).Size.Width;

            var hasExplicitWidth = CssValueParser.IsValidLength(box.Width);
            var hasExplicitMinWidth = box.MinWidth != "0" && CssValueParser.IsValidLength(box.MinWidth);

            if (!hasExplicitWidth && !hasExplicitMinWidth) return null;

            double width = hasExplicitWidth
                ? CssValueParser.ParseLength(box.Width, basis, box) + box.ActualBoxSizeIncludedWidth
                : 0;

            // Max-width clamps before min-width, so min-width still wins on conflict per CSS 2.1 §10.4 -
            // the same order the existing async GetBoxWidth(RGraphics, CssBox, double?) overload already
            // uses.
            if (CssValueParser.IsValidLength(box.MaxWidth))
            {
                var maxWidth = CssValueParser.ParseLength(box.MaxWidth, basis, box) + box.ActualBoxSizeIncludedWidth;
                width = Math.Min(width, maxWidth);
            }

            if (hasExplicitMinWidth)
            {
                var minWidth = CssValueParser.ParseLength(box.MinWidth, basis, box) + box.ActualBoxSizeIncludedWidth;
                width = Math.Max(width, minWidth);
            }

            return width;
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

            using var pooledTokens = CssValueParser.GetCssTokensPooled(box.AspectRatio);
            List<Token> tokens = pooledTokens;
            if (!AspectRatioGrammar.TryParse(tokens, out var ratio) || ratio is not (> 0)) return false;

            height = box.Size.Width / ratio.Value + box.ActualBoxSizeIncludedHeight;
            return true;
        }

        /// <summary>
        /// True when the box has a definite used height: a non-percentage length, or a percentage resolved
        /// against a height-definite containing block. A percentage against an indefinite (auto-height)
        /// containing block is NOT definite — CSS Box Sizing 4 §5 treats it as automatic. Recurses through
        /// <see cref="IsHeightDefinite"/> rather than reading a cached flag — see that method's own remarks
        /// for why this is safe to call at any point during layout.
        /// </summary>
        internal static bool HasDefiniteHeight(CssBox box)
        {
            if (!CssValueParser.IsValidLength(box.Height)) return false;
            if (!CssValueParser.DependsOnPercentage(box.Height)) return true;

            // A fixed box's percentage height resolves against the page area (CSS 2.1 §10.1: the initial
            // containing block), which always has a definite height - the same basis GetBoxHeight's own
            // isFixedToPage branch uses. PercentageBase would instead hand back the nearest in-flow block
            // container, which says nothing about what a fixed box actually resolves against. This case
            // became reachable when CssBox.ResolvePositionedAutoBlockMargins started asking (§10.6.4 is
            // written for absolute and fixed alike); TryGetAspectRatioWidth, the original caller, only ever
            // runs for absolute boxes.
            if (box.Position.Value is PositionMode.Fixed && box.HtmlContainer is not null) return true;
            if (box.Position.Value is PositionMode.Absolute) return true;

            return IsHeightDefinite(PercentageBase(box));
        }

        /// <summary>
        /// True when a box's used height is "specified explicitly" per CSS 2.1
        /// <see href="https://www.w3.org/TR/CSS21/visudet.html#the-height-property">§10.5</see> / CSS
        /// Sizing 3 <see href="https://www.w3.org/TR/css-sizing-3/#definite">§4</see>'s "definite size" —
        /// computable purely from the box's own declared height and, recursively, its containing block's
        /// own definiteness, with no dependency on any bottom-up-resolved value except an
        /// already-top-down-resolved width (for the aspect-ratio branch, since CSS width is always resolved
        /// before a box's own content is laid out) and, once a flex/grid layout algorithm has resolved a
        /// stretched/main-axis height for this box this pass, <see cref="CssBox.AlgorithmicDefiniteHeight"/>.
        /// Safe to call at ANY point during layout, including before this box's own content — or any
        /// ancestor's — has been processed.
        /// </summary>
        /// <remarks>
        /// Replaces the old <c>CssBox.IsHeightCalculated</c> cached field (removed), which was only ever
        /// written bottom-up, in each box's own post-content layout epilogue — so it could only be trusted
        /// once <c>ApplyHeight</c> had already run for every relevant ancestor, which for many callers
        /// (an inline-flowed atomic inline-level box's own percentage-height resolution among them, issue
        /// <see href="https://github.com/jhaygood86/PeachPDF/issues/1167">#1167</see>) is later than the
        /// point at which the answer is actually needed. Definiteness does not require layout to have
        /// happened at all — CSS Sizing 3 §4 defines it recursively over the box tree's own declared sizes
        /// — so this is a from-scratch, on-demand computation instead of a cache read.
        /// <para>
        /// Equivalent by construction to what the old field was assigned exactly once, in
        /// <c>ApplyHeight</c>: <c>isRootWithPageHeight || isDefiniteHeight || isRatioHeight</c>, where
        /// <c>isDefiniteHeight = HasDefiniteHeight(box)</c> and <c>isRatioHeight = !isDefiniteHeight &amp;&amp;
        /// TryGetAspectRatioHeight(box, out _)</c>. Since the two are OR'd, the <c>!isDefiniteHeight</c>
        /// guard on the ratio term is redundant in the union - this method is exactly that simplified
        /// expression, with <see cref="HasDefiniteHeight"/>'s own recursive tail changed to call this
        /// method instead of reading the old field.
        /// </para>
        /// </remarks>
        internal static bool IsHeightDefinite(CssBox box)
        {
            // Terminates the recursion unconditionally, not only when HtmlContainer is set: CssBox.
            // ContainingBlock returns `this` for ANY box with no ParentBox (not just the true, attached
            // root), so without this a detached box (no HtmlContainer, e.g. one built for a standalone
            // measurement/unit test) with a percentage Height would recurse into itself forever via
            // HasDefiniteHeight -> IsHeightDefinite(PercentageBase(box) == box). A detached box has no
            // page to be definite against, so it correctly answers false here rather than looping.
            if (box == box.ContainingBlock) return box.HtmlContainer is not null;
            if (box.AlgorithmicDefiniteHeight is not null) return true;
            if (HasDefiniteHeight(box)) return true;
            return TryGetAspectRatioHeight(box, out _);
        }

        /// <summary>
        /// The box's own definite border-box height, resolved purely from its declared height chain
        /// (CSS 2.1 §10.5, §10.7's min/max-height clamps, CSS Sizing 3 §4, and a flex/grid algorithm's
        /// <see cref="CssBox.AlgorithmicDefiniteHeight"/>) — the value-returning twin of
        /// <see cref="IsHeightDefinite"/>. Returns <c>null</c> exactly when <see cref="IsHeightDefinite"/>
        /// is false, OR when <paramref name="box"/> is one of the two kinds whose final used height can
        /// still grow past this value from content even though it is "definite" by declaration: a table
        /// cell stretched by its row (CSS 2.1 §17.5.3), or a box whose height comes from a preferred
        /// <c>aspect-ratio</c> (not from its own <c>height</c> declaration, and not from a flex/grid
        /// algorithm, which is its own, later-checked, always-authoritative sizing model — see below)
        /// while also establishing an independent formatting context that could still grow it past that
        /// ratio-derived value from a contained float (CSS 2.1 §10.6.7) — the same condition
        /// <see cref="ApplyHeight"/>'s own float-containment growth is gated on (<c>!isDefiniteHeight</c>,
        /// i.e. <see cref="HasDefiniteHeight"/> is false; a box whose OWN <c>height</c> is genuinely
        /// declared is never grown past it by §10.6.3 regardless of floats, so only the ratio case needs
        /// this carve-out). For exactly those two kinds, callers must fall back to the existing
        /// (already-correct, if bottom-up-only) <c>CssBox.Size.Height</c> read; every other box's definite
        /// height is fully static and this never needs that fallback.
        /// <para>
        /// A flex/grid algorithm's own resolved height (<see cref="CssBox.AlgorithmicDefiniteHeight"/>) is
        /// checked <i>before</i> the box's own declared <c>Height</c>, not after: for a flex item, the
        /// algorithm's resolved main/cross size is always authoritative over the raw declaration once the
        /// algorithm has run (flex-grow/flex-shrink can make the two genuinely differ — e.g. a
        /// column-direction item declaring <c>height: 20%</c> with <c>flex-grow: 1</c> and no siblings
        /// flex-grows to fill 100% of the container, not 20%), and unlike ordinary block auto-sizing, a
        /// flex/grid item's used cross/main size does not get further grown by a float inside the item's
        /// own content the way §10.6.7's ordinary auto-height-block rule does — so it needs no
        /// independent-formatting-context carve-out either, even though a flex/grid item is itself always
        /// an independent-formatting-context box. This ordering matches <see cref="IsHeightDefinite"/>'s
        /// own — the two must agree on priority, not just on the final boolean, since a caller resolving a
        /// percentage basis needs the same value <see cref="IsHeightDefinite"/> promised was available.
        /// </para>
        /// </summary>
        internal static double? ResolveDefiniteHeightValue(CssBox box)
        {
            if (box.IsTableCell || box.DerivedStyle.ActualDisplay is Keywords.Table or Keywords.InlineTable)
                return null; // §17.5.3 row-stretch can grow this past its own declared height

            // See IsHeightDefinite's own remarks on why this terminates unconditionally, not only when
            // HtmlContainer is set.
            if (box == box.ContainingBlock)
                return box.HtmlContainer is not null ? box.HtmlContainer.PageGeometry.GetPage(0).BandHeight : null;

            // The percentage basis for box's own Height/MinHeight/MaxHeight alike — computed at most once
            // per property (not per read) since none of them can change within this call. A fixed box
            // resolves against the page band (CSS 2.1 §10.1, mirroring GetBoxHeight's own isFixedToPage
            // branch); an absolutely-positioned box's own percentage height is deliberately left
            // unresolved here (returns null, so the caller falls back to a live Size.Height read) — this
            // IS reachable (an absolutely-positioned box can appear anywhere in a PercentageBase chain,
            // e.g. nested position:absolute ancestors), not merely a defensive unreachable branch;
            // resolving it properly needs GetBoxHeight's own nearest-positioned-ancestor basis logic, a
            // separate, addressable follow-up rather than a blocker for the common in-flow case.
            double? PercentageHeightBasis() => box.Position.Value switch
            {
                PositionMode.Fixed when box.HtmlContainer is not null
                    => box.HtmlContainer.PageGeometry.GetPage(0).BandHeight,
                PositionMode.Absolute => null,
                _ => ResolveDefiniteHeightValue(PercentageBase(box)),
            };

            double? declared = null;

            if (box.AlgorithmicDefiniteHeight is { } algoHeight)
            {
                declared = algoHeight;
            }
            else if (HasDefiniteHeight(box))
            {
                if (!CssValueParser.DependsOnPercentage(box.Height))
                {
                    declared = CssValueParser.ParseLength(box.Height, 0, box) + box.ActualBoxSizeIncludedHeight;
                }
                else if (PercentageHeightBasis() is { } heightBasis)
                {
                    declared = CssValueParser.ParseLength(box.Height, heightBasis, box) + box.ActualBoxSizeIncludedHeight;
                }
            }
            else if (DomUtils.EstablishesIndependentFormattingContext(box))
            {
                // §10.6.7 float-containment growth could still apply on top of a ratio-derived height for
                // a box whose OWN Height isn't itself definite - see this method's own remarks.
                return null;
            }
            else if (TryGetAspectRatioHeight(box, out var ratioHeight))
            {
                declared = ratioHeight;
            }

            if (declared is null) return null;

            // CSS 2.1 §10.7: an unconditional min-height floor, then a max-height shrink that re-floors
            // by the same min-height on conflict. minHeightValue is resolved once and reused for both
            // steps rather than re-derived; if a percentage min/max-height's own basis can't be statically
            // resolved (the box's containing block is itself a table cell, or an absolutely-positioned
            // box — see PercentageHeightBasis's own remarks), the whole result is unknowable statically,
            // so this returns null (the caller falls back to a live read) rather than silently skipping
            // just that one clamp and risking an under- or over-clamped value.
            double? minHeightValue = null;

            if (CssValueParser.IsValidLength(box.MinHeight))
            {
                double? minBasis = CssValueParser.DependsOnPercentage(box.MinHeight) ? PercentageHeightBasis() : 0;
                if (minBasis is null) return null;
                minHeightValue = CssValueParser.ParseLength(box.MinHeight, minBasis.Value, box) + box.ActualBoxSizeIncludedHeight;
            }

            if (minHeightValue is { } floor && floor > declared) declared = floor;

            if (CssValueParser.IsValidLength(box.MaxHeight))
            {
                double? maxBasis = CssValueParser.DependsOnPercentage(box.MaxHeight) ? PercentageHeightBasis() : 0;
                if (maxBasis is null) return null;
                var maxHeight = CssValueParser.ParseLength(box.MaxHeight, maxBasis.Value, box) + box.ActualBoxSizeIncludedHeight;

                if (declared > maxHeight)
                {
                    declared = maxHeight;
                    if (minHeightValue is { } reflow && declared < reflow) declared = reflow; // min wins on conflict
                }
            }

            return declared;
        }

        /// <summary>
        /// Computes a box's box-sizing-box width from its <c>aspect-ratio</c> and its definite used height —
        /// the height→width counterpart of <see cref="TryGetAspectRatioHeight"/>. Applies only where the width
        /// would otherwise be shrink-to-fit (an absolutely-positioned auto-width box): a normal-flow block's
        /// auto inline size is stretch-fit, which wins over the ratio (CSS Box Sizing 4 §5.1), so the caller
        /// must not use this on a stretch-fit box. The returned width is the box-sizing box's width (matching
        /// <see cref="GetBoxWidth(RGraphics, CssBox, double?)"/>'s explicit-width branch), so the caller adds <c>ActualBoxSizeIncludedWidth</c>
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

            using var pooledTokens = CssValueParser.GetCssTokensPooled(box.AspectRatio);
            List<Token> tokens = pooledTokens;
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
            //
            // §17.5.3 makes the SAME "maximum of specified and content" rule apply to the table box
            // itself: "the height of a 'table' element's box is the maximum of the table's specified
            // height and the sum of the row heights". Reached here because this call runs generically on
            // every box in PerformLayoutEpilogue, including a table's own outer box, after
            // CssLayoutEngineTable.Layout has already set ActualBottom from real row geometry - Math.Max
            // against that is exactly "never shrink below the rows' real content", the same carve-out the
            // cell already gets, rather than the general §10.6.3 rule clipping a short explicit height
            // below content that CssLayoutEngineTable.PerformLayout may since have grown to satisfy it
            // (issue #1116).
            var neverShrinksBelowContent = box.IsTableCell
                || box.DerivedStyle.ActualDisplay is Keywords.Table or Keywords.InlineTable;

            box.ActualBottom = boxHeight is not null && !neverShrinksBelowContent
                ? box.Location.Y + height
                : Math.Max(box.ActualBottom, box.Location.Y + height);

            // Whether a DESCENDANT's percentage height/min-height/max-height resolves against this box,
            // per CSS 2.1 §10.5: only true when this box's own height is "specified explicitly" (a
            // definite, non-auto length, or a percentage against a containing block that is itself
            // height-definite), or this box is the root/initial containing block (whose used height is the
            // page height regardless of its `height` computed value) — see IsHeightDefinite, which computes
            // this on demand rather than needing it cached here. It must NOT simply mirror "GetBoxHeight
            // returned a usable number" - GetBoxHeight also returns a real number for plain content-driven
            // `height: auto` boxes (e.g. via ActualBoxSizingHeight/Words height), and treating THAT as
            // "definite" incorrectly makes every percentage-height descendant of an auto-height block
            // resolve against that content-driven height instead of being treated as auto (CSS2.1 Acid2
            // test's `.nose { height: 60% }` inside auto-height `.picture` is exactly this trap - it must
            // resolve to `auto`, not to a huge value derived from `.picture`'s own content height).
            var isRootWithPageHeight = box == box.ContainingBlock && box.HtmlContainer is not null;
            var isDefiniteHeight = HasDefiniteHeight(box);

            // CSS 2.1 §10.6.7: a box that establishes a formatting context of its own and takes its height
            // from content grows to cover any floating descendant whose bottom margin edge falls below its
            // bottom content edge. This is the whole reason `overflow: hidden` (and a float, an
            // inline-block, an absolutely-positioned box, a flex/grid item...) "contains" its floats while
            // an ordinary block does not - and without it such a box holding nothing but a float came out
            // zero-height, which is what made Acid2's `blockquote.first.one` - the second row of the face,
            // a shrink-wrapped absolutely-positioned box whose only content is one float - invisible: its
            // 2em black side borders had no height to be drawn over.
            //
            // Gated on !isDefiniteHeight, not merely on `height: auto`: an indefinite percentage height is
            // automatic too (the same reading IsHeightDefinite's own aspect-ratio fallback already relies
            // on). A definite height is the used height regardless of content (§10.6.3), float included, so
            // it is left alone; the min/max-height clamps below then apply to the result either way, since
            // §10.6.7's increase is
            // part of computing the auto height rather than something that outranks §10.7.
            if (!isDefiniteHeight && !isRootWithPageHeight && DomUtils.ContainsItsFloats(box))
            {
                var lowestFloatBottom = DomUtils.LowestFloatBottomInOwnFormattingContext(box);

                if (!double.IsNegativeInfinity(lowestFloatBottom))
                {
                    box.ActualBottom = Math.Max(box.ActualBottom,
                        lowestFloatBottom + box.ActualPaddingBottom + box.ActualBorderBottomWidth);
                }
            }

            // Unlike min-height/explicit-height (which only ever grow ActualBottom), max-height must be
            // able to shrink the box below its content's natural extent — content simply overflows past
            // ActualBottom, mirroring the existing overflow:hidden clip elsewhere in this engine. Applied
            // last, and min-height wins over it on conflict (CSS 2.1 §10.7).
            var isContainingBlockHeightDefinite = IsHeightDefinite(box.ContainingBlock);

            if (CssValueParser.IsValidLength(box.MaxHeight) &&
                (isContainingBlockHeightDefinite || !CssValueParser.DependsOnPercentage(box.MaxHeight)))
            {
                var maxHeightBasis = ResolveDefiniteHeightValue(box.ContainingBlock) ?? box.ContainingBlock.Size.Height;
                var maxHeight = CssValueParser.ParseLength(box.MaxHeight, maxHeightBasis, box) + box.ActualBoxSizeIncludedHeight;
                var maxBottom = box.Location.Y + maxHeight;

                if (box.ActualBottom > maxBottom)
                {
                    box.ActualBottom = maxBottom;

                    // min-height wins over max-height on conflict (CSS 2.1 §10.7)
                    if (CssValueParser.IsValidLength(box.MinHeight) &&
                        (isContainingBlockHeightDefinite || !CssValueParser.DependsOnPercentage(box.MinHeight)))
                    {
                        var minHeightBasis = ResolveDefiniteHeightValue(box.ContainingBlock) ?? box.ContainingBlock.Size.Height;
                        var minHeight = CssValueParser.ParseLength(box.MinHeight, minHeightBasis, box) + box.ActualBoxSizeIncludedHeight;
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

                var clears = box.EffectiveClear;

                clearance = Math.Max(clearance, GetClearance(siblingBox, clears));

                if (!siblingBox.IsFloated) continue;

                switch (siblingBox.EffectiveFloatSide)
                {
                    case Floating.Left when clears is ClearMode.Right:
                    case Floating.Right when clears is ClearMode.Left:
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
                switch (childBox.EffectiveFloatSide)
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

        private static void FloatBoxLeft(CssBox box, CssBox containingBox, double startX, double startY)
        {
            var limitRight = ContentRightOf(containingBox, startY);

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
                var intersectingFloat = DomUtils.GetFirstIntersectingFloatBox(box, coordinates, box.EffectiveFloatSide);

                if (intersectingFloat is null) break;

                switch (intersectingFloat.EffectiveFloatSide)
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

                    // css-break-3 §5.1: this drop may have carried the float onto a fragmentainer of a
                    // different inline size, so the boundary later iterations test against is re-derived
                    // at the Y it reached, rather than kept from the band the scan opened in. A
                    // uniform-width document re-derives the same number, so nothing changes for it.
                    limitRight = ContentRightOf(containingBox, coordinates.Top);
                    coordinates.Right = limitRight;
                }
            } while (true);

            box.Location = new RPoint(coordinates.Left, coordinates.Top);

        }

        private static void FloatBoxRight(CssBox box, CssBox containingBox, double startX, double startY)
        {
            var limitRight = ContentRightOf(containingBox, startY);

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
                var intersectingFloat = DomUtils.GetFirstIntersectingFloatBox(box, coordinates, box.EffectiveFloatSide);

                if (intersectingFloat is null) break;

                switch (intersectingFloat.EffectiveFloatSide)
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
                    coordinates.Top = coordinates.MaxBottom;

                    // Re-derive at the band the drop reached (css-break-3 §5.1), mirroring FloatBoxLeft.
                    limitRight = ContentRightOf(containingBox, coordinates.Top);
                    coordinates.Right = limitRight - box.ActualMarginRight;
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

                // ApplyStringSet seeds each NamedString.Y from box.Location.Y, which is meaningless for
                // a plain inline box (only its words get a position; the box itself never does) - it
                // stays at whatever default the box happened to carry, corrupting the page attribution
                // MarginBoxRenderer.ResolveNamedString relies on. coordinates.CurrentY, read here at the
                // exact moment this box opens, is always correct regardless of how far its content later
                // continues - unlike the exit-side correction below (FinalizeFlowBoxExit), which a break
                // inside this box's own content (on this pass or the one that finally completes it, since
                // an in-progress ancestor's own FlowBox call returns before reaching its exit bookkeeping
                // whenever a break occurs anywhere in its recursion - #341) can skip forever, leaving this
                // the only assignment a straddling box is guaranteed to get.
                foreach (var namedString in box.NamedStrings.Values)
                {
                    namedString.Y = coordinates.CurrentY;
                }
            }

            return opensHere;
        }

        /// <summary>
        /// Assigns <paramref name="child"/>'s used width from its declared one, per
        /// <see href="https://www.w3.org/TR/CSS22/visudet.html#inlineblock-width">CSS 2.1 §10.3.9</see>:
        /// shrink-to-fit sizes a non-replaced inline-block only when its <c>width</c> is <c>auto</c>, and
        /// an explicit width is used as declared.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Nothing else assigns one on this path. An inline-block whose own content is inlines-only is
        /// flowed into <paramref name="parent"/>'s line boxes rather than laid out as a box of its own
        /// (<see cref="FlowAtomicBlockContentChild"/>, which does resolve a width, is only reached when the
        /// box holds block-level content), so <c>width</c> used to size nothing at all: the box painted its
        /// background, border and overflow clip at whatever its content happened to measure — nothing at
        /// all, for an empty one
        /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/1091">#1091</see>).
        /// </para>
        /// <para>
        /// The resolved length is the right value for <see cref="CssBox.Size"/> under either
        /// <c>box-sizing</c>, because <c>Size.Width</c> means whichever one that property selects:
        /// <c>ActualBoxSizeIncludedWidth</c> adds the padding and border back under <c>content-box</c> and
        /// adds nothing under <c>border-box</c>. A percentage resolves against the child box's containing
        /// block using the same page-aware basis as <see cref="GetBoxWidth(RGraphics, CssBox, double?)"/>. <c>auto</c> is left alone,
        /// which is exactly the case §10.3.9 hands to shrink-to-fit.
        /// </para>
        /// </remarks>
        /// <param name="child">the box being placed on the line</param>
        /// <param name="parent">
        /// the box whose children are being flowed — <paramref name="child"/> when <c>FlowBox</c> is
        /// iterating over itself, which is not a child placement and so resolves nothing
        /// </param>
        /// <param name="blockTop">
        /// the document Y of the line on which <paramref name="child"/> is being placed, used to select
        /// the containing block's page-aware percentage basis
        /// </param>
        /// <returns>
        /// the used content width, for the caller to reserve on the line — or null when this box is not
        /// one that declares its own width. Read back off the box rather than returned from the arithmetic
        /// above so that both numbers are the one assignment, whichever <c>box-sizing</c> is in play.
        /// </returns>
        private static double? ResolveAtomicInlineDeclaredWidth(CssBox child, CssBox parent, double blockTop)
        {
            if (child.DerivedStyle.ActualDisplay is not Keywords.InlineBlock
                || ReferenceEquals(child, parent)
                || !CssValueParser.IsValidLength(child.Width))
            {
                return null;
            }

            var declared = CssValueParser.ParseLength(
                child.Width, PageAwareWidthBasis(child.ContainingBlock, blockTop), child);

            if (child.BoxSizing.Value is BoxSizingMode.BorderBox)
            {
                // css-ui-3 §6.2: under `border-box` the used width is floored so that the content width
                // cannot become negative - i.e. at the box's own border and padding. Without this the
                // content width below (AvailableWidth) goes negative and the box reserves less room on
                // the line than its own border occupies.
                declared = Math.Max(declared,
                    child.ActualBorderLeftWidth + child.ActualPaddingLeft
                    + child.ActualPaddingRight + child.ActualBorderRightWidth);
            }

            child.Size = new RSize(declared, child.Size.Height);
            return child.AvailableWidth;
        }

        /// <summary>
        /// The declared/used <b>border-box</b> height of an inline-flowed atomic inline-level box, from
        /// its own <c>height</c>/<c>min-height</c> — or <c>null</c> when neither applies, meaning the
        /// box's content-derived rectangle stands as-is
        /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/1101">#1101</see>).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Unlike <see cref="ResolveAtomicInlineDeclaredWidth"/>, this does not write
        /// <see cref="CssBox.Size"/> — there is no same-pass consumer on the height axis analogous to the
        /// line's cursor advance on the width axis (the reservation this box's declared width still needs
        /// to make for what follows it on the line); this is a pure computation the caller combines with
        /// the box's own content-derived rectangle height. <see cref="FinalizeFlowBoxExit"/>'s own
        /// <c>MaxBottom</c> line-height correction
        /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/1166">#1166</see>) calls this method
        /// too, so both the line/flow and the painted rectangle stay consistent with exactly one
        /// resolution of "what height did this box declare".
        /// </para>
        /// <para>
        /// A percentage <c>height</c>/<c>min-height</c>
        /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/1167">#1167</see>) resolves against
        /// <see cref="ResolveDefiniteHeightValue"/> when <see cref="IsHeightDefinite"/> says the containing
        /// block's height is definite — both computed purely from declared CSS, on demand, with no
        /// dependency on the containing block's own layout epilogue having run yet (unlike the old
        /// <c>CssBox.IsHeightCalculated</c> cached flag this replaced). When the containing block's height
        /// is genuinely indefinite (content-driven, per CSS 2.1
        /// <see href="https://www.w3.org/TR/CSS21/visudet.html#the-height-property">§10.5</see>), the
        /// percentage correctly has no effect here, same as it never did.
        /// </para>
        /// <para>
        /// <c>max-height</c> is not read, matching <see cref="ResolveAtomicInlineDeclaredWidth"/>'s own
        /// scope, which never reads <c>max-width</c> either — this method only ever grows a box's
        /// rectangle (never shrinks it below its natural content), so a clamp that can shrink the used
        /// value below content, per CSS 2.1 §10.7, would need a different combining rule than the
        /// <c>Math.Max</c> this one uses for <c>min-height</c>.
        /// </para>
        /// </remarks>
        private static double? ResolveAtomicInlineDeclaredHeight(CssBox box)
        {
            double? declared = null;

            if (CssValueParser.IsValidLength(box.Height) &&
                (!CssValueParser.DependsOnPercentage(box.Height) || IsHeightDefinite(box.ContainingBlock)))
            {
                var basis = CssValueParser.DependsOnPercentage(box.Height)
                    ? ResolveDefiniteHeightValue(box.ContainingBlock) ?? box.ContainingBlock.Size.Height
                    : 0; // unused by ParseLength for a non-percentage length
                declared = CssValueParser.ParseLength(box.Height, basis, box) + box.ActualBoxSizeIncludedHeight;
            }

            if (CssValueParser.IsValidLength(box.MinHeight) &&
                (!CssValueParser.DependsOnPercentage(box.MinHeight) || IsHeightDefinite(box.ContainingBlock)))
            {
                var basis = CssValueParser.DependsOnPercentage(box.MinHeight)
                    ? ResolveDefiniteHeightValue(box.ContainingBlock) ?? box.ContainingBlock.Size.Height
                    : 0;
                var minHeight = CssValueParser.ParseLength(box.MinHeight, basis, box) + box.ActualBoxSizeIncludedHeight;
                declared = declared is { } d ? Math.Max(d, minHeight) : minHeight;
            }

            if (declared is not { } result) return null;

            if (box.BoxSizing.Value is BoxSizingMode.BorderBox)
            {
                // css-sizing-3 §6.2, the same floor ResolveAtomicInlineDeclaredWidth applies on the inline
                // axis: a border-box declared height cannot shrink the box below its own border+padding.
                result = Math.Max(result,
                    box.ActualBorderTopWidth + box.ActualPaddingTop
                    + box.ActualPaddingBottom + box.ActualBorderBottomWidth);
            }

            return result;
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
            // declared height (e.g. an inline-block button whose vertical padding exceeds its one
            // small-font text line, or a declared `height`/`min-height` per #1166), so extend
            // MaxBottom to cover the box's full height from where it started. This must be
            // trueStartY-anchored: the old
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
            // atomic inline-level box (inline-block/inline-table) contributes its full margin box
            // to the line it sits on (CSS2.1 §10.8: "For replaced elements, inline-block elements,
            // and inline-table elements, this is the height of their margin box").
            //
            // Compares against ResolveAtomicInlineDeclaredHeight(box), not box.ActualHeight
            // (Size.Height + ActualBoxSizeIncludedHeight): Size.Height is never assigned for an
            // inline-flowed inline-block (#1166), so box.ActualHeight was always just its own
            // border+padding here, regardless of any declared height/min-height - this branch was
            // dead in exactly the case it was meant to cover. ResolveAtomicInlineDeclaredHeight
            // resolves the actual declared border-box height/min-height (including, once #1167
            // extends it, a percentage against a definite containing block) with no dependency on
            // Size.Height ever being set.
            //
            // Like the width branch below, this compares an advance measured in this pass against the
            // box's whole height, so it only means anything for a box this pass both opened and
            // finished. For one it merely walked through the deficit is the box's entire height, which
            // it would then add to the flow all over again.
            //
            // And never for the flow root itself (box == blockBox): CreateLineBoxes flows every block,
            // grid item, flex item and table cell holding inline content as FlowBox(blockBox, blockBox), so
            // this exit runs for the root too. Its own declared height/min-height is applied by
            // ApplyHeight/GetBoxHeight (min-height, max-height, the table cell's own maximum), and its
            // startY is the CONTENT edge while ResolveAtomicInlineDeclaredHeight answers in border-box
            // terms - so comparing them here counted the root's vertical padding and border twice (a
            // padded one-line block came out P + max(content, declared) tall, since the caller adds the
            // bottom padding and border to MaxBottom again). Only a NESTED atomic inline is compared.
            if (box != blockBox && opensHere && box.DerivedStyle.ActualDisplay is not Keywords.Inline
                && ResolveAtomicInlineDeclaredHeight(box) is { } declaredFlowHeight
                && coordinates.MaxBottom - trueStartY < declaredFlowHeight)
            {
                coordinates.MaxBottom = trueStartY + declaredFlowHeight;
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
            // Both sides of the comparison are CONTENT widths, and that is the whole of it. `startX` is
            // this box's content-box left edge - FlowBox's per-child dispatch adds a child's leftSpacing
            // (margin+border+padding) to the cursor BEFORE recursing into it - and `rightSpacing` is
            // added by that same dispatch AFTER this runs, so the span measured here begins and ends at
            // the content box. `box.ActualWidth` is the BORDER-box width (Size.Width + padding + border,
            // see ActualBoxSizingWidth), so comparing a content advance against it made every inline
            // carrying left padding or a left border look narrower than it really was by exactly that
            // padding and border - and the correction below then added them to the line a second time,
            // pushing everything after the box right by one padding+border (issue #1093).
            // `Size.Width` is the declared content width this branch is actually about; for a plain
            // inline it is 0, so the branch now correctly does nothing at all - and an inline-block,
            // the one inline-level display that does declare a width and still reaches here, is taken
            // by the branch above instead (see ReserveAtomicInlineUsedWidth for the two things it needs
            // that this does not do).
            var usedContentWidth = coordinates.CurrentX - startX;

            if (opensHere && box.DerivedStyle.ActualDisplay is Keywords.InlineBlock)
            {
                ReserveAtomicInlineUsedWidth(box, coordinates, startOrdinal, startX, trueStartY, usedContentWidth);
            }
            else if (opensHere && box.IsInline && 0 <= usedContentWidth && usedContentWidth < box.Size.Width)
            {
                // hack for actual width handling
                coordinates.CurrentX += box.Size.Width - usedContentWidth;

                // The rectangle is the box's BORDER box - it is what the background and border paint
                // from - so it starts one border+padding back from the content edge `startX` names.
                // Margin is excluded on both counts, since ActualWidth does not include it either.
                coordinates.Line.Rectangles.Add(box, new RRect(
                    startX - box.ActualBorderLeftWidth - box.ActualPaddingLeft,
                    trueStartY, box.ActualWidth, box.ActualHeight));
            }

            // handle box that is only a whitespace
            //
            // ...unless the cursor is still at the beginning of a line, where
            // https://www.w3.org/TR/css-text-3/#white-space-phase-2 removes a collapsible space
            // sequence outright. The advance is only ever collapsible white space to begin with (a
            // preserved one arrives as a real IsSpaces word, which leaves Words.Count non-zero and
            // so never reaches here), so no white-space test is needed alongside the line-start one.
            // Source formatted across lines is what produces it: the newline between a `<br>` and the
            // element after it used to indent the whole line it opened by one space (issue #1087).
            if (opensHere && box.Text is { Length: > 0 } && string.IsNullOrWhiteSpace(box.Text) && !box.IsImage && box.IsInline && box.Boxes.Count == 0 && box.Words.Count == 0
                && !IsAtLineStart(coordinates))
            {
                coordinates.CurrentX += box.ActualWordSpacing;

                // This advance belongs to no word at all - neither neighbour records it in its own
                // HasSpaceAfter/HasSpaceBefore - so the next word placed would otherwise read as
                // contiguous with the previous one. It is a real word separator, and
                // `<span>AA</span> <span>BB</span>` is exactly the common markup that produces it.
                coordinates.PendingWordSeparator = true;
            }

            // Finalize what was captured at entry, now that this box's content has actually been placed
            // and coordinates.CurrentY reflects where it landed - mirrors CssBox.PerformLayoutImp's own
            // late-stage Y-correction (done there once Location is final), which a plain inline box
            // never gets a Location for in the first place. Gated on opensHere for the same reason as
            // the entry-side guard in PrepareFlowBoxEntry: a pass merely resuming this box's
            // already-placed content into a later fragmentainer must not re-stamp its NamedStrings' Y
            // to wherever *this* pass's cursor happens to sit (typically the resumed fragmentainer's own
            // top, since the walk hasn't advanced past already-placed words yet) - that would discard
            // the box's true position from when it actually opened.
            //
            // `page` is deliberately NOT registered for an inline box here (issue #149): per css-page-3
            // §7.2, `page` only applies to boxes that create class-A break points, which are block-level
            // by definition - an inline box is not one, so registering it was already spec-marginal, and
            // doing so at its own (unsnapped, mid-line) Y made it a real source of corruption on its own
            // (see .claude/recent-fixes/2026-08-02-inline-string-set-and-named-page-corruption-across-reflow.md,
            // whose unregister-before-register/opensHere fixes protected this dead-end registration
            // along with the still-needed NamedStrings one above).
            if (opensHere && box != blockBox && box.NamedStrings.Count > 0)
            {
                foreach (var namedString in box.NamedStrings.Values)
                {
                    namedString.Y = coordinates.CurrentY;
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
        /// <see cref="FinalizeFlowBoxExit"/>'s width handling for an atomic inline-level box, per
        /// <see href="https://www.w3.org/TR/CSS22/visudet.html#inlineblock-width">CSS 2.1 §10.3.9</see>:
        /// such a box occupies its own used width, and paints its border box at that width whether or not
        /// its content fills it. The declared width was assigned to <see cref="CssBox.Size"/> before this
        /// box's content was placed (<see cref="ResolveAtomicInlineDeclaredWidth"/>).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two things separate this from the plain-inline branch beside it, which is otherwise the same
        /// idea. First, <c>Size.Width</c> is the <i>border</i>-box width under
        /// <c>box-sizing: border-box</c>, so it is not the width to reserve between a content-box left
        /// edge and a trailing spacing the caller adds afterwards; <see cref="CssBox.AvailableWidth"/> is,
        /// under either value. Second, that branch states the box's rectangle whenever its content came
        /// out narrower — which for a box whose words <i>did</i> land on this line is then merged with
        /// the rectangle those words bubble up (<see cref="CssLineBox.UpdateRectangle"/>), an edge-wise
        /// min/max that moves the box's block axis: a rectangle spanning <c>trueStartY</c> to
        /// <see cref="CssBox.ActualHeight"/> pulls its top up from the ink to the line's flow top, since
        /// neither names where the box's content finally settles (<c>ApplyVerticalAlignment</c> has not
        /// run). Here the rectangle is stated only for a box that visited no word at all — the empty
        /// checkbox-glyph shape, which has nothing to bubble — and every other one keeps its words'
        /// rectangle and is widened on the inline axis alone afterwards
        /// (<see cref="WidenAtomicInlineRectangles"/>).
        /// </para>
        /// <para>
        /// The rectangle is the BORDER box, so it starts one leading border+padding left of
        /// <paramref name="startX"/> — exactly as <paramref name="trueStartY"/> already steps back over
        /// the top border and padding. Its content span is whichever is the greater of the declared width
        /// and what the box did advance the cursor by: a box holding no word of its own can still hold
        /// something that took room on the line, another empty inline-block's padding say.
        /// </para>
        /// </remarks>
        /// <param name="box">the atomic inline-level box whose flow has just finished</param>
        /// <param name="coordinates">the line being flowed</param>
        /// <param name="startOrdinal">the word ordinal when this box was entered</param>
        /// <param name="startX">this box's content-box left edge on the line</param>
        /// <param name="trueStartY">this box's border-box top</param>
        /// <param name="usedContentWidth">how far this box's own content advanced the cursor</param>
        private static void ReserveAtomicInlineUsedWidth(CssBox box, CssLineBoxCoordinates coordinates,
            int startOrdinal, double startX, double trueStartY, double usedContentWidth)
        {
            // Only on the line this box opened on, which for an atomic inline is the one it closes on
            // too. One whose own content wrapped has an extent spread over lines that neither a single
            // advance nor a single rectangle describes - and FlowBox's per-child loop reserves the
            // declared width on the cursor regardless, so nothing is lost by declining here.
            if (usedContentWidth < 0 || !ReferenceEquals(box.FirstHostingLineBox, coordinates.Line)) return;

            // Zero for an auto width, where the content's own advance is all there is.
            var contentWidth = box.AvailableWidth;

            if (usedContentWidth < contentWidth)
            {
                coordinates.CurrentX = startX + contentWidth;
            }

            if (coordinates.WordOrdinal != startOrdinal) return;

            var leadingSpacing = box.ActualBorderLeftWidth + box.ActualPaddingLeft;
            var trailingSpacing = box.ActualBorderRightWidth + box.ActualPaddingRight;

            coordinates.Line.Rectangles[box] = new RRect(
                startX - leadingSpacing,
                trueStartY,
                leadingSpacing + Math.Max(usedContentWidth, contentWidth) + trailingSpacing,
                box.ActualHeight);
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

            // Unlike a plain inline box, b.Location is already final here, so string-set can be applied
            // and finalized in one step rather than split across entry/exit like plain inlines. `page`
            // is deliberately NOT registered for an inline-flex box (issue #149) - see the plain-inline
            // exit path above (FlowBox) for why: an inline-level box never creates a class-A break point,
            // so `page` doesn't apply to it per css-page-3 §7.2.
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
        /// Resolves an atomic inline-level box's used border-box width under the ordinary CSS 2.1 §10.3.9
        /// width algorithm - a declared, non-auto <c>width</c> (length, percentage, or <c>calc()</c>,
        /// via <see cref="GetBoxWidth(RGraphics, CssBox, double?)"/>'s own already-correct resolution of all three against the
        /// containing block's real, page-aware basis) as-is, or otherwise shrink-to-fit, floored by both
        /// the box's own min-content width and an explicit <c>min-width</c> - without committing any
        /// placement geometry.
        /// <para>
        /// For <c>inline-block</c> this IS the box's real used width, applied by the caller
        /// (<see cref="PrepareAtomicBlockContentChild"/>) once the line fit check
        /// (<see cref="FitAtomicInlineOnLine"/>) passes. For <c>inline-table</c>/<c>inline-grid</c>/
        /// <c>inline-flex</c>, called with the very same box and blockTop, it is only an ESTIMATE for
        /// that fit check - never a value used to size the box - since each of those settles its own
        /// real used width independently once its own layout (column/track algorithm, or the flex
        /// algorithm) actually runs. Sharing this one resolution rather than a second, narrower
        /// reimplementation for the other three displays is what a fit check needs: an
        /// <c>inline-table</c> with a percentage <c>width</c>'s wrap decision has to reflect the same
        /// percentage/<c>calc()</c>/min-width-floored width its own layout will actually use, or the two
        /// can disagree about whether it fits (issue #1105).
        /// </para>
        /// </summary>
        private static async ValueTask<double> ResolveAtomicInlineBlockWidth(
            RGraphics g, CssBox b, double blockTop)
        {
            var stretchWidth = await GetBoxWidth(g, b, blockTop);

            if (!CssValueParser.IsValidLength(b.Width))
            {
                var fitContentWidth = await GetFitContentWidth(g, b, stretchWidth);
                fitContentWidth = Math.Max(fitContentWidth, await GetMinContentWidth(g, b));

                if (b.MinWidth != "0" && CssValueParser.IsValidLength(b.MinWidth))
                {
                    var minWidth = CssValueParser.ParseLength(b.MinWidth, b.ContainingBlock.Size.Width, b)
                        + b.ActualBoxSizeIncludedWidth;
                    fitContentWidth = Math.Max(fitContentWidth, minWidth);
                }

                return fitContentWidth;
            }

            // GetBoxWidth returns a declared `width` exactly as declared, so under
            // `box-sizing: content-box` it names the CONTENT box while Location.X will name the border
            // box's own left edge. ActualBoxSizeIncludedWidth is the conversion between the two
            // (zero under `border-box`, where the declared length already is the border box), and it is
            // what keeps a box that reaches this path because its own inline content wraps
            // (LaysOutAsAnAtomicBox) the same painted width as the identical box whose content happens
            // to fit on one line - that one is sized by ResolveAtomicInlineDeclaredWidth, which reads
            // the declared length the same way.
            return stretchWidth + b.ActualBoxSizeIncludedWidth;
        }

        /// <summary>
        /// The three-way result of preflighting an atomic inline-level box against what is left of the
        /// current line (CSS 2.1 §9.4.2) - see <see cref="FitAtomicInlineOnLine"/>.
        /// </summary>
        private enum AtomicInlineLineFit
        {
            /// <summary>The box fits (or the block's own <c>white-space</c> forbids wrapping, or it is
            /// already at the start of the line, where wrapping could never help) - lay it out where it
            /// already sits.</summary>
            Fits,

            /// <summary>The box did not fit and the line was closed for it. <c>coordinates.CurrentX</c>
            /// now names its position on the freshly-opened line, and the caller should re-resolve any
            /// width of its own that can depend on the line's position (an inline-block's does; an
            /// inline-table/-grid/-flex's does not, since their own layout settles it independently).</summary>
            Wrapped,

            /// <summary>line-clamp stopped the layout instead of wrapping the box onto a new line. The
            /// caller must return immediately without laying the box out at all.</summary>
            ClampedStop
        }

        /// <summary>
        /// Preflights an atomic inline-level box's estimated outer (margin-box) width against what is
        /// left of the current line and, if it does not fit, closes the line for it (CSS 2.1 §9.4.2) -
        /// shared by inline-block (issue #1103) and, since issue #1105, by inline-table/inline-grid/
        /// inline-flex, which previously reached <see cref="FlowAtomicBlockContentChild"/>/
        /// <see cref="FlowInlineFlexChild"/> with no fit check of any kind and so never wrapped no matter
        /// how far they overflowed the line.
        /// </summary>
        /// <param name="g">Graphics context, forwarded to <see cref="TryApplyLineClamp"/>.</param>
        /// <param name="blockBox">The block establishing the inline formatting context <paramref name="b"/>
        /// flows within - the box whose own <c>white-space</c>/line-clamp/text-indent govern the line.</param>
        /// <param name="box">The box whose recursive <c>FlowBox</c> call is placing <paramref name="b"/> -
        /// passed through to <see cref="LeftFloatAt"/>/<see cref="RightFloatAt"/>, which query floats
        /// relative to it - both a float preceding <paramref name="box"/> as an ordinary sibling AND one
        /// living among <paramref name="box"/>'s own inline content (issue #1038), so an atomic inline-level
        /// box sharing a line with an inline float wraps around it exactly as an ordinary word would.</param>
        /// <param name="b">The atomic inline-level box being preflighted.</param>
        /// <param name="outerContentWidth">
        /// The box's estimated outer content-box width (border/padding included, margin excluded) - for
        /// this fit check ONLY, never a value later used to size the box itself. All four displays resolve
        /// this the same way, via <see cref="ResolveAtomicInlineBlockWidth"/> (see its own remarks for why
        /// that one resolution is shared rather than reimplemented per display): a declared width (length,
        /// percentage, or <c>calc()</c>) as-is, or otherwise shrink-to-fit floored by min-content and by an
        /// explicit <c>min-width</c>. For inline-block this IS the value <see cref="FlowAtomicBlockContentChild"/>
        /// goes on to apply; inline-table/inline-grid/inline-flex settle their real used width
        /// independently once their own layout actually runs, so their estimate is discarded either way.
        /// </param>
        /// <param name="coordinates">The line-building state being advanced.</param>
        /// <param name="lineSpacing">Passed through to <see cref="OpenNextLine"/> for the new line's leading.</param>
        /// <param name="lineStartX">Passed through to <see cref="OpenNextLine"/> as the new line's left edge.</param>
        /// <param name="leftSpacing"><paramref name="b"/>'s own margin+border+padding, re-applied after a wrap
        /// re-establishes the line start from scratch.</param>
        /// <param name="clonedTrailing">Room reserved for a <c>box-decoration-break: clone</c> ancestor's
        /// re-inserted trailing border/padding, subtracted from the fit check the same way the ordinary
        /// per-word wrap decision does.</param>
        /// <param name="clonesDecorations">Whether <paramref name="b"/>'s container clones decorations across
        /// breaks, so a wrap must re-apply the cloned leading inset too.</param>
        /// <param name="isRtl">Whether <paramref name="blockBox"/> flows right-to-left, which flips which
        /// side of the line the fit check and the reopened line's text-indent are measured from.</param>
        private static async ValueTask<AtomicInlineLineFit> FitAtomicInlineOnLine(RGraphics g,
            CssBox blockBox, CssBox box, CssBox b, double outerContentWidth,
            CssLineBoxCoordinates coordinates, double lineSpacing, double lineStartX, double leftSpacing,
            double clonedTrailing, bool clonesDecorations, bool isRtl)
        {
            var actualLimitRight = coordinates.Line.ContentRight;
            var rightFloat = RightFloatAt(coordinates, box);
            if (rightFloat is not null)
            {
                actualLimitRight = rightFloat.Location.X - rightFloat.ActualMarginLeft;
            }

            if (isRtl)
            {
                actualLimitRight -= GetLineTextIndent(blockBox,
                    coordinates.Line.Equals(blockBox.LineBoxes[0]), coordinates.Line.FollowsForcedBreak);
            }

            var borderLeft = coordinates.CurrentX - b.ActualPaddingLeft - b.ActualBorderLeftWidth;
            var outerRight = borderLeft + outerContentWidth + b.ActualMarginRight + clonedTrailing;
            var blockPermitsWrap = blockBox.WhiteSpace.Value is not (Whitespace.NoWrap or Whitespace.Pre);

            if (!blockPermitsWrap || IsAtLineStart(coordinates) || outerRight <= actualLimitRight + 0.01)
            {
                return AtomicInlineLineFit.Fits;
            }

            if (TryApplyLineClamp(g, blockBox, coordinates, actualLimitRight, 0, clonedTrailing))
            {
                b.SuppressFragmentEmissionForCurrentLayout();
                coordinates.ClampedStop = true;
                return AtomicInlineLineFit.ClampedStop;
            }

            OpenNextLine(blockBox, coordinates, lineSpacing, lineStartX,
                coordinates.WordOrdinal, followsForcedBreak: false, isRtl);

            var leftFloat = LeftFloatAt(coordinates, box);
            if (leftFloat is not null)
            {
                coordinates.CurrentX = leftFloat.ActualRight + leftFloat.ActualMarginRight;
            }

            coordinates.CurrentX += leftSpacing + (clonesDecorations
                ? DomUtils.ClonedInlineStart(b.ParentBox, blockBox)
                : 0);

            return AtomicInlineLineFit.Wrapped;
        }

        /// <summary>
        /// Positions an inline-table/inline-grid, or an inline-block whose content needs an independent
        /// formatting context, at the current parent-line cursor. For an inline-block it also commits the
        /// already-resolved used width before child layout. The actual child layout and parent-line
        /// registration remain in <see cref="FlowAtomicBlockContentChild"/>.
        /// </summary>
        private static async ValueTask PrepareAtomicBlockContentChild(RGraphics g, CssBox b,
            CssLineBoxCoordinates coordinates, double? resolvedInlineBlockWidth = null)
        {
            // coordinates.CurrentX is already past the left margin+border+padding (leftSpacing was added
            // by the caller), so Location.X sits at the border-left edge (after margin).
            b.Location = new RPoint(
                coordinates.CurrentX - b.ActualPaddingLeft - b.ActualBorderLeftWidth,
                coordinates.CurrentY);
            b.ActualBottom = b.Location.Y;

            // An inline-table/inline-grid resolves its own width internally via its own column/track
            // algorithm and needs no pre-step. A plain inline-block never gets ActualRight resolved by
            // anything else on this path - unlike the ordinary inline-block-with-inline-content case (which
            // never reaches this helper at all), so it's resolved here unconditionally: GetBoxWidth alone
            // handles an explicit length/percentage width, and an auto width additionally needs the
            // shrink-to-fit refinement (CSS2.1 §10.3.9) GetFitContentWidth/GetMinContentWidth add, mirroring
            // the orthogonal-flow-child shrink-to-fit case at CssBox.PlaceAndSizeBlockChild (~line
            // 4269-4300). Without this, CssBox.LayoutContents' own ContainsInlinesOnly/LayoutBlockChildren
            // dispatch for this box's content - reached next, via LayoutContentAtItsAssignedPosition - would
            // run against an unresolved (zero) ClientRight.
            if (b.DerivedStyle.ActualDisplay == Keywords.InlineBlock)
            {
                var usedWidth = resolvedInlineBlockWidth
                    ?? await ResolveAtomicInlineBlockWidth(g, b, coordinates.CurrentY);
                b.ActualRight = b.Location.X + usedWidth;
            }
        }

        private static async ValueTask FlowAtomicBlockContentChild(RGraphics g, CssBox b,
            CssLineBoxCoordinates coordinates, double? resolvedInlineBlockWidth = null)
        {
            // A speculative wrap may have rejected this box earlier in the same layout generation.
            // Reaching real placement is the authoritative point at which it becomes fragment-visible.
            b.AllowFragmentEmissionForCurrentLayout();
            await PrepareAtomicBlockContentChild(g, b, coordinates, resolvedInlineBlockWidth);
            b.ActualBottom = b.Location.Y;
            b.FirstHostingLineBox = coordinates.Line;
            b.LastHostingLineBox = coordinates.Line;

            // Same as FlowInlineFlexChild: b.Location is already final here, so string-set can be applied
            // and finalized in one step. `page` is deliberately not registered here either (issue #149) -
            // an inline-level box never creates a class-A break point.
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

            await LayoutContentUnbroken(g, b);

            // A box whose own content is inlines-only just laid out line boxes of its own, and came back
            // out of that machinery carrying one rectangle per line - the shape an INLINE box needs, so
            // that its background and border follow its content line by line. This box is not inline: it
            // is an atomic box whose border box is the single rectangle registered on the parent's line
            // below. Leaving the per-line ones behind paints its border once per internal line, as a
            // short box around each of them on top of the real one. Nothing else reads them - the box's
            // own lines address their content through their own rectangles, not through the box's.
            b.Rectangles.Clear();

            // Advance to content-right so that the outer rightSpacing addition lands correctly.
            coordinates.CurrentX = b.ClientRight;
            coordinates.MaxRight = Math.Max(coordinates.MaxRight, b.Location.X + b.ActualBoxSizingWidth);
            // An atomic inline contributes its margin box to the surrounding line's height (CSS 2.1
            // §10.8.1). The wrap path opens the next line from MaxBottom, so omitting the bottom
            // margin made consecutive rows of inline-block cards touch even though their horizontal
            // margins were honored.
            coordinates.MaxBottom = Math.Max(coordinates.MaxBottom,
                b.Location.Y + b.ActualBoxSizingHeight + b.ActualMarginBottom);

            // Register the box in the parent line so its border/background is painted.
            coordinates.Line.Rectangles[b] = new RRect(
                b.Location.X, b.Location.Y, b.ActualBoxSizingWidth, b.ActualBoxSizingHeight);
        }

        /// <summary>
        /// Closes the current inline-formatting line and opens its successor. The caller owns the
        /// content-specific work on either side of that boundary (speculative extent rollback,
        /// line-clamp, first-line remeasurement, float intersection, and leading decorations).
        /// </summary>
        private static void OpenNextLine(CssBox blockBox, CssLineBoxCoordinates coordinates,
            double lineSpacing, double lineStartX, int startOrdinal, bool followsForcedBreak, bool isRtl)
        {
            coordinates.ConsecutiveHyphenatedLines =
                coordinates.CurrentLineHyphenated ? coordinates.ConsecutiveHyphenatedLines + 1 : 0;
            coordinates.CurrentLineHyphenated = false;
            coordinates.Line.PrecedesForcedBreak = followsForcedBreak;
            coordinates.CurrentX = lineStartX;
            coordinates.CurrentY = coordinates.MaxBottom + lineSpacing;
            coordinates.Line = new CssLineBox(blockBox)
            {
                FollowsForcedBreak = followsForcedBreak,
                ContentRight = LineContentRightOf(blockBox, coordinates.CurrentY),
                ContentLeft = lineStartX,
                StartOrdinal = startOrdinal
            };
            coordinates.LineStartOrdinal = startOrdinal;
            coordinates.TrailingRegionalIndicatorCount = 0;
            coordinates.TrailingGraphemeContext = string.Empty;

            if (!isRtl)
            {
                coordinates.CurrentX += GetLineTextIndent(blockBox, isFirstLine: false,
                    coordinates.Line.FollowsForcedBreak);
            }
        }

        /// <summary>
        /// Places a floated child encountered mid-inline-flow (issue #1038: a float that is a sibling of
        /// ordinary inline content within one inline formatting context, rather than a separate block-level
        /// box that merely precedes or follows one) and lays out its own content.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Positioned directly from the live inline cursor rather than through
        /// <see cref="CssBox.PerformLayoutImp"/>'s ordinary block-child path
        /// (<c>CssBox.PlaceAndSizeBlockChild</c>/<c>ResolveBlockChildOffset</c>): that path resolves a
        /// block-level child's top from its previous BLOCK-level sibling's bottom edge, which has no
        /// relationship to <paramref name="coordinates"/>'s current line - and CSS 2.1 §9.5.1 rule 6 puts a
        /// float's outer top no higher than the top of THAT line, the one this flow is building right now.
        /// This mirrors <see cref="PrepareAtomicBlockContentChild"/>'s own direct-from-cursor placement for
        /// an inline-block.
        /// </para>
        /// <para>
        /// Content is then laid out through <see cref="CssBox.LayoutContentAtItsAssignedPosition"/> - the
        /// same entry point <see cref="FlowAtomicBlockContentChild"/> already uses for an inline-block
        /// holding real block-level content - so a float with block-level children of its own gets the
        /// identical, already-tested block-content layout machinery an ordinary block-level floated
        /// sibling already gets via <c>CssBox.PlaceAndSizeBlockChild</c>: <c>framePlacesChild: false</c> is
        /// what makes that shared entry point skip re-resolving a position this method has already
        /// assigned, while still running the box's own prologue/content/epilogue phases unchanged.
        /// </para>
        /// <para>
        /// <b>Scope</b>: like <see cref="CssLayoutEngine.FloatBox"/> itself (via
        /// <c>containingBox.Boxes.IndexOf(box)</c>), this assumes <paramref name="b"/> is a direct child of
        /// <paramref name="blockBox"/>'s own <see cref="CssBox.Boxes"/> - the shape #1038 and every existing
        /// float-placement call site already assume. A float nested deeper inside another inline element's
        /// own children is placed here too (FlowBox recurses into nested inlines regardless), but
        /// <c>ClearBox</c>'s own sibling scan would not find it among the right list in that shape, exactly
        /// as it already would not for an ordinary block-level float in that position today.
        /// </para>
        /// <para>
        /// <b>Pagination</b>: the content is laid out as one unbroken run (<see cref="LayoutContentUnbroken"/>),
        /// because nothing up this inline flow resumes a record that a box it places itself leaves behind. One
        /// taller than the page it starts on therefore runs on past the page's foot and each page shows the
        /// slice that falls in it, rather than the lines after a break being dropped (issue #1201). It is not
        /// fragmented at its own break points, so it does not continue <i>beside</i> the text flowing around it
        /// onto the next page; that is float fragmentation proper (issue #317).
        /// </para>
        /// </remarks>
        private static async ValueTask FlowFloatChild(RGraphics g, CssBox blockBox, CssBox b, CssLineBoxCoordinates coordinates)
        {
            // A speculative wrap may have rejected this box earlier in the same layout generation -
            // reaching real placement is the authoritative point at which it becomes fragment-visible,
            // exactly as for FlowAtomicBlockContentChild's identical call.
            b.AllowFragmentEmissionForCurrentLayout();

            b.Location = new RPoint(
                blockBox.ClientLeft + b.ActualMarginLeft, coordinates.Line.FlowTop ?? coordinates.CurrentY);
            b.ActualBottom = b.Location.Y;

            var width = await GetBoxWidth(g, b, b.Location.Y);
            b.ActualRight = b.Location.X + width + b.ActualBoxSizeIncludedWidth;

            // The actual left/right placement scan (past any other already-placed float) and `clear`
            // handling - the same call the ordinary block-child float path makes from
            // CssBox.CommitBlockChildOffset.
            FloatBox(b);

            (coordinates.InlineFloats ??= []).Add(b);

            await LayoutContentUnbroken(g, b);
        }

        /// <summary>
        /// Lays out <paramref name="b"/>'s own content at the position it was given, as one unbroken run:
        /// with the fragmentainer detached and per-word page breaks suppressed, the way an unbreakable box's
        /// content is laid out (<c>CssBox.LayoutContents</c>'s monolithic path).
        /// </summary>
        /// <remarks>
        /// For a box the inline flow places itself - a float among inline content, an inline-block holding
        /// block-level content. Nothing up that flow resumes a record such a box leaves behind, so a break
        /// taken inside it would drop everything after the break: a float of twenty lines on pages that hold
        /// eight showed eight (issues #1201, #1332). Laid out whole, its geometry runs on past the page's
        /// foot and each page's fragment shows the slice that falls in it, as an unbreakable box's does
        /// (css-break-3 §2: content that cannot be broken may be sliced to avoid losing it, §4.4).
        /// </remarks>
        private static async ValueTask LayoutContentUnbroken(RGraphics g, CssBox b)
        {
            var container = b.HtmlContainer;
            var previousFragmentainer = container?.DetachFragmentainer();
            var previousSuppress = container?.SuppressWordPageBreaks ?? false;
            if (container is not null) container.SuppressWordPageBreaks = true;

            try
            {
                await b.LayoutContentAtItsAssignedPosition(g);
            }
            finally
            {
                if (container is not null)
                {
                    container.RestoreFragmentainer(previousFragmentainer);
                    container.SuppressWordPageBreaks = previousSuppress;
                }
            }
        }

        /// <summary>
        /// The most restrictive of <paramref name="coordinates"/>'s own
        /// <see cref="CssLineBoxCoordinates.InlineFloats"/> on <paramref name="side"/> whose vertical span
        /// covers the line currently being built - the counterpart, for a float <see cref="FlowBox"/> has
        /// placed directly among a box's own inline content, of what
        /// <see cref="DomUtils.GetLastLeftIntersectingFloatBox"/> (furthest reach wins) and
        /// <see cref="DomUtils.GetLastRightIntersectingFloatBox"/> (narrowest reach wins) already do for a
        /// float that precedes the box being flowed as a SIBLING of it. Kept as a separate, small,
        /// unoptimized scan over <see cref="CssLineBoxCoordinates.InlineFloats"/> (typically holding very
        /// few floats) rather than folded into those two: they walk ancestors and preceding siblings by
        /// index for performance reasons that do not apply here, and would need an index bound to safely
        /// avoid reading a not-yet-placed float's stale geometry if changed to scan a box's own children.
        /// </summary>
        /// <remarks>
        /// A left float is a POINT-collision test against <see cref="CssLineBoxCoordinates.CurrentX"/> -
        /// the same convention <see cref="DomUtils.GetLastLeftIntersectingFloatBox"/> already uses (see its
        /// own remarks) - not merely a vertical-span one: once the cursor has genuinely advanced past the
        /// float's right edge (placing earlier words on this same line), it must stop being reported as
        /// intersecting, or every later word on the line is clamped straight back to that edge instead of
        /// being left where it actually landed. A right float keeps the lookahead convention
        /// <see cref="DomUtils.GetLastRightIntersectingFloatBox"/> already documents instead (its own
        /// remarks: "independent of the cursor's current position") - it caps how far right content on
        /// this row may extend in advance, not where the cursor happens to sit right now.
        /// </remarks>
        private static CssBox? GetIntersectingInlineFloat(CssLineBoxCoordinates coordinates, Floating side)
        {
            if (coordinates.InlineFloats is not { Count: > 0 } floats) return null;

            CssBox? best = null;

            foreach (var floatBox in floats)
            {
                // The resolved physical side, not the specified keyword: an `inside`/`outside` float
                // placed among inline content is a left or a right one depending on its page.
                if (floatBox.EffectiveFloatSide != side) continue;

                var top = floatBox.Location.Y;
                var bottom = floatBox.ActualBottom + floatBox.ActualMarginBottom;

                if (coordinates.CurrentY < top || coordinates.CurrentY >= bottom) continue;

                if (side == Floating.Left)
                {
                    var rightEdge = floatBox.ActualRight + floatBox.ActualMarginRight;

                    // The cursor has already moved past this float's right edge (earlier words on this
                    // line advanced it there) - it no longer constrains anything further along the line.
                    if (coordinates.CurrentX >= rightEdge) continue;

                    if (best is null || rightEdge > best.ActualRight + best.ActualMarginRight)
                    {
                        best = floatBox;
                    }
                }
                else if (floatBox.ActualRight + floatBox.ActualMarginRight > coordinates.Line.ContentLeft
                         && (best is null || floatBox.Location.X - floatBox.ActualMarginLeft
                             < best.Location.X - best.ActualMarginLeft))
                {
                    // Not one lying wholly left of the line: a float in an earlier column of a multi-column
                    // container covers the same rows as this column's lines without being beside them.
                    best = floatBox;
                }
            }

            return best;
        }

        /// <summary>
        /// The more restrictive of the last LEFT float intersecting the line at <paramref name="reference"/>'s
        /// position - combining <see cref="DomUtils.GetLastLeftIntersectingFloatBox"/> (a float preceding
        /// <paramref name="reference"/> as a sibling of it, or of one of its ancestors) with
        /// <see cref="GetIntersectingInlineFloat"/> (a float <see cref="FlowBox"/> placed directly among
        /// <paramref name="reference"/>'s own inline content, issue #1038) - since neither one alone can
        /// discover a float the other shape reaches. A document with no float in the #1038 shape leaves
        /// <see cref="CssLineBoxCoordinates.InlineFloats"/> empty, so this returns exactly what the
        /// ancestor-only lookup already did on its own.
        /// </summary>
        /// <remarks>
        /// Shared rather than duplicated: originally a local function inside <see cref="FlowBox"/> (which
        /// still calls it, passing its own <c>coordinates</c>/<c>box</c>), promoted to a standalone method
        /// once <see cref="FitAtomicInlineOnLine"/> needed the identical combining logic for an atomic
        /// inline-level box's own line-fit check (issue #1105) - a float living among the SAME box's
        /// inline content must narrow that check's available width exactly as it narrows an ordinary
        /// word's, and a plain <see cref="DomUtils.GetLastLeftIntersectingFloatBox"/> call there would miss
        /// it precisely as this method's own remarks describe.
        /// </remarks>
        private static CssBox? LeftFloatAt(CssLineBoxCoordinates coordinates, CssBox reference)
        {
            var ancestorFloat = LineOwnerIsIsolatedFromOuterFloats(coordinates, reference)
                ? null
                : DomUtils.GetLastLeftIntersectingFloatBox(reference, coordinates);
            var inlineFloat = GetIntersectingInlineFloat(coordinates, Floating.Left);

            if (ancestorFloat is null) return inlineFloat;
            if (inlineFloat is null) return ancestorFloat;

            return inlineFloat.ActualRight + inlineFloat.ActualMarginRight
                   > ancestorFloat.ActualRight + ancestorFloat.ActualMarginRight
                ? inlineFloat
                : ancestorFloat;
        }

        /// <summary>
        /// Whether <paramref name="reference"/> is the block whose lines are being laid out AND that block
        /// is an out-of-flow box, a table cell, or a grid or flex item - a formatting-context root that is
        /// not itself placed beside outside floats - so no float outside it can shorten those lines.
        /// </summary>
        /// <remarks>
        /// <see href="https://www.w3.org/TR/CSS21/visuren.html#block-formatting">CSS 2.1 §9.4.1</see>: a line
        /// box is shortened only by floats in its own block formatting context, and a box that establishes a
        /// new one contains its floats and is unaffected by any outside it (issue #1335 - a preceding
        /// <c>float: left</c> pushed an absolutely positioned box's inline content 523pt to the right, off
        /// the page). The ancestor walk in <see cref="DomUtils.GetLastLeftIntersectingFloatBox"/> cannot
        /// apply this itself at its starting level: the box it starts from is also the box being
        /// <i>placed</i> by <c>FloatBoxLeft</c>, and a float must still see its own preceding siblings (see
        /// the note in <c>FindIntersectingFloatBox</c>). Line flow knows its own owner, so it asks here
        /// instead. Only the owner itself is tested: when <paramref name="reference"/> is a descendant, the
        /// walk climbs to the owner and its non-starting-level check already stops there.
        /// </remarks>
        private static bool LineOwnerIsIsolatedFromOuterFloats(CssLineBoxCoordinates coordinates, CssBox reference)
        {
            if (!ReferenceEquals(reference, coordinates.Line.OwnerBox)) return false;

            // Only the roots that are not themselves placed beside a float: an in-flow root
            // (`overflow: hidden`, a table caption) has to avoid the floats next to it (CSS 2.1 §9.5), and
            // this engine does that by narrowing its lines, so those keep the outer float lookup.
            return reference.IsFloated
                   || reference.IsPageFloated
                   || reference.IsAbsolutelyPositioned
                   || reference.DerivedStyle.ActualDisplay == Keywords.TableCell
                   || reference.ParentBox?.DerivedStyle.ActualDisplay is Keywords.Flex or Keywords.InlineFlex
                       or Keywords.Grid or Keywords.InlineGrid;
        }

        /// <summary>
        /// The RIGHT-float counterpart of <see cref="LeftFloatAt"/> - see its own remarks for why this is
        /// shared between <see cref="FlowBox"/> and <see cref="FitAtomicInlineOnLine"/> rather than
        /// duplicated, and why the two sides combine <see cref="DomUtils.GetLastRightIntersectingFloatBox"/>
        /// with <see cref="GetIntersectingInlineFloat"/> differently (narrowest reach wins here, matching
        /// <see cref="DomUtils.GetLastRightIntersectingFloatBox"/>'s own lookahead convention).
        /// </summary>
        private static CssBox? RightFloatAt(CssLineBoxCoordinates coordinates, CssBox reference)
        {
            var ancestorFloat = DomUtils.GetLastRightIntersectingFloatBox(reference, coordinates);
            var inlineFloat = GetIntersectingInlineFloat(coordinates, Floating.Right);

            if (ancestorFloat is null) return inlineFloat;
            if (inlineFloat is null) return ancestorFloat;

            return inlineFloat.Location.X - inlineFloat.ActualMarginLeft
                   < ancestorFloat.Location.X - ancestorFloat.ActualMarginLeft
                ? inlineFloat
                : ancestorFloat;
        }

        /// <summary>
        /// Whether <paramref name="child"/> is an <c>inline-block</c> that has to be laid out as a
        /// genuine atomic box (<see cref="FlowAtomicBlockContentChild"/>) rather than have its content
        /// flowed into <paramref name="parent"/>'s own line boxes.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Two shapes need it. One is a box holding real block-level content
        /// (<see cref="HasBlockLevelDescendant"/>, issue #473), which has no inline formatting context to
        /// be flattened into in the first place. The other is a box whose inline content <b>does not fit
        /// on one line inside it</b>: flattening that one hands its words to the parent's line breaker,
        /// which wraps them at the parent's measure rather than the box's, so the box's own content
        /// escapes it and its border box is drawn as two disjoint line rectangles - in a wrapped document
        /// that lands across whatever sits above the line
        /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/1053">#1053</see>). An atomic inline
        /// establishes its own formatting context and wraps inside its own width
        /// (<see href="https://www.w3.org/TR/css-display-3/#atomic-inline">css-display-3 §2.3</see>), so
        /// the box that cannot hold its content on one line is exactly the one that must own the lines.
        /// </para>
        /// <para>
        /// The measurement is made in border-box terms on both sides, because
        /// <see cref="GetMaxContentWidth"/> reports a box's own border and padding along with its
        /// content. A declared <c>width</c> names the used width directly
        /// (<see cref="ResolveAtomicInlineDeclaredWidth"/>'s rule, deliberately not assigned here — this
        /// runs before the cursor reaches the box's content edge); an <c>auto</c> one is shrink-to-fit
        /// (CSS 2.1 §10.3.9), which only wraps when the content cannot fit the containing block either.
        /// </para>
        /// </remarks>
        private static async ValueTask<bool> LaysOutAsAnAtomicBox(RGraphics g, CssBox child, CssBox parent, double blockTop)
        {
            if (child.DerivedStyle.ActualDisplay is not Keywords.InlineBlock || ReferenceEquals(child, parent))
            {
                return false;
            }

            if (HasBlockLevelDescendant(child)) return true;

            var available = PageAwareWidthBasis(child.ContainingBlock, blockTop);

            if (CssValueParser.IsValidLength(child.Width))
            {
                var declared = CssValueParser.ParseLength(child.Width, available, child);

                available = child.BoxSizing.Value is BoxSizingMode.BorderBox
                    ? Math.Max(declared, child.ActualBorderLeftWidth + child.ActualPaddingLeft
                        + child.ActualPaddingRight + child.ActualBorderRightWidth)
                    : declared + child.ActualBoxSizeIncludedWidth;
            }
            else
            {
                available -= child.ActualMarginLeft + child.ActualMarginRight;
            }

            if (available <= 0) return false;

            // A hair of slack, so a box whose content measures its own width exactly - the overwhelmingly
            // common `width` set to fit its label - is not pushed onto the atomic path by rounding.
            return await GetMaxContentWidth(g, child) > available + 0.01;
        }

        /// <summary>
        /// Whether <paramref name="box"/> has a genuinely block-level box anywhere in its subtree - looking
        /// THROUGH nested atomic inline-level boxes rather than stopping at them, unlike
        /// <see cref="DomUtils.ContainsInlinesOnly"/>'s own deep-descent counterpart in <c>DomParser</c>
        /// (which deliberately treats an atomic box as opaque for DOM-fixup purposes - see issue #473).
        /// Used to decide whether an inline-block box needs FlowAtomicBlockContentChild's block-content
        /// layout path rather than the ordinary recursive-inline-flow one: a
        /// <c>&lt;table style="display:inline-block"&gt;</c> whose rows were wrapped in a synthesized,
        /// <c>Display=InlineTable</c> anonymous box (an inline-block table isn't <see cref="CssBox.IsBlock"/>,
        /// so <c>DomParser.CorrectAnonymousTablesGenerateMissingParents</c> gives it an inline-table wrapper
        /// rather than a block one) has only ONE direct child, and that child is itself
        /// <see cref="CssBox.IsInline"/> - a shallow check reports "purely inline content" for a box that in
        /// fact holds a whole table's worth of block-level rows one level further down.
        /// </summary>
        private static bool HasBlockLevelDescendant(CssBox box)
        {
            foreach (var childBox in box.Boxes)
            {
                if (childBox.DerivedStyle.ActualDisplay == Keywords.None) continue;
                if (!childBox.IsInline) return true;
                if (HasBlockLevelDescendant(childBox)) return true;
            }

            return false;
        }

        /// <summary>
        /// The total thickness a line box holding <paramref name="word"/> is obliged to occupy — the
        /// two sides of <see cref="LineBoxContributionOf"/> summed. Used by the vertical-writing-mode
        /// engine, which sizes a line's cross axis but has no baseline of its own to place content
        /// against; <see cref="FlowBox"/> keeps the two sides apart instead, because it does.
        /// </summary>
        private static double LineBoxExtentOf(CssRect word, CssBox blockBox) =>
            // No double-overline reservation here: vertical-writing-mode decoration geometry is still
            // horizontal-only (issue #1075's own remaining scope), so reserving ascent-side headroom for
            // a decoration that direction doesn't even apply to yet would only inflate the column for no
            // current benefit.
            LineBoxContributionOf(word, blockBox, pixelsPerPoint: 0, reserveDoubleOverlineReach: false).Height;

        /// <summary>
        /// One inline box's share of a line box, split at the baseline, per
        /// <see href="https://www.w3.org/TR/CSS21/visudet.html#line-height">CSS 2.1 §10.8.1</see>: its
        /// glyph content area (<c>ascent</c>/<c>descent</c>) with the <b>leading</b> —
        /// <c>line-height - (ascent + descent)</c> — split in half and added to each side. A
        /// <c>line-height</c> larger than the font centres the content area in the line box rather than
        /// hanging it from the top; a smaller one makes the leading negative and the glyphs deliberately
        /// overflow the line on both sides.
        /// </summary>
        private static LineBoxExtent HalfLeadingExtentOf(RFont font, double lineHeight)
        {
            var halfLeading = (lineHeight - font.Height) / 2;

            return new LineBoxExtent(font.Ascent + halfLeading, font.Height - font.Ascent + halfLeading);
        }

        /// <summary>
        /// <see cref="HalfLeadingExtentOf"/> for <paramref name="box"/>, extended on the ascent side by
        /// <see cref="FragmentPainter.DoubleOverlineExtraReachAbove"/> when <paramref name="box"/> itself
        /// carries a <c>double</c> overline (issue #1124) - so a line box reserves the headroom that
        /// decoration's outer stroke needs above the box's own top edge, rather than leaving it to
        /// whatever margin/pagination happens to already be there. Zero extra reach is the overwhelming
        /// common case and this is then identical to <see cref="HalfLeadingExtentOf"/>.
        /// </summary>
        private static LineBoxExtent HalfLeadingExtentWithDecoration(CssBox box, double pixelsPerPoint)
        {
            var extent = HalfLeadingExtentOf(box.ActualFont, box.ActualLineHeight);
            var reach = FragmentPainter.DoubleOverlineExtraReachAbove(box, pixelsPerPoint);
            return reach > 0 ? extent with { AboveBaseline = extent.AboveBaseline + reach } : extent;
        }

        /// <summary>
        /// How far a line box holding <paramref name="word"/> is obliged to reach on each side of its
        /// baseline, per <see href="https://www.w3.org/TR/CSS21/visudet.html#line-height">CSS 2.1
        /// §10.8.1</see>: the per-side largest <see cref="HalfLeadingExtentOf"/> among every inline box
        /// the text sits inside — the word's effective style and its owner's inline ancestors up to
        /// <paramref name="blockBox"/> — and the <b>strut</b>, an imaginary inline box carrying
        /// <paramref name="blockBox"/>'s own font and <c>line-height</c> that is present on every line
        /// box holding content.
        /// </summary>
        /// <remarks>
        /// The two sides are maximised <i>independently</i>, which is the whole reason this returns a
        /// pair rather than a height: a line's tallest box above the baseline need not be its deepest
        /// below, so the line can be taller than any single <c>line-height</c> on it. Summing to a
        /// height first — as this did before real baseline alignment existed — silently assumed every
        /// box shared one baseline offset, which is true only while every font on the line is the same
        /// size.
        /// <para>
        /// Replaced and atomic inline content is deliberately absent here: §10.8 sizes those from the
        /// element's own margin box rather than from font metrics. <see cref="ApplyVerticalAlignment"/>
        /// folds that geometry into the closed line's baseline extent; <c>MaxBottom</c> at the call site
        /// keeps the still-open line large enough for wrapping and fragmentation decisions.
        /// </para>
        /// <para>
        /// Shared by both line-layout engines — <see cref="FlowBox"/> for horizontal writing modes and
        /// <see cref="CreateVerticalLineBoxes"/> (via <see cref="LineBoxExtentOf"/>, which wants only the
        /// summed thickness) for vertical ones — so the two cannot drift into disagreeing about how tall
        /// a line is. The effective style is <see cref="CssRect.FirstLineStyle"/> when present, because a
        /// <c>::first-line</c> font or line-height can differ from the owner's normal style, and per
        /// CSS Pseudo 4 it replaces the root inline box's own contribution rather than joining it.
        /// </para>
        /// <para>
        /// The strut itself is skipped entirely when <paramref name="blockBox"/>.<see
        /// cref="CssBox.IsReplacedBlockWrapper"/>: that box is never a real inline formatting context an
        /// author could see, only the single-child bookkeeping wrapper built around a <c>display: block</c>
        /// replaced element so it has somewhere to be an atomic inline word. Reserving the strut there
        /// re-added the exact gap <c>display: block</c> exists to remove, and the excess compounded once
        /// per element (issue #1127).
        /// </para>
        /// </remarks>
        private static LineBoxExtent LineBoxContributionOf(CssRect word, CssBox blockBox, double pixelsPerPoint,
            bool reserveDoubleOverlineReach)
        {
            // The first-line pseudo wraps the root inline box, so its inherited font and line-height
            // replace the block's ordinary strut on that line and can reduce as well as increase the
            // line box. With no ::first-line in play the strut is the block's own, and the word's own
            // inline box contributes separately alongside it.
            var ownerBox = word.OwnerBox;
            var strutStyle = word.FirstLineStyle ?? blockBox;

            LineBoxExtent ExtentOf(CssBox box) => reserveDoubleOverlineReach
                ? HalfLeadingExtentWithDecoration(box, pixelsPerPoint)
                : HalfLeadingExtentOf(box.ActualFont, box.ActualLineHeight);

            var extent = blockBox.IsReplacedBlockWrapper ? default : ExtentOf(strutStyle);

            // A replaced element's own line-height does not contribute; its margin box is added by
            // GrowLineToItsExtent instead. A non-replaced inline ancestor around it still owns an
            // ordinary inline box, so the loop below deliberately continues to include those.
            if (word.FirstLineStyle is null && !word.IsImage)
                extent = extent.Union(ExtentOf(ownerBox));

            // A word owned by the block itself has no inline ancestors. Starting at its parent in
            // that case would walk *outside* the line's formatting context and let an outer element's
            // larger font inflate this line (e.g. an 8pt chart value inside a 12pt table cell).
            for (var inlineAncestor = ReferenceEquals(ownerBox, blockBox) ? null : ownerBox.ParentBox;
                 inlineAncestor is not null && !ReferenceEquals(inlineAncestor, blockBox);
                 inlineAncestor = inlineAncestor.ParentBox)
            {
                extent = extent.Union(ExtentOf(inlineAncestor));
            }

            return extent;
        }

        /// <summary>
        /// Makes <paramref name="baselineExtent"/> the baseline-aligned extent of the line
        /// <paramref name="coordinates"/> is on, and grows the flow's <c>MaxBottom</c> to hold the line.
        /// </summary>
        /// <param name="coordinates">the flow, on the line being grown</param>
        /// <param name="baselineExtent">the line's whole baseline-aligned extent, not an increment</param>
        private static void CommitLineExtent(CssLineBoxCoordinates coordinates, LineBoxExtent baselineExtent)
        {
            var line = coordinates.Line;
            line.BaselineAlignedExtent = baselineExtent;
            var extent = IncludeEdgeAlignedAtomicHeights(
                baselineExtent, line.TopAlignedAtomicHeight, line.BottomAlignedAtomicHeight);
            line.BaselineExtent = extent;

            // The line box's own top, stated while the cursor still names it. Every question about
            // which fragmentainer a line is in is asked of this, not of a word on it - the two part
            // company by the half-leading as soon as a word is placed on its baseline.
            line.FlowTop = line.FlowTop is { } known
                ? Math.Min(known, coordinates.CurrentY)
                : coordinates.CurrentY;

            if (coordinates.MaxBottom - coordinates.CurrentY < extent.Height)
                coordinates.MaxBottom += extent.Height - (coordinates.MaxBottom - coordinates.CurrentY);
        }

        /// <summary>
        /// Grows the line <paramref name="coordinates"/> is on, which already holds content, by the empty
        /// inlines in <paramref name="extent"/>, and takes a break at the line's start if that pushes a word
        /// on it across the fragmentainer. A line holding no content is left alone: CSS 2.1 §9.4.2 keeps it
        /// at zero height, empty inlines or not.
        /// </summary>
        /// <remarks>
        /// The break is the one <see cref="FlowBox"/> takes for a word that straddles, asked of the words
        /// already on the line as the grown line will sit them. They were each asked when they were placed,
        /// but against the line's smaller extent, and nothing else asks again: a taller word placed later is
        /// asked itself, and an empty inline has no word to ask.
        /// </remarks>
        /// <param name="blockBox">the block whose inline content is being flowed</param>
        /// <param name="coordinates">the flow, on the line to grow</param>
        /// <param name="extent">the empty inlines' extent</param>
        private static void GrowLineForEmptyInlines(CssBox blockBox, CssLineBoxCoordinates coordinates, LineBoxExtent extent)
        {
            if (IsAtLineStart(coordinates)) return;

            var line = coordinates.Line;
            var aboveBefore = line.BaselineExtent?.AboveBaseline;
            CommitLineExtent(coordinates, line.BaselineAlignedExtent is { } held ? held.Union(extent) : extent);

            if (blockBox.IsFixed || blockBox.HtmlContainer?.SuppressWordPageBreaks == true
                || coordinates.Fragmentainer is null)
            {
                return;
            }

            var above = line.BaselineExtent!.Value.AboveBaseline;
            var shift = above - (aboveBefore ?? 0);
            if (shift <= 0) return;

            var container = blockBox.HtmlContainer!;
            var lineTop = line.FlowTop!.Value;

            foreach (var word in line.Words)
            {
                if (word.IsLineBreak || GrownLineTopOf(word) is not { } grownTop) continue;

                var originalTop = word.Top;
                word.Top = grownTop;
                var straddles = word.WouldStraddleFragmentainer();
                var fitsNowhere = straddles && word.LineFitsNoFragmentainer(lineTop);
                var resumeSlot = straddles && !fitsNowhere ? word.ResumeSlotForBreakBefore() : 0;
                word.Top = originalTop;

                if (!straddles || fitsNowhere) continue;

                coordinates.Break = new InlineBreakToken(
                    blockBox, resumeSlot, [], coordinates.LineStartOrdinal, CompletedLineCount: 0,
                    FollowsForcedBreak: line.FollowsForcedBreak,
                    ConsecutiveHyphenatedLines: coordinates.ConsecutiveHyphenatedLines);
                return;
            }

            StepOverADeepLine(coordinates, container);

            // Where the grown line puts the word, or null for a word it does not move, asked where
            // ApplyVerticalAlignment will put it. One aligned to the line's top stays put, and one aligned to
            // its bottom or middle goes against the grown line box. Otherwise text moves down with the
            // baseline, and a replaced element, which still sits at the line's top where the flow placed it,
            // rests its bottom margin edge on the baseline.
            double? GrownLineTopOf(CssRect word)
            {
                var height = line.BaselineExtent!.Value.Height;
                var marginBottom = word.IsImage ? word.OwnerBox.ActualMarginBottom : 0;

                return EffectiveVerticalAlignOf(word.OwnerBox, line, out _).Value switch
                {
                    { IsValue: false, Keyword: VerticalAlignment.Top } => null,
                    { IsValue: false, Keyword: VerticalAlignment.Bottom } => lineTop + height - marginBottom - word.Height,
                    { IsValue: false, Keyword: VerticalAlignment.Middle } => lineTop + (height - word.Height) / 2,
                    _ when word.IsImage => lineTop + above - marginBottom - word.Height,
                    _ => word.Top + shift
                };
            }
        }

        /// <summary>
        /// Steps the flow's fragmentainer cursor to where the line it is on ends, when an empty inline has made
        /// that line too deep for any fragmentainer, as <see cref="FlowBox"/> does for a word too tall for any
        /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/435">#435</see>).
        /// </summary>
        /// <remarks>
        /// Such a line overflows the fragmentainer it starts in rather than breaking (css-break-3 §2), so the
        /// content after it flows into a later band without a break recording the crossing. Asked of the line
        /// and not of a word on it: the words on a deep line need not straddle anything, since the text sits at
        /// the line's baseline, possibly wholly inside a later band. Unless the cursor follows, every later
        /// question this pass asks is answered about the band it opened, and the lines after the deep one
        /// were drawn in its fragmentainer.
        /// </remarks>
        /// <param name="coordinates">the flow, on the grown line</param>
        /// <param name="container">the container being laid out</param>
        private static void StepOverADeepLine(CssLineBoxCoordinates coordinates, HtmlContainerInt container)
        {
            var line = coordinates.Line;

            if (coordinates.Fragmentainer is not { } fragmentainer
                || line is not { FlowTop: { } top, BaselineExtent: { } extent }
                || !MonolithicContent.FitsNoFragmentainer(extent.Height, 0, 0, container))
            {
                return;
            }

            fragmentainer.StepOverTo(container.SlotEndingAt(top + extent.Height));
        }

        /// <summary>
        /// Gives the empty inlines held for the next word (<see cref="CssLineBoxCoordinates.PendingEmptyInlineExtent"/>)
        /// to the line an atomic inline (inline-block, inline-flex, inline-table, inline-grid) has just landed
        /// on instead. Such a box places no word, so without this the held inlines passed it and went to the
        /// next word's line, which is a later line when that word wraps.
        /// </summary>
        /// <param name="blockBox">the block whose inline content is being flowed</param>
        /// <param name="coordinates">the flow, just after the atomic inline was placed</param>
        /// <returns>true when growing the line took a break, so the flow has to stop</returns>
        private static bool FoldHeldEmptyInlinesIntoAtomicInlinesLine(CssBox blockBox, CssLineBoxCoordinates coordinates)
        {
            if (coordinates.PendingEmptyInlineExtent is not { } held) return false;

            HandHeldEmptyInlinesToTheLine(coordinates);
            GrowLineForEmptyInlines(blockBox, coordinates, held);

            return coordinates.Break is not null;
        }

        /// <summary>
        /// Records the held empty inlines on the line that has taken their extent
        /// (<see cref="CssLineBox.EmptyInlines"/>), and clears them from the flow.
        /// </summary>
        /// <param name="coordinates">the flow, on the line taking them</param>
        private static void HandHeldEmptyInlinesToTheLine(CssLineBoxCoordinates coordinates)
        {
            if (coordinates.PendingEmptyInlines is { } boxes)
                (coordinates.Line.EmptyInlines ??= []).AddRange(boxes);

            coordinates.PendingEmptyInlines = null;
            coordinates.PendingEmptyInlineExtent = null;
        }

        /// <summary>
        /// Whether <paramref name="box"/> holds in-flow content that places no word of its own: an
        /// inline-block, inline-flex, inline-table or inline-grid, at any depth through its plain inlines.
        /// Such an inline is on its line all the same, so it is not an empty inline.
        /// </summary>
        /// <param name="box">an inline that consumed no word ordinal</param>
        /// <returns>true when it holds atomic inline content</returns>
        private static bool HoldsAtomicInlineContent(CssBox box)
        {
            foreach (var child in box.Boxes)
            {
                if (child.DerivedStyle.ActualDisplay == Keywords.None || child.IsFloated) continue;
                if (child.IsAbsolutelyPositioned && !child.IsInline) continue;

                if (child.DerivedStyle.ActualDisplay != Keywords.Inline || HoldsAtomicInlineContent(child))
                    return true;
            }

            return false;
        }

        private static LineBoxExtent IncludeEdgeAlignedAtomicHeights(
            LineBoxExtent extent, double topAlignedHeight, double bottomAlignedHeight)
        {
            if (topAlignedHeight > extent.Height)
                extent = extent with { BelowBaseline = extent.BelowBaseline + topAlignedHeight - extent.Height };

            if (bottomAlignedHeight > extent.Height)
                extent = extent with { AboveBaseline = extent.AboveBaseline + bottomAlignedHeight - extent.Height };

            return extent;
        }

        /// <summary>
        /// Recursively flows the content of the box using the inline model
        /// </summary>
        /// <param name="g">Device Info</param>
        /// <param name="blockBox">Blockbox that contains the text flow</param>
        /// <param name="box">Current box to flow its content</param>
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
        private static async ValueTask FlowBox(RGraphics g, CssBox blockBox, CssBox box, double lineSpacing, double lineStartX, CssLineBoxCoordinates coordinates, double clonedResumeStart = 0)
        {
            var startX = coordinates.CurrentX;
            var startY = coordinates.CurrentY;

            // How much of the block axis a line holding this box's text is obliged to occupy, per CSS 2.1
            // §10.8.1: the line box is tall enough for *every* inline box on it, plus the strut - an
            // imaginary inline box carrying the block's own font and line-height, present on every line box
            // that holds content. The inline boxes this box's words sit inside are exactly this box and its
            // inline ancestors up to blockBox (FlowBox only ever recurses through inline boxes), so the
            // whole chain is walked: reading only the two ends would make a `line-height` declared on an
            // intermediate <span> inert whenever that span held no direct text of its own.
            //
            // Grows the line the cursor is currently on to at least that height. Called at two points per
            // word, deliberately: once before the wrap decision, because the float-intersection queries
            // (DomUtils.GetLastLeft/RightIntersectingFloatBox) and the wrap itself read MaxBottom as the
            // bottom of the line being closed; and once after the word has actually been placed, because a
            // word that wrapped landed on a *new* line whose CurrentY the first call knew nothing about.
            // Without the second call a line whose only word arrived by wrapping contributes no height at
            // all - the common "paragraph's last line holds one word" shape, which came out one whole
            // line-height short.
            void GrowLineToItsExtent(CssRect word)
            {
                var line = coordinates.Line;
                var baselineExtent = LineBoxContributionOf(word, blockBox, g.PixelsPerPoint, reserveDoubleOverlineReach: true);

                // An outside marker is not in this flow (IsOutsideMarker), but it does sit on this
                // line's baseline, so the line has to be tall enough to hold it - otherwise a marker
                // in a font larger than its item's text overlaps whatever precedes the item.
                //
                // Its ASCENT side only: the marker hangs outside the principal box, so its descender has
                // nothing under it to push down, and browsers do not let it. Measured in Chrome on
                // `<li>Item` at 8.5pt with `::marker { font-size: 20pt }`: the line's own extent above the
                // baseline goes from 10px to 24px - the marker's whole ascent - while below it stays at
                // the item's own 2px, rather than growing to the marker's ~5.7px descent. css-lists-3
                // §3.5 leaves this interaction expressly undefined, so browsers are the reference.
                if (blockBox.LineBoxes.Count > 0 && ReferenceEquals(line, blockBox.LineBoxes[0])
                    && OutsideMarkerExtentOf(blockBox) is { } markerExtent)
                {
                    baselineExtent = baselineExtent with
                    {
                        AboveBaseline = Math.Max(baselineExtent.AboveBaseline, markerExtent.AboveBaseline)
                    };
                }

                // Empty inlines passed since the last word go wherever this one lands (PlaceEmptyInline).
                if (coordinates.PendingEmptyInlineExtent is { } emptyInlines)
                    baselineExtent = baselineExtent.Union(emptyInlines);

                baselineExtent = line.BaselineAlignedExtent is { } held
                    ? held.Union(baselineExtent)
                    : baselineExtent;

                if (word.IsImage)
                {
                    // A replaced inline's line-height does not size its line box; its complete margin
                    // box does (CSS 2.1 §10.8.1). A baseline-aligned one reaches wholly above its
                    // bottom-margin-edge baseline. `top`/`bottom` are different: their baseline is not
                    // used for alignment, so first pretending that the whole image sits above it and
                    // only later aligning its edge grows the line by the strut's opposite-side extent.
                    // Acid2 exposes that exact double count: an 18pt bottom-aligned object plus the
                    // strut's 6.175pt descent became a 24.175pt line and shifted the object down.
                    //
                    // Grow the already-accumulated line on the side opposite the aligned edge instead.
                    // This also composes with earlier, taller baseline content: the margin box only asks
                    // that the final total height can contain it, not that it owns either baseline side.
                    var atomicHeight = word.OwnerBox.ActualMarginTop + word.Height + word.OwnerBox.ActualMarginBottom;
                    switch (EffectiveVerticalAlignOf(word.OwnerBox, line, out _).Value.Keyword)
                    {
                        case VerticalAlignment.Top:
                            line.TopAlignedAtomicHeight = Math.Max(line.TopAlignedAtomicHeight, atomicHeight);
                            break;
                        case VerticalAlignment.Bottom:
                            line.BottomAlignedAtomicHeight = Math.Max(line.BottomAlignedAtomicHeight, atomicHeight);
                            break;
                        default:
                            baselineExtent = baselineExtent.Union(new LineBoxExtent(atomicHeight, 0));
                            break;
                    }
                }

                CommitLineExtent(coordinates, baselineExtent);
            }

            // An inline box that placed no word still sits on a line, with its own strut: CSS 2.1 §9.4.2
            // generates it for an empty inline element, and §10.8.1 sizes the line from every inline box
            // on it. Nothing else would count it, since a line grows per word (GrowLineToItsExtent). Its
            // inline ancestors are counted too: one whose first word is still to come, on a later line,
            // has begun on this one all the same. The inline's own vertical-align is not applied (#1308).
            //
            // Which line it is on depends on the break opportunity nearest it. With none since the line's
            // last word it goes with that word, onto the current line. After a space, or at the start of a
            // line, it comes after the opportunity and goes with the next word, which may wrap, so it is
            // held for that word (GrowLineToItsExtent).
            void PlaceEmptyInline(CssBox emptyInline)
            {
                // The pass that broke here gave it to the line before the break, and kept that line.
                if (coordinates.EmptyInlinesBeforeResume?.Contains(emptyInline) == true) return;

                var extent = HalfLeadingExtentWithDecoration(emptyInline, g.PixelsPerPoint);

                for (var inline = emptyInline.ParentBox;
                     inline is not null && !ReferenceEquals(inline, blockBox);
                     inline = inline.ParentBox)
                {
                    extent = extent.Union(HalfLeadingExtentWithDecoration(inline, g.PixelsPerPoint));
                }

                if (coordinates.PendingWordSeparator || IsAtLineStart(coordinates))
                {
                    coordinates.PendingEmptyInlineExtent = coordinates.PendingEmptyInlineExtent is { } held
                        ? held.Union(extent)
                        : extent;
                    (coordinates.PendingEmptyInlines ??= []).Add(emptyInline);
                    return;
                }

                var line = coordinates.Line;
                GrowLineForEmptyInlines(blockBox, coordinates, extent);

                if (coordinates.Break is null) (line.EmptyInlines ??= []).Add(emptyInline);
            }

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

                // A `display: none` child generates no box (CSS Display 3 §2.5): nothing of it - not its
                // words, and not its margin, border or padding - takes part in the line. DomUtils.
                // ContainsInlinesOnly lets such a child sit among the inline content of a box that is
                // still flowed as one inline formatting context (`<style>…</style>text`, `a<script/>b`),
                // so it reaches this loop and must be stepped over here. The box itself is exempt: the
                // self-iteration convention above hands FlowBox a box that is its own only child.
                if (b.DerivedStyle.ActualDisplay == Keywords.None && !ReferenceEquals(b, box)) continue;

                // The same question as `opensHere` above, asked of this child: has the walk reached the
                // resume point yet, or is it still fast-forwarding through content an earlier
                // fragmentainer placed?
                var childStartOrdinal = coordinates.WordOrdinal;
                var childOpensHere = childStartOrdinal >= coordinates.ResumeOrdinal;

                // An absolutely positioned box is out of flow (CSS 2.1 §9.6): nothing of it sits on a line,
                // so it is set aside here and laid out by CreateLineBoxes once the lines are final. It is
                // not flowed further, which would have placed its content inline, nor is any ::first-line
                // style or line spacing applied to it. A box already placed on an earlier fragmentainer
                // (!childOpensHere) keeps that placement. An inline-level one - a replaced element the
                // cascade does not blockify - is still flowed as the word it always was.
                if (b.IsAbsolutelyPositioned && !b.IsInline && !ReferenceEquals(b, box))
                {
                    if (childOpensHere && b.DerivedStyle.ActualDisplay != Keywords.None)
                    {
                        SetAsideAtTheCursor(coordinates, b, childStartOrdinal);
                    }

                    continue;
                }

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
                // by a padding it already paid on an earlier fragmentainer - unless it re-opens with its
                // own top border/padding on every fragment by declaration (box-decoration-break: clone,
                // css-break-3 §6.2), in which case a pass continuing b's own straddling content applies
                // it again too (#343). A pass that walks through a b already fully finished earlier takes
                // this same branch but places nothing new (every word ordinal is skipped below), so
                // coordinates.Line never changes and the "undo" after this dispatch cancels the shift
                // with no effect - safe to pre-shift unconditionally for clone rather than having to tell
                // "still straddling" apart from "already done" up front.
                // Excludes an inline-block routed to FlowAtomicBlockContentChild below (see
                // LaysOutAsAnAtomicBox): that path positions and sizes the box (border/padding included)
                // as one atomic unit via its own box-model machinery - applying this pre-shift too would
                // double the top inset for it.
                var laysOutAsAtomicBox = await LaysOutAsAnAtomicBox(g, b, box, coordinates.CurrentY);
                var appliesAtomicInset = b.DerivedStyle.ActualDisplay is Keywords.InlineBlock && !ReferenceEquals(b, box)
                    && !laysOutAsAtomicBox;
                var clonesAtomicDecorations = appliesAtomicInset && b.BoxDecorationBreak.Value == BoxDecorationBreakMode.Clone;
                var atomicTopInset = appliesAtomicInset ? b.ActualBorderTopWidth + b.ActualPaddingTop : 0;
                var atomicBottomInset = appliesAtomicInset ? b.ActualBorderBottomWidth + b.ActualPaddingBottom : 0;
                var preShiftedForAtomicInset = atomicTopInset > 0 && (childOpensHere || clonesAtomicDecorations);
                var lineBeforeChild = coordinates.Line;

                if (preShiftedForAtomicInset)
                {
                    coordinates.CurrentY += atomicTopInset;
                }

                // A display:block replaced element's (img/svg) synthetic wrapper (IsReplacedBlockWrapper)
                // makes b its own inline "word" - when b has no declared width, ResolveAutoHorizontalMargin's
                // fallback reads b.FirstWord.Width (see its own remarks), but that word is only populated by
                // the ordinary `await b.MeasureWordsSize(g)` call further down this same loop iteration - too
                // late for the ActualMarginLeft/Right read leftSpacing performs right below (issue #1178).
                // Measure it here instead. Scoped to the undeclared-width case specifically (rather than
                // every IsReplacedBlockWrapper child) because a declared width never reaches FirstWord.Width
                // in ResolveAutoHorizontalMargin at all - pre-measuring for that case would just rerun
                // MeasureWordsSize's cheap-but-not-free size computation twice for no benefit. MeasureWordsSize
                // is safe to call twice per iteration (its image-load/SVG-prefetch guard is `_wordsSizeMeasured`,
                // but the actual size computation below that guard always reruns), so the ordinary call later
                // in this same iteration is left in place unchanged.
                if (b.ParentBox is { IsReplacedBlockWrapper: true } && !CssValueParser.IsValidLength(b.Width))
                {
                    await b.MeasureWordsSize(g);
                }

                // A float (like an absolute/fixed-positioned child, already excluded here) never sits ON
                // the line - FlowFloatChild/FloatBox derive its real position from blockBox.ClientLeft/
                // ActualMarginLeft, never from coordinates.CurrentX - so its own margin/border/padding
                // must not be added to the cursor at all. Left in (as it was before #1038, when a float
                // could not yet be reached mid-line), the float's own leftSpacing gets added to CurrentX
                // at line ~3895 below and, whenever no preceding float on the line overrides it again at
                // the lastLeftIntersectingFloatBox check further down, survives into the NEXT sibling's
                // iteration unchanged (this branch continues without ever resetting CurrentX itself) - a
                // float with a non-zero margin-left/border-left-width/padding-left that is first on its
                // line then leaves the cursor short of (or past) its own true right edge instead of
                // untouched, which can make GetIntersectingInlineFloat's point-collision test wrongly
                // conclude the float has already been passed, dropping the space the next sibling should
                // reserve for it (CSS 2.1 §9.5.1 rule 6).
                var excludedFromLineSpacing = b.Position.Value is PositionMode.Absolute or PositionMode.Fixed || b.IsFloated;
                var leftSpacing = excludedFromLineSpacing ? 0 : b.ActualMarginLeft + b.ActualBorderLeftWidth + b.ActualPaddingLeft;
                var rightSpacing = excludedFromLineSpacing ? 0 : b.ActualMarginRight + b.ActualBorderRightWidth + b.ActualPaddingRight;

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

                var lastLeftIntersectingFloatBox = LeftFloatAt(coordinates, box);

                if (lastLeftIntersectingFloatBox is not null)
                {
                    coordinates.CurrentX = lastLeftIntersectingFloatBox.ActualRight + lastLeftIntersectingFloatBox.ActualMarginRight + appliedLeftSpacing;
                }

                // Where b's own content box starts on this line, after every branch above that can
                // establish it - which is what an atomic inline-level box's declared width has to be
                // measured from, since it occupies that width rather than the width its words happened
                // to measure. Resolved before b's content is placed so that FlowBox's own exit
                // bookkeeping for b (FinalizeFlowBoxExit) can reserve the rest of it and paint the
                // border box at it.
                var childContentStartX = coordinates.CurrentX;
                var childDeclaredContentWidth = ResolveAtomicInlineDeclaredWidth(b, box, coordinates.CurrentY);

                // !ReferenceEquals(b, box): a floated box with no children of its own but real words
                // (a replaced element - <img align="left">'s presentational float is the reachable case,
                // via DomParser.CorrectReplacedElementBoxes' IsReplacedBlockWrapper) hits FlowBox's own
                // self-iteration convention (`boxes = [box]` a few lines up, when box.Boxes is empty but
                // box.Words is not) - so this loop's b IS box on that call. By the time this specific
                // FlowBox call runs at all, b has already been positioned as a float by whichever CALLER
                // reached it (FlowFloatChild itself, laying out b's own content via
                // LayoutContentAtItsAssignedPosition - see that method's own remarks) - this inner call's
                // job is only to flow b's own word to satisfy PlacesItselfAsBlockBox's ContainsInlinesOnly
                // dispatch (vacuously true for a box with no children), not to place b all over again.
                // Without this guard, FlowFloatChild → LayoutContentAtItsAssignedPosition →
                // (ContainsInlinesOnly, vacuous) → CreateLineBoxes → FlowBox → self-iteration → this
                // branch → FlowFloatChild again, forever - a real stack overflow for exactly this shape.
                if (b.IsFloated && !ReferenceEquals(b, box))
                {
                    // A float contributes no word ordinals of its own (CSS 2.1 §9.5: it is taken out of
                    // the normal flow), so childOpensHere - computed from childStartOrdinal, the ordinal
                    // this loop had already reached before visiting b - is exactly "has this box been
                    // structurally reached before the point an earlier fragmentainer pass stopped at",
                    // the same question InlineTable/InlineGrid/an atomic inline-block ask of themselves
                    // below. A float this pass has already placed (on an earlier fragmentainer) still has
                    // to re-enter CssLineBoxCoordinates.InlineFloats here, though, since that list starts
                    // empty on every fresh pass - its real, already-committed geometry from the earlier
                    // pass is what lets a later line on THIS pass keep narrowing around it correctly.
                    if (!childOpensHere)
                    {
                        (coordinates.InlineFloats ??= []).Add(b);
                    }
                    else
                    {
                        await FlowFloatChild(g, blockBox, b, coordinates);
                    }

                    // A float never sits on the line - it contributes no content width to the cursor at
                    // all (subsequent content narrows around it via LeftFloatAt/RightFloatAt instead) - so
                    // every one of the shared per-child steps below this dispatch chain (the atomic-inset
                    // undo, the declared-width reservation, and above all the trailing-spacing add, which
                    // would otherwise nudge the cursor by this box's own margin/border/padding with no
                    // corresponding advance to nudge it from) is meaningless for it and skipped entirely.
                    continue;
                }

                if (b.Words.Count > 0)
                {
                    var wrapNoWrapBox = false;

                    // This only makes sense as a *local* exception - moving b's whole run to a fresh line
                    // when it doesn't fit is the correct behaviour for a `nowrap` span nested in otherwise-
                    // wrapping content (b.WhiteSpace.Value == NoWrap while blockBox's own white-space still
                    // permits line breaks elsewhere), but when blockBox itself is `nowrap`/`pre`, no line
                    // break is possible anywhere in its content - not before b, not within it - so creating
                    // one here just to relocate b is never correct: it produces a second line box despite
                    // white-space:nowrap forbidding wrapping altogether (issue #841).
                    var blockBoxPermitsWrap = blockBox.WhiteSpace.Value != Whitespace.NoWrap
                                               && blockBox.WhiteSpace.Value != Whitespace.Pre;

                    if (blockBoxPermitsWrap && b.WhiteSpace.Value == Whitespace.NoWrap && coordinates.CurrentX > lineStartX)
                    {
                        var boxRight = coordinates.CurrentX;

                        foreach (var word in b.Words)
                            boxRight += word.FullWidth;

                        var noWrapLimitRight = isRtl
                            ? coordinates.Line.ContentRight - GetLineTextIndent(blockBox, coordinates.Line.Equals(blockBox.LineBoxes[0]), coordinates.Line.FollowsForcedBreak)
                            : coordinates.Line.ContentRight;

                        if (boxRight > noWrapLimitRight + LineFitTolerance)
                            wrapNoWrapBox = true;
                    }

                    // The space belongs before b's first word. If that word was placed in an earlier
                    // fragmentainer, so was the space.
                    var childHasLeadingWhitespace = childOpensHere && DomUtils.IsBoxHasWhitespace(b);

                    // Same css-text-3 phase II removal as this function's own whitespace-only-box tail
                    // performs, for the other shape the same collapsed space takes -
                    // `<br><span> b</span>`, where the space is inside b rather than in a box of its own.
                    // The flag itself is left alone: it also tells HasOrdinaryWrapOpportunityBefore below
                    // that a wrap opportunity exists before b's first word, which the space offers
                    // whether or not it is rendered.
                    if (childHasLeadingWhitespace && !IsAtLineStart(coordinates))
                    {
                        coordinates.CurrentX += box.ActualWordSpacing;
                        coordinates.PendingWordSeparator = true;
                    }

                    for (var wordIndex = 0; wordIndex < b.Words.Count; wordIndex++)
                    {
                        var word = b.Words[wordIndex];

                        // The walk order is fixed, so this ordinal names a position in it. Words below
                        // the resume point were placed in an earlier fragmentainer: their geometry and
                        // their line boxes already exist and this pass must not touch them.
                        var wordOrdinal = coordinates.WordOrdinal++;
                        if (wordOrdinal < coordinates.ResumeOrdinal) continue;

                        // The line the cursor is on before this word is placed - see GrowLineToItsExtent.
                        // Applied per word rather than when a line is created: §9.4.2 makes a line box
                        // holding no content zero-height, so an empty block must not gain a strut's worth
                        // of height out of nothing.
                        var maxBottomBeforeIncomingWord = coordinates.MaxBottom;
                        var lineExtentBeforeIncomingWord = coordinates.Line.BaselineExtent;
                        var baselineAlignedExtentBeforeIncomingWord = coordinates.Line.BaselineAlignedExtent;
                        var topAlignedAtomicHeightBeforeIncomingWord = coordinates.Line.TopAlignedAtomicHeight;
                        var bottomAlignedAtomicHeightBeforeIncomingWord = coordinates.Line.BottomAlignedAtomicHeight;
                        GrowLineToItsExtent(word);

                        var actualLimitRight = coordinates.Line.ContentRight;
                        var lastRightIntersectingFloatBox = RightFloatAt(coordinates, box);

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
                                         && coordinates.CurrentX + word.Width + rightSpacing + clonedTrailing > actualLimitRight + LineFitTolerance
                                         && (b.WhiteSpace.Value != Whitespace.PreWrap || !word.IsSpaces);

                        // hyphens:auto/manual: before giving up and wrapping the whole word, see if a
                        // cached candidate break point (from ParseToWords - either an explicit soft
                        // hyphen or an automatic Hyphenator suggestion) lets a hyphenated prefix
                        // fit in the space remaining on the current line instead - gated by
                        // hyphenate-limit-lines (no more than N consecutive hyphenated lines) and
                        // hyphenate-limit-zone (only bother when skipping the hyphen would otherwise
                        // leave more than the zone's worth of unfilled space on this line).
                        var availableWidth = actualLimitRight - coordinates.CurrentX - rightSpacing - clonedTrailing;
                        var previousWord = coordinates.Line.Words.Count > 0
                            ? coordinates.Line.Words[^1]
                            : null;
                        var hasOrdinaryWrapBefore = HasOrdinaryWrapOpportunityBefore(
                            previousWord, word, childHasLeadingWhitespace && wordIndex == 0,
                            coordinates.TrailingRegionalIndicatorCount,
                            coordinates.TrailingGraphemeContext);
                        var isGraphemeBoundary = IsGraphemeBoundaryBefore(
                            previousWord, word, coordinates.TrailingRegionalIndicatorCount,
                            coordinates.TrailingGraphemeContext);

                        if (!word.SuppressWrapBefore && overflows && !word.IsLineBreak && !wrapNoWrapBox &&
                            word.HyphenationCandidates is { Count: > 0 } &&
                            IsWithinHyphenateLimitLines(blockBox, coordinates.ConsecutiveHyphenatedLines) &&
                            IsWithinHyphenateLimitZone(blockBox, availableWidth, actualLimitRight - lineStartX) &&
                            TryHyphenateWord(g, b, word, availableWidth, out var prefixWord, out var suffixWord))
                        {
                            b.Words[wordIndex] = prefixWord!;
                            b.Words.Insert(wordIndex + 1, suffixWord!);
                            word = prefixWord!;
                            overflows = false;
                            coordinates.CurrentLineHyphenated = true;
                        }

                        if (overflows && !wrapNoWrapBox &&
                            (coordinates.Line.Words.Count == 0 || !hasOrdinaryWrapBefore) &&
                            TryOverflowWrapWord(g, word, availableWidth, out var overflowPrefix,
                                out var overflowSuffix))
                        {
                            b.Words[wordIndex] = overflowPrefix!;
                            b.Words.Insert(wordIndex + 1, overflowSuffix!);
                            word = overflowPrefix!;
                            overflows = false;
                        }

                        // A resumed flow's opening line is empty, so there is nothing to wrap away from;
                        // honouring the wrap would leave a blank line at the top of the fragmentainer.
                        // Asked through IsAtLineStart rather than Words.Count, so the line a <br> has
                        // just opened counts as empty too - it holds that break's own zero-width marker
                        // word, and wrapping off it blanks the line the break was meant to start.
                        var emergencyBoundaryWrap = overflows && !IsAtLineStart(coordinates)
                            && !hasOrdinaryWrapBefore && isGraphemeBoundary
                            && AllowsOverflowWrapAtBoundary(previousWord, word);
                        var wrapping = (!word.SuppressWrapBefore
                                && (word.IsLineBreak || wrapNoWrapBox || (overflows && hasOrdinaryWrapBefore)))
                            || emergencyBoundaryWrap;

                        if (wrapping && coordinates.SuppressLeadingWrap && IsAtLineStart(coordinates))
                        {
                            wrapping = false;
                            wrapNoWrapBox = false;
                        }

                        coordinates.SuppressLeadingWrap = false;

                        if (wrapping)
                        {
                            // The incoming word belongs to the new line, not the one being closed (or, for
                            // a clamped stop below, to a line that will never open at all) - its line-height
                            // was applied speculatively above so float intersection and wrapping could
                            // inspect the candidate line extent; remove it before advancing the cursor, in
                            // every case a new line doesn't actually start with this word. A forced-break
                            // marker itself terminates the current line and retains its extent.
                            if (!word.IsLineBreak)
                            {
                                coordinates.MaxBottom = maxBottomBeforeIncomingWord;
                                coordinates.Line.BaselineExtent = lineExtentBeforeIncomingWord;
                                coordinates.Line.BaselineAlignedExtent = baselineAlignedExtentBeforeIncomingWord;
                                coordinates.Line.TopAlignedAtomicHeight = topAlignedAtomicHeightBeforeIncomingWord;
                                coordinates.Line.BottomAlignedAtomicHeight = bottomAlignedAtomicHeightBeforeIncomingWord;
                            }
                            else
                            {
                                // An empty inline before a forced break stays on the line the break ends,
                                // which has just taken it along with the break's own extent.
                                HandHeldEmptyInlinesToTheLine(coordinates);
                            }

                            // line-clamp (CSS Overflow 4 §block-ellipsis / §max-lines): once the block has
                            // already produced as many lines as its declared limit, this new line must never
                            // open - the content stops here, permanently (not "paused for a later
                            // fragmentainer pass", which is what coordinates.Break means), with a generated
                            // ellipsis word on the last visible line in its place. Checked ahead of every
                            // other consequence of wrapping (hyphenation bookkeeping, the new CssLineBox, RTL
                            // text-indent, etc.) since none of that should happen for a line that will never
                            // exist - but only after the MaxBottom restore just above, which applies whether
                            // or not this stop actually clamps (issue: an unrestored MaxBottom otherwise
                            // inflates the clamped block's own height by however tall the never-rendered
                            // next word would have been).
                            if (TryApplyLineClamp(g, blockBox, coordinates, actualLimitRight, rightSpacing, clonedTrailing))
                            {
                                coordinates.ClampedStop = true;
                                return;
                            }

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
                            OpenNextLine(blockBox, coordinates, lineSpacing, lineStartX, wordOrdinal,
                                word.IsLineBreak, isRtl);

                            lastLeftIntersectingFloatBox = LeftFloatAt(coordinates, b);

                            if (lastLeftIntersectingFloatBox is not null)
                            {
                                coordinates.CurrentX = lastLeftIntersectingFloatBox.ActualRight + lastLeftIntersectingFloatBox.ActualMarginRight + leftSpacing;
                            }

                            if (word.IsImage || word.Equals(b.FirstWord))
                            {
                                // b's own box starts on this new line, so its full leading spacing applies
                                // however its decorations break; only its cloning ancestors re-open here.
                                coordinates.CurrentX += leftSpacing;

                                if (clonesDecorations)
                                    coordinates.CurrentX += DomUtils.ClonedInlineStart(b.ParentBox, blockBox);
                            }

                            // b's own first word wrapped onto this new line, so box - the box whose own
                            // recursive FlowBox call this is, which stamped its FirstHostingLineBox at
                            // entry, before any of this was known - does not really start on the line it
                            // was entered on when b is what its content genuinely begins with: either b
                            // IS box (self-iteration - a leaf holding its own words directly), or b is
                            // box's own first child (its words are read here without a nested FlowBox
                            // call, see the self-iteration remark above). The same then cascades up
                            // through every further inline ancestor that is, in turn, its own parent's
                            // first child. CssLineBox.UpdateRectangle's own leading-spacing subtraction
                            // walks this same inline-ancestor chain checking this field, so correcting it
                            // at the source is what lets that subtraction fire correctly on its own,
                            // without BubbleRectangles compensating for its absence afterward (#342).
                            //
                            // b == box (self-iteration) already got its own leading spacing added above,
                            // via `leftSpacing`, computed for b. When b != box, b's own leading spacing
                            // is real and already reserved the same way - but box's own (and every
                            // cascaded ancestor's) is not: FlowBox's normal "childOpensHere" path adds an
                            // ancestor's leading spacing only when that ancestor's own per-child dispatch
                            // runs, which this wrap pre-empted by placing b's word without ever reaching
                            // it. Adding it here, for exactly the ancestors this cascade corrects, is what
                            // keeps the word position itself (not just the rectangle) agreeing with the
                            // corrected FirstHostingLineBox.
                            if (word.Equals(b.FirstWord) && (ReferenceEquals(b, box) || IsFirstChildOfItsParent(b)))
                            {
                                for (var ancestor = box; ancestor is { IsInline: true }; ancestor = ancestor.ParentBox)
                                {
                                    if (!ReferenceEquals(ancestor, b))
                                    {
                                        coordinates.CurrentX += ancestor.Position.Value is not (PositionMode.Absolute or PositionMode.Fixed)
                                            ? ancestor.ActualMarginLeft + ancestor.ActualBorderLeftWidth + ancestor.ActualPaddingLeft
                                            : 0;
                                    }

                                    ancestor.FirstHostingLineBox = coordinates.Line;

                                    // Same correction as PrepareFlowBoxEntry's own entry-side stamp
                                    // (#341) for a string-set ancestor whose recorded Y was seeded before
                                    // this wrap was known about - a page attribution this ancestor's
                                    // opening line, not the seed line PrepareFlowBoxEntry stamped it with.
                                    foreach (var namedString in ancestor.NamedStrings.Values)
                                    {
                                        namedString.Y = coordinates.CurrentY;
                                    }

                                    if (!IsFirstChildOfItsParent(ancestor)) break;
                                }
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

                        // A normal break before this word had priority while the previous line still held
                        // content. Now that the word has moved whole to a fresh line, an overlong token may
                        // use overflow-wrap inside itself. Recompute both float edges at the new Y; the
                        // boundary calculated before the wrap belongs to the line we just closed.
                        if (wrapping && overflows && !word.IsLineBreak)
                        {
                            var emergencyLimitRight = coordinates.Line.ContentRight;
                            var emergencyRightFloat = RightFloatAt(coordinates, box);
                            if (emergencyRightFloat is not null)
                            {
                                emergencyLimitRight = emergencyRightFloat.Location.X
                                                      - emergencyRightFloat.ActualMarginLeft - rightSpacing;
                            }

                            if (isRtl)
                            {
                                emergencyLimitRight -= GetLineTextIndent(blockBox,
                                    coordinates.Line.Equals(blockBox.LineBoxes[0]),
                                    coordinates.Line.FollowsForcedBreak);
                            }

                            var emergencyAvailable = emergencyLimitRight - coordinates.CurrentX
                                                     - rightSpacing - clonedTrailing;
                            if (word.Width > emergencyAvailable &&
                                TryOverflowWrapWord(g, word, emergencyAvailable, out overflowPrefix,
                                    out overflowSuffix))
                            {
                                b.Words[wordIndex] = overflowPrefix!;
                                b.Words.Insert(wordIndex + 1, overflowSuffix!);
                                word = overflowPrefix!;
                            }
                        }

                        // Read before the word joins the line, which is the only moment this is
                        // answerable: afterwards the word is itself on the line and the answer is
                        // always "no". Consumed by the PrecededByWordSeparator assignment below.
                        var wordOpensTheLine = IsAtLineStart(coordinates);

                        coordinates.Line.ReportExistanceOf(word);
                        (coordinates.TrailingRegionalIndicatorCount, coordinates.TrailingGraphemeContext) =
                            UpdateTrailingTextState(word, coordinates.TrailingRegionalIndicatorCount,
                                coordinates.TrailingGraphemeContext);

                        lastLeftIntersectingFloatBox = LeftFloatAt(coordinates, box);

                        if (lastLeftIntersectingFloatBox is not null)
                        {
                            coordinates.CurrentX = lastLeftIntersectingFloatBox.ActualRight + lastLeftIntersectingFloatBox.ActualMarginRight + appliedLeftSpacing;
                        }

                        // A preserved tab (CssRect.OriginalText - stable even though CssBox.MeasureWordsSize/
                        // ApplyFirstLineStyleOverride already overwrote Text/FirstLineText with an earlier,
                        // approximated expansion - see CssBox.ExpandTabs' own doc comment) gets its final,
                        // exact expansion right here, using this word's real distance from its real rendered
                        // line's own start (coordinates.CurrentX - coordinates.Line.ContentLeft) - a value
                        // shared across every sibling box already placed on this line, and correctly reset
                        // by the wrap/new-line handling above whether the break was explicit or a soft wrap.
                        // This is the one point in layout where that real position is actually known, which
                        // is exactly what closes the two gaps the per-box approximation couldn't (issue #885).
                        if (word is CssRectWord tabRectWord && tabRectWord.OriginalText is { } rawTabText &&
                            rawTabText.IndexOf('\t') >= 0)
                        {
                            var tabStyleSource = word.FirstLineStyle ?? word.OwnerBox;
                            var tabFont = CssBox.ResolveWordFont(word, tabStyleSource);
                            var trueLineX = coordinates.CurrentX - coordinates.Line.ContentLeft;
                            var expandedTabText = CssBox.ExpandTabs(rawTabText, g, tabFont,
                                tabStyleSource.ActualTextShapingFeatures, tabStyleSource.ResolvedTabSize, ref trueLineX);

                            if (word.FirstLineStyle != null)
                                word.FirstLineText = expandedTabText;
                            else
                                tabRectWord.ReplaceText(expandedTabText);

                            var expandedWidth = g.MeasureString(expandedTabText, tabFont, tabStyleSource.ActualTextShapingFeatures).Width;
                            if (tabStyleSource.ActualLetterSpacing != 0)
                            {
                                expandedWidth += g.CountShapedGlyphs(expandedTabText, tabFont, tabStyleSource.ActualTextShapingFeatures) *
                                                 tabStyleSource.ActualLetterSpacing;
                            }
                            word.Width = expandedWidth;
                        }

                        word.Left = coordinates.CurrentX;
                        word.Top = coordinates.CurrentY;

                        // The word has landed - and if it got here by wrapping, it landed on a line the
                        // call before the wrap decision never saw. Grow that line now, against the cursor's
                        // post-wrap CurrentY.
                        GrowLineToItsExtent(word);

                        // Its line now holds the empty inlines passed before it.
                        var tookEmptyInlines = coordinates.PendingEmptyInlineExtent is not null;
                        HandHeldEmptyInlinesToTheLine(coordinates);

                        // ...then sit it on that line's baseline straight away, rather than leaving it at
                        // the line's top for ApplyVerticalAlignment to move later. CSS 2.1 §10.8.1 puts a
                        // word's content area half a leading below its line box's top, and the questions
                        // asked immediately below - does this word straddle the fragmentainer, does it
                        // overflow every one - are asked of the word's own rectangle. Answering them at the
                        // line's top and only then moving the glyphs down by the half-leading let a word
                        // that had just been judged to fit end up across the boundary after all, and the
                        // emitter claimed it in both fragmentainers (MulticolLayoutIntegrationTests'
                        // AtAnAvoidColumnBreak_TheContentMovesAlone_SinceItsHeightIsNotYetKnown states
                        // this: the same word painted twice, in two columns).
                        //
                        // An approximation on purpose, and a self-correcting one: the line's extent is only
                        // as final as the words placed on it so far, so a taller word arriving later moves
                        // this one again. ApplyVerticalAlignment re-derives every offset from the closed
                        // line and is the authority; for a line whose fonts are all one size - which is
                        // nearly all of them - the two agree exactly and it has nothing left to do.
                        word.Top += HalfLeadingOffsetOf(word, coordinates.Line);

                        // Assigned, never accumulated: this box tree can be laid out again (a
                        // shrink-to-fit ancestor's provisional pass, a variable-page-width reflow), and
                        // each pass re-derives the flag from the same three sources rather than
                        // compounding the last pass's answer. See CssRect.PrecededByWordSeparator.
                        //
                        // Never for the word that opens a line: css-text-3 phase II removed the space
                        // in front of it, and a space that is not rendered is not a justification
                        // opportunity either. HasSpaceBefore has to be overruled here rather than
                        // upstream - it is a fact about the source text, true of `alpha` in
                        // `…<br>\nalpha…` no matter which line the word lands on, so suppressing the
                        // flow's own PendingWordSeparator alone still left text-align: justify an
                        // expansion point at the head of the line (issue #1087).
                        word.PrecededByWordSeparator = !wordOpensTheLine
                            && (coordinates.PendingWordSeparator || word.HasSpaceBefore);
                        coordinates.PendingWordSeparator = word.HasSpaceAfter;

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
                            // css-gcpm-3 §2.8's footnote-policy: line: a footnote's own note area didn't
                            // fit its call's landing page (HtmlContainerInt.ResolveFootnotesForThisAttempt,
                            // on an earlier pass), so this word - part of the CssBoxFootnoteCall's own
                            // number text - is forced off this page even though it would fit here on its
                            // own. Reuses the exact same natural-overflow InlineBreakToken machinery below
                            // (word.WouldStraddleFragmentainer's own branch), which already resumes at
                            // coordinates.LineStartOrdinal - the start of the line this word is on, not
                            // just this word - satisfying css-gcpm-3's "at the start of the line containing
                            // the footnote reference" for free. The one thing that natural branch cannot
                            // reuse is its own resume-slot math: ResumeSlotForBreakBefore re-derives "does
                            // this genuinely not fit" from live geometry, which would answer "the current
                            // slot" for a word that, taken alone, actually does fit - so the forced case
                            // names the next slot directly instead.
                            //
                            // One-shot per LayoutDocument invocation (HtmlContainer.FootnotePolicyLineBreaksTakenThisPass,
                            // cleared at the top of that method, not alongside FootnotePolicyForcedLineCalls
                            // itself) - without it, this same pass's own fragmentainer walk resumes the call
                            // onto the next page and asks the identical question again there, forcing it one
                            // further page every time with nothing to stop it, since the call's membership in
                            // FootnotePolicyForcedLineCalls does not change merely because it moved.
                            var forcedFootnoteLineBreak = word.OwnerBox is CssBoxFootnoteCall footnoteCall
                                && box.HtmlContainer!.FootnotePolicyForcedLineCalls.Contains(footnoteCall)
                                && box.HtmlContainer!.FootnotePolicyLineBreaksTakenThisPass.Add(footnoteCall);

                            // The same question CssRect.BreakPage used to ask after the fact, answered
                            // here instead so this flow can stop before placing the word rather than
                            // relocating it and carrying on. The break is taken at the start of the line,
                            // not at this word: a line box is monolithic (css-break-3 §4.1), so the whole
                            // of it moves to the next fragmentainer rather than leaving its shorter words
                            // behind. A line too deep for any fragmentainer is not moved, since it would
                            // straddle again on every one (LineFitsNoFragmentainer).
                            var straddles = word.WouldStraddleFragmentainer();
                            var lineFitsNowhere = straddles
                                && word.LineFitsNoFragmentainer(coordinates.Line.FlowTop ?? word.Top);

                            if ((straddles && !lineFitsNowhere) || forcedFootnoteLineBreak)
                            {
                                // CompletedLineCount is filled in by CreateLineBoxes once the
                                // in-progress line has been discarded, since that is what fixes how
                                // many lines this fragmentainer actually kept.
                                coordinates.Break = new InlineBreakToken(
                                    blockBox,
                                    // Derived from where the break actually fell (the band this line
                                    // was trying to sit in), never assumed to be "the pass after this
                                    // one" - see ResumeSlotForBreakBefore's own remarks. The forced case
                                    // is the one exception: nothing about this word's own geometry says
                                    // it doesn't fit, so there is no "where it actually fell" to derive -
                                    // the next slot after the one it's actually sitting in is the only
                                    // coherent answer.
                                    forcedFootnoteLineBreak && !(straddles && !lineFitsNowhere)
                                        ? box.HtmlContainer!.SlotStartingAt(word.Top) + 1
                                        : word.ResumeSlotForBreakBefore(),
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
                            // So does a line an empty inline this word took has made too deep for any.
                            if (tookEmptyInlines) StepOverADeepLine(coordinates, box.HtmlContainer!);

                            if (word.OverflowsEveryFragmentainer())
                            {
                                coordinates.Fragmentainer.StepOverTo(box.HtmlContainer!.SlotEndingAt(word.Bottom));
                            }
                        }

                        coordinates.CurrentX = word.Left + word.FullWidth;

                        coordinates.MaxRight = Math.Max(coordinates.MaxRight, word.Right);

                        // CSS 2.1 §10.8: a non-replaced inline box contributes exactly its own
                        // `line-height` to the line box - its glyph content area (`word.Height`, the
                        // font's own ascent+descent, which is what the box's background/border area is
                        // drawn from) is free to be *taller* and simply overflow, which is what negative
                        // leading means. Letting a text word raise MaxBottom turned the line's height into
                        // max(line-height, font height), so any line-height shorter than the font's own
                        // height was silently ignored. Replaced/atomic inline content (IsImage: an image,
                        // an inline <svg>, MathML, a form control) is the case §10.8 does size from the
                        // box itself, so it still contributes.
                        if (word.IsImage)
                            coordinates.MaxBottom = Math.Max(coordinates.MaxBottom, word.Bottom);

                        if (b.Position.Value != PositionMode.Absolute) continue;

                        word.Left += box.ActualMarginLeft;
                        word.Top += box.ActualMarginTop;
                    }

                    if (coordinates.Break is not null || coordinates.ClampedStop)
                    {
                        // Under clone this pass's own portion of b closes here with its own bottom
                        // border/padding (css-break-3 §6.2), same as the true-final-pass case below -
                        // the break just means it isn't that pass (#343). A line-clamp stop takes the
                        // same early return (nothing after it should ever be visited), but never applies
                        // clone's re-opened decorations - there is no later pass to open them for.
                        if (clonesAtomicDecorations && coordinates.Break is not null) ApplyAtomicInlineVerticalInsets(b, coordinates, atomicBottomInset);
                        return;
                    }

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
                else if (b.DerivedStyle.ActualDisplay == Keywords.InlineTable
                         || b.DerivedStyle.ActualDisplay == Keywords.InlineGrid
                         || laysOutAsAtomicBox)
                {
                    // An inline-table/inline-grid's structural children (rows, grid items) are never
                    // inline-formatting-context content regardless of what's inside them, so those two are
                    // routed here unconditionally. An inline-block is routed here when it has a real
                    // block-level descendant somewhere in its subtree (issue #473), or when its own inline
                    // content cannot fit on one line inside it (see LaysOutAsAnAtomicBox) - the ordinary
                    // case (an inline-block whose text fits on one line) keeps using the recursive FlowBox
                    // call below, unchanged. HasBlockLevelDescendant looks THROUGH nested atomic inline-level
                    // boxes rather than stopping at them (unlike DomUtils.ContainsInlinesOnly/
                    // DomParser.ContainsInlinesOnlyDeep): a <table style="display:inline-block"> whose <tr>
                    // rows were wrapped in an anonymous Display=InlineTable box (DomParser.
                    // CorrectAnonymousTablesGenerateMissingParents, since an inline-block table isn't
                    // IsBlock) has only ONE direct child, and that child is itself IsInline (InlineTable is
                    // in CssBox.IsInline's set) - a shallow "are all direct children inline" check is fooled
                    // by that wrapper into reporting "yes, purely inline content" for a box that in fact
                    // holds a whole table's worth of block-level rows one level further down. Same
                    // resumed-pass guard as the inline-flex branch below: this box is treated as monolithic
                    // within the surrounding inline flow, so a pass only walking through it (already placed
                    // on an earlier fragmentainer) places nothing again.
                    if (!childOpensHere) continue;

                    // Resolve an inline-block's used outer width before laying out its independent
                    // formatting context. An atomic inline is one unbreakable item in this line; when it
                    // does not fit the remaining measure, CSS 2.1 §9.4.2 closes the current line and puts
                    // the whole box on the next one. Doing this before child layout is significant:
                    // line-clamp can stop here without first emitting content that must then be undone.
                    double? resolvedInlineBlockWidth = null;
                    if (b.DerivedStyle.ActualDisplay == Keywords.InlineBlock)
                    {
                        resolvedInlineBlockWidth = await ResolveAtomicInlineBlockWidth(
                            g, b, coordinates.CurrentY);

                        var fitResult = await FitAtomicInlineOnLine(g, blockBox, box, b,
                            resolvedInlineBlockWidth.Value, coordinates, lineSpacing, lineStartX,
                            leftSpacing, clonedTrailing, clonesDecorations, isRtl);

                        if (fitResult == AtomicInlineLineFit.ClampedStop) return;

                        if (fitResult == AtomicInlineLineFit.Wrapped)
                        {
                            childContentStartX = coordinates.CurrentX;

                            // The next line can belong to a fragmentainer with a different inline measure;
                            // percentages and auto shrink-to-fit widths must resolve at that final Y.
                            resolvedInlineBlockWidth = await ResolveAtomicInlineBlockWidth(
                                g, b, coordinates.CurrentY);
                        }
                    }
                    else
                    {
                        // inline-table/inline-grid reach this branch unconditionally (see the comment
                        // above) and, unlike inline-block, never had a fit check at all until issue #1105:
                        // FlowAtomicBlockContentChild positioned them at coordinates.CurrentX no matter how
                        // far that overhung the containing block's right edge. ResolveAtomicInlineBlockWidth
                        // (see its own remarks) is reused here as an ESTIMATE for this check only - their
                        // own column/track layout settles the real used width once
                        // FlowAtomicBlockContentChild's recursive LayoutContents call actually runs.
                        var fitCheckWidth = await ResolveAtomicInlineBlockWidth(g, b, coordinates.CurrentY);

                        var fitResult = await FitAtomicInlineOnLine(g, blockBox, box, b, fitCheckWidth,
                            coordinates, lineSpacing, lineStartX, leftSpacing, clonedTrailing,
                            clonesDecorations, isRtl);

                        if (fitResult == AtomicInlineLineFit.ClampedStop) return;

                        // No re-resolution after a Wrapped result, unlike inline-block below (whose
                        // resolvedInlineBlockWidth IS the value actually applied, so a stale pre-wrap
                        // estimate would mis-size the box itself). Here fitCheckWidth is discarded either
                        // way: FlowAtomicBlockContentChild's PrepareAtomicBlockContentChild sets b.Location
                        // from coordinates.CurrentX/CurrentY - already the post-wrap position by the time
                        // it runs - and CssLayoutEngineTable/Grid.PerformLayout each derive their own
                        // percentage/auto width fresh from that same b.Location/b.ContainingBlock when
                        // THEY run (CssLayoutEngineTable.GetAvailableTableWidth keys off _tableBox.ClientTop;
                        // CssLayoutEngineGrid.PerformLayout calls GetBoxWidth(g, _gridBox) with no blockTop
                        // override, defaulting to _gridBox.Location.Y) - so neither can read a value cached
                        // from before the wrap.

                        // Unlike inline-block, childDeclaredContentWidth is always null for inline-table/
                        // inline-grid (ResolveAtomicInlineDeclaredWidth only ever resolves one for
                        // Keywords.InlineBlock), so childContentStartX has no consumer here - nothing to
                        // update after a wrap.
                    }

                    await FlowAtomicBlockContentChild(g, b, coordinates, resolvedInlineBlockWidth);
                    if (FoldHeldEmptyInlinesIntoAtomicInlinesLine(blockBox, coordinates)) return;
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

                    // inline-flex had no fit check of any kind before issue #1105 - FlowInlineFlexChild
                    // positioned it unconditionally too. ResolveAtomicInlineBlockWidth (see its own
                    // remarks) is reused here as an ESTIMATE (CSS Flexbox 1 §9.2's shrink-to-fit main
                    // size); the flex algorithm settles the real used width once FlowInlineFlexChild's own
                    // CssLayoutEngineFlex.PerformLayout call runs. Unlike inline-block, childContentStartX
                    // has no consumer for an inline-flex either - see the inline-table/inline-grid branch
                    // above - so nothing to update after a wrap. No re-resolution after a Wrapped result
                    // either, for the same reason given there: FlowInlineFlexChild positions b.Location
                    // from the already-post-wrap coordinates BEFORE CssLayoutEngineFlex.PerformLayout runs,
                    // and that engine's own GetBoxWidth(g, _flexBox) call (no blockTop override) derives
                    // fresh from b.Location.Y at that point, never from this discarded estimate.
                    var flexFitCheckWidth = await ResolveAtomicInlineBlockWidth(g, b, coordinates.CurrentY);

                    var flexFitResult = await FitAtomicInlineOnLine(g, blockBox, box, b, flexFitCheckWidth,
                        coordinates, lineSpacing, lineStartX, leftSpacing, clonedTrailing, clonesDecorations,
                        isRtl);

                    if (flexFitResult == AtomicInlineLineFit.ClampedStop) return;

                    await FlowInlineFlexChild(g, b, coordinates);
                    if (FoldHeldEmptyInlinesIntoAtomicInlinesLine(blockBox, coordinates)) return;
                }
                else
                {
                    await FlowBox(g, blockBox, b, lineSpacing, lineStartX, coordinates, childClonedResumeStart);

                    if (coordinates.Break is not null || coordinates.ClampedStop)
                    {
                        // Same as the branch above: under clone this pass's own portion of b closes here
                        // with its own bottom border/padding, same as the true-final-pass case below - the
                        // break just means it isn't that pass (#343). A line-clamp stop takes the same
                        // early return but never applies clone's re-opened decorations - see the branch above.
                        if (clonesAtomicDecorations && coordinates.Break is not null) ApplyAtomicInlineVerticalInsets(b, coordinates, atomicBottomInset);
                        return;
                    }

                    // Same as the branch above: an inset applies to the words this pass placed, and a
                    // box it placed none for has already had it.
                    if (coordinates.PlacedSince(childStartOrdinal))
                    {
                        ApplyAtomicInlineVerticalInsets(b, coordinates, atomicBottomInset);
                    }

                    // An inline whose subtree took no word ordinal and holds no atomic inline is empty on its
                    // line, and still sizes it.
                    if (childOpensHere && coordinates.WordOrdinal == childStartOrdinal
                        && b.DerivedStyle.ActualDisplay == Keywords.Inline && !HoldsAtomicInlineContent(b))
                    {
                        PlaceEmptyInline(b);

                        if (coordinates.Break is not null) return;
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

                // The declared width also has to be reserved for a box that took the block-content
                // path above (FlowAtomicBlockContentChild), whose own cursor advance - `CurrentX =
                // b.ClientRight` - comes out one padding short of the declared content box. Harmless
                // where the box already reserved it: this only ever moves the cursor forward, and
                // FinalizeFlowBoxExit has already put it exactly here for a box that went through the
                // inline-flow path instead.
                if (childDeclaredContentWidth is { } declaredContentWidth)
                {
                    coordinates.CurrentX = Math.Max(coordinates.CurrentX, childContentStartX + declaredContentWidth);
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
        /// Restores every emergency <c>overflow-wrap</c> split before a fresh, non-resumed layout. A split
        /// is chosen against one line's available measure; carrying it into a later full reflow would turn
        /// that emergency point into an unconditional ordinary wrap opportunity.
        /// </summary>
        private static void RestoreOverflowWrapSplits(CssBox box)
        {
            // A suffix may itself have been split later. Restore from right to left so the deepest
            // prefix/suffix pair is merged before its parent pair; the insertion adjacency checked by
            // TryRestoreOverflowWrapSplit is consequently re-established at every level.
            for (var i = box.Words.Count - 1; i >= 0; i--)
            {
                if (box.Words[i] is not CssRectWord
                    {
                        PreOverflowWrapWord: { } original,
                        OverflowWrapSuffix: { } suffix
                    } prefix)
                    continue;

                TryRestoreOverflowWrapSplit(prefix, original, suffix, awaitsNextFragmentainer: false);
            }

            foreach (var child in box.Boxes)
                RestoreOverflowWrapSplits(child);
        }

        /// <summary>
        /// Undoes a split made for a line that fragmentation discarded, so the resumed fragmentainer can
        /// choose a new point against its own full line measure.
        /// </summary>
        internal static void UndoAbandonedOverflowWrapSplits(CssLineBox discardedLine)
        {
            foreach (var word in discardedLine.Words)
            {
                if (word is CssRectWord
                    {
                        PreOverflowWrapWord: { } original,
                        OverflowWrapSuffix: { } suffix
                    } prefix)
                {
                    TryRestoreOverflowWrapSplit(prefix, original, suffix, awaitsNextFragmentainer: true);
                }
            }
        }

        private static bool TryRestoreOverflowWrapSplit(CssRectWord prefix, CssRectWord original,
            CssRectWord suffix, bool awaitsNextFragmentainer)
        {
            var ownerWords = prefix.OwnerBox.Words;
            var prefixIndex = ownerWords.IndexOf(prefix);
            if (prefixIndex < 0 || prefixIndex + 1 >= ownerWords.Count
                                || !ReferenceEquals(ownerWords[prefixIndex + 1], suffix))
                return false;

            // Split fragments are inserted as one adjacent pair. Removing by index preserves the
            // prefix index used below and refuses to merge unrelated/non-adjacent list entries.
            ownerWords.RemoveAt(prefixIndex + 1);
            ownerWords[prefixIndex] = original;
            if (awaitsNextFragmentainer)
                original.AwaitsTheNextFragmentainer = true;
            return true;
        }

        /// <summary>
        /// Whether the cursor still sits at the beginning of the line it is on - nothing occupying
        /// inline space has been placed on that line yet. This is what
        /// <see href="https://www.w3.org/TR/css-text-3/#white-space-phase-2">css-text-3 phase II</see>
        /// asks before removing a collapsible space sequence.
        /// </summary>
        /// <remarks>
        /// A forced-break word is placed on the line it <i>opens</i> rather than on the one it closes
        /// (see <see cref="FlowBox"/>'s own wrap branch, which stamps the new line's
        /// <see cref="CssLineBox.FollowsForcedBreak"/> from that same word), and it occupies no width,
        /// so a line whose only word so far is one is still at its start - which is the whole point of
        /// this check for the space that follows a <c>&lt;br&gt;</c>.
        /// <see cref="CssLineBox.Rectangles"/> covers what occupies the line without contributing a
        /// word of its own. The case it is here for is the atomic inline-level box (an inline-block
        /// holding block-level content, an inline-table, inline-grid or inline-flex) placed by
        /// <see cref="FlowAtomicBlockContentChild"/>/<see cref="FlowInlineFlexChild"/>: Chromium
        /// renders the space after one, and without this clause it would be dropped as line-leading.
        /// Two other writers reach the dictionary during the flow and are worth knowing about before
        /// changing either: <c>FlowBox</c>'s own "hack for actual width handling" tail (an inline box
        /// whose content came out narrower than its declared <c>Size.Width</c> - which for a plain
        /// inline is 0, so in practice only an inline-level box with a declared width, see #1093) is
        /// the only other one, and <see cref="CssLineBox"/>'s own bookkeeping
        /// (<see cref="CssLineBox.UpdateRectangle"/>/<see cref="CssLineBox.AssignRectanglesToBoxes"/>)
        /// runs only after the flow, so it is never seen here. A float is deliberately not counted:
        /// it is out of flow, and a line that begins beside one begins at the float's edge with its
        /// leading white space removed - measured against Chromium, which agrees to 0.005px.
        /// </remarks>
        private static bool IsAtLineStart(CssLineBoxCoordinates coordinates)
        {
            if (coordinates.Line.Rectangles.Count > 0) return false;

            // Walked back-to-front so the common answer costs one comparison: a line that already
            // holds content ends in it, and this runs once per collapsed space on the line.
            var words = coordinates.Line.Words;
            for (var i = words.Count - 1; i >= 0; i--)
            {
                if (!words[i].IsLineBreak) return false;
            }

            return true;
        }

        private static bool AllowsOverflowWrap(CssRect word) =>
            word is CssRectWord { IsLineBreak: false }
            && word.OwnerBox.OverflowWrap.Value != OverflowWrap.Normal
            && word.OwnerBox.WhiteSpacePermitsWrapping;

        private static bool AllowsOverflowWrapAtBoundary(CssRect? previous, CssRect word) =>
            AllowsOverflowWrap(word) || previous is not null && AllowsOverflowWrap(previous);

        /// <summary>
        /// Whether the boundary before <paramref name="word"/> is an ordinary soft-wrap opportunity.
        /// Inline element boundaries are ignored: adjacent text split only by markup remains one
        /// unbreakable token, while the parser's real whitespace, hyphen, CJK, and break-all boundaries
        /// retain their existing behavior.
        /// </summary>
        /// <remarks>
        /// A forced break is never an opportunity to break <i>again</i>. The marker word's text is a
        /// newline, so it satisfies the <see cref="CssRect.IsSpaces"/> arm below - but it is placed on
        /// the line it <i>opens</i> (see <see cref="IsAtLineStart"/>), so treating it as a boundary put
        /// a soft wrap opportunity at the very start of a line, which
        /// <see href="https://www.w3.org/TR/css-text-3/#line-breaking">css-text-3 §5</see> does not
        /// allow ("a line must not begin ... with a soft wrap opportunity"). Taking it wrapped an
        /// overlong word straight off the line the <c>&lt;br&gt;</c> had just opened, rendering that
        /// line blank - see <c>.claude/invariants/inline-a-forced-break-word-sits-on-the-line-it-opens.md</c>.
        /// </remarks>
        internal static bool HasOrdinaryWrapOpportunityBefore(
            CssRect? previous, CssRect word, bool hasWhitespaceBefore = false,
            int precedingRegionalIndicatorCount = 0, string? precedingGraphemeContext = null)
        {
            if (word.SuppressWrapBefore || previous is null || previous.IsLineBreak)
                return false;

            if (previous is not CssRectWord previousWord || word is not CssRectWord currentWord)
                return true;

            // Two words cut from the same box's text: the Unicode line breaking algorithm has already said, over the whole text
            // and with word-break applied, whether a line may end between them (a space, a hyphen, an ideograph, a slash ...).
            if (currentWord.UnicodeBreakBefore is { } unicodeBreak && ReferenceEquals(previousWord.OwnerBox, currentWord.OwnerBox))
            {
                return unicodeBreak && IsGraphemeBoundaryBefore(previousWord, currentWord, precedingRegionalIndicatorCount,
                    precedingGraphemeContext);
            }

            Rune.DecodeLastFromUtf16(previousWord.Text.AsSpan(), out var previousRune, out _);
            Rune.DecodeFromUtf16(currentWord.Text.AsSpan(), out var currentRune, out _);
            if (!IsGraphemeBoundaryBefore(previousWord, currentWord, precedingRegionalIndicatorCount,
                    precedingGraphemeContext)
                || ProhibitsLineBreakAfter(previousRune) || ProhibitsLineBreakBefore(currentRune))
                return false;

            if (hasWhitespaceBefore || HasInterElementWhitespaceBefore(currentWord) || previousWord.IsSpaces
                || previousWord.HasSpaceAfter || currentWord.HasSpaceBefore
                || previousWord.OwnerBox.WordBreak.Value == WordBreak.BreakAll
                || currentWord.OwnerBox.WordBreak.Value == WordBreak.BreakAll)
                return true;

            return previousRune.Value == '-'
                || CommonUtils.IsAsianCharacter(previousRune)
                || CommonUtils.IsAsianCharacter(currentRune)
                || CommonUtils.IsEmojiLineBreakCharacter(previousRune)
                || CommonUtils.IsEmojiLineBreakCharacter(currentRune);
        }

        internal static bool IsGraphemeBoundaryBefore(
            CssRect? previous, CssRect word, int precedingRegionalIndicatorCount = 0,
            string? precedingGraphemeContext = null)
        {
            if (previous is not CssRectWord previousWord || word is not CssRectWord currentWord)
                return true;

            Rune.DecodeFromUtf16(currentWord.Text.AsSpan(), out var currentRune, out _);
            if (IsRegionalIndicator(currentRune) && precedingRegionalIndicatorCount % 2 != 0)
                return false;

            if (ReferenceEquals(previousWord.OwnerBox, currentWord.OwnerBox)
                && !currentWord.SuppressWrapBefore
                && currentWord.OwnerBox.WordBreak.Value != WordBreak.BreakAll)
                return true;

            var context = string.IsNullOrEmpty(precedingGraphemeContext)
                ? previousWord.Text
                : precedingGraphemeContext;
            var combined = string.Concat(context, currentWord.Text);
            var boundary = context.Length;
            return Array.BinarySearch(StringInfo.ParseCombiningCharacters(combined), boundary) >= 0;
        }

        private static bool ProhibitsLineBreakAfter(Rune rune) =>
            Rune.GetUnicodeCategory(rune) is UnicodeCategory.OpenPunctuation
                or UnicodeCategory.InitialQuotePunctuation
            || IsPrefixNumericLineBreakCharacter(rune);

        private static bool ProhibitsLineBreakBefore(Rune rune) =>
            Rune.GetUnicodeCategory(rune) is UnicodeCategory.ClosePunctuation
                or UnicodeCategory.FinalQuotePunctuation
            || rune.Value is '!' or ',' or '.' or '/' or ':' or ';' or '?'
            || IsPostfixNumericLineBreakCharacter(rune);

        private static bool IsPrefixNumericLineBreakCharacter(Rune rune) => rune.Value switch
        {
            0x0024 or 0x002B or 0x005C or >= 0x00A3 and <= 0x00A5 or 0x00B1 or 0x058F
                or >= 0x07FE and <= 0x07FF or 0x09FB or 0x0AF1 or 0x0BF9 or 0x0E3F or 0x17DB
                or >= 0x20A0 and <= 0x20A6 or >= 0x20A8 and <= 0x20B5
                or >= 0x20B7 and <= 0x20BA or >= 0x20BC and <= 0x20BD or 0x20BF
                or >= 0x20C1 and <= 0x20CF or 0x2116 or >= 0x2212 and <= 0x2213
                or 0xFE69 or 0xFF04 or 0xFFE1 or >= 0xFFE5 and <= 0xFFE6 or 0x1E2FF => true,
            _ => false
        };

        private static bool IsPostfixNumericLineBreakCharacter(Rune rune) => rune.Value switch
        {
            0x0025 or 0x00A2 or 0x00B0 or >= 0x0609 and <= 0x060B or 0x066A
                or >= 0x09F2 and <= 0x09F3 or 0x09F9 or 0x0D79 or >= 0x2030 and <= 0x2037
                or 0x2057 or 0x20A7 or 0x20B6 or 0x20BB or 0x20BE or 0x20C0 or 0x2103
                or 0x2109 or 0xA838 or 0xFDFC or 0xFE6A or 0xFF05 or 0xFFE0
                or >= 0x11FDD and <= 0x11FE0 or 0x1ECAC or 0x1ECB0 => true,
            _ => false
        };

        internal static bool IsRegionalIndicator(Rune rune) => rune.Value is >= 0x1F1E6 and <= 0x1F1FF;

        internal static int UpdateTrailingRegionalIndicatorCount(int precedingCount, ReadOnlySpan<char> text)
        {
            var count = 0;
            while (!text.IsEmpty
                && Rune.DecodeLastFromUtf16(text, out var rune, out var charsConsumed) == OperationStatus.Done
                && IsRegionalIndicator(rune))
            {
                count++;
                text = text[..^charsConsumed];
            }

            return text.IsEmpty ? precedingCount + count : count;
        }

        internal static string UpdateTrailingGraphemeContext(string precedingContext, string text)
        {
            var combined = string.Concat(precedingContext, text);
            if (combined.Length == 0)
                return string.Empty;

            var boundaries = StringInfo.ParseCombiningCharacters(combined);
            return combined[boundaries[^1]..];
        }

        private static (int RegionalIndicatorCount, string GraphemeContext) UpdateTrailingTextState(
            CssRect word, int regionalIndicatorCount, string graphemeContext)
        {
            if (word is not CssRectWord textWord || string.IsNullOrEmpty(textWord.Text))
                return (0, string.Empty);

            regionalIndicatorCount = UpdateTrailingRegionalIndicatorCount(
                regionalIndicatorCount, textWord.Text.AsSpan());
            graphemeContext = UpdateTrailingGraphemeContext(graphemeContext, textWord.Text);
            return (regionalIndicatorCount, graphemeContext);
        }

        private static bool HasInterElementWhitespaceBefore(CssRectWord word) =>
            DomUtils.PrecedingBoxAcrossFirstChildChain(word.OwnerBox) is { } predecessor
            && EndsWithCollapsibleWhitespace(predecessor);

        /// <summary>
        /// Whether <paramref name="box"/> ends in collapsible inter-element whitespace, descending to the
        /// last text-bearing leaf rather than reading <see cref="CssBox.Text"/> off <paramref name="box"/>
        /// itself - a structural container's own Text is always null, since its content lives in its
        /// children. Reading only the immediate predecessor's Text hides whitespace nested inside a
        /// non-leaf inline sibling (<c>&lt;span&gt;A&lt;/span&gt;&lt;b&gt;&lt;i&gt; &lt;/i&gt;&lt;/b&gt;&lt;span&gt;B&lt;/span&gt;</c>),
        /// welding the text on either side into one unbreakable token. Mirrors the same descent the
        /// regional-indicator parity walk in <see cref="CssBox"/> makes for the same reason.
        /// </summary>
        private static bool EndsWithCollapsibleWhitespace(CssBox box)
        {
            for (var i = box.Boxes.Count - 1; i >= 0; i--)
            {
                var child = box.Boxes[i];
                if (EndsWithCollapsibleWhitespace(child))
                    return true;

                // Real content between that whitespace and this boundary ends the search; a child that
                // contributed nothing at all (an empty inline) is skipped over to the one before it.
                if (child.Text is not null || child.Words.Count > 0)
                    return false;
            }

            return box.Boxes.Count == 0
                && box.Text is { } text
                && HtmlUtils.IsNullOrCollapsibleWhitespace(text);
        }

        /// <summary>
        /// Splits an overflowing word at the last extended-grapheme-cluster boundary whose prefix fits.
        /// No hyphen is inserted. The search is logarithmic so a pathological URL does not turn line
        /// layout into a quadratic sequence of successively shorter shaping calls.
        /// </summary>
        private static bool TryOverflowWrapWord(RGraphics g, CssRect word, double availableWidth,
            out CssRectWord? prefix, out CssRectWord? suffix)
        {
            prefix = null;
            suffix = null;

            if (!AllowsOverflowWrap(word) || word is not CssRectWord rectWord || availableWidth <= 0)
                return false;

            var boundaries = StringInfo.ParseCombiningCharacters(rectWord.PreMirrorText);
            if (boundaries.Length < 2) return false;

            var low = 1;
            var high = boundaries.Length - 1;
            var bestBreak = -1;

            while (low <= high)
            {
                var middle = low + ((high - low) / 2);
                var breakAt = boundaries[middle];
                var trial = rectWord.SliceForOverflowWrapMeasurement(0, breakAt);
                var trialWidth = MeasureOverflowWrapWord(g, trial);

                if (trialWidth <= availableWidth)
                {
                    bestBreak = breakAt;
                    low = middle + 1;
                }
                else
                {
                    high = middle - 1;
                }
            }

            if (bestBreak <= 0) return false;

            (prefix, suffix) = rectWord.SplitForOverflowWrap(bestBreak);
            MeasureOverflowWrapWord(g, prefix);
            MeasureOverflowWrapWord(g, suffix);
            return true;
        }

        private static double MeasureOverflowWrapWord(RGraphics g, CssRectWord word)
        {
            var styleSource = word.FirstLineStyle ?? word.OwnerBox;
            var font = CssBox.ResolveWordFont(word, styleSource);
            var text = word.FirstLineText ?? word.Text;
            var features = styleSource.ResolveWordShapingFeatures(word);
            var width = g.MeasureString(text, font, features).Width;

            if (styleSource.ActualLetterSpacing != 0)
                width += g.CountShapedGlyphs(text, font, features) * styleSource.ActualLetterSpacing;

            word.Width = width;
            word.Height = styleSource.ActualFont.Height;
            return width;
        }

        /// <summary>Measures <c>overflow-wrap:anywhere</c>'s min-content contribution for one word.</summary>
        internal static double MeasureOverflowWrapMinWidth(RGraphics g, CssRectWord word)
        {
            var text = word.PreMirrorText;
            var boundaries = StringInfo.ParseCombiningCharacters(text);
            var widest = 0d;

            for (var i = 0; i < boundaries.Length; i++)
            {
                var start = boundaries[i];
                var end = i + 1 < boundaries.Length ? boundaries[i + 1] : text.Length;
                var cluster = word.SliceForOverflowWrapMeasurement(start, end - start);
                widest = Math.Max(widest, MeasureOverflowWrapWord(g, cluster));
            }

            return widest;
        }

        /// <summary>
        /// <c>hyphenate-limit-lines</c> (CSS Text 4 §6.3.5): whether the line about to be built is still
        /// allowed to end in a hyphen, given how many immediately preceding lines already did.
        /// </summary>
        private static bool IsWithinHyphenateLimitLines(CssBox blockBox, int consecutiveHyphenatedLines)
        {
            var limit = blockBox.HyphenateLimitLines.Value;
            return !limit.IsValue || consecutiveHyphenatedLines < limit.Value!.Value;
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
        /// an explicit soft hyphen position or an automatic <c>Hyphenator</c> suggestion) whose
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
                    Height = b.ActualFont.Height,
                    UnicodeBreakBefore = word.UnicodeBreakBefore
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
        /// CSS Overflow 4 §block-ellipsis/§max-lines (the module <c>line-clamp</c> itself now lives in -
        /// CSS Overflow 3 explicitly defers it): once <paramref name="blockBox"/> has already produced as
        /// many lines as its declared limit, this appends a generated ellipsis word to the line currently
        /// being built (the last one that will ever be visible) and reports that the whole block's
        /// content is done - permanently, not merely paused for a later fragmentainer pass. The ellipsis
        /// text itself is <see cref="CssBox.BlockEllipsis"/> (default <c>"…"</c> when unset/<c>auto</c>);
        /// when it is <c>none</c> (stored as <c>""</c>), the block still stops here but no ellipsis word is
        /// generated at all - see the short-circuit below. Returns false (leaving
        /// <paramref name="coordinates"/> untouched) when <c>line-clamp</c> is <c>none</c>, the limit
        /// hasn't been reached yet, or the line has no content of its own to attach the ellipsis to (an
        /// emergency case - see the empty-line guard below - deliberately left to wrap normally rather
        /// than emit a blank clamped line).
        /// </summary>
        /// <remarks>
        /// Word-granularity, not character-granularity: this pops whole trailing words until the
        /// ellipsis fits rather than splitting the last one character-by-character the way
        /// <c>text-overflow</c>'s paint-time truncation does (<c>FragmentPainter.TextOverflow.cs</c>) -
        /// and, per spec, this is the *specified* behavior (§block-ellipsis places the ellipsis "after
        /// the last soft wrap opportunity that would still allow the entire block overflow ellipsis to
        /// fit"), not a simplification of it. The floor below - never popping the line's last remaining
        /// word - is what §block-ellipsis's own fallback covers: when no soft wrap opportunity leaves
        /// room, the ellipsis overflows the line rather than the line losing real content it would
        /// otherwise have kept.
        ///
        /// The ellipsis is measured and painted using whichever real word survives as the line's own
        /// last word once popping is done, resolved fresh here rather than reused from before popping
        /// started (a popped word's own owner is no longer part of the line at all). Spec (§block-ellipsis)
        /// wants it "wrapped in an anonymous inline box... as a direct child of the block container"
        /// instead - a real, separate anonymous inline this engine does not synthesize - so an ellipsis
        /// following a specially-styled trailing span (a colored/bordered run of text, say) can still
        /// visually pick up that span's own decorations; see the accepted-gap note.
        /// </remarks>
        private static bool TryApplyLineClamp(RGraphics g, CssBox blockBox, CssLineBoxCoordinates coordinates,
            double actualLimitRight, double rightSpacing, double clonedTrailing)
        {
            if (blockBox.LineClamp.Value is not { IsValue: true, Value: { } limit }) return false;
            if (blockBox.LineBoxes.Count < limit) return false;
            if (coordinates.Line.Words.Count == 0) return false;

            // block-ellipsis: none (CSS Overflow 4) - the block still stops at its line limit, but with
            // no ellipsis marker at all, so there is nothing to pop room for or append; the already-placed
            // words on the last visible line are left exactly as they are.
            if (string.Equals(blockBox.BlockEllipsis, Keywords.None, StringComparison.OrdinalIgnoreCase)) return true;

            var fitLimit = actualLimitRight - rightSpacing - clonedTrailing;

            // Pop trailing words - whole words, not characters, see this method's own remarks - until
            // the ellipsis (sized against whichever word is currently last) fits in the line's own
            // available width, or only one word (which must stay, so the line keeps real content instead
            // of losing all of it - see this method's own remarks) remains. A popped word has to come out
            // of its OWNER box's own Words list too, not just this line's - FragmentEmitter walks
            // CssBox.Words directly (see the ellipsis word's own comment below), so a word only removed
            // from the line's bookkeeping would still carry its original, already-assigned position there
            // and would still paint, landing underneath/beside the ellipsis instead of actually being
            // replaced by it. Each removal is recorded on blockBox so a fresh layout pass over the same
            // tree can put it back first - see LineClampPoppedWords.
            // block-ellipsis's raw declared text is stored as-is (still carrying its quotes, same
            // convention as hyphenate-character - see the property's own css-properties.json comment);
            // re-parsed here into the actual ellipsis text. "auto" (the default) and any other bare
            // keyword fail TryParseSingleString (it only accepts a genuine quoted <string> token), which
            // is exactly the fallback to the spec's own UA-default ellipsis this needs.
            var ellipsisText = CssValueParser.TryParseSingleString(blockBox.BlockEllipsis, out var customEllipsisText)
                ? customEllipsisText
                : "…";
            CssRect last;
            CssBox styleSource;
            double ellipsisWidth;
            while (true)
            {
                last = coordinates.Line.Words[^1];
                styleSource = last.OwnerBox;
                ellipsisWidth = g.MeasureString(ellipsisText, styleSource.ActualFont, styleSource.ActualTextShapingFeatures).Width;

                if (coordinates.Line.Words.Count <= 1 || last.Right + ellipsisWidth <= fitLimit)
                    break;

                coordinates.Line.Words.RemoveAt(coordinates.Line.Words.Count - 1);

                var ownerIndex = styleSource.Words.IndexOf(last);
                styleSource.Words.RemoveAt(ownerIndex);

                (blockBox.LineClampPoppedWords ??= []).Add((styleSource, ownerIndex, last));
            }

            var x = last.Right;
            var ellipsisWord = new CssRectWord(styleSource, ellipsisText, hasSpaceBefore: false, hasSpaceAfter: false)
            {
                Width = ellipsisWidth,
                Height = styleSource.ActualFont.Height,
                Left = x,
                Top = coordinates.CurrentY
            };

            // Fragment emission (FragmentEmitter.BuildDraft) walks a box's own CssBox.Words - not the
            // per-line CssLineBox.Words ReportExistanceOf below adds to - to decide what actually reaches
            // the fragment tree paint reads from, so the generated word needs a real home in both lists:
            // owner.Words is what makes it exist as far as painting is concerned, and the line's own list
            // is what makes WordsOf/BubbleRectangles fold it into the owner's line-local rectangle.
            // styleSource must be a box that can legitimately hold words at all - never blockBox itself
            // when it has child boxes of its own (CssLayoutEngine.FlowBox silently skips a box's own
            // Words whenever it also has Boxes - see the dom-a-box-must-never-hold-both-its-own-words-and-
            // child-boxes invariant) - which is exactly why this is the surviving real word's own owner,
            // not blockBox.
            styleSource.Words.Add(ellipsisWord);
            blockBox.LineClampEllipsisWord = (styleSource, ellipsisWord);
            coordinates.Line.ReportExistanceOf(ellipsisWord);

            // An auto-width block sizes itself from MaxRight (see CreateLineBoxes below); the ordinary
            // per-word placement loop is what normally advances it; the ellipsis is generated after that
            // loop already ran for this line; without this, an auto-width clamped block would come out
            // exactly the popped content's width narrower than what it actually paints.
            coordinates.MaxRight = Math.Max(coordinates.MaxRight, x + ellipsisWidth);

            return true;
        }

        /// <summary>
        /// Undoes every mutation <see cref="TryApplyLineClamp"/> made to <paramref name="box"/>'s (and its
        /// popped words' owners') <see cref="CssBox.Words"/> lists, before a fresh (non-resumed) layout
        /// pass lays <paramref name="box"/> out again. Mirrors <see cref="RestoreOverflowWrapSplits"/> for
        /// the same reason: <see cref="CssBox.Words"/> is mutated in place and never rebuilt from scratch
        /// between passes, so a clamp decision made on an earlier pass would otherwise compound (a second
        /// pass sees an already-shortened word list and appends a second ellipsis, a third sees that
        /// result and appends a third, ...) instead of being re-derived fresh each time.
        /// </summary>
        private static void RestoreLineClampMutations(CssBox box)
        {
            if (box.LineClampEllipsisWord is { } ellipsis)
            {
                ellipsis.Owner.Words.Remove(ellipsis.Word);
                box.LineClampEllipsisWord = null;
            }

            // Reinserted in reverse removal order (last popped, first restored) - TryApplyLineClamp always
            // pops from the tail of a growing gap, so undoing in that same LIFO order is what lands every
            // word back at the index it actually occupied before any of them were removed.
            if (box.LineClampPoppedWords is { } popped)
            {
                for (var i = popped.Count - 1; i >= 0; i--)
                {
                    var (owner, index, word) = popped[i];
                    owner.Words.Insert(Math.Min(index, owner.Words.Count), word);
                }

                box.LineClampPoppedWords = null;
            }
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
        /// Whether <paramref name="box"/> is the first of <see cref="CssBox.Boxes"/> on its own parent -
        /// i.e. its parent's content genuinely begins with it. Shared by the wrap branch in
        /// <see cref="FlowBox"/> (which corrects an inline ancestor chain's stale
        /// <see cref="CssBox.FirstHostingLineBox"/> when this box's own first word wraps to a new line)
        /// and, historically, <see cref="BubbleRectangles"/>'s own compensation for the same case.
        /// </summary>
        private static bool IsFirstChildOfItsParent(CssBox box) =>
            box.ParentBox is { } parent && parent.Boxes.Count > 0 && ReferenceEquals(parent.Boxes[0], box);

        /// <summary>
        /// Widens each atomic inline-level box on <paramref name="line"/> from the rectangle its own
        /// content just bubbled up to the used width
        /// <see href="https://www.w3.org/TR/CSS22/visudet.html#inlineblock-width">CSS 2.1 §10.3.9</see>
        /// gives it — the declared <c>width</c> <see cref="ResolveAtomicInlineDeclaredWidth"/> assigned,
        /// which is wider than the content whenever the content does not fill it.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Runs here, after <see cref="BubbleRectangles"/>, rather than where the width is reserved on the
        /// line (<see cref="FinalizeFlowBoxExit"/>), for two reasons. The rectangle does not exist yet at
        /// that point — the words state it here — so stating one there would mean <i>merging</i> a
        /// separately-derived block-axis extent into it, and the flow knows nothing about where the box's
        /// content finally settles vertically; and the rectangle's own left edge is already correct here,
        /// having been through <c>text-align</c>'s per-line shift and
        /// <see cref="CssLineBox.UpdateRectangle"/>'s leading-spacing step-back. Anchoring on it keeps this
        /// a purely inline-axis correction.
        /// </para>
        /// <para>
        /// Only for a box that both opened and closed on this line: <see cref="CssLineBox.UpdateRectangle"/>
        /// folds a box's leading and trailing border and padding into its rectangle only on those lines, so
        /// on any other line the rectangle is a slice whose width the box's own used width does not
        /// describe. An atomic inline is not supposed to have more than one in the first place, but on this
        /// path its inline content is flowed into the surrounding block's own line boxes and so can wrap.
        /// </para>
        /// </remarks>
        private static void WidenAtomicInlineRectangles(CssLineBox line)
        {
            // Gathered before anything is written, and gathered into a list allocated only once there is
            // something to put in it: this runs for every line of every document, and the overwhelming
            // majority hold no atomic inline at all.
            List<CssBox>? widening = null;

            foreach (var (box, rect) in line.Rectangles)
            {
                if (!IsWhollyHostedOnLine(box, line)) continue;

                // Only ever forward: content wider than the declared width overflows rather than being
                // pulled back to it, which is what `overflow: visible` means.
                if (box.ActualBoxSizingWidth <= rect.Width) continue;

                (widening ??= []).Add(box);
            }

            if (widening is null) return;

            foreach (var box in widening)
            {
                var rect = line.Rectangles[box];
                line.Rectangles[box] = new RRect(rect.X, rect.Y, box.ActualBoxSizingWidth, rect.Height);
            }
        }

        /// <summary>
        /// Whether <paramref name="box"/> is an atomic inline-level box wholly hosted on
        /// <paramref name="line"/> — the one line it both opened and closed on, per
        /// <see cref="CssBox.FirstHostingLineBox"/>/<see cref="CssBox.LastHostingLineBox"/> — which is the
        /// eligibility <see cref="WidenAtomicInlineRectangles"/> and <see cref="HeightenAtomicInlineRectangles"/>
        /// both need before correcting <paramref name="line"/>'s rectangle for it: a box spanning more than
        /// one of the parent's own lines has only a slice of its extent on any single one.
        /// </summary>
        private static bool IsWhollyHostedOnLine(CssBox box, CssLineBox line) =>
            box.DerivedStyle.ActualDisplay is Keywords.InlineBlock
            && ReferenceEquals(box.FirstHostingLineBox, line)
            && ReferenceEquals(box.LastHostingLineBox, line);

        /// <summary>
        /// Which edge of an atomic inline-level box's post-alignment rectangle
        /// <see cref="HeightenAtomicInlineRectangles"/> must keep fixed while growing it to a declared
        /// height — the edge <see cref="ApplyVerticalAlignment"/>'s per-<c>vertical-align</c>-case
        /// arithmetic already anchored at a position independent of the box's own (natural, pre-growth)
        /// height, per CSS 2.1 <see href="https://www.w3.org/TR/CSS21/visudet.html#propdef-vertical-align">
        /// §10.8.1</see>.
        /// </summary>
        private enum RectangleGrowthAnchor
        {
            /// <summary>The box's own top stays fixed; extra height is added below it.</summary>
            Top,

            /// <summary>The box's own bottom stays fixed; extra height is added above it.</summary>
            Bottom,

            /// <summary>The box's own vertical center stays fixed; extra height splits evenly around it.</summary>
            Middle,

            /// <summary>
            /// Neither edge is safely knowable post-alignment without risking a worse mispositioning than
            /// leaving the rectangle's current top fixed — see <see cref="AnchorOf"/>'s own remarks.
            /// </summary>
            Circular
        }

        /// <summary>
        /// Classifies <paramref name="box"/>'s <see cref="RectangleGrowthAnchor"/> for
        /// <see cref="HeightenAtomicInlineRectangles"/>, from the same <c>vertical-align</c> this box was
        /// just positioned by in <see cref="ApplyVerticalAlignment"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Derived by checking, for each case <see cref="ApplyVerticalAlignment"/>'s own per-box switch
        /// distinguishes, whether that case's <c>OffsetBoxWithinLine</c> delta is a function of the box's
        /// own (natural, pre-growth) <c>rect.Height</c> — if it is not, the edge that delta places the box
        /// at is height-independent and therefore safe to hold fixed while growing:
        /// <see cref="VerticalAlignment.Top"/> and <see cref="VerticalAlignment.TextTop"/> place
        /// <c>rect.Top</c> directly (independent of height) → <see cref="RectangleGrowthAnchor.Top"/>.
        /// <see cref="VerticalAlignment.Bottom"/> and <see cref="VerticalAlignment.TextBottom"/> place
        /// <c>rect.Bottom</c> directly (the height term cancels out of the delta arithmetic) →
        /// <see cref="RectangleGrowthAnchor.Bottom"/>. <see cref="VerticalAlignment.Middle"/> centers the
        /// box in the line using its natural height, so growing symmetrically around the box's own
        /// already-centered midpoint keeps that midpoint exactly on the line's own middle regardless of the
        /// natural height that produced it → <see cref="RectangleGrowthAnchor.Middle"/>.
        /// </para>
        /// <para>
        /// The default <c>baseline</c> case splits in two: with real, non-replaced content
        /// (<see cref="AtomicInlineBaselineOf"/>'s word-ascent branch), the box's <c>rect.Top</c> is placed
        /// from the word's own top minus a height-independent offset (the word sits a fixed distance below
        /// the box's own top border/padding, regardless of the box's height) →
        /// <see cref="RectangleGrowthAnchor.Top"/>, matching content's natural top-anchoring inside the
        /// box's own content area. <see cref="VerticalAlignment.Sub"/>/<see cref="VerticalAlignment.Super"/>/
        /// a length or percentage offset only ever reach this method already excluded from the empty/
        /// <c>overflow</c>-hidden case below (an empty or non-<c>visible</c> box's baseline offset does not
        /// depend on sub/super/an author-declared offset at all — CSS 2.1 §10.8.1 defines those as relative
        /// to the box's <i>own</i> baseline, which for a content-bearing box is itself top-anchored), so
        /// they are folded into <see cref="RectangleGrowthAnchor.Top"/> too.
        /// </para>
        /// <para>
        /// An empty box, or one whose <c>overflow</c> isn't <c>visible</c>, has no baseline of its own —
        /// <see cref="AtomicInlineBaselineOf"/> falls back to its bottom margin edge, and that fallback is
        /// read from inside <see cref="ApplyVerticalAlignment"/>'s own baseline-extent fold, using the
        /// box's still-natural (pre-growth) rectangle, before this method ever runs. An earlier,
        /// since-reverted attempt at growing this specific case from its (algebraically bottom-anchored)
        /// post-alignment position produced a measured regression — an empty bordered box's emitted
        /// fragment landed at <c>Y = -20</c> instead of <c>20</c> in what is now
        /// <c>TheEmittedFragmentCarriesTheDeclaredHeight</c> — so this case is classified
        /// <see cref="RectangleGrowthAnchor.Circular"/> and left growing downward, unchanged, matching this
        /// box shape's pre-#1169 behavior exactly (<see href="https://github.com/jhaygood86/PeachPDF/issues/1169">
        /// #1169</see>).
        /// </para>
        /// </remarks>
        private static RectangleGrowthAnchor AnchorOf(CssBox box, CssLineBox line)
        {
            var effectiveVerticalAlign = EffectiveVerticalAlignOf(box, line, out _);

            if (effectiveVerticalAlign.Value.IsValue)
            {
                // A length/percentage offset - reaches this method only when the box has real content
                // (see remarks), so it is top-anchored the same as default baseline-with-content.
                return RectangleGrowthAnchor.Top;
            }

            switch (effectiveVerticalAlign.Value.Keyword)
            {
                case VerticalAlignment.Bottom:
                case VerticalAlignment.TextBottom:
                    return RectangleGrowthAnchor.Bottom;
                case VerticalAlignment.Middle:
                    return RectangleGrowthAnchor.Middle;
                case VerticalAlignment.Top:
                case VerticalAlignment.TextTop:
                case VerticalAlignment.Sub:
                case VerticalAlignment.Super:
                    return RectangleGrowthAnchor.Top;
                default:
                    // Default baseline (and the deprecated PeachBaselineMiddle sentinel, which has no
                    // distinct inline-layout effect - see its own Keywords doc comment).
                    return box.Overflow.Value != Overflow.Visible || !HasWordOnLine(box, line)
                        ? RectangleGrowthAnchor.Circular
                        : RectangleGrowthAnchor.Top;
            }
        }

        /// <summary>
        /// Grows <paramref name="rect"/> to <paramref name="declaredHeight"/> from
        /// <paramref name="anchor"/>, holding that edge (or, for <see cref="RectangleGrowthAnchor.Middle"/>,
        /// the midpoint) fixed. <see cref="RectangleGrowthAnchor.Circular"/> grows downward, matching
        /// <see cref="RectangleGrowthAnchor.Top"/> — see <see cref="AnchorOf"/>'s own remarks for why.
        /// </summary>
        private static RRect GrowAtomicInlineRectangle(RRect rect, double declaredHeight, RectangleGrowthAnchor anchor)
        {
            var newTop = anchor switch
            {
                RectangleGrowthAnchor.Bottom => rect.Bottom - declaredHeight,
                RectangleGrowthAnchor.Middle => rect.Top - (declaredHeight - rect.Height) / 2,
                _ => rect.Y
            };

            return new RRect(rect.X, newTop, rect.Width, declaredHeight);
        }

        /// <summary>
        /// The block-axis counterpart of <see cref="WidenAtomicInlineRectangles"/>: grows each atomic
        /// inline-level box on <paramref name="line"/> from the rectangle its own content just bubbled up
        /// to its declared/used height
        /// (<see href="https://www.w3.org/TR/CSS22/visudet.html#normal-block">CSS 2.1 §10.6.3</see>), when
        /// that is taller than what the content produced
        /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/1101">#1101</see>), from whichever
        /// edge <see cref="AnchorOf"/> says <see cref="ApplyVerticalAlignment"/> already anchored
        /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/1169">#1169</see>).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Unlike <see cref="WidenAtomicInlineRectangles"/>, this runs <i>after</i>
        /// <see cref="ApplyVerticalAlignment"/> rather than between it and <see cref="BubbleRectangles"/>.
        /// <see cref="ApplyVerticalAlignment"/> folds every baseline-aligned atomic inline's <i>whole
        /// margin box</i> — not just its own baseline point — into the line's shared baseline extent (CSS
        /// 2.1 §10.8.1), which is exactly how a declared height is supposed to size the <i>line</i> too;
        /// that is handled separately, by <see cref="FinalizeFlowBoxExit"/>'s own <c>MaxBottom</c>
        /// extension (<see href="https://github.com/jhaygood86/PeachPDF/issues/1166">#1166</see>), which
        /// runs earlier, during the flow itself, rather than by feeding a grown rectangle back into this
        /// fold. Growing the rectangle before the fold would feed it a height the rest of the line was
        /// never sized for, moving every other box on the line along with it. Running after alignment
        /// instead means the box's own final, already-decided position is what gets grown — invisible to
        /// <see cref="ApplyVerticalAlignment"/> itself. A table cell's own auto-height is a different,
        /// later mechanism (<c>CssBox.GetMaximumBottom</c>) that reads a box's <c>Rectangles</c> directly,
        /// so a declared-height box inside a cell does grow that cell (and its row) — consistent with CSS
        /// 2.1 §17.5.3 sizing a cell to its content, not a special case this method adds.
        /// </para>
        /// <para>
        /// Never shrinks a rectangle already taller than the declared height — the "declared height
        /// smaller than content" case this deliberately leaves alone, matching
        /// <see cref="WidenAtomicInlineRectangles"/>'s own equivalent choice not to pull an overflowing
        /// rectangle back to a narrower declared width.
        /// </para>
        /// </remarks>
        private static void HeightenAtomicInlineRectangles(CssLineBox line)
        {
            List<(CssBox Box, double Height, RectangleGrowthAnchor Anchor)>? heightening = null;

            foreach (var (box, rect) in line.Rectangles)
            {
                if (!IsWhollyHostedOnLine(box, line)) continue;

                if (ResolveAtomicInlineDeclaredHeight(box) is not { } declaredHeight) continue;
                if (declaredHeight <= rect.Height) continue;

                (heightening ??= []).Add((box, declaredHeight, AnchorOf(box, line)));
            }

            if (heightening is null) return;

            foreach (var (box, declaredHeight, anchor) in heightening)
            {
                line.Rectangles[box] = GrowAtomicInlineRectangle(line.Rectangles[box], declaredHeight, anchor);
            }
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
                    x = Math.Min(x, word.Left);
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
        /// whether the flow reached the end of the owning block's content, which is what
        /// <see cref="EndsAParagraph"/> needs — see <see cref="FinalizeLineBoxes"/>, whose parameter this is.
        /// </param>
        private static void ApplyHorizontalAlignment(CssLineBox lineBox, bool blockFinished)
        {
            // A display:block image/SVG's synthetic single-word wrapper (IsReplacedBlockWrapper) is not
            // a real inline formatting context an author can see or target - it exists solely so this
            // engine can size the replaced element as an atomic inline "word" (see the flag's own
            // remarks). css-text-3 §6.1 scopes text-align (and, by the same reasoning, text-align-last)
            // to a block's inline-level content only, which this wrapper's one "word" semantically is
            // not - exactly the reasoning LineBoxContributionOf already relies on to exempt it from the
            // CSS 2.1 §10.8 strut. The wrapped element's own horizontal position is already fully settled
            // by GetActualMarginLeft/GetActualMarginRight's CSS 2.1 §10.3.3/§10.3.4 auto-margin
            // resolution at flow time, so returning here - rather than resolving ActualTextAlignAll, whose
            // case HorizontalAlignment.Left below is already a no-op - leaves that position untouched
            // instead of re-centering/re-flushing it a second, spec-unsupported way (issue #1176).
            if (lineBox.OwnerBox.IsReplacedBlockWrapper) return;

            // text-align-all's initial/logical values, start/end (css-text-3 §6.1), resolve against the
            // owning box's own direction - the CSS-OM-visible value (box.TextAlignAll) stays exactly as
            // authored/defaulted; only this *used*-value resolution is direction-aware. ActualTextAlignAll
            // (DerivedStyle) has already fully resolved match-parent by this point - it can never appear
            // here, so ResolveLogicalAlignment only ever sees Start/End/Left/Right/Center/Justify.
            var isRtl = lineBox.OwnerBox.Direction.Value == DirectionMode.Rtl;
            var towardStart = isRtl ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            var towardEnd = isRtl ? HorizontalAlignment.Left : HorizontalAlignment.Right;

            var declared = ResolveLogicalAlignment(lineBox.OwnerBox.ActualTextAlignAll, towardStart, towardEnd);

            var (textAlign, opportunities) = ResolveUsedAlignment(lineBox,
                EndsAParagraph(lineBox, blockFinished), declared, towardStart, towardEnd);

            switch (textAlign)
            {
                case HorizontalAlignment.Right:
                    ApplyRightAlignment(lineBox);
                    break;
                case HorizontalAlignment.Center:
                    ApplyCenterAlignment(lineBox);
                    break;
                case HorizontalAlignment.Justify:
                    ApplyJustifyAlignment(lineBox, opportunities);
                    break;
            }
        }

        /// <summary>
        /// Resolves <c>text-align</c>'s logical <c>start</c>/<c>end</c> keywords (css-text-3 §6.1) to the
        /// physical pair the caller supplies - which is the box's own <c>direction</c> for horizontal flow
        /// and the column's inline-start edge for vertical flow, hence the parameters rather than a bool.
        /// Every other value is already physical and passes through.
        /// </summary>
        private static HorizontalAlignment ResolveLogicalAlignment(HorizontalAlignment value,
            HorizontalAlignment towardStart, HorizontalAlignment towardEnd) => value switch
            {
                HorizontalAlignment.Start => towardStart,
                HorizontalAlignment.End => towardEnd,
                var other => other
            };

        /// <summary>
        /// Whether <paramref name="lineBox"/> is a line that ends a paragraph, which
        /// <see href="https://www.w3.org/TR/css-text-3/#text-align-property">css-text-3 §6.1</see> aligns
        /// per <c>text-align-last</c> instead of per <c>text-align</c>: the block's own last line, and the
        /// last line before a forced line break.
        /// </summary>
        /// <remarks>
        /// The block only <i>has</i> a last line once its flow actually finished - a pass that stopped at a
        /// fragmentation break leaves the line it stopped on at the end of the list without it being the end
        /// of the block, which is what <paramref name="blockFinished"/> distinguishes. The forced-break half
        /// needs no such care: <see cref="CssLineBox.PrecedesForcedBreak"/> is stated by the flow when the
        /// break closes the line, so it is already correct on a line finalized by an earlier pass.
        /// </remarks>
        private static bool EndsAParagraph(CssLineBox lineBox, bool blockFinished) =>
            lineBox.PrecedesForcedBreak
            || (blockFinished && lineBox.Equals(lineBox.OwnerBox.LineBoxes[^1]));

        /// <summary>
        /// The alignment <paramref name="lineBox"/> is actually laid out with, and - when that is
        /// <see cref="HorizontalAlignment.Justify"/> - how many
        /// <see cref="IsJustificationOpportunity">justification opportunities</see> it has to spread the
        /// leftover over. Shared by the horizontal and vertical dispatchers, which differ only in the
        /// physical pair <paramref name="towardStart"/>/<paramref name="towardEnd"/> and in how
        /// <paramref name="endsAParagraph"/> is decided.
        /// </summary>
        /// <remarks>
        /// Two css-text-3 rules meet here. §6.1/§6.3: a line that ends a paragraph is aligned by
        /// <c>text-align-last</c>, not by <c>text-align</c>.
        /// <see href="https://www.w3.org/TR/css-text-3/#justify-algos">§6.4.3</see>: a line whose contents
        /// "cannot be stretched to the full width of the line box" - here, a justified line with no
        /// justification opportunity at all - "must be aligned as specified by the
        /// <c>text-align-last</c> property", so it too is handed to <c>text-align-last</c>.
        /// <para>
        /// §6.4.3 continues "(If <c>text-align-last</c> is <c>justify</c>, then they must be aligned as
        /// for <c>center</c>.)" This centres that case, per the parenthetical, even though Chromium,
        /// Gecko and WebKit all start-align it instead - none of the three implements the parenthetical.
        /// This is a deliberate spec-literal divergence from every browser engine.
        /// </para>
        /// </remarks>
        private static (HorizontalAlignment Alignment, int Opportunities) ResolveUsedAlignment(
            CssLineBox lineBox, bool endsAParagraph, HorizontalAlignment textAlign,
            HorizontalAlignment towardStart, HorizontalAlignment towardEnd)
        {
            var used = endsAParagraph
                ? ResolveLastLineAlignment(lineBox.OwnerBox, textAlign, towardStart, towardEnd)
                : textAlign;

            // The opportunity walk is O(words), so it runs once and only for a line being justified.
            if (used != HorizontalAlignment.Justify) return (used, 0);

            var opportunities = CountJustificationOpportunities(lineBox);
            if (opportunities > 0) return (used, opportunities);

            // Center, not towardStart: §6.4.3's parenthetical says a text-align-last: justify line with
            // no justification opportunity is centred, not start-aligned - see the remarks above.
            var fallback = ResolveLastLineAlignment(lineBox.OwnerBox, textAlign, towardStart, towardEnd);
            return (fallback == HorizontalAlignment.Justify ? HorizontalAlignment.Center : fallback, 0);
        }

        /// <summary>
        /// The used alignment of a line <see cref="EndsAParagraph">ending a paragraph</see>, per
        /// <see href="https://www.w3.org/TR/css-text-3/#text-align-last-property">css-text-3 §6.3</see>'s
        /// <c>text-align-last</c>. Its initial <c>auto</c> defers to <paramref name="textAlign"/> - the
        /// already-logical-resolved <c>text-align</c> - except under <c>justify</c>, where §6.3 makes it
        /// start instead, which is why an ordinary justified paragraph's last line is ragged.
        /// </summary>
        /// <remarks>
        /// Reads <see cref="CssBox.ActualTextAlignLast"/> (<see cref="DerivedStyle.ActualTextAlignLast"/>),
        /// not the raw <c>TextAlignLast</c> longhand value - <c>match-parent</c> (set via the <c>text-align</c>
        /// shorthand or directly) is already fully resolved by that point, so it can never reach this
        /// switch; a parent that never declared its own <c>text-align-last</c> naturally falls through to
        /// the <c>Auto</c> arm below via the same recursive resolution.
        /// </remarks>
        private static HorizontalAlignment ResolveLastLineAlignment(CssBox blockBox, HorizontalAlignment textAlign,
            HorizontalAlignment towardStart, HorizontalAlignment towardEnd) =>
            blockBox.ActualTextAlignLast switch
            {
                TextAlignLast.Start => towardStart,
                TextAlignLast.End => towardEnd,
                TextAlignLast.Left => HorizontalAlignment.Left,
                TextAlignLast.Right => HorizontalAlignment.Right,
                TextAlignLast.Center => HorizontalAlignment.Center,
                TextAlignLast.Justify => HorizontalAlignment.Justify,
                // auto
                _ => textAlign == HorizontalAlignment.Justify ? towardStart : textAlign
            };

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

            var runs = Bidi.ReorderLine(levels, 0, levels.Length);

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
                // characters where they have a mirror image), matching how Bidi's own
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
            MirrorWordTextIfNeeded(word, level, mirror);
            word.Left = slotLeft;
        }

        /// <summary>
        /// UAX#9 L4 mirroring's pure-text half, shared between <see cref="PlaceBidiRunWord"/> (horizontal,
        /// writes <see cref="CssRect.Left"/>) and <see cref="PlaceVerticalBidiRunWord"/> (vertical, writes
        /// <see cref="CssRect.Top"/>) - the one piece of bidi run placement that is genuinely axis-independent.
        /// </summary>
        private static void MirrorWordTextIfNeeded(CssRect word, byte level, bool mirror)
        {
            if (!mirror || word is not CssRectWord { IsSpaces: false, IsLineBreak: false } rectWord)
                return;

            if (rectWord.EffectiveJoiningForms is not null)
            {
                // An Arabic-family joining word never mutates its own text - GSUB needs it in true
                // logical order to match real fonts' contextual rlig rules (e.g. lam-alef), which
                // wouldn't match the reversed/mirrored order every other RTL word gets here. Only record
                // that it displays right-to-left; CssRectWord.DisplayOrderReversed's own remarks cover
                // how that reaches shaping instead.
                rectWord.MarkDisplayOrderReversed();
                return;
            }

            // Mirrors from the word's own stable pre-mirror text, not its current (possibly already
            // mirrored) Text - mirroring is an involution, so a box tree laid out more than once
            // against the same word objects (HtmlContainerInt's variable-page-width reflow re-runs
            // LayoutDocument, re-deriving line boxes and re-applying this on every pass) would
            // otherwise toggle back to unmirrored on every second application.
            rectWord.ReplaceText(Bidi.Mirror(rectWord.PreMirrorText, level));
        }

        /// <summary>
        /// The vertical counterpart of <see cref="ApplyHorizontalAlignment"/>: resolves the used value of
        /// <c>text-align</c> and repositions <paramref name="lineBox"/>'s words along the inline axis
        /// (physical Y for a vertical box) accordingly.
        /// </summary>
        /// <remarks>
        /// Unlike horizontal flow - where natural (pre-alignment) placement is always flush to the same
        /// physical edge regardless of <c>direction</c> - a vertical box's own natural placement already
        /// flushes to physical-bottom under <c>direction: rtl</c> (baked into
        /// <see cref="WritingModeFrame.ToPhysical(RRect)"/>'s own <see cref="WritingModeFrame.InlineStartIsBottom"/>
        /// branch). So <c>left</c> (CSS Writing Modes' line-left/line-right: direction-independent, always
        /// physical-top here) needs an active case - it is only a no-op under LTR, unlike horizontal's
        /// <c>Left</c>, which is always a no-op.
        /// </remarks>
        private static void ApplyVerticalTextAlignment(CssLineBox lineBox, WritingModeFrame finalFrame,
            bool isLastColumn, double clientTop, double clientBottom)
        {
            // A vertical column's inline-start edge is physical bottom under direction:rtl, so that is the
            // pair start/end - and text-align-last's own start/end - resolve against here.
            var towardStart = finalFrame.InlineStartIsBottom ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            var towardEnd = finalFrame.InlineStartIsBottom ? HorizontalAlignment.Left : HorizontalAlignment.Right;

            // See ApplyHorizontalAlignment's own IsReplacedBlockWrapper remarks - the same css-text-3
            // §6.1 reasoning exempts this synthetic wrapper's column from text-align/text-align-last
            // here too. Unlike horizontal, this axis's natural (pre-alignment) placement is not always
            // already flush to the column's own inline-start edge (see this method's own remarks above),
            // so the exemption still has to actively flush to towardStart - it just must never read
            // ActualTextAlignAll/ActualTextAlignLast to decide which edge that is (issue #1176).
            if (lineBox.OwnerBox.IsReplacedBlockWrapper)
            {
                // towardStart == Right exactly when InlineStartIsBottom (see its own derivation just
                // above) - read that directly rather than through the just-derived HorizontalAlignment,
                // so this stays correct by construction if a future writing-mode case changes how
                // towardStart itself is derived.
                ApplyVerticalFlushAlignment(lineBox, toBottom: finalFrame.InlineStartIsBottom,
                    clientTop, clientBottom);
                return;
            }

            var declared = ResolveLogicalAlignment(lineBox.OwnerBox.ActualTextAlignAll, towardStart, towardEnd);

            // isLastColumn rather than EndsAParagraph's blockFinished-gated check: CreateVerticalLineBoxes is
            // always a single monolithic pass with no fragmentation break to leave an ambiguous "last" column
            // behind, so the last entry in the list genuinely is the block's last column.
            var (textAlign, opportunities) = ResolveUsedAlignment(lineBox,
                isLastColumn || lineBox.PrecedesForcedBreak, declared, towardStart, towardEnd);

            switch (textAlign)
            {
                case HorizontalAlignment.Left:
                    ApplyVerticalFlushAlignment(lineBox, toBottom: false, clientTop, clientBottom);
                    break;
                case HorizontalAlignment.Right:
                    ApplyVerticalFlushAlignment(lineBox, toBottom: true, clientTop, clientBottom);
                    break;
                case HorizontalAlignment.Center:
                    ApplyVerticalCenterAlignment(lineBox, clientTop, clientBottom);
                    break;
                case HorizontalAlignment.Justify:
                    ApplyVerticalJustifyAlignment(lineBox, finalFrame, opportunities, clientTop, clientBottom);
                    break;
            }
        }

        /// <summary>
        /// <c>text-align: left</c>/<c>right</c> (and the direction-resolved <c>start</c>/<c>end</c>) for a
        /// vertical box: flushes every word in the column to the box's own physical top
        /// (<paramref name="toBottom"/> false) or physical bottom (true) edge. Scans <see cref="CssLineBox.Words"/>
        /// directly for the column's own content extent rather than reusing horizontal's "read the last
        /// word" shortcut - document order is not physical order for a vertical+RTL column (see
        /// <see cref="ApplyVerticalBidiReordering"/>'s own remarks). Takes the box's own inline-axis edges
        /// as parameters rather than reading <c>lineBox.OwnerBox.ClientTop</c>/<c>ClientBottom</c> live: for
        /// a box with a definite (non-auto) height, <c>ActualBottom</c> - and so <c>ClientBottom</c> - is
        /// not actually settled until <c>ApplyHeight</c> runs in the layout epilogue, well after this
        /// method's caller (<see cref="CreateVerticalLineBoxes"/>) returns.
        /// </summary>
        private static void ApplyVerticalFlushAlignment(CssLineBox lineBox, bool toBottom, double clientTop, double clientBottom)
        {
            if (lineBox.Words.Count == 0) return;

            var (contentTop, contentBottom) = GetColumnContentExtent(lineBox);
            var targetTop = toBottom ? clientBottom - (contentBottom - contentTop) : clientTop;
            var diff = targetTop - contentTop;

            // targetTop - contentTop is, by construction, exactly the shift needed to flush the column's
            // content to the requested edge - there is no direction in which a nonzero diff should be
            // discarded. The previous one-directional guard (diff > 0 for toBottom, diff < 0 otherwise)
            // assumed natural (pre-alignment) placement is never further from the target edge than "the
            // wrong way", which holds for a definite-height box or LTR vertical content (whose natural
            // placement never depends on clientBottom), but not for an auto-height RTL vertical box: its
            // natural placement is anchored during word layout against a placeholder bottom edge far below
            // the real one (see CreateVerticalLineBoxes's own heightIsAuto wrapLimit fallback), so both
            // toBottom:true and toBottom:false need a same-direction (very negative) correction once the
            // real, much closer final bottom is known - toBottom:false's guard happened to allow this by
            // coincidence of sign; toBottom:true's did not, leaving every word positioned far outside the
            // box's own real bottom edge (issue #797). Only a genuinely near-zero diff (content already
            // flush) is worth skipping, matching ApplyVerticalCenterAlignment's own guard shape.
            if (!(Math.Abs(diff) > 0)) return;

            foreach (var word in lineBox.Words)
            {
                word.Top += diff;
            }
        }

        /// <summary>
        /// <c>text-align: center</c> for a vertical box: centers the column's own content between the
        /// box's physical top and bottom edges. See <see cref="ApplyVerticalFlushAlignment"/>'s own remarks
        /// for why the edges are passed in rather than read live off <c>lineBox.OwnerBox</c>.
        /// </summary>
        private static void ApplyVerticalCenterAlignment(CssLineBox lineBox, double clientTop, double clientBottom)
        {
            if (lineBox.Words.Count == 0) return;

            var (contentTop, contentBottom) = GetColumnContentExtent(lineBox);
            var availableExtent = clientBottom - clientTop;
            var contentExtent = contentBottom - contentTop;
            var diff = clientTop + (availableExtent - contentExtent) / 2 - contentTop;

            if (!(Math.Abs(diff) > 0)) return;

            foreach (var word in lineBox.Words)
            {
                word.Top += diff;
            }
        }

        /// <summary>
        /// A column's own content extent along the inline axis (physical top/bottom) - shared by
        /// <see cref="ApplyVerticalFlushAlignment"/> and <see cref="ApplyVerticalCenterAlignment"/>. Scans
        /// <see cref="CssLineBox.Words"/> directly rather than reading the first/last word by document
        /// order - document order is not physical order for a vertical+RTL column (see
        /// <see cref="ApplyVerticalBidiReordering"/>'s own remarks).
        /// </summary>
        private static (double Top, double Bottom) GetColumnContentExtent(CssLineBox lineBox)
        {
            double contentTop = double.MaxValue, contentBottom = double.MinValue;
            foreach (var word in lineBox.Words)
            {
                contentTop = Math.Min(contentTop, word.Top);
                contentBottom = Math.Max(contentBottom, word.Bottom);
            }

            return (contentTop, contentBottom);
        }

        /// <summary>
        /// <c>text-align: justify</c> for a vertical box: the inline-axis counterpart of
        /// <see cref="ApplyJustifyAlignment"/>, distributing the column's leftover extent over its
        /// <see cref="IsJustificationOpportunity">justification opportunities</see> only. Which columns
        /// reach here is <see cref="ApplyVerticalTextAlignment"/>'s decision - a column ending a paragraph
        /// (the block's last, or one a forced break closed) and one with nothing to expand at have both
        /// already been routed to <c>text-align-last</c> instead, exactly as in
        /// <see cref="ApplyHorizontalAlignment"/>.
        /// <c>text-indent</c> is not implemented for vertical content at all yet, so no indent is applied
        /// here (compare <see cref="ApplyJustifyAlignment"/>'s own <c>GetLineTextIndent</c> call). See
        /// <see cref="ApplyVerticalFlushAlignment"/>'s own remarks for why the edges are parameters.
        /// </summary>
        /// <remarks>
        /// Anchors the column at its own inline-start edge before distributing, which horizontal has no
        /// counterpart to: an auto-height vertical box lays its words out against a placeholder far
        /// edge and only learns the real one afterwards (see <see cref="ApplyVerticalFlushAlignment"/>'s
        /// own remarks, issue #797), so - unlike a horizontal line, which the flow always leaves flush
        /// against the edge it started from - a pure progressive shift would justify the column in the
        /// wrong place entirely.
        /// </remarks>
        private static void ApplyVerticalJustifyAlignment(CssLineBox lineBox, WritingModeFrame finalFrame,
            int opportunities, double clientTop, double clientBottom)
        {
            var (contentTop, contentBottom) = GetColumnContentExtent(lineBox);
            var leftover = clientBottom - clientTop - (contentBottom - contentTop);

            // §6.1: an overflowing column is start-aligned and spills past the end edge.
            if (leftover <= 0) return;

            // Inline-start is physical bottom under direction: rtl, and the walk runs toward physical
            // top from there - so both the anchor and the distributed share change sign together.
            var inlineStartIsBottom = finalFrame.InlineStartIsBottom;
            var anchor = inlineStartIsBottom ? clientBottom - contentBottom : clientTop - contentTop;
            var towardInlineEnd = inlineStartIsBottom ? -1d : 1d;

            var seen = 0;

            for (var i = 0; i < lineBox.Words.Count; i++)
            {
                if (i > 0 && IsJustificationOpportunity(lineBox.Words[i - 1], lineBox.Words[i]))
                    seen++;

                lineBox.Words[i].Top += anchor + towardInlineEnd * leftover * seen / opportunities;
            }
        }

        /// <summary>
        /// The vertical counterpart of <see cref="ApplyBidiReordering"/>: UAX#9 L2 (visual reordering) + L4
        /// (mirroring) for a vertical box's column, reordering along the inline axis (physical Y) instead
        /// of physical X.
        /// </summary>
        /// <remarks>
        /// Not a simple <c>Left</c>&#8594;<c>Top</c> port: raw physical <see cref="CssRect.Top"/> is not
        /// monotonic in document order for vertical+<c>direction: rtl</c> content (natural placement there
        /// already flushes to physical-bottom - see <see cref="ApplyVerticalTextAlignment"/>'s own remarks),
        /// unlike SVG's <c>GlyphInfo.Py</c>, which <c>SvgRenderer.LayoutGlyphs</c> already computes
        /// monotonically along the pen's own advance direction. A naive axis swap here would corrupt the
        /// run-reflection math (the "boundary between this run and the next" / "content span" logic every
        /// run's reflection depends on being expressed in a coordinate that only ever increases along
        /// document order). Instead, every word's physical <c>Top</c> is normalized to a logical distance
        /// from the column's own inline-start edge before reordering, and converted back to physical
        /// <c>Top</c> once each word's new position is known - exactly the quantity the original
        /// placement loop's own <c>inlineOffset</c> held before <see cref="WritingModeFrame.ToPhysical(RRect)"/>
        /// converted it, so it is guaranteed monotonically non-decreasing in document order regardless of
        /// direction, both here and after <see cref="ApplyVerticalTextAlignment"/> has already run (flush/
        /// center only add one uniform shift to every word; justify rebuilds positions via a walk that is
        /// itself monotonic in document order by construction).
        /// </remarks>
        private static void ApplyVerticalBidiReordering(CssLineBox lineBox, WritingModeFrame finalFrame,
            double clientTop, double clientBottom)
        {
            if (lineBox.Words.Count == 0) return;

            var ownerBox = lineBox.OwnerBox;

            double ToLogicalOffset(CssRect w) => finalFrame.InlineStartIsBottom
                ? clientBottom - w.Bottom
                : w.Top - clientTop;

            var levels = new byte[lineBox.Words.Count];
            var slots = new double[lineBox.Words.Count];
            for (var i = 0; i < levels.Length; i++)
            {
                levels[i] = lineBox.Words[i].BidiLevel;
                slots[i] = ToLogicalOffset(lineBox.Words[i]);
            }

            // L1 clause 4: a trailing run of whitespace/line-break words at the very end of the column
            // resets to the paragraph's base level - mirrors ApplyBidiReordering's own identical clause.
            var baseLevel = ownerBox.Direction.Value == DirectionMode.Rtl ? (byte)1 : (byte)0;
            var j = levels.Length - 1;
            while (j >= 0 && lineBox.Words[j].IsSpaces)
            {
                levels[j] = baseLevel;
                j--;
            }

            var runs = Bidi.ReorderLine(levels, 0, levels.Length);

            if (runs.Count == 1 && !runs[0].IsRtl) return;

            // The line's last word has no CssRect.FullHeight (there is no vertical counterpart of
            // FullWidth) - ActualWordSpacing is added explicitly so the fallback boundary agrees with
            // ApplyBidiReordering's own use of FullWidth here, which a plain `.Height` silently drops.
            double SlotBoundaryAfter(int index) =>
                index + 1 < lineBox.Words.Count
                    ? slots[index + 1]
                    : slots[index] + lineBox.Words[index].Height + lineBox.Words[index].ActualWordSpacing;

            var runNewStart = slots[0];

            foreach (var run in runs)
            {
                var runOldStart = slots[run.Start];
                var lastIndexInRun = run.Start + run.Length - 1;

                var runContentExtent = slots[lastIndexInRun] + lineBox.Words[lastIndexInRun].Height - runOldStart;
                var trailingGap = SlotBoundaryAfter(lastIndexInRun) - (runOldStart + runContentExtent);

                for (var k = 0; k < run.Length; k++)
                {
                    var idx = run.Start + k;
                    var word = lineBox.Words[idx];
                    var offsetFromRunStart = slots[idx] - runOldStart;

                    var newLogicalOffset = run.IsRtl
                        ? runNewStart + runContentExtent - (offsetFromRunStart + word.Height)
                        : runNewStart + offsetFromRunStart;

                    var newTop = finalFrame.InlineStartIsBottom
                        ? clientBottom - newLogicalOffset - word.Height
                        : clientTop + newLogicalOffset;

                    PlaceVerticalBidiRunWord(word, newTop, run.Level, mirror: run.IsRtl);
                }

                runNewStart += runContentExtent + trailingGap;
            }
        }

        private static void PlaceVerticalBidiRunWord(CssRect word, double newTop, byte level, bool mirror)
        {
            MirrorWordTextIfNeeded(word, level, mirror);
            word.Top = newTop;
        }

        /// <summary>
        /// The initial <c>vertical-align</c>, used where a box's own declared value must not be read as an
        /// inline alignment — see <see cref="ApplyVerticalAlignment"/>'s table-cell case.
        /// </summary>
        private static readonly CssProperty<CssKeywordOrValue<VerticalAlignment, LengthOrCalc>> BaselineVerticalAlign =
            CssProperty<CssKeywordOrValue<VerticalAlignment, LengthOrCalc>>.FromValue(
                Keywords.Baseline, new CssKeywordOrValue<VerticalAlignment, LengthOrCalc>(VerticalAlignment.Baseline, null));

        private static CssProperty<CssKeywordOrValue<VerticalAlignment, LengthOrCalc>> EffectiveVerticalAlignOf(
            CssBox box, CssLineBox lineBox, out CssBox styledBox)
        {
            styledBox = box;
            while (styledBox.HtmlTag is null && !styledBox.IsMarkerPseudoElement && styledBox.ParentBox is not null)
                styledBox = styledBox.ParentBox;

            var ownerBox = lineBox.OwnerBox;
            if (ReferenceEquals(lineBox, ownerBox.LineBoxes.FirstOrDefault())
                && ownerBox.ResolvedFirstLineStyle is { } firstLineStyle
                && firstLineStyle.VerticalAlign != ownerBox.VerticalAlign)
            {
                return firstLineStyle.VerticalAlign;
            }

            return styledBox.DerivedStyle.ActualDisplay == Keywords.TableCell
                ? BaselineVerticalAlign
                : styledBox.VerticalAlign;
        }

        /// <summary>
        /// Applies vertical alignment to the linebox
        /// </summary>
        /// <param name="lineBox"></param>
        private static void ApplyVerticalAlignment(CssLineBox lineBox)
        {
            // Where the flow left this line's content: every word on it was placed at the line's own
            // top, so this is the line box's top edge (less any border/padding UpdateRectangle folded
            // into a rectangle). Every case below is expressed as an offset from it, which is what lets
            // a line the flow gave no baseline to - an empty one, or one from the vertical-writing-mode
            // engine - fall through unchanged.
            var flowTop = double.MinValue;

            foreach (var box in lineBox.Rectangles.Keys)
            {
                flowTop = Math.Max(flowTop, lineBox.Rectangles[box].Top);
            }

            // An atomic inline contributes its whole margin box around its baseline (CSS 2.1
            // §10.8.1), not just the font metrics its phantom word inherited. A replaced element's
            // baseline is its bottom margin edge. A text-bearing inline-block instead uses the
            // baseline of its last in-flow line box while overflow is visible. FlowBox lays
            // the inline-block's words down inside its top border and padding, but those words also feed
            // the surrounding line's ordinary font extent. If a plain text sibling follows, its earlier
            // FlowTop wins and the baseline below used to ignore the atomic box's top inset; alignment
            // then pulled the inline-block upward by exactly border-top + padding-top, into the preceding
            // block. Fold the atomic margin-box distances into the shared extent before resolving that
            // baseline, so surrounding text moves down to the inline-block's internal baseline instead.
            var baselineExtent = lineBox.BaselineAlignedExtent ?? lineBox.BaselineExtent;
            var baselineOrigin = lineBox.FlowTop ?? flowTop;

            foreach (var (box, rect) in lineBox.Rectangles)
            {
                if (EffectiveVerticalAlignOf(box, lineBox, out _).Value.Keyword != VerticalAlignment.Baseline)
                {
                    continue;
                }

                if (AtomicInlineBaselineOf(box, lineBox, rect) is not { } atomicBaseline) continue;

                var marginTop = rect.Top - box.ActualMarginTop;
                var marginBottom = rect.Bottom + box.ActualMarginBottom;

                var atomicExtent = new LineBoxExtent(
                    atomicBaseline - marginTop,
                    marginBottom - atomicBaseline);

                baselineExtent = baselineExtent is { } held ? held.Union(atomicExtent) : atomicExtent;

                // Replaced words are initially placed at FlowTop without their vertical margins. Their
                // top margin therefore belongs below that origin and alignment moves the border box down
                // by it. Inline-block rectangles already include their flow-relative vertical position,
                // so retain the older margin-edge origin for those.
                if (!box.IsImage)
                    baselineOrigin = Math.Min(baselineOrigin, marginTop);
            }

            if (baselineExtent is { } alignedExtent)
            {
                lineBox.BaselineAlignedExtent = alignedExtent;
                baselineExtent = IncludeEdgeAlignedAtomicHeights(
                    alignedExtent, lineBox.TopAlignedAtomicHeight, lineBox.BottomAlignedAtomicHeight);
            }

            lineBox.BaselineExtent = baselineExtent;

            // CSS 2.1 §10.8.1: the line box's baseline sits AboveBaseline below its top, and every
            // inline box on it hangs its own content area from that one baseline - so a box whose font
            // is smaller than the line's tallest moves DOWN to meet it, rather than staying flush with
            // the line's top as it did while this engine had no baseline of its own. Corrected below by
            // the same floor the boxes themselves get, so this names the baseline they actually sit on.
            lineBox.BaselineY = baselineExtent is { } lineExtent
                ? baselineOrigin + lineExtent.AboveBaseline
                : null;

            // How far a box has to move from where the flow left it to sit on this line's baseline.
            // Zero for anything the baseline does not govern: a line with no baseline at all, or a box
            // that has neither text nor atomic replaced content on this line.
            //
            // Expressed as a shift of the whole box - rectangle and words together, via
            // OffsetBoxWithinLine - rather than as an absolute top for its words alone. An inline box's
            // rectangle IS its content area plus its border and padding (CssLineBox.UpdateRectangle), so
            // moving the words out from under it detaches a padded inline-block's label from its own
            // padding box, which is exactly what happened when this set word tops directly.
            // The box's own first word, wherever in its subtree it sits: an inline box that holds no text
            // directly (a <span> around an anonymous text box, an inline-block around its label) still has
            // to move with the content it wraps, or its background and border part company with the words
            // inside it. Null means the baseline does not govern this box at all.
            double? BaselineShiftOf(CssBox box)
            {
                if (lineBox.BaselineY is not { } baselineY) return null;

                // An atomic inline is aligned by its own box (AtomicInlineBaselineOf), and it is atomic,
                // so everything within it moves by ITS delta: the walk starts at the box itself and
                // continues through its ancestors, and a descendant on this line takes the enclosing
                // box's shift rather than deriving one of its own from its own font metrics. Letting a
                // descendant derive its own put an `overflow: hidden` inline-block's clip and the text
                // inside it in different places, and the box then painted completely empty - its words
                // were clipped away (Chrome keeps them, and so does the baseline_alignment showcase).
                for (var atomic = box; atomic is not null; atomic = atomic.ParentBox)
                {
                    // The box establishing this line's formatting context bounds the walk: it takes no
                    // place on its own line, and nothing above it is on this line at all.
                    if (ReferenceEquals(atomic, lineBox.OwnerBox)) break;

                    if (atomic.DerivedStyle.ActualDisplay != Keywords.InlineBlock) continue;
                    if (!lineBox.Rectangles.TryGetValue(atomic, out var atomicRect)) continue;
                    if (AtomicInlineBaselineOf(atomic, lineBox, atomicRect) is not { } atomicBaseline) continue;

                    return baselineY - atomicBaseline;
                }

                if (FirstNonReplacedWordOf(box, lineBox) is { } word)
                    return baselineY - (word.FirstLineStyle ?? word.OwnerBox).ActualFont.Ascent - word.Top;

                // Replaced content aligns its bottom margin edge with the shared baseline. Use the
                // replaced word's owner even while visiting an inline ancestor around it: both the
                // descendant's word/rectangle and the ancestor's bubbled decoration rectangle then
                // receive the same delta, so a wrapping span's background stays attached.
                if (FirstReplacedWordOf(box, lineBox) is { } replaced
                    && lineBox.Rectangles.TryGetValue(replaced.OwnerBox, out var replacedRect))
                {
                    return baselineY - (replacedRect.Bottom + replaced.OwnerBox.ActualMarginBottom);
                }

                return null;
            }

            // A ::first-line rule's vertical-align (if it sets one) applies to everything on the
            // target's first formatted line, overriding each box's own value - ::first-line has no
            // synthesized box of its own to carry a per-box override, and unlike font/color/spacing
            // (resolved per-word in FlowBox/PaintWords), vertical-align is inherently a whole-line
            // concept already (this method itself runs once per line), so the simplest, most direct
            // application is a single line-wide override rather than per-word plumbing.
            //
            var boxes = new List<CssBox>(lineBox.Rectangles.Keys);

            // Snapshot the line's own top/bottom extents up front, from the original (pre-alignment)
            // rectangles - same convention this method already uses for "baseline" above, so every
            // case below aligns against the line's original geometry rather than a value some earlier
            // box in this loop already shifted.
            // Seeded from the line box itself, not only from its rectangles: since a word is placed on
            // its baseline, those rectangles sit half a leading below the line's top and end half a
            // leading above its bottom, so reading them alone would align `vertical-align: top` to the
            // topmost INK rather than to the line box §10.8.1 defines it against. The rectangles are
            // still folded in because top/bottom-aligned content can reach past the baseline extent,
            // and because replaced content grows the open line through MaxBottom before its margin-box
            // extent is folded into the closed line above.
            var lineTop = lineBox.FlowTop ?? double.MaxValue;
            var lineBottom = lineBox.FlowTop is { } lineBoxTop && lineBox.BaselineExtent is { } boxExtent
                ? lineBoxTop + boxExtent.Height
                : double.MinValue;

            foreach (var box in boxes)
            {
                var r = lineBox.Rectangles[box];
                lineTop = Math.Min(lineTop, r.Top);
                lineBottom = Math.Max(lineBottom, r.Bottom);
            }

            // Resolved for every box up front, for the same reason lineTop/lineBottom are: each delta is
            // measured from where the FLOW left that box's words, and the loop below moves them. Reading
            // it lazily let a box whose words had already been shifted by its own descendant's turn
            // measure against the shifted position and conclude it had nowhere to go - which detached a
            // padded inline-block's border box from the label inside it.
            var baselineDeltas = new Dictionary<CssBox, double>(boxes.Count);

            // A `line-height` shorter than the font makes the leading negative, and §10.8 then has the
            // content area overflow its line box on BOTH sides (CSS 2.1 §10.8.1). This used to be floored
            // at the line's own top - both here and in HalfLeadingOffsetOf's own flow-time placement -
            // because Fragmentation.FragmentEmitter decided which fragmentainer a word belonged to from
            // the word's own rectangle, and ink escaping above its line box escaped the fragmentainer the
            // line was placed in. The emitter now resolves a word's fragmentainer through the line that
            // owns it (FragmentEmitter.ClaimsLine, keyed by CssRect.Line) rather than the word's own
            // rectangle, so a line's content is claimed as one unit regardless of where its ink actually
            // sits - the floor's only reason to exist is gone, and baseline alignment is free to place
            // content above the line box exactly as a length/percentage vertical-align offset already
            // could.
            foreach (var box in boxes)
            {
                baselineDeltas[box] = BaselineShiftOf(box) ?? 0;
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
                // the value the author actually declared. A synthesized ::marker box stops the walk at
                // itself instead of continuing to its owning <li> - unlike an anonymous text-node
                // wrapper, it IS the thing vertical-align should apply to (CssBoxMarker sets its own
                // VerticalAlign for a list-style-position: inside shape marker, to center it in the
                // line the way an outside marker's own hand-computed position already does).
                var effectiveVerticalAlign = EffectiveVerticalAlignOf(box, lineBox, out var styledBoxForVerticalAlign);

                // A length/percentage (CSS 2.1 §10.8.1, issue #603) raises (positive) or lowers
                // (negative) the box by this distance from its own baseline - a percentage resolves
                // against the styled box's own line-height, mirroring sub/super's own baseline-relative
                // offset below rather than any of the line-extent-relative cases.
                var baselineDelta = baselineDeltas[box];
                var alignsMarginBox = styledBoxForVerticalAlign.IsImage;

                if (effectiveVerticalAlign.Value is { IsValue: true, Value: { } lengthOrCalc })
                {
                    var offset = CssValueParser.ParseLength(lengthOrCalc, styledBoxForVerticalAlign.ActualLineHeight, styledBoxForVerticalAlign);
                    OffsetBoxWithinLine(lineBox, box, baselineDelta - offset);
                    continue;
                }

                //Important notes on http://www.w3.org/TR/CSS21/tables.html#height-layout
                switch (effectiveVerticalAlign.Value.Keyword)
                {
                    case VerticalAlignment.Sub:
                        OffsetBoxWithinLine(lineBox, box, baselineDelta + rect.Height * .5f);
                        break;
                    case VerticalAlignment.Super:
                        OffsetBoxWithinLine(lineBox, box, baselineDelta - rect.Height * .2f);
                        break;
                    case VerticalAlignment.Top:
                        OffsetBoxWithinLine(lineBox, box,
                            lineTop + (alignsMarginBox ? styledBoxForVerticalAlign.ActualMarginTop : 0) - rect.Top);
                        break;
                    case VerticalAlignment.Bottom:
                        OffsetBoxWithinLine(lineBox, box,
                            lineBottom - (alignsMarginBox ? styledBoxForVerticalAlign.ActualMarginBottom : 0) - rect.Bottom);
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
                        // The parent's own content area, hung from the line's baseline - not from this
                        // box's baseline-aligned top, which is already offset by this box's own ascent.
                        var fontTop = (lineBox.BaselineY ?? flowTop) - referenceFont.Ascent;
                        var target = effectiveVerticalAlign.Value.Keyword == VerticalAlignment.TextTop
                            ? fontTop
                            : fontTop + referenceFont.Height - rect.Height;
                        OffsetBoxWithinLine(lineBox, box, target - rect.Top);
                        break;
                    default:
                        // baseline, and PeachBaselineMiddle (the deprecated img align=middle sentinel -
                        // see Keywords.PeachBaselineMiddle - has no distinct inline-layout effect,
                        // matching its pre-existing behavior before this typed-storage conversion).
                        OffsetBoxWithinLine(lineBox, box, baselineDelta);
                        break;
                }
            }
        }

        /// <summary>
        /// How far below its line box's top <paramref name="word"/>'s own content area sits — the
        /// half-leading CSS 2.1 <see href="https://www.w3.org/TR/CSS21/visudet.html#leading">§10.8.1</see>
        /// puts above it, measured against the line's extent so far.
        /// </summary>
        /// <remarks>
        /// Can be negative: a <c>line-height</c> shorter than the font makes the leading negative, and
        /// §10.8.1 has the content area overflow the line box on both sides - this used to be floored at
        /// zero because <c>Fragmentation.FragmentEmitter</c> decided which fragmentainer a word belonged
        /// to from the word's own rectangle, and upward-escaping ink would escape the fragmentainer the
        /// line was placed in. The emitter now resolves a word's fragmentainer through the line that owns
        /// it instead (<c>FragmentEmitter.ClaimsLine</c>, keyed by <see cref="CssRect.Line"/>), so a line's
        /// content is claimed as one unit regardless of where its ink sits, and the floor has nothing left
        /// to protect.
        /// <para>
        /// Zero for replaced and atomic inline content, which §10.8 sizes and aligns from the element's
        /// own margin box rather than from font metrics; <see cref="ApplyVerticalAlignment"/> handles it
        /// after the line closes.
        /// </para>
        /// </remarks>
        private static double HalfLeadingOffsetOf(CssRect word, CssLineBox lineBox)
        {
            if (word.IsImage || lineBox.BaselineExtent is not { } extent) return 0;

            var ascent = (word.FirstLineStyle ?? word.OwnerBox).ActualFont.Ascent;

            return extent.AboveBaseline - ascent;
        }

        /// <summary>
        /// The first word of <paramref name="box"/>'s own subtree on <paramref name="lineBox"/> that is
        /// not replaced/atomic content, or null when it has none there — which is what
        /// <see cref="ApplyVerticalAlignment"/> both measures a box's baseline offset from and uses to
        /// decide whether the baseline governs it at all.
        /// </summary>
        /// <remarks>
        /// Null for a box whose content on this line is entirely replaced/atomic — that case is found
        /// separately by <see cref="FirstReplacedWordOf"/> and aligned from its bottom margin edge.
        /// </remarks>
        private static CssRect? FirstNonReplacedWordOf(CssBox box, CssLineBox lineBox)
        {
            foreach (var word in lineBox.Words)
            {
                if (word.IsImage) continue;

                for (var owner = word.OwnerBox; owner is not null; owner = owner.ParentBox)
                {
                    if (ReferenceEquals(owner, box)) return word;
                }
            }

            return null;
        }

        /// <summary>
        /// Where CSS 2.1 <see href="https://www.w3.org/TR/CSS21/visudet.html#line-height">§10.8.1</see>
        /// puts <paramref name="box"/>'s own baseline, given the rectangle <paramref name="rect"/> the
        /// flow left it at on <paramref name="lineBox"/> — or null when <paramref name="box"/> is not an
        /// atomic inline at all, so the line's shared baseline governs it through its font metrics
        /// instead. The one place the rule lives: <see cref="ApplyVerticalAlignment"/> reads it twice,
        /// once to size the closed line around the box and once to decide how far the box has to move,
        /// and the two must not be able to disagree.
        /// </summary>
        /// <remarks>
        /// The four cases §10.8.1 distinguishes, in the order it distinguishes them:
        /// <list type="bullet">
        /// <item>A replaced element (<see cref="CssBox.IsImage"/> — the engine's atomic replaced flag,
        /// shared by raster images, inline SVG, MathML, form controls and vector shape markers) is
        /// aligned by its <b>bottom margin edge</b>.</item>
        /// <item>So is an <c>inline-block</c> whose <c>overflow</c> is not <c>visible</c>, whether or not
        /// it has in-flow line boxes of its own.</item>
        /// <item>An <c>inline-block</c> that was laid out as a genuine atomic unit
        /// (<see cref="FlowAtomicBlockContentChild"/>) holds its content in line boxes of its own, and
        /// the <b>last</b> of those carries the baseline §10.8.1 asks for.</item>
        /// <item>An <c>inline-block</c> whose inlines-only content this engine flowed into the
        /// surrounding line instead has no line box of its own to read, so the baseline is
        /// reconstructed from the content it put on this line — but only while this line both opens and
        /// closes it, since the rule names the box's <i>last</i> line and a box spanning two of the
        /// parent's lines does not have it here.</item>
        /// </list>
        /// With no in-flow line box at all — an empty box — §10.8.1 falls back to the bottom margin edge
        /// again, which is also what lets a tall empty box grow the line <i>above</i> the baseline rather
        /// than merely being moved once the line's height was settled.
        /// </remarks>
        private static double? AtomicInlineBaselineOf(CssBox box, CssLineBox lineBox, RRect rect)
        {
            // A box that ESTABLISHES this line's formatting context takes no place on it. FlowBox
            // iterates a block over itself, so an inline-block laid out atomically appears among the
            // rectangles of its own lines; reading its last line's baseline back there would align every
            // line inside it to the last one, spreading its own content apart and out of its box.
            if (ReferenceEquals(box, lineBox.OwnerBox)) return null;

            if (box.IsImage) return rect.Bottom + box.ActualMarginBottom;
            if (box.DerivedStyle.ActualDisplay != Keywords.InlineBlock) return null;
            if (box.Overflow.Value != Overflow.Visible) return rect.Bottom + box.ActualMarginBottom;

            if (LastOwnLineBaselineOf(box) is { } ownBaseline) return ownBaseline;

            if (FirstNonReplacedWordOf(box, lineBox) is { } word
                && ReferenceEquals(box.FirstHostingLineBox, lineBox)
                && ReferenceEquals(box.LastHostingLineBox, lineBox))
            {
                return word.Top + (word.FirstLineStyle ?? word.OwnerBox).ActualFont.Ascent;
            }

            return HasWordOnLine(box, lineBox) ? null : rect.Bottom + box.ActualMarginBottom;
        }

        /// <summary>
        /// The baseline of the last line box in <paramref name="box"/>'s own normal flow — the quantity
        /// CSS 2.1 §10.8.1 names for a baseline-aligned <c>inline-block</c> — or null when it holds none,
        /// which is the ordinary case here: an inline-block whose inlines-only content this engine flowed
        /// into the surrounding formatting context owns no line box anywhere in its subtree.
        /// </summary>
        /// <remarks>
        /// Walks the subtree back-to-front, since "last line box" is the last one in document order and a
        /// box laid out atomically holds its lines on the block-level descendants inside it rather than on
        /// itself. Out-of-flow and floated descendants are skipped: §10.8.1 says <i>in the normal flow</i>,
        /// and a float hanging below the box's own content would otherwise supply the baseline.
        /// </remarks>
        private static double? LastOwnLineBaselineOf(CssBox box)
        {
            for (var i = box.Boxes.Count - 1; i >= 0; i--)
            {
                var child = box.Boxes[i];

                if (child.DerivedStyle.ActualDisplay == Keywords.None) continue;
                if (child.Position.Value is PositionMode.Absolute or PositionMode.Fixed) continue;
                if (child.Float.Value is not Floating.None) continue;

                if (LastOwnLineBaselineOf(child) is { } nested) return nested;
            }

            for (var i = box.LineBoxes.Count - 1; i >= 0; i--)
            {
                if (box.LineBoxes[i].BaselineY is { } baselineY) return baselineY;
            }

            return null;
        }

        /// <summary>
        /// The first atomic replaced word in <paramref name="box"/>'s subtree on
        /// <paramref name="lineBox"/>, or null when it has none there. Walking ancestors as well as the
        /// direct owner keeps an inline wrapper's decoration rectangle coupled to the replaced element
        /// it contains when baseline alignment moves both.
        /// </summary>
        private static CssRect? FirstReplacedWordOf(CssBox box, CssLineBox lineBox)
        {
            foreach (var word in lineBox.Words)
            {
                if (!word.IsImage) continue;

                for (var owner = word.OwnerBox; owner is not null; owner = owner.ParentBox)
                {
                    if (ReferenceEquals(owner, box)) return word;
                }
            }

            return null;
        }

        /// <summary>
        /// Whether <paramref name="box"/>'s subtree owns any word on <paramref name="lineBox"/>,
        /// including replaced content. Used to distinguish a genuinely empty inline-block (whose
        /// baseline is its bottom margin edge) from one whose line contains only an image or another
        /// atomic word and therefore needs that content's baseline handling.
        /// </summary>
        private static bool HasWordOnLine(CssBox box, CssLineBox lineBox)
        {
            foreach (var word in lineBox.Words)
            {
                for (var owner = word.OwnerBox; owner is not null; owner = owner.ParentBox)
                {
                    if (ReferenceEquals(owner, box)) return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The line-box extent an <c>outside</c> <c>::marker</c> on <paramref name="blockBox"/> claims of
        /// its item's first line, or null when the box is not a list item carrying one. Only the
        /// <see cref="LineBoxExtent.AboveBaseline"/> side is consumed - see the call site.
        /// </summary>
        /// <remarks>
        /// The marker is excluded from the item's inline flow (<see cref="CssBox.IsOutsideMarker"/>) — it
        /// is positioned beside the principal block box, not inside it — but it does sit on that box's
        /// first baseline (<c>CssBoxMarker.PerformLayoutImp</c>), so the line has to be tall enough to
        /// hold it: a marker in a font larger than its item's own text would otherwise reach up out of
        /// the line and collide with whatever precedes the item.
        /// <see href="https://www.w3.org/TR/css-lists-3/#list-style-position-property">css-lists-3
        /// §3.5</see> leaves this interaction expressly undefined ("The size or contents of the marker
        /// box may affect … the height of its first line box; this interaction is also not defined"), so
        /// this follows what browsers actually do rather than a rule.
        /// <para>
        /// Only the item's <i>own</i> inline content is reached: an item whose content is block-level
        /// (<c>&lt;li&gt;&lt;p&gt;…&lt;/p&gt;&lt;/li&gt;</c>) produces its first line inside that child
        /// block, which is a different <c>blockBox</c> and never asks. That marker is still baseline-
        /// aligned, just without growing the line it sits on.
        /// </para>
        /// </remarks>
        private static LineBoxExtent? OutsideMarkerExtentOf(CssBox blockBox)
        {
            if (blockBox.DerivedStyle.ActualDisplay != Keywords.ListItem) return null;

            foreach (var child in blockBox.Boxes)
            {
                if (CssBox.IsOutsideMarker(child))
                    return HalfLeadingExtentOf(child.ActualFont, child.ActualLineHeight);
            }

            return null;
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

            // A box laid out as a genuine atomic unit (FlowAtomicBlockContentChild) keeps its content in
            // line boxes of ITS own rather than in this one, so none of it is reachable through
            // lineBox.WordsOf and none of it would move. Translate the whole subtree instead - which also
            // carries its own Location (what the fragment emitter reads) and notifies a fragmentainer an
            // earlier pass may already have frozen. Its own rectangle on this line is still moved below:
            // OffsetTop walks CssBox.Rectangles, which CssLineBox.AssignRectanglesToBoxes has not yet
            // populated for this line - it runs after alignment.
            if (!ReferenceEquals(lineBox.OwnerBox, box) && LastOwnLineBaselineOf(box) is not null)
            {
                box.OffsetTop(delta);
                OffsetOwnLineBoxes(box, delta);
            }

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
        /// Shifts the cached geometry of every line box inside <paramref name="box"/>'s own subtree by
        /// <paramref name="delta"/>, alongside the words and rectangles <see cref="CssBox.OffsetTop(double)"/>
        /// has just moved.
        /// </summary>
        /// <remarks>
        /// <see cref="CssLineBox.FlowTop"/> and <see cref="CssLineBox.BaselineY"/> are numbers a closed
        /// line records about where it ended up, not views onto the words, so a subtree translation leaves
        /// them naming the position the box no longer occupies. Everything that asks a line where it
        /// begins reads <c>FlowTop</c> through <see cref="CssLineBox.LineTop"/> (this repo's rule for that
        /// question), and <c>CssBoxMarker</c> sits an outside marker on <c>BaselineY</c> — so an atomic
        /// inline-block moved onto its line's baseline would otherwise carry stale answers for both.
        /// </remarks>
        private static void OffsetOwnLineBoxes(CssBox box, double delta)
        {
            foreach (var line in box.LineBoxes)
            {
                if (line.FlowTop is { } flowTop) line.FlowTop = flowTop + delta;
                if (line.BaselineY is { } baselineY) line.BaselineY = baselineY + delta;
            }

            foreach (var child in box.Boxes)
            {
                OffsetOwnLineBoxes(child, delta);
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
        /// <see href="https://www.w3.org/TR/css-text-3/#text-align-property">css-text-3 §6.1</see>'s
        /// <c>justify</c>: the leftover width is distributed over the line's
        /// <see cref="IsJustificationOpportunity">justification opportunities</see> only - never over
        /// every word boundary, which opens a gap where the source has no white space at all
        /// (<c>A&lt;span&gt;B&lt;/span&gt;</c>, an <c>&lt;img&gt;</c> with nothing around it).
        /// </summary>
        /// <remarks>
        /// <para>
        /// Shifts each word progressively rather than re-walking the line from its start edge (the same
        /// approach <see cref="ApplyLeaderFill"/> takes, and for the same reason): every natural advance
        /// the flow computed - <c>word-spacing</c>, <c>letter-spacing</c>, a float-narrowed line start,
        /// an inline box's own padding, a whitespace-only inline box - is preserved exactly, and
        /// §6.4.1's "space distributed by justification is <i>in addition to</i> the spacing defined by
        /// letter-spacing or word-spacing" is then true by construction. A from-scratch walk has to
        /// re-derive each of those, and the one it used to re-derive (a flat per-word share) is what
        /// this fixes.
        /// </para>
        /// <para>
        /// A line whose content is too long for its measure is not stretched at all: it is §6.1's
        /// overflowing line, explicitly start-aligned ("any content that doesn't fit overflows the line
        /// box's end edge"). Confirmed against Chromium, which leaves a lone overflowing word on a
        /// justified non-last line at the line's start edge. §6.4.3's <i>unexpandable text</i> - a line
        /// with no justification opportunity at all - never reaches here in the first place; see
        /// <see cref="ResolveUsedAlignment"/>.
        /// </para>
        /// </remarks>
        /// <param name="lineBox">the line to justify</param>
        /// <param name="opportunities">
        /// how many justification opportunities the line has, already counted by
        /// <see cref="ResolveUsedAlignment"/> - which is also what decided this line is to be stretched at
        /// all, so it is always positive here.
        /// </param>
        private static void ApplyJustifyAlignment(CssLineBox lineBox, int opportunities)
        {
            var indent = GetLineTextIndent(lineBox.OwnerBox, lineBox.Equals(lineBox.OwnerBox.LineBoxes[0]),
                lineBox.FollowsForcedBreak);

            // text-indent belongs on the line-start side (css-text-3 §3), which is the physical right
            // under RTL - and FlowBox reserves it there by narrowing the *wrap boundary* rather than by
            // moving the start position, so RTL's end edge is inset by the indent here while LTR's is
            // not (LTR's indent is already baked into where the flow started the line). This runs before
            // ApplyBidiReordering, which only reflects positions within the span these two edges bound -
            // it never moves the span itself - so which edge carries the indent here is what decides
            // which physical side it ends up on after mirroring.
            var isRtl = lineBox.OwnerBox.Direction.Value == DirectionMode.Rtl;
            var lineEnd = lineBox.ContentRight - (isRtl ? indent : 0);
            var leftover = lineEnd - lineBox.Words[^1].Right;

            // §6.1: an overflowing line is start-aligned and spills past the end edge. Distributing the
            // negative leftover instead would pull each word back through the previous one's trailing
            // edge - overlapping, garbled text rather than a coherent overflowing line (issue #840).
            // For LTR the start edge is where the flow already left the line, so nothing is done; for RTL
            // it is the physical right one, and reaching it from an overflowing natural position is
            // exactly ApplyRightAlignment's own negative-diff case.
            if (leftover <= 0)
            {
                if (isRtl) ApplyRightAlignment(lineBox);
                return;
            }

            // Scaled from the running opportunity count rather than accumulated per gap, so the final
            // word lands exactly on the end edge instead of a rounding error short of it.
            var seen = 0;

            for (var i = 1; i < lineBox.Words.Count; i++)
            {
                if (IsJustificationOpportunity(lineBox.Words[i - 1], lineBox.Words[i]))
                    seen++;

                lineBox.Words[i].Left += leftover * seen / opportunities;
            }
        }

        /// <summary>
        /// How many <see cref="IsJustificationOpportunity">justification opportunities</see> sit between
        /// the words of <paramref name="lineBox"/>, in logical order. Zero for a line holding one word,
        /// and for one whose words are all contiguous in the source.
        /// </summary>
        private static int CountJustificationOpportunities(CssLineBox lineBox)
        {
            var opportunities = 0;

            for (var i = 1; i < lineBox.Words.Count; i++)
            {
                if (IsJustificationOpportunity(lineBox.Words[i - 1], lineBox.Words[i]))
                    opportunities++;
            }

            return opportunities;
        }

        /// <summary>
        /// Whether the boundary between <paramref name="previous"/> and <paramref name="word"/> - two
        /// words adjacent on one line, in logical order - is a justification opportunity, per
        /// <see href="https://www.w3.org/TR/css-text-3/#justify-algos">css-text-3 §6.4.5</see>'s minimum
        /// requirements for <c>text-justify: auto</c> (the only method PeachPDF implements): a word
        /// separator, or the boundary between a typographic character unit of a block script and any
        /// other one. §6.4.5's third bullet - the same rule for <i>clustered</i> (South-East Asian)
        /// scripts - has nothing to apply to: this engine has no clustered-script line breaking at all,
        /// so such a run is never split into the words an opportunity would sit between.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Block scripts are approximated by <see cref="CommonUtils.IsAsianCharacter"/> (U+4E00 to
        /// U+FA2D). <c>CssBox.ParseToWords</c> no longer uses that predicate: it cuts words where the Unicode
        /// line breaking algorithm allows a line to end, which includes kana and CJK punctuation, so the
        /// opportunities recognized here are a subset of the word boundaries that split produces. A Latin word has no internal boundary to see (the whole word is one
        /// <see cref="CssRect"/>), and the boundary a hyphen or a <c>word-break: break-all</c> /
        /// <c>overflow-wrap</c> split leaves behind is deliberately not one either - §6.4.5 lists word
        /// separators and block/clustered-script letters, not every soft wrap opportunity.
        /// </para>
        /// <para>
        /// Tying the model to that split does cost one boundary §6.4.5 would grant. The split breaks
        /// <i>after</i> the first ideograph it meets, so a Latin run immediately followed by one comes
        /// out as a single word (<c>AB書</c>) and the Latin-to-ideograph boundary inside it is invisible
        /// to every layer, not only to this one - Chromium does expand there. Widening it would mean
        /// changing <c>ParseToWords</c>, which is line-breaking infrastructure, so it stays as it is;
        /// the ideograph-to-Latin direction (<c>書AB</c>) is unaffected and does get its opportunity.
        /// </para>
        /// <para>
        /// Verified against Chromium, which gives a word separator and a CJK letter boundary the same
        /// expansion on one mixed line (0.297px each at <c>font: 16px monospace</c> on
        /// <c>一二AB三四 CD 五六…</c>) - the equal-priority treatment §6.4.1 describes, and the
        /// reason a single count and a single share are enough here.
        /// </para>
        /// <para>
        /// <see cref="CssRect.PrecededByWordSeparator"/> only ever sees <i>collapsible</i> white space,
        /// which is the only kind the flow turns into an advance rather than into a word of its own.
        /// Preserved white space (<c>white-space: pre</c>/<c>pre-wrap</c>) arrives as a real
        /// <see cref="CssRect.IsSpaces"/> word instead, and is just as much a word separator - §6.1 does
        /// permit a UA to treat non-collapsible white space as offering no opportunity at all, but that
        /// would stop a <c>pre-wrap</c> block justifying entirely, which is neither what a browser does
        /// nor what this engine did before. The opportunity is taken to sit <i>after</i> the run, so a
        /// space between two words contributes one opportunity and not two.
        /// </para>
        /// <para>
        /// A forced break is excluded from that arm even though it satisfies it: a
        /// <c>&lt;br&gt;</c>'s marker word is a newline, so
        /// <see cref="CssRect.IsSpaces"/> is true of it - but it is a line terminator, not white space
        /// between two pieces of content, and §6.4.1's opportunities sit between characters. It is
        /// also placed on the line it <i>opens</i> (see <see cref="IsAtLineStart"/>), so counting it
        /// expanded the head of every justified line a <c>&lt;br&gt;</c> began, indenting that line by
        /// one share with no white space in the source at all (issue #1087).
        /// </para>
        /// </remarks>
        private static bool IsJustificationOpportunity(CssRect previous, CssRect word) =>
            word.PrecededByWordSeparator
            || previous is { IsSpaces: true, IsLineBreak: false }
            || EndsWithBlockScriptLetter(previous)
            || StartsWithBlockScriptLetter(word);

        /// <summary>Whether <paramref name="word"/>'s last character belongs to a block script.</summary>
        private static bool EndsWithBlockScriptLetter(CssRect word) =>
            word.Text is { Length: > 0 } text
            && Rune.DecodeLastFromUtf16(text.AsSpan(), out var rune, out _) == OperationStatus.Done
            && CommonUtils.IsAsianCharacter(rune);

        /// <summary>Whether <paramref name="word"/>'s first character belongs to a block script.</summary>
        private static bool StartsWithBlockScriptLetter(CssRect word) =>
            word.Text is { Length: > 0 } text
            && Rune.DecodeFromUtf16(text.AsSpan(), out var rune, out _) == OperationStatus.Done
            && CommonUtils.IsAsianCharacter(rune);

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
            var right = line.ContentRight - (isRtl ? indent : 0);
            var diff = right - lastWord.Right - lastWord.OwnerBox.ActualBorderRightWidth - lastWord.OwnerBox.ActualPaddingRight;
            diff /= 2;

            // right - lastWord.Right is, by construction, exactly the shift needed to center the line
            // around its target - there is no direction in which a nonzero diff should be discarded. The
            // previous positive-only guard assumed natural (pre-alignment, left-to-right-flowing) placement
            // is never further right than the target, which holds whenever the line's content fits; an
            // overflowing line (an unbreakable nowrap run wider than the box) needs the same negative-diff
            // shift to center around the overflow, spilling symmetrically past both edges instead of being
            // left un-shifted at its natural position. Mirrors ApplyRightAlignment's identical fix (issue
            // #797/#840) - a uniform shift like this one can never overlap words (every word moves by the
            // same amount, so their relative order and spacing are preserved), unlike
            // ApplyJustifyAlignment's own per-word spacing, which needed a different fix for the same
            // underlying issue.
            if (!(Math.Abs(diff) > 0)) return;

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
            // of this fix did), double-reserves the space and can leave `diff` negative even for a line
            // that isn't actually overflowing - which the guard below now shifts (see its own remarks)
            // rather than leaving un-aligned, so double-reserving the indent is a correctness bug to avoid
            // here, not a harmlessly-absorbed edge case.
            var isFirstLine = line.Equals(line.OwnerBox.LineBoxes[0]);
            var indent = GetLineTextIndent(line.OwnerBox, isFirstLine, line.FollowsForcedBreak);
            var isRtl = line.OwnerBox.Direction.Value == DirectionMode.Rtl;

            var lastWord = line.Words[^1];
            var right = line.ContentRight - (isRtl ? indent : 0);
            var diff = right - lastWord.Right - lastWord.OwnerBox.ActualBorderRightWidth - lastWord.OwnerBox.ActualPaddingRight;

            // right - lastWord.Right is, by construction, exactly the shift needed to flush the line to
            // its target right edge - there is no direction in which a nonzero diff should be discarded.
            // The previous positive-only guard assumed natural (pre-alignment, always left-to-right-flowing)
            // placement is never further right than the target, which holds whenever the line's content
            // fits; a line wider than its container (an overflowing nowrap line, or one unbreakable word
            // wider than the box) needs the same negative-diff shift to stay flush-right and spill past the
            // *left* edge instead of being left un-shifted at its natural position, spilling past the right
            // edge like left-aligned content would. Mirrors ApplyVerticalFlushAlignment's identical fix
            // (issue #797) for the horizontal axis - see that method's own remarks for the full reasoning.
            if (!(Math.Abs(diff) > 0)) return;

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
                // An absolutely positioned descendant is out of flow (CSS 2.1 §9.6), so it contributes nothing
                // to its ancestors' intrinsic sizes. Measured here, a nowrap badge inside a shrink-to-fit float
                // widened the float to the badge's own width.
                if (childBox.IsAbsolutelyPositioned) continue;

                var childBoxWidth = await GetBoxWidth(g, childBox);
                childBoxWidth = await GetLargestChildWidth(g, childBox, Math.Max(currentSize, childBoxWidth));
                currentSize = childBoxWidth > currentSize ? childBoxWidth : currentSize;
            }

            return currentSize;
        }


        #endregion
    }
}
