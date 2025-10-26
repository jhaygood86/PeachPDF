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

using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Entities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Utility class for traversing DOM structure and execution stuff on it.
    /// </summary>
    internal sealed class DomUtils
    {
        /// <summary>
        /// Check if the given location is inside the given box deep.<br/>
        /// Check inner boxes and all lines that the given box spans to.
        /// </summary>
        /// <param name="box">the box to check</param>
        /// <param name="location">the location to check</param>
        /// <returns>true - location inside the box, false - otherwise</returns>
        public static bool IsInBox(CssBox box, RPoint location)
        {
            foreach (var line in box.Rectangles)
            {
                if (line.Value.Contains(location))
                    return true;
            }

            foreach (var childBox in box.Boxes)
            {
                if (IsInBox(childBox, location))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Check if the given box contains only inline child boxes.
        /// </summary>
        /// <param name="box">the box to check</param>
        /// <returns>true - only inline child boxes, false - otherwise</returns>
        public static bool ContainsInlinesOnly(CssBox box)
        {
            return box.Boxes.All(b => b.IsInline);
        }

        /// <summary>
        /// Recursively searches for the parent with the specified HTML Tag name
        /// </summary>
        /// <param name="root"></param>
        /// <param name="tagName"></param>
        /// <param name="box"></param>
        public static CssBox FindParent(CssBox root, string tagName, CssBox? box)
        {
            while (true)
            {
                if (box is null)
                {
                    return root;
                }

                if (box.HtmlTag != null && box.HtmlTag.Name.Equals(tagName, StringComparison.CurrentCultureIgnoreCase))
                {
                    return box.ParentBox ?? root;
                }

                box = box.ParentBox;
            }
        }

        /// <summary>
        /// Gets the previous sibling of this box.
        /// </summary>
        /// <returns>Box before this one on the tree. Null if it is the first</returns>
        public static CssBox? GetPreviousSibling(CssBox b, bool includeFloats = true)
        {
            if (b.ParentBox == null) return null;

            var index = b.ParentBox.Boxes.IndexOf(b);
            if (index <= 0) return null;
            var diff = 1;
            var sib = b.ParentBox.Boxes[index - diff];

            while ((sib.Display == CssConstants.None || sib.Position == CssConstants.Absolute || sib.Position == CssConstants.Fixed || (!includeFloats && sib.IsFloated)) && index - diff - 1 >= 0)
            {
                sib = b.ParentBox.Boxes[index - ++diff];
            }

            sib = sib.Display == CssConstants.None || sib.Position == CssConstants.Fixed || (!includeFloats && sib.IsFloated) ? null : sib;

            return sib;
        }

        public static IEnumerable<CssBox> GetFollowingSiblings(CssBox box, Predicate<CssBox> matcher, bool isConsecutive)
        {
            if (box.ParentBox == null) yield break;

            var index = box.ParentBox.Boxes.IndexOf(box);

            const int diff = 1;

            while (box.ParentBox.Boxes.Count > index + diff)
            {
                var sib = box.ParentBox.Boxes[index + diff];

                if (matcher.Invoke(sib))
                {
                    yield return sib;
                }
                else if (isConsecutive)
                {
                    yield break;
                }

                index += diff;
            }
        }

        /// <summary>
        /// Gets the previous sibling of this box.
        /// </summary>
        /// <returns>Box before this one on the tree. Null if its the first</returns>
        public static CssBox? GetPreviousContainingBlockSibling(CssBox b)
        {
            var conBlock = b;
            var index = conBlock.ParentBox!.Boxes.IndexOf(conBlock);
            while (conBlock.ParentBox != null && index < 1 && conBlock.Display != CssConstants.Block && conBlock.Display != CssConstants.Table && conBlock.Display != CssConstants.TableCell && conBlock.Display != CssConstants.ListItem)
            {
                conBlock = conBlock.ParentBox;
                index = conBlock.ParentBox != null ? conBlock.ParentBox.Boxes.IndexOf(conBlock) : -1;
            }
            conBlock = conBlock.ParentBox;

            if (conBlock == null || index <= 0) return null;
            var diff = 1;
            var sib = conBlock.Boxes[index - diff];

            while ((sib.Display == CssConstants.None || sib.Position == CssConstants.Absolute || sib.Position == CssConstants.Fixed) && index - diff - 1 >= 0)
            {
                sib = conBlock.Boxes[index - ++diff];
            }

            return sib.Display == CssConstants.None ? null : sib;
        }

        /// <summary>
        /// fix word space for first word in inline tag.
        /// </summary>
        /// <param name="box">the box to check</param>
        public static bool IsBoxHasWhitespace(CssBox box)
        {
            if (box.Words[0].IsImage || !box.Words[0].HasSpaceBefore || !box.IsInline) return false;

            var sib = GetPreviousContainingBlockSibling(box);

            return sib is { IsInline: true };
        }

        /// <summary>
        /// Get css box under the given sub-tree at the given x,y location, get the inner most.<br/>
        /// the location must be in correct scroll offset.
        /// </summary>
        /// <param name="box">the box to start search from</param>
        /// <param name="location">the location to find the box by</param>
        /// <param name="visible">Optional: if to get only visible boxes (default - true)</param>
        /// <returns>css link box if exists or null</returns>
        public static CssBox? GetCssBox(CssBox? box, RPoint location, bool visible = true)
        {
            if (box == null) return null;

            if ((visible && box.Visibility != CssConstants.Visible) ||
                (!box.Bounds.IsEmpty && !box.Bounds.Contains(location))) return null;

            foreach (var childBox in box.Boxes)
            {
                if (CommonUtils.GetFirstValueOrDefault(box.Rectangles, box.Bounds).Contains(location))
                {
                    return GetCssBox(childBox, location) ?? childBox;
                }
            }

            return null;
        }

        /// <summary>
        /// Collect all link boxes found in the HTML tree.
        /// </summary>
        /// <param name="box">the box to start search from</param>
        /// <param name="linkBoxes">collection to add all link boxes to</param>
        public static void GetAllLinkBoxes(CssBox? box, List<CssBox> linkBoxes)
        {
            switch (box)
            {
                case null:
                    return;
                case { IsClickable: true, Visibility: CssConstants.Visible }:
                    linkBoxes.Add(box);
                    break;
            }

            foreach (var childBox in box.Boxes)
            {
                GetAllLinkBoxes(childBox, linkBoxes);
            }
        }

        /// <summary>
        /// Get css link box under the given sub-tree at the given x,y location.<br/>
        /// the location must be in correct scroll offset.
        /// </summary>
        /// <param name="box">the box to start search from</param>
        /// <param name="location">the location to find the box by</param>
        /// <returns>css link box if exists or null</returns>
        public static CssBox? GetLinkBox(CssBox? box, RPoint location)
        {
            switch (box)
            {
                case null:
                    return null;
                case { IsClickable: true, Visibility: CssConstants.Visible } when IsInBox(box, location):
                    return box;
            }

            if (!box.ClientRectangle.IsEmpty && !box.ClientRectangle.Contains(location)) return null;

            foreach (var childBox in box.Boxes)
            {
                var foundBox = GetLinkBox(childBox, location);
                if (foundBox != null)
                    return foundBox;
            }

            return null;
        }

        /// <summary>
        /// Get css box under the given sub-tree with the given id.<br/>
        /// </summary>
        /// <param name="box">the box to start search from</param>
        /// <param name="id">the id to find the box by</param>
        /// <returns>css box if exists or null</returns>
        public static CssBox? GetBoxById(CssBox? box, string? id)
        {
            if (box == null || string.IsNullOrEmpty(id)) return null;

            if (box.HtmlTag != null && id.Equals(box.HtmlTag.TryGetAttribute("id"), StringComparison.OrdinalIgnoreCase))
            {
                return box;
            }

            foreach (var childBox in box.Boxes)
            {
                var foundBox = GetBoxById(childBox, id);
                if (foundBox != null)
                    return foundBox;
            }

            return null;
        }

        /// <summary>
        /// Gets css box under the given subtree with the given tag name
        /// </summary>
        /// <param name="box">the box to start search from</param>
        /// <param name="tagName">the tag name to find the box by</param>
        /// <returns>css box if exists or null</returns>
        public static CssBox? GetBoxByTagName(CssBox? box, string? tagName)
        {
            if (box == null || string.IsNullOrEmpty(tagName)) return null;

            if (box.HtmlTag is not null && box.HtmlTag.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase))
            {
                return box;
            }

            foreach (var childBox in box.Boxes)
            {
                var foundBox = GetBoxByTagName(childBox, tagName);
                if (foundBox != null)
                    return foundBox;
            }

            return null;
        }

        /// <summary>
        /// Get css line box under the given sub-tree at the given y location or the nearest line from the top.<br/>
        /// the location must be in correct scroll offset.
        /// </summary>
        /// <param name="box">the box to start search from</param>
        /// <param name="location">the location to find the box at</param>
        /// <returns>css word box if exists or null</returns>
        public static CssLineBox? GetCssLineBox(CssBox? box, RPoint location)
        {
            CssLineBox? line = null;
            if (box != null)
            {
                if (box.LineBoxes.Count > 0)
                {
                    if (box.HtmlTag is not { Name: "td" } || box.Bounds.Contains(location))
                    {
                        foreach (var lineBox in box.LineBoxes)
                        {
                            foreach (var rect in lineBox.Rectangles)
                            {
                                if (rect.Value.Top <= location.Y)
                                {
                                    line = lineBox;
                                }

                                if (rect.Value.Top > location.Y)
                                {
                                    return line;
                                }
                            }
                        }
                    }
                }

                foreach (var childBox in box.Boxes)
                {
                    line = GetCssLineBox(childBox, location) ?? line;
                }
            }

            return line;
        }

        /// <summary>
        /// Get css word box under the given sub-tree at the given x,y location.<br/>
        /// the location must be in correct scroll offset.
        /// </summary>
        /// <param name="box">the box to start search from</param>
        /// <param name="location">the location to find the box at</param>
        /// <returns>css word box if exists or null</returns>
        public static CssRect? GetCssBoxWord(CssBox? box, RPoint location)
        {
            if (box is not { Visibility: CssConstants.Visible }) return null;

            if (box.LineBoxes.Count > 0)
            {
                foreach (var lineBox in box.LineBoxes)
                {
                    var wordBox = GetCssBoxWord(lineBox, location);
                    if (wordBox != null)
                        return wordBox;
                }
            }

            if (!box.ClientRectangle.IsEmpty && !box.ClientRectangle.Contains(location)) return null;

            foreach (var childBox in box.Boxes)
            {
                var foundWord = GetCssBoxWord(childBox, location);
                if (foundWord != null)
                {
                    return foundWord;
                }
            }

            return null;
        }

        /// <summary>
        /// Get css word box under the given sub-tree at the given x,y location.<br/>
        /// the location must be in correct scroll offset.
        /// </summary>
        /// <param name="lineBox">the line box to search in</param>
        /// <param name="location">the location to find the box at</param>
        /// <returns>css word box if exists or null</returns>
        public static CssRect? GetCssBoxWord(CssLineBox lineBox, RPoint location)
        {
            foreach (var rects in lineBox.Rectangles)
            {
                foreach (var word in rects.Key.Words)
                {
                    // add word spacing to word width so sentence won't have hols in it when moving the mouse
                    var rect = word.Rectangle;
                    rect.Width += word.OwnerBox.ActualWordSpacing;
                    if (rect.Contains(location))
                    {
                        return word;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// This returns the nearest positioned ancestor, or the root if none is found
        /// </summary>
        /// <param name="box">The box to use for locating</param>
        /// <returns>the nearest positioned ancestor, or the root if none is found</returns>
        public static CssBox GetNearestPositionedAncestor(CssBox box)
        {
            var currentBox = box;

            do
            {
                currentBox = currentBox.ParentBox;
            } while (currentBox is { IsPositioned: false, ParentBox: not null });

            return currentBox!;
        }

        public static CssBox? GetFirstIntersectingFloatBox(CssBox reference, CssFloatCoordinates coordinates, string floatProp)
        {
            while (true)
            {
                if (reference.ParentBox is null)
                {
                    return null;
                }

                var currentBoxIdx = reference.ParentBox.Boxes.IndexOf(reference);

                for (var i = 0; i < currentBoxIdx; i++)
                {
                    var next = GetNextIntersectingFloatBox(reference.ParentBox.Boxes[i], coordinates, floatProp);

                    if (next is not null)
                    {
                        return next;
                    }
                }

                reference = reference.ParentBox;
            }
        }

        public static CssBox? GetLastLeftIntersectingFloatBox(CssBox box, CssLineBoxCoordinates coordinates)
        {
            var left = coordinates.CurrentX;
            CssBox? lastIntersectingFloat = null;

            do
            {
                CssFloatCoordinates floatCoordinates = new()
                {
                    Left = left,
                    Top = coordinates.CurrentY,
                    MarginLeft = box.ActualMarginLeft,
                    MarginRight = box.ActualMarginRight,
                    MaxBottom = coordinates.MaxBottom,
                    ReferenceWidth = 0,
                    Right = coordinates.MaxRight
                };

                var intersectingFloat = GetFirstIntersectingFloatBox(box, floatCoordinates, CssConstants.Left);

                if (intersectingFloat is null)
                {
                    break;
                }

                left = intersectingFloat.ActualRight + intersectingFloat.ActualMarginRight;
                lastIntersectingFloat = intersectingFloat;

            } while (true);

            return lastIntersectingFloat;
        }

        public static CssBox? GetLastRightIntersectingFloatBox(CssBox box, CssLineBoxCoordinates coordinates, double referenceWidth)
        {
            var left = coordinates.CurrentX;
            CssBox? lastIntersectingFloat = null;

            do
            {
                CssFloatCoordinates floatCoordinates = new()
                {
                    Left = left,
                    Top = coordinates.CurrentY,
                    MarginLeft = box.ActualMarginLeft,
                    MarginRight = box.ActualMarginRight,
                    MaxBottom = coordinates.MaxBottom,
                    ReferenceWidth = referenceWidth,
                    Right = left + referenceWidth
                };

                var intersectingFloat = GetFirstIntersectingFloatBox(box, floatCoordinates, CssConstants.Left);

                if (intersectingFloat is null)
                {
                    break;
                }

                left = intersectingFloat.ActualRight + intersectingFloat.ActualMarginRight;
                lastIntersectingFloat = intersectingFloat;

            } while (true);

            return lastIntersectingFloat;
        }

        public static CssBox? GetNearestParentElementBox(CssBox box)
        {
            var parentBox = box.ParentBox;

            while (parentBox is not null)
            {
                if (parentBox.HtmlTag is not null)
                {
                    return parentBox;
                }

                parentBox = parentBox.ParentBox;
            }

            return null;
        }

        public static IEnumerable<CssBox> FlattenStackingContext(CssBox box)
        {
            foreach (var childBox in box.Boxes)
            {
                if (!IsStackingContextBox(childBox))
                {
                    yield return childBox;
                }

                var flattenedChildBoxes = FlattenStackingContext(childBox);

                foreach (var flattenedStackingBox in flattenedChildBoxes)
                {
                    if (!IsStackingContextBox(flattenedStackingBox))
                    {
                        yield return flattenedStackingBox;
                    }
                }
            }
        }

        public static bool IsStackingContextBox(CssBox box)
        {
            if (box.IsRoot)
            {
                return true;
            }

            if (box.Position is CssConstants.Absolute or CssConstants.Relative && box.ZIndex is not CssConstants.Auto)
            {
                return true;
            }

            if (box.Position is CssConstants.Fixed or CssConstants.Sticky)
            {
                return true;
            }

            return false;
        }

        public static IEnumerable<List<CssBox>> GetBoxesByLayers(IEnumerable<CssBox> cssBoxes)
        {
            var boxesByLayer = new Dictionary<int, List<CssBox>>();

            foreach (var cssBox in cssBoxes)
            {
                var zIndex = 0;

                if (cssBox.ZIndex is not CssConstants.Auto)
                {
                    zIndex = int.Parse(cssBox.ZIndex);
                }

                if (!boxesByLayer.ContainsKey(zIndex))
                {
                    boxesByLayer[zIndex] = [];
                }

                boxesByLayer[zIndex].Add(cssBox);
            }

            return boxesByLayer.OrderBy(x => x.Key).Select(x => x.Value);
        }

        public static bool IsProperTableChild(CssBox box)
        {
            return box.IsTableRowGroupBox || box.Display is CssConstants.TableRow ||
                   box.Display is CssConstants.TableColumn || box.Display is CssConstants.TableColumnGroup ||
                   box.Display is CssConstants.TableCaption;
        }

        private static CssBox? GetNextIntersectingFloatBox(CssBox box, CssFloatCoordinates coordinates, string floatProp)
        {
            if (IsFloatIntersecting(coordinates, floatProp, box))
            {
                return box;
            }

            foreach (var childBox in box.Boxes)
            {
                var foundBox = GetNextIntersectingFloatBox(childBox, coordinates, floatProp);
                if (foundBox != null)
                {
                    return foundBox;
                }
            }

            return null;
        }

        private static bool IsFloatIntersecting(CssFloatCoordinates coordinates, string floatProp, CssBox targetBox)
        {
            if (!targetBox.IsFloated) return false;

            // vertical conflict
            if (!(coordinates.Top < targetBox.ActualBottom) || !(targetBox.Location.Y <= coordinates.Top)) return false;

            var targetRight = targetBox.ActualRight + targetBox.ActualMarginRight;
            var targetLeft = targetBox.Location.X - targetBox.ActualMarginLeft;

            var currentLeft = coordinates.Left - coordinates.MarginLeft;

            switch (floatProp)
            {
                case CssConstants.Left when targetRight > currentLeft && targetLeft <= currentLeft:
                case CssConstants.Right when targetLeft > coordinates.FloatRightStartX + coordinates.MarginLeft + coordinates.ReferenceWidth + coordinates.MarginRight:
                    return true;
                default:
                    return false;
            }
        }
    }
}