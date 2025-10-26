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
            ArgumentNullException.ThrowIfNull(imageWord.OwnerBox);

            var width = new CssLength(imageWord.OwnerBox.Width);
            var height = new CssLength(imageWord.OwnerBox.Height);

            var hasImageTagWidth = width is { Number: > 0, Unit: CssUnit.Pixels };
            var hasImageTagHeight = height is { Number: > 0, Unit: CssUnit.Pixels };
            var scaleImageHeight = false;

            if (hasImageTagWidth)
            {
                imageWord.Width = width.Number;
            }
            else if (width is { Number: > 0, IsPercentage: true })
            {
                imageWord.Width = width.Number * imageWord.OwnerBox.ContainingBlock.Size.Width;
                scaleImageHeight = true;
            }
            else if (imageWord.Image != null)
            {
                imageWord.Width = imageWord.Image.Width;
            }
            else
            {
                imageWord.Width = hasImageTagHeight ? height.Number / 1.14f : 20;
            }

            var maxWidth = new CssLength(imageWord.OwnerBox.MaxWidth);
            if (maxWidth.Number > 0)
            {
                double maxWidthVal = -1;
                if (maxWidth.Unit == CssUnit.Pixels)
                {
                    maxWidthVal = maxWidth.Number;
                }
                else if (maxWidth.IsPercentage)
                {
                    maxWidthVal = maxWidth.Number * imageWord.OwnerBox.ContainingBlock.Size.Width;
                }

                if (maxWidthVal > -1 && imageWord.Width > maxWidthVal)
                {
                    imageWord.Width = maxWidthVal;
                    scaleImageHeight = !hasImageTagHeight;
                }
            }

            if (hasImageTagHeight)
            {
                imageWord.Height = height.Number;
            }
            else if (imageWord.Image != null)
            {
                imageWord.Height = imageWord.Image.Height;
            }
            else
            {
                imageWord.Height = imageWord.Width > 0 ? imageWord.Width * 1.14f : 22.8f;
            }

            if (imageWord.Image != null)
            {
                // If only the width was set in the html tag, ratio the height.
                if ((hasImageTagWidth && !hasImageTagHeight) || scaleImageHeight)
                {
                    // Divide the given tag width with the actual image width, to get the ratio.
                    var ratio = imageWord.Width / imageWord.Image.Width;
                    imageWord.Height = imageWord.Image.Height * ratio;
                }
                // If only the height was set in the html tag, ratio the width.
                else if (hasImageTagHeight && !hasImageTagWidth)
                {
                    // Divide the given tag height with the actual image height, to get the ratio.
                    var ratio = imageWord.Height / imageWord.Image.Height;
                    imageWord.Width = imageWord.Image.Width * ratio;
                }
            }

            imageWord.Height += imageWord.OwnerBox.ActualBorderBottomWidth + imageWord.OwnerBox.ActualBorderTopWidth + imageWord.OwnerBox.ActualPaddingTop + imageWord.OwnerBox.ActualPaddingBottom;
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

        public static async ValueTask<double> GetBoxWidth(RGraphics g, CssBox box)
        {
            var width = box.ContainingBlock.ClientRight - box.ContainingBlock.ClientLeft - box.ActualMarginLeft - box.ActualMarginRight;

            if (box.Words.Count > 0)
            {
                width = box.Words.Sum(x => x.FullWidth);
            }

            if (box.Width != CssConstants.Auto && !string.IsNullOrEmpty(box.Width))
            {
                width = CssValueParser.ParseLength(box.Width, box.ContainingBlock.Size.Width, box);
            }

            if (box is { Width: CssConstants.Auto, Position: not CssConstants.Absolute })
            {
                width -= box.ActualBoxSizeIncludedWidth;
            }

            if (box is { Width: CssConstants.Auto, Position: CssConstants.Absolute })
            {
                width = await GetFitContentWidth(g, box, box.ContainingBlock.Size.Width);
            }

            return width;
        }

        public static double? GetBoxHeight(CssBox box)
        {
            var height = box.ActualBoxSizingHeight;

            if (box == box.ContainingBlock && box.HtmlContainer is not null)
            {
                height = Math.Max(height, box.HtmlContainer.PageSize.Height);
            }

            if (box.Words.Count > 0)
            {
                height = Math.Max(height, box.Words.Sum(w => w.Height));
            }

            if (CssValueParser.IsValidLength(box.MinHeight))
            {
                var minHeight = CssValueParser.ParseLength(box.MinHeight, box.ContainingBlock.Size.Height, box) + box.ActualBoxSizeIncludedHeight;

                if (minHeight > height)
                {
                    height = minHeight;
                }
            }

            if (CssValueParser.IsValidLength(box.Height))
            {
                if (!box.ContainingBlock.IsHeightCalculated && box.Height.EndsWith('%'))
                {
                    return null;
                }

                var cssHeight = CssValueParser.ParseLength(box.Height, box.ContainingBlock.Size.Height, box) + box.ActualBoxSizeIncludedHeight;

                if (cssHeight > height)
                {
                    height = cssHeight;
                }
            }

            return height;
        }

        public static void ApplyParentHeight(CssBox box)
        {
            foreach (var childBox in box.Boxes)
            {
                ApplyHeight(childBox);
                ApplyParentHeight(childBox);
            }
        }

        public static void ApplyHeight(CssBox box)
        {
            var boxHeight = GetBoxHeight(box);
            var height = boxHeight ?? 0;
            box.ActualBottom = Math.Max(box.ActualBottom, box.Location.Y + height);
            box.IsHeightCalculated = boxHeight is not null;
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

                clearance = Math.Max(clearance, siblingBox.ActualBottom);

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

                switch (childBox.Float)
                {
                    case CssConstants.Left when box.Clear is CssConstants.Right:
                    case CssConstants.Right when box.Clear is CssConstants.Left:
                        continue;
                }

                clearance = Math.Max(clearance, childBox.ActualBottom);
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

            var boxes = box.Boxes;

            if (boxes.Count is 0 && box.Words.Count > 0)
            {
                boxes = [box];
            }

            foreach (var b in boxes)
            {
                var leftSpacing = (b.Position != CssConstants.Absolute && b.Position != CssConstants.Fixed) ? b.ActualMarginLeft + b.ActualBorderLeftWidth + b.ActualPaddingLeft : 0;
                var rightSpacing = (b.Position != CssConstants.Absolute && b.Position != CssConstants.Fixed) ? b.ActualMarginRight + b.ActualBorderRightWidth + b.ActualPaddingRight : 0;

                b.RectanglesReset();
                await b.MeasureWordsSize(g);

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

                    foreach (var word in b.Words)
                    {
                        if (coordinates.MaxBottom - coordinates.CurrentY < box.ActualLineHeight)
                            coordinates.MaxBottom += box.ActualLineHeight - (coordinates.MaxBottom - coordinates.CurrentY);

                        var actualLimitRight = limitRight;
                        var lastRightIntersectingFloatBox = DomUtils.GetLastRightIntersectingFloatBox(box, coordinates, word.FullWidth);

                        if (lastRightIntersectingFloatBox is not null)
                        {
                            actualLimitRight = lastRightIntersectingFloatBox.Location.X -
                                               lastRightIntersectingFloatBox.ActualMarginLeft - rightSpacing;
                        }

                        if ((b.WhiteSpace != CssConstants.NoWrap && b.WhiteSpace != CssConstants.Pre && coordinates.CurrentX + word.Width + rightSpacing > actualLimitRight
                             && (b.WhiteSpace != CssConstants.PreWrap || !word.IsSpaces))
                            || word.IsLineBreak || wrapNoWrapBox)
                        {
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

                        if (box is { IsFixed: false, IsTableCell: false })
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
                }
                else
                {
                    await FlowBox(g, blockBox, b, limitRight, lineSpacing, lineStartX, coordinates);
                }

                coordinates.CurrentX += rightSpacing;
            }

            // handle height setting
            if (coordinates.MaxBottom - startY < box.ActualHeight)
            {
                coordinates.MaxBottom = box.ActualHeight - (coordinates.MaxBottom - startY);
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

            box.LastHostingLineBox = coordinates.Line;
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
        /// <param name="g"></param>
        /// <param name="lineBox"></param>
        private static void ApplyVerticalAlignment(CssLineBox lineBox)
        {
            var baseline = double.MinValue;

            foreach (var box in lineBox.Rectangles.Keys)
            {
                baseline = Math.Max(baseline, lineBox.Rectangles[box].Top);
            }

            var boxes = new List<CssBox>(lineBox.Rectangles.Keys);

            foreach (var box in boxes)
            {
                //Important notes on http://www.w3.org/TR/CSS21/tables.html#height-layout
                switch (box.VerticalAlign)
                {
                    case CssConstants.Sub:
                        lineBox.SetBaseLine(box, baseline + lineBox.Rectangles[box].Height * .5f);
                        break;
                    case CssConstants.Super:
                        lineBox.SetBaseLine(box, baseline - lineBox.Rectangles[box].Height * .2f);
                        break;
                    case CssConstants.TextTop:
                    case CssConstants.TextBottom:
                    case CssConstants.Top:
                    case CssConstants.Bottom:
                    case CssConstants.Middle:

                        break;
                    default:
                        //case: baseline
                        lineBox.SetBaseLine(box, baseline);
                        break;
                }
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
        /// <param name="g"></param>
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
        /// <param name="g"></param>
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