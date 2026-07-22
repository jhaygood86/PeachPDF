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
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
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

            if (intrinsicWidth is > 0 && intrinsicHeight is > 0)
            {
                // If only the width was set in the html tag, ratio the height.
                if ((hasImageTagWidth && !hasImageTagHeight) || scaleImageHeight)
                {
                    // Divide the given tag width with the actual image width, to get the ratio.
                    var ratio = word.Width / intrinsicWidth.Value;
                    word.Height = intrinsicHeight.Value * ratio;
                }
                // If only the height was set in the html tag, ratio the width.
                else if (hasImageTagHeight && !hasImageTagWidth)
                {
                    // Divide the given tag height with the actual image height, to get the ratio.
                    var ratio = word.Height / intrinsicHeight.Value;
                    word.Width = intrinsicWidth.Value * ratio;
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
                    if (intrinsicWidth is > 0 && intrinsicHeight is > 0 && word.Height > 0)
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
                    if (intrinsicWidth is > 0 && intrinsicHeight is > 0 && word.Height > 0)
                    {
                        word.Width *= minHeightVal / word.Height;
                    }
                    word.Height = minHeightVal;
                }
            }

            word.Height += word.OwnerBox.ActualBorderBottomWidth + word.OwnerBox.ActualBorderTopWidth + word.OwnerBox.ActualPaddingTop + word.OwnerBox.ActualPaddingBottom;
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
        public static async ValueTask CreateLineBoxes(RGraphics g, CssBox blockBox)
        {
            ArgumentNullException.ThrowIfNull(g);
            ArgumentNullException.ThrowIfNull(blockBox);

            blockBox.LineBoxes.Clear();

            var limitRight = blockBox.ClientRight;

            //Get the start x and y of the blockBox
            var startX = blockBox.ClientLeft;
            var startY = blockBox.ClientTop;

            CssLineBoxCoordinates coordinates = new()
            {
                Line = new CssLineBox(blockBox),
                CurrentX = startX + blockBox.ActualTextIndent,
                CurrentY = startY,
                MaxRight = startX,
                MaxBottom = startY
            };

            //Flow words and boxes
            await FlowBox(g, blockBox, blockBox, limitRight, 0, startX, coordinates);

            // if width is not restricted we need to lower it to the actual width
            if (blockBox.ActualRight >= 90999)
            {
                blockBox.ActualRight = coordinates.MaxRight + blockBox.ActualPaddingRight + blockBox.ActualBorderRightWidth;
            }

            //Gets the rectangles for each line-box
            foreach (var lineBox in blockBox.LineBoxes)
            {
                ApplyHorizontalAlignment(lineBox);
                ApplyRightToLeft(blockBox, lineBox);
                BubbleRectangles(blockBox, lineBox);
                ApplyVerticalAlignment(lineBox);
                lineBox.AssignRectanglesToBoxes();
            }

            blockBox.ActualBottom = coordinates.MaxBottom + blockBox.ActualPaddingBottom + blockBox.ActualBorderBottomWidth;

            // handle limiting block height when overflow is hidden
            if (blockBox.Height != CssConstants.Auto && blockBox.Overflow == CssConstants.Hidden && blockBox.ActualBottom - blockBox.Location.Y > blockBox.ActualHeight)
            {
                blockBox.ActualBottom = blockBox.Location.Y + blockBox.ActualHeight + blockBox.ActualPaddingBottom + blockBox.ActualPaddingTop;
            }
        }

        /// <summary>
        /// Applies special vertical alignment for table-cells
        /// </summary>
        /// <param name="g"></param>
        /// <param name="cell"></param>
        public static void ApplyCellVerticalAlignment(RGraphics g, CssBox cell)
        {
            ArgumentNullException.ThrowIfNull(g);
            ArgumentNullException.ThrowIfNull(cell);

            if (cell.VerticalAlign is CssConstants.Top or CssConstants.Baseline)
                return;

            var cellBottom = cell.ClientBottom;
            var bottom = CssBox.GetMaximumBottom(cell, 0f);

            var dist = cell.VerticalAlign switch
            {
                CssConstants.Bottom => cellBottom - bottom,
                CssConstants.Middle => (cellBottom - bottom) / 2,
                _ => 0d
            };

            foreach (var b in cell.Boxes)
            {
                b.OffsetTop(dist);
            }
        }

        public static void FloatBox(CssBox box)
        {
            if (box is { Float: CssConstants.None, Clear: CssConstants.None })
            {
                return;
            }

            var containingBox = box.ContainingBlock!;

            var limitRight = containingBox.ClientRight;

            //Get the start x and y of the blockBox
            var startX = containingBox.ClientLeft;
            var startY = box.Location.Y;

            var currentBoxIdx = containingBox.Boxes.IndexOf(box);

            switch (box.Float)
            {
                case CssConstants.Left:
                    FloatBoxLeft(box, startX, startY, limitRight);
                    break;
                case CssConstants.Right:
                    FloatBoxRight(box, startX, startY, limitRight);
                    break;
            }

            if (box.Clear is not CssConstants.None)
            {
                ClearBox(box, currentBoxIdx, containingBox);
            }
        }

        public static double GetActualMarginLeft(CssBox box, double? boxWidth = null)
        {
            if (box.MarginLeft is not CssConstants.Auto)
            {
                return CssValueParser.ParseLength(box.MarginLeft, box.ContainingBlock.Size.Width, box);
            }

            if (box.MarginRight is not CssConstants.Auto) return 0;

            if (box.Display.StartsWith("table-") && box.Display != CssConstants.TableCaption)
            {
                return 0;
            }

            // This will be used by the table layout engine later with boxWidth provided
            if (box.Display is CssConstants.Table && boxWidth is null)
            {
                return 0;
            }

            var width = boxWidth ?? box.Size.Width;
            var containingWidth = box.ContainingBlock.Size.Width;
            var remainingWidth = containingWidth - width;

            return remainingWidth / 2;

        }

        public static double GetActualMarginRight(CssBox box, double? boxWidth = null)
        {
            if (box.MarginRight is not CssConstants.Auto)
            {
                return CssValueParser.ParseLength(box.MarginRight, box.ContainingBlock.Size.Width, box);
            }

            if (box.MarginLeft is not CssConstants.Auto) return 0;

            if (box.Display.StartsWith("table-") && box.Display != CssConstants.TableCaption)
            {
                return 0;
            }

            // This will be used by the table layout engine later with boxWidth provided
            if (box.Display is CssConstants.Table && boxWidth is null)
            {
                return 0;
            }

            var width = boxWidth ?? box.Size.Width;
            var containingWidth = box.ContainingBlock.Size.Width;
            var remainingWidth = containingWidth - width;

            return remainingWidth / 2;

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
                if (childBox.Display == CssConstants.None) continue;

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
                if (!string.IsNullOrEmpty(b.Width) && b.Width != CssConstants.Auto) return false;
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

        public static async ValueTask<double> GetBoxWidth(RGraphics g, CssBox box)
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
            // Keyed off the box's own Location.Y (the previous layout pass's final position - see
            // HtmlContainerInt.PerformLayout's reflow loop), since a box's width is resolved before its
            // Location is assigned in this pass.
            var availableRight = box.ContainingBlock.ClientRight;
            if (box.HtmlContainer is { } htmlContainer && htmlContainer.UseVariablePageWidth
                && IsUnconstrainedMainColumn(box.ContainingBlock))
            {
                availableRight = htmlContainer.PageContentRightOf(box.Location.Y)
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
            if (box.Width != CssConstants.Auto && !string.IsNullOrEmpty(box.Width)
                && !(box.Display == CssConstants.Inline && box.Words.Count == 0))
            {
                // Absolute boxes resolve a percentage width against their nearest positioned ancestor
                // (CSS 2.1 §10.1), consistent with GetBoxHeight and the left/top positioning code.
                width = CssValueParser.ParseLength(box.Width, PercentageBase(box).Size.Width, box);
            }

            if (box is { Width: CssConstants.Auto, Position: not CssConstants.Absolute })
            {
                width -= box.ActualBoxSizeIncludedWidth;
            }

            if (box is { Width: CssConstants.Auto, Position: CssConstants.Absolute })
            {
                var absCb = PercentageBase(box);
                if (box.Left != CssConstants.Auto && box.Right != CssConstants.Auto)
                {
                    // CSS 2.1 §10.3.7: an absolutely-positioned box with auto width but both `left` and
                    // `right` set fills the space between them in the containing block (rather than shrinking
                    // to fit its content). This is what sizes a Charts.css area/line `td::before` (auto width,
                    // `inset: 0`) to cover its cell.
                    var left = CssValueParser.ParseLength(box.Left, absCb.Size.Width, box);
                    var right = CssValueParser.ParseLength(box.Right, absCb.Size.Width, box);
                    // An over-constrained fill (left + right wider than the containing block) clamps to 0, per
                    // CSS 2.1 §10.3.7 (a used width is never negative).
                    width = Math.Max(0, absCb.Size.Width - left - right - box.ActualMarginLeft - box.ActualMarginRight - box.ActualBoxSizeIncludedWidth);
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
            box.Position is CssConstants.Absolute ? DomUtils.GetNearestPositionedAncestor(box) : box.ContainingBlock;

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
                    return null;
                }

                height = CssValueParser.ParseLength(box.Height, heightCb.Size.Height, box) + box.ActualBoxSizeIncludedHeight;
            }
            else if (box.Position is CssConstants.Absolute
                     && box.Top != CssConstants.Auto && box.Bottom != CssConstants.Auto
                     && heightCb.IsHeightCalculated)
            {
                // CSS 2.1 §10.6.4: an absolutely-positioned box with auto height but both `top` and `bottom`
                // set fills the space between them in the containing block. The counterpart of the §10.3.7
                // width rule above; sizes a Charts.css area/line `td::before` (auto height, `inset: 0`) to
                // its cell's height.
                var top = CssValueParser.ParseLength(box.Top, heightCb.Size.Height, box);
                var bottom = CssValueParser.ParseLength(box.Bottom, heightCb.Size.Height, box);
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

            if (string.IsNullOrEmpty(box.AspectRatio) || box.AspectRatio == CssConstants.Auto) return false;
            if (box.Size.Width <= 0) return false;

            var tokens = CssValueParser.GetCssTokens(box.AspectRatio);
            if (!AspectRatioGrammar.TryParse(tokens, out var ratio) || ratio is not (> 0)) return false;

            height = box.Size.Width / ratio.Value + box.ActualBoxSizeIncludedHeight;
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
                if (childBox.Display is CssConstants.TableCell or CssConstants.TableRow
                    or CssConstants.TableRowGroup or CssConstants.TableHeaderGroup or CssConstants.TableFooterGroup)
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
            box.ActualBottom = boxHeight is not null
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
            // An aspect-ratio with a definite width and an auto height yields a definite (ratio-derived)
            // height too, so a percentage-height descendant resolves against it — this is what lets the
            // Charts.css bars take their height from the ratio-sized tbody.
            var isRatioHeight = !CssValueParser.IsValidLength(box.Height) && TryGetAspectRatioHeight(box, out _);
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

                clearance = Math.Max(clearance, GetClearance(siblingBox, box.Clear));

                if (!siblingBox.IsFloated) continue;

                switch (siblingBox.Float)
                {
                    case CssConstants.Left when box.Clear is CssConstants.Right:
                    case CssConstants.Right when box.Clear is CssConstants.Left:
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

        private static double GetClearance(CssBox box, string clearPropValue)
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
                switch (childBox.Float)
                {
                    case CssConstants.Left when clearPropValue is CssConstants.Right:
                    case CssConstants.Right when clearPropValue is CssConstants.Left:
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
                var intersectingFloat = DomUtils.GetFirstIntersectingFloatBox(box, coordinates, box.Float);

                if (intersectingFloat is null) break;

                switch (intersectingFloat.Float)
                {
                    case CssConstants.Left:
                        coordinates.Left = intersectingFloat.ActualRight + intersectingFloat.ActualMarginRight + box.ActualMarginLeft;
                        break;
                    case CssConstants.Right:
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
                var intersectingFloat = DomUtils.GetFirstIntersectingFloatBox(box, coordinates, box.Float);

                if (intersectingFloat is null) break;

                switch (intersectingFloat.Float)
                {
                    case CssConstants.Left:
                        coordinates.Left = intersectingFloat.ActualRight;
                        break;
                    case CssConstants.Right:
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
        /// Recursively flows the content of the box using the inline model
        /// </summary>
        /// <param name="g">Device Info</param>
        /// <param name="blockBox">Blockbox that contains the text flow</param>
        /// <param name="box">Current box to flow its content</param>
        /// <param name="limitRight">Maximum reached right</param>
        /// <param name="lineSpacing">Space to use between rows of text</param>
        /// <param name="lineStartX">x starting coordinate for when breaking lines of text</param>
        /// <param name="coordinates">Current coordinates being used</param>
        private static async ValueTask FlowBox(RGraphics g, CssBox blockBox, CssBox box, double limitRight, double lineSpacing, double lineStartX, CssLineBoxCoordinates coordinates)
        {
            var startX = coordinates.CurrentX;
            var startY = coordinates.CurrentY;
            box.FirstHostingLineBox = coordinates.Line;

            // Inline elements (e.g. <b>/<span>) never get their own PerformLayoutImp call - that only
            // happens for block children (CssBox.PerformLayoutImp's childBox.PerformLayout loop), which
            // is exactly the path skipped when a block's children are ContainsInlinesOnly and flowed
            // here instead. FlowBox's own entry/exit (this line and LastHostingLineBox below) is the one
            // place that visits every inline box, in DOM order, exactly once - so it's where string-set
            // and named-page registration (normally done near the top/bottom of PerformLayoutImp) have to
            // happen for inline boxes. blockBox itself is excluded since its own PerformLayoutImp already
            // handles it correctly before CreateLineBoxes is even called.
            if (box != blockBox && !string.IsNullOrEmpty(box.StringSet) && box.StringSet != CssConstants.None)
            {
                CssNamedStringEngine.ApplyStringSet(box);
            }

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
                if (b is { IsMarkerPseudoElement: true, ListStylePosition: not CssConstants.Inside }) continue;

                var leftSpacing = (b.Position != CssConstants.Absolute && b.Position != CssConstants.Fixed) ? b.ActualMarginLeft + b.ActualBorderLeftWidth + b.ActualPaddingLeft : 0;
                var rightSpacing = (b.Position != CssConstants.Absolute && b.Position != CssConstants.Fixed) ? b.ActualMarginRight + b.ActualBorderRightWidth + b.ActualPaddingRight : 0;

                b.RectanglesReset();
                await b.MeasureWordsSize(g);

                // Still on blockBox's first formatted line and a ::first-line rule applies to it -
                // measure b's words using the first-line font/spacing instead of its own. If b's
                // content turns out to straddle the line-1/2 boundary, the wrap-handling block below
                // corrects the words that actually end up on line 2+ back to b's own normal style.
                if (blockBox.ResolvedFirstLineStyle != null && coordinates.Line == blockBox.LineBoxes[0])
                {
                    b.ApplyFirstLineStyleOverride(g, blockBox.ResolvedFirstLineStyle);
                }

                coordinates.CurrentX += leftSpacing;

                var lastLeftIntersectingFloatBox = DomUtils.GetLastLeftIntersectingFloatBox(box, coordinates);

                if (lastLeftIntersectingFloatBox is not null)
                {
                    coordinates.CurrentX = lastLeftIntersectingFloatBox.ActualRight + lastLeftIntersectingFloatBox.ActualMarginRight + leftSpacing;
                }

                if (b.Words.Count > 0)
                {
                    var wrapNoWrapBox = false;

                    if (b.WhiteSpace == CssConstants.NoWrap && coordinates.CurrentX > lineStartX)
                    {
                        var boxRight = coordinates.CurrentX;

                        foreach (var word in b.Words)
                            boxRight += word.FullWidth;

                        if (boxRight > limitRight)
                            wrapNoWrapBox = true;
                    }

                    if (DomUtils.IsBoxHasWhitespace(b))
                        coordinates.CurrentX += box.ActualWordSpacing;

                    for (var wordIndex = 0; wordIndex < b.Words.Count; wordIndex++)
                    {
                        var word = b.Words[wordIndex];

                        if (coordinates.MaxBottom - coordinates.CurrentY < box.ActualLineHeight)
                            coordinates.MaxBottom += box.ActualLineHeight - (coordinates.MaxBottom - coordinates.CurrentY);

                        var actualLimitRight = limitRight;
                        var lastRightIntersectingFloatBox = DomUtils.GetLastRightIntersectingFloatBox(box, coordinates, word.FullWidth);

                        if (lastRightIntersectingFloatBox is not null)
                        {
                            actualLimitRight = lastRightIntersectingFloatBox.Location.X -
                                               lastRightIntersectingFloatBox.ActualMarginLeft - rightSpacing;
                        }

                        var overflows = b.WhiteSpace != CssConstants.NoWrap && b.WhiteSpace != CssConstants.Pre
                                         && coordinates.CurrentX + word.Width + rightSpacing > actualLimitRight
                                         && (b.WhiteSpace != CssConstants.PreWrap || !word.IsSpaces);

                        // hyphens:auto/manual: before giving up and wrapping the whole word, see if a
                        // cached candidate break point (from ParseToWords - either an explicit soft
                        // hyphen or an automatic HyphenationEngine suggestion) lets a hyphenated prefix
                        // fit in the space remaining on the current line instead.
                        if (!word.SuppressWrapBefore && overflows && !word.IsLineBreak && !wrapNoWrapBox &&
                            word.HyphenationCandidates is { Count: > 0 } &&
                            TryHyphenateWord(g, b, word, actualLimitRight - coordinates.CurrentX - rightSpacing, out var prefixWord, out var suffixWord))
                        {
                            b.Words[wordIndex] = prefixWord!;
                            b.Words.Insert(wordIndex + 1, suffixWord!);
                            word = prefixWord!;
                            overflows = false;
                        }

                        if (!word.SuppressWrapBefore && (overflows || word.IsLineBreak || wrapNoWrapBox))
                        {
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

                            coordinates.Line = new CssLineBox(blockBox);

                            if (word.IsImage || word.Equals(b.FirstWord))
                            {
                                coordinates.CurrentX += leftSpacing;
                            }
                        }

                        coordinates.Line.ReportExistanceOf(word);

                        lastLeftIntersectingFloatBox = DomUtils.GetLastLeftIntersectingFloatBox(box, coordinates);

                        if (lastLeftIntersectingFloatBox is not null)
                        {
                            coordinates.CurrentX = lastLeftIntersectingFloatBox.ActualRight + lastLeftIntersectingFloatBox.ActualMarginRight + leftSpacing;
                        }

                        word.Left = coordinates.CurrentX;
                        word.Top = coordinates.CurrentY;

                        if (box is { IsFixed: false, IsTableCell: false } && box.HtmlContainer?.SuppressWordPageBreaks != true)
                        {
                            word.BreakPage();
                        }

                        coordinates.CurrentX = word.Left + word.FullWidth;

                        coordinates.MaxRight = Math.Max(coordinates.MaxRight, word.Right);
                        coordinates.MaxBottom = Math.Max(coordinates.MaxBottom, word.Bottom);

                        if (b.Position != CssConstants.Absolute) continue;

                        word.Left += box.ActualMarginLeft;
                        word.Top += box.ActualMarginTop;
                    }

                    // A box holding its words directly (e.g. a ::before/::after pseudo-element,
                    // whose generated text lives on the box itself rather than an anonymous
                    // child) never goes through the FlowBox recursion below, so it gets its
                    // atomic-inline vertical insets here. Skipped for the self-iteration case
                    // (boxes = [box], b == box): there the insets are applied by the PARENT's
                    // own recursion branch after this call returns - applying both would inset
                    // the words twice.
                    if (!ReferenceEquals(b, box))
                    {
                        ApplyAtomicInlineVerticalInsets(b, box, coordinates);
                    }
                }
                else if (b.Display == CssConstants.InlineFlex)
                {
                    // Treat inline-flex as an atomic inline element: run flex layout then advance the
                    // cursor by the box's outer size.  coordinates.CurrentX is already past the left
                    // margin+border+padding (leftSpacing was added above), so Location.X sits at the
                    // border-left edge (after margin).
                    b.Location = new RPoint(
                        coordinates.CurrentX - b.ActualPaddingLeft - b.ActualBorderLeftWidth,
                        coordinates.CurrentY);
                    b.ActualBottom = b.Location.Y;
                    b.FirstHostingLineBox = coordinates.Line;
                    b.LastHostingLineBox  = coordinates.Line;

                    // Unlike a plain inline box, b.Location is already final here, so string-set/named-page
                    // can be applied and finalized together rather than split across entry/exit like the
                    // FlowBox recursion case above.
                    if (!string.IsNullOrEmpty(b.StringSet) && b.StringSet != CssConstants.None)
                    {
                        CssNamedStringEngine.ApplyStringSet(b);
                        foreach (var namedString in b.NamedStrings.Values)
                        {
                            namedString.Y = b.Location.Y;
                        }
                    }

                    if (!string.IsNullOrEmpty(b.PageName) && b.PageName != "auto")
                    {
                        b.RegisteredNamedPageElement = b.HtmlContainer?.RegisterNamedPageElement(b.PageName, b.Location.Y);
                    }

                    await CssLayoutEngineFlex.PerformLayout(g, b);

                    // Advance to content-right so that the outer rightSpacing addition lands correctly.
                    coordinates.CurrentX = b.ClientRight;
                    coordinates.MaxRight  = Math.Max(coordinates.MaxRight,  b.Location.X + b.ActualBoxSizingWidth);
                    coordinates.MaxBottom = Math.Max(coordinates.MaxBottom, b.Location.Y + b.ActualBoxSizingHeight);

                    // Register the box in the parent line so its border/background is painted.
                    coordinates.Line.Rectangles[b] = new RRect(
                        b.Location.X, b.Location.Y, b.ActualBoxSizingWidth, b.ActualBoxSizingHeight);
                }
                else
                {
                    await FlowBox(g, blockBox, b, limitRight, lineSpacing, lineStartX, coordinates);
                    ApplyAtomicInlineVerticalInsets(b, box, coordinates);
                }

                coordinates.CurrentX += rightSpacing;
            }

            // handle height setting: the flowed content came out shorter than the box's own
            // ActualHeight (e.g. an inline-block button whose vertical padding exceeds its one
            // small-font text line), so extend MaxBottom to cover the box's full height from
            // where it started. This must be startY-anchored: the old
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
            if (box.Display is not CssConstants.Inline && coordinates.MaxBottom - startY < box.ActualHeight)
            {
                coordinates.MaxBottom = startY + box.ActualHeight;
            }

            // handle width setting
            if (box.IsInline && 0 <= coordinates.CurrentX - startX && coordinates.CurrentX - startX < box.ActualWidth)
            {
                // hack for actual width handling
                coordinates.CurrentX += box.ActualWidth - (coordinates.CurrentX - startX);
                coordinates.Line.Rectangles.Add(box, new RRect(startX, startY, box.ActualWidth, box.ActualHeight));
            }

            // handle box that is only a whitespace
            if (box.Text is { Length: > 0 } && string.IsNullOrWhiteSpace(box.Text) && !box.IsImage && box.IsInline && box.Boxes.Count == 0 && box.Words.Count == 0)
            {
                coordinates.CurrentX += box.ActualWordSpacing;
            }

            // Finalize what was captured at entry, now that this box's content has actually been placed
            // and coordinates.CurrentY reflects where it landed - mirrors CssBox.PerformLayoutImp's own
            // late-stage Y-correction/named-page registration (done there once Location is final), which
            // a plain inline box never gets a Location for in the first place.
            if (box != blockBox)
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
                    box.RegisteredNamedPageElement = box.HtmlContainer?.RegisterNamedPageElement(box.PageName, coordinates.CurrentY);
                }
            }

            box.LastHostingLineBox = coordinates.Line;
        }

        /// <summary>
        /// Tries to split <paramref name="word"/> at the widest of its precomputed
        /// <see cref="CssRect.HyphenationCandidates"/> (set by <see cref="CssBox.ParseToWords"/> — either
        /// an explicit soft hyphen position or an automatic <c>HyphenationEngine</c> suggestion) whose
        /// hyphenated prefix (with a literal trailing <c>-</c>, actually measured) still fits in
        /// <paramref name="availableWidth"/>. Candidates are tried from the last (rightmost, keeping the
        /// most text on the current line) to the first, so the result is the longest prefix that fits —
        /// not just the first candidate found. Returns false (leaving <paramref name="prefix"/>/
        /// <paramref name="suffix"/> null) if no candidate fits, in which case the caller falls back to
        /// wrapping the whole word as before.
        /// </summary>
        private static bool TryHyphenateWord(RGraphics g, CssBox b, CssRect word, double availableWidth, out CssRectWord? prefix, out CssRectWord? suffix)
        {
            prefix = null;
            suffix = null;

            var text = word.Text;
            var candidates = word.HyphenationCandidates;
            if (string.IsNullOrEmpty(text) || candidates is not { Count: > 0 })
                return false;

            for (var i = candidates.Count - 1; i >= 0; i--)
            {
                var breakAt = candidates[i];
                if (breakAt <= 0 || breakAt >= text.Length) continue;

                var prefixText = text[..breakAt] + "-";
                var prefixWidth = g.MeasureString(prefixText, b.ActualFont).Width;
                if (prefixWidth > availableWidth) continue;

                var suffixText = text[breakAt..];
                prefix = new CssRectWord(b, prefixText, word.HasSpaceBefore, false)
                {
                    Width = prefixWidth,
                    Height = b.ActualFont.Height
                };
                suffix = new CssRectWord(b, suffixText, false, word.HasSpaceAfter)
                {
                    Width = g.MeasureString(suffixText, b.ActualFont).Width,
                    Height = b.ActualFont.Height
                };
                return true;
            }

            return false;
        }

        /// <summary>
        /// CSS2.1 §8.1 box model: an atomic inline-level box's content is laid out inside its
        /// padding box, so its flowed words must sit border+padding-top BELOW the box's top
        /// edge - <see cref="FlowBox"/> placed them at the line's CurrentY (the box's top).
        /// Without this inset, <see cref="CssLineBox.UpdateRectangle"/>'s padding expansion
        /// pushes the box's background/border rect UP above the text instead (a button's label
        /// hugged its top border). Shifts the flowed words down into the content box and grows
        /// the flow bottom to cover the full padding box. Plain <c>display: inline</c> boxes
        /// are excluded per §10.8.1 - their vertical padding paints without taking vertical
        /// space, which the existing rect expansion alone already models correctly. An
        /// absolutely/fixed-positioned box still gets its words inset (its content sits inside
        /// its own padding box regardless), but never grows the in-flow
        /// <see cref="CssLineBoxCoordinates.MaxBottom"/> - out-of-flow boxes must not affect
        /// ancestor flow height (CSS2.1 §9.6).
        /// </summary>
        /// <param name="b">the just-flowed inline-level box</param>
        /// <param name="flowContext">the box whose inline flow <paramref name="b"/> participates in
        /// (<see cref="FlowBox"/>'s own <c>box</c> parameter) - carries the same fixed/table-cell
        /// page-break exemptions the per-word flow placement uses</param>
        /// <param name="coordinates">the current line coordinates</param>
        private static void ApplyAtomicInlineVerticalInsets(CssBox b, CssBox flowContext, CssLineBoxCoordinates coordinates)
        {
            if (b.Display is not CssConstants.InlineBlock)
                return;

            var topInset = b.ActualBorderTopWidth + b.ActualPaddingTop;
            var bottomInset = b.ActualBorderBottomWidth + b.ActualPaddingBottom;

            if (topInset <= 0 && bottomInset <= 0)
                return;

            // Word flow already ran CssRect.BreakPage per word; the inset shift below can push
            // a line that legitimately fit above a page boundary back across it, so the shifted
            // words re-check - a monolithic line must land fully within one fragmentainer
            // (css-break §4). Same exemptions as the flow-time call.
            var breakPages = flowContext is { IsFixed: false, IsTableCell: false };
            var maxWordBottom = OffsetFlowedWords(b, topInset, breakPages);

            if (maxWordBottom > double.MinValue
                && b.Position is not (CssConstants.Absolute or CssConstants.Fixed))
            {
                coordinates.MaxBottom = Math.Max(coordinates.MaxBottom, maxWordBottom + bottomInset);
            }
        }

        /// <summary>
        /// Shifts every word already flowed inside <paramref name="box"/>'s subtree down by
        /// <paramref name="amount"/> (an atomic inline-level box's border+padding-top content
        /// inset - see <see cref="ApplyAtomicInlineVerticalInsets"/>), optionally re-running the
        /// page-boundary check on each shifted word, and returns the lowest word bottom after
        /// the shift, or <see cref="double.MinValue"/> when the subtree holds no words at all.
        /// </summary>
        private static double OffsetFlowedWords(CssBox box, double amount, bool breakPages)
        {
            var maxBottom = double.MinValue;

            foreach (var word in box.Words)
            {
                word.Top += amount;

                if (breakPages && box.HtmlContainer?.SuppressWordPageBreaks != true)
                {
                    word.BreakPage();
                }

                maxBottom = Math.Max(maxBottom, word.Bottom);
            }

            foreach (var child in box.Boxes)
            {
                maxBottom = Math.Max(maxBottom, OffsetFlowedWords(child, amount, breakPages));
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
                    var left = word.Left;

                    if (box == box.ParentBox!.Boxes[0] && word == box.Words[0] && word == line.Words[0] && line != line.OwnerBox.LineBoxes[0] && !word.IsLineBreak)
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
        /// <param name="lineBox"></param>
        private static void ApplyHorizontalAlignment(CssLineBox lineBox)
        {
            switch (lineBox.OwnerBox.TextAlign)
            {
                case CssConstants.Right:
                    ApplyRightAlignment(lineBox);
                    break;
                case CssConstants.Center:
                    ApplyCenterAlignment(lineBox);
                    break;
                case CssConstants.Justify:
                    ApplyJustifyAlignment(lineBox);
                    break;
            }
        }

        /// <summary>
        /// Applies right to left direction to words
        /// </summary>
        /// <param name="blockBox"></param>
        /// <param name="lineBox"></param>
        private static void ApplyRightToLeft(CssBox blockBox, CssLineBox lineBox)
        {
            if (blockBox.Direction == CssConstants.Rtl)
            {
                ApplyRightToLeftOnLine(lineBox);
            }
            else
            {
                foreach (var box in lineBox.RelatedBoxes)
                {
                    if (box.Direction == CssConstants.Rtl)
                    {
                        ApplyRightToLeftOnSingleBox(lineBox, box);
                    }
                }
            }
        }

        /// <summary>
        /// Applies RTL direction to all the words on the line.
        /// </summary>
        /// <param name="line">the line to apply RTL to</param>
        private static void ApplyRightToLeftOnLine(CssLineBox line)
        {
            if (line.Words.Count <= 0) return;

            var left = line.Words[0].Left;
            var right = line.Words[^1].Right;

            foreach (var word in line.Words)
            {
                var diff = word.Left - left;
                var wright = right - diff;
                word.Left = wright - word.Width;
            }
        }

        /// <summary>
        /// Applies RTL direction to specific box words on the line.
        /// </summary>
        /// <param name="lineBox"></param>
        /// <param name="box"></param>
        private static void ApplyRightToLeftOnSingleBox(CssLineBox lineBox, CssBox box)
        {
            var leftWordIdx = -1;
            var rightWordIdx = -1;

            for (var i = 0; i < lineBox.Words.Count; i++)
            {
                if (lineBox.Words[i].OwnerBox != box) continue;

                if (leftWordIdx < 0)
                    leftWordIdx = i;
                rightWordIdx = i;
            }

            if (leftWordIdx <= -1 || rightWordIdx <= leftWordIdx) return;

            var left = lineBox.Words[leftWordIdx].Left;
            var right = lineBox.Words[rightWordIdx].Right;

            for (var i = leftWordIdx; i <= rightWordIdx; i++)
            {
                var diff = lineBox.Words[i].Left - left;
                var wright = right - diff;
                lineBox.Words[i].Left = wright - lineBox.Words[i].Width;
            }
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
            // actually declare vertical-align" signal by itself: VerticalAlign is unconditionally
            // copied by InheritStyle's "always" section, so the shadow box already has a non-null
            // value (matching the block's own) even when no matched rule ever mentions vertical-align.
            // Comparing against the block's own value is a cheap, good-enough proxy for "was this
            // actually declared" (it only under-detects the harmless edge case of a rule re-declaring
            // the same value the block already had).
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
                var effectiveVerticalAlign = firstLineVerticalAlign ?? box.VerticalAlign;

                //Important notes on http://www.w3.org/TR/CSS21/tables.html#height-layout
                switch (effectiveVerticalAlign)
                {
                    case CssConstants.Sub:
                        lineBox.SetBaseLine(box, baseline + rect.Height * .5f);
                        break;
                    case CssConstants.Super:
                        lineBox.SetBaseLine(box, baseline - rect.Height * .2f);
                        break;
                    case CssConstants.Top:
                        OffsetBoxWithinLine(lineBox, box, lineTop - rect.Top);
                        break;
                    case CssConstants.Bottom:
                        OffsetBoxWithinLine(lineBox, box, lineBottom - rect.Bottom);
                        break;
                    case CssConstants.Middle:
                        var lineMiddleTop = lineTop + (lineBottom - lineTop - rect.Height) / 2;
                        OffsetBoxWithinLine(lineBox, box, lineMiddleTop - rect.Top);
                        break;
                    case CssConstants.TextTop:
                    case CssConstants.TextBottom:
                        // Align with the top/bottom of the parent's font box, per CSS1 §5.6.11 - not
                        // the line's own extents (that's top/bottom above), so this references the
                        // parent element's own ActualFont rather than lineTop/lineBottom.
                        //
                        // "box" here may be an anonymous text-node box rather than the real element
                        // vertical-align was declared on (e.g. for "<span style='vertical-align:
                        // text-top'>text</span>", the word's owner is an anonymous child of the span,
                        // whose own ParentBox is the span itself, not the span's real CSS parent) - so
                        // walk up to the nearest ancestor that has an HtmlTag before reading ParentBox,
                        // otherwise this would reference the span's own font instead of its parent's.
                        var styledBox = box;
                        while (styledBox.HtmlTag is null && styledBox.ParentBox is not null)
                            styledBox = styledBox.ParentBox;
                        var referenceFont = (styledBox.ParentBox ?? styledBox).ActualFont;
                        var fontTop = baseline - referenceFont.Ascent;
                        var target = effectiveVerticalAlign == CssConstants.TextTop
                            ? fontTop
                            : fontTop + referenceFont.Height - rect.Height;
                        OffsetBoxWithinLine(lineBox, box, target - rect.Top);
                        break;
                    default:
                        //case: baseline
                        lineBox.SetBaseLine(box, baseline);
                        break;
                }
            }
        }

        /// <summary>
        /// Shifts a single box's rectangle and words within one specific line box by <paramref name="delta"/>.
        /// Scoped to this <paramref name="lineBox"/> only (via <see cref="CssLineBox.WordsOf"/>/
        /// <see cref="CssLineBox.Rectangles"/>) rather than <see cref="CssBox.OffsetTop"/>'s all-lines,
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
        /// Applies centered alignment to the text on the line-box
        /// </summary>
        /// <param name="lineBox"></param>
        private static void ApplyJustifyAlignment(CssLineBox lineBox)
        {
            if (lineBox.Equals(lineBox.OwnerBox.LineBoxes[^1]))
                return;

            var indent = lineBox.Equals(lineBox.OwnerBox.LineBoxes[0]) ? lineBox.OwnerBox.ActualTextIndent : 0f;
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
            var currentX = lineBox.OwnerBox.ClientLeft + indent;

            foreach (var word in lineBox.Words)
            {
                word.Left = currentX;
                currentX = word.Right + spacing;

                if (word == lineBox.Words[^1])
                {
                    word.Left = lineBox.OwnerBox.ClientRight - word.Width;
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

            var lastWord = line.Words[^1];
            var right = line.OwnerBox.ActualRight - line.OwnerBox.ActualPaddingRight - line.OwnerBox.ActualBorderRightWidth;
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


            var lastWord = line.Words[^1];
            var right = line.OwnerBox.ActualRight - line.OwnerBox.ActualPaddingRight - line.OwnerBox.ActualBorderRightWidth;
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