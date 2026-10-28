using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Parse;
using PeachBrackets.Html.Utils;
using PeachBrackets.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using TextPosition = PeachBrackets.Text.TextPosition;

namespace PeachBrackets.Html.TextGeometry
{
    /// <summary>
    /// Changes the text of a loaded document in place: maps positions in the laid-out text back to the text boxes they came from, splices the
    /// new text in (removing text boxes it empties), and redoes for the affected paragraphs what loading does to text - bidi levels, whitespace
    /// collapsing across inline boundaries, splitting into words. It does not lay anything out; the caller does.
    /// </summary>
    internal static class TextEditor
    {
        private const char SoftHyphen = '­';

        /// <summary>A place in the source text of one text box.</summary>
        /// <param name="Box">the text box (a leaf that holds <see cref="CssBox.Text"/>)</param>
        /// <param name="Index">the UTF-16 index in that text, from 0 to its length</param>
        internal readonly record struct SourcePoint(CssBox Box, int Index);

        /// <summary>Why the whole document cannot be re-laid out in place, or null when it can.</summary>
        public static string? DocumentRefusal(HtmlContainerInt container)
        {
            if (container.Root is null) return "The document has no content.";
            if (container.StructureTagBuilder is not null) return "A structure tag sink is attached; its structure would describe the document as it was.";
            if (container.HasFootnotes) return "The document has footnotes, whose placement depends on every page.";
            if (container.HasPageFloats) return "The document has page floats, whose placement depends on every page.";
            if (container.HasSizeContainers) return "The document uses container queries, which are resolved by loading it again.";
            if (container.UseVariableInlineMeasure) return "The document gives pages different widths.";
            if (container.NamedPageElements.Count > 0) return "The document uses named pages.";
            if (container.RunningElements.Count > 0) return "The document has running elements.";
            if (DomUtils.AnyBoxHasTargetPageContent(container.Root)) return "The document refers to page numbers (target-counter), which depend on every page.";
            if (HasMultiColumn(container.Root)) return "The document has multi-column content.";

            return null;
        }

        private static bool HasMultiColumn(CssBox box)
        {
            if (box.EstablishesMultiColumnContext) return true;

            foreach (var child in box.Boxes)
            {
                if (HasMultiColumn(child)) return true;
            }

            return false;
        }

        /// <summary>
        /// The source text a position in <paramref name="runs"/> stands for. A position between two words of one run, or at the join of two
        /// runs of a line, can be the end of the earlier word or the start of the later one; <paramref name="isRangeStart"/> picks the later.
        /// </summary>
        public static (SourcePoint? Point, string? Reason) ToSource(IReadOnlyList<TextRun> runs, TextPosition position, bool isRangeStart)
        {
            if ((uint)position.RunIndex >= (uint)runs.Count)
                return (null, "The position is not in the document's text.");

            var run = runs[position.RunIndex];
            if ((uint)position.CharIndex > (uint)run.Text.Length)
                return (null, "The position is not in the text of its run.");

            TextWordSlice? chosen = null;
            foreach (var slice in run.Slices)
            {
                if (position.CharIndex < slice.TextStart || position.CharIndex > slice.TextStart + slice.Length)
                    continue;

                chosen = slice;
                if (!isRangeStart) break;
            }

            if (chosen is not { } found)
                return (null, "The position is between words and cannot be mapped to a text box.");

            var word = found.Word;
            var box = word.OwnerBox;
            if (!IsEditable(box, out var reason))
                return (null, reason);

            if (word.SourceStart < 0 || box.Text is not { } text)
                return (null, "The text there was changed by layout (a small-caps or soft-hyphen split) and cannot be mapped back.");

            var index = SourceIndexInWord(word, text, position.CharIndex - found.TextStart, found.Length);
            return index < 0 ? (null, "The position cannot be mapped to the source text.") : (new SourcePoint(box, index), null);
        }

        // Offset k into the word's own text -> index into the box's text, which is longer by the soft hyphens layout removed from the word.
        private static int SourceIndexInWord(CssRect word, string text, int k, int wordLength)
        {
            if (word.SourceLength == wordLength)
                return word.SourceStart + k;

            var index = word.SourceStart;
            var counted = 0;
            while (counted < k && index < text.Length)
            {
                if (text[index] != SoftHyphen) counted++;
                index++;
            }

            return counted == k ? index : -1;
        }

        /// <summary>Whether text edits may touch <paramref name="box"/>: ordinary document text, not generated content or foreign markup.</summary>
        private static bool IsEditable(CssBox box, out string? reason)
        {
            reason = null;
            if (box.Text is null) { reason = "The text is generated content."; }
            else if (box.IsPseudoElement || box is CssBoxMarker) reason = "The text is generated content.";
            else if (box.IsWithinForeignContent) reason = "The text is inside SVG or MathML.";
            else if (box.IsBrElement) reason = "The position is on a line break.";

            return reason is null;
        }

        /// <summary>Where a place in some box's text is in the laid-out text, or null when that box shows nothing.</summary>
        public static TextPosition? ToPosition(IReadOnlyList<TextRun> runs, SourcePoint point)
        {
            TextPosition? nearest = null;
            var nearestDistance = int.MaxValue;

            for (var r = 0; r < runs.Count; r++)
            {
                foreach (var slice in runs[r].Slices)
                {
                    var word = slice.Word;
                    if (!ReferenceEquals(word.OwnerBox, point.Box) || word.SourceStart < 0) continue;

                    var start = word.SourceStart;
                    var end = start + word.SourceLength;
                    if (point.Index >= start && point.Index <= end)
                    {
                        return new TextPosition(r, slice.TextStart + WordOffset(word, point.Box.Text!, point.Index, slice.Length));
                    }

                    // Not in a word (in the white space between two): the nearest edge of one.
                    var distance = point.Index < start ? start - point.Index : point.Index - end;
                    if (distance < nearestDistance)
                    {
                        nearestDistance = distance;
                        nearest = new TextPosition(r, point.Index < start ? slice.TextStart : slice.TextStart + slice.Length);
                    }
                }
            }

            return nearest;
        }

        private static int WordOffset(CssRect word, string text, int sourceIndex, int wordLength)
        {
            if (word.SourceLength == wordLength)
                return sourceIndex - word.SourceStart;

            var counted = 0;
            for (var i = word.SourceStart; i < sourceIndex && i < text.Length; i++)
            {
                if (text[i] != SoftHyphen) counted++;
            }

            return counted;
        }

        /// <summary>
        /// Replaces the text from <paramref name="start"/> to <paramref name="end"/> (in document order, either way round) with
        /// <paramref name="insert"/>, which goes where the range began. Text boxes the replacement empties are removed from the tree.
        /// </summary>
        /// <returns>the place just after the inserted text, or null with <paramref name="reason"/> when nothing was changed</returns>
        public static SourcePoint? Replace(HtmlContainerInt container, SourcePoint start, SourcePoint end, string insert, out string? reason)
        {
            reason = null;
            var leaves = new List<CssBox>();
            CollectLeaves(container.Root!, leaves);

            var first = IndexOfBox(leaves, start.Box);
            var last = IndexOfBox(leaves, end.Box);
            if (first < 0 || last < 0)
            {
                reason = "The text is no longer in the document.";
                return null;
            }

            if (first > last || (first == last && start.Index > end.Index))
            {
                (start, end) = (end, start);
                (first, last) = (last, first);
            }

            if (start.Index > start.Box.Text!.Length || end.Index > end.Box.Text!.Length)
            {
                reason = "The position is outside the text it names.";
                return null;
            }

            var paragraphs = new List<CssBox>();
            void Touch(CssBox leaf)
            {
                var paragraph = CssBidiParagraphResolver.ParagraphRootOf(leaf);
                if (!paragraphs.Contains(paragraph)) paragraphs.Add(paragraph);
            }

            var emptied = new List<CssBox>();
            var cleaned = FixNewLines(insert);

            if (first == last)
            {
                var text = start.Box.Text!;
                start.Box.Text = string.Concat(text.AsSpan(0, start.Index), cleaned, text.AsSpan(end.Index));
                Touch(start.Box);
                if (start.Box.Text.Length == 0) emptied.Add(start.Box);
            }
            else
            {
                start.Box.Text = string.Concat(start.Box.Text!.AsSpan(0, start.Index), cleaned);
                Touch(start.Box);
                if (start.Box.Text.Length == 0) emptied.Add(start.Box);

                // What lies between is deleted whole, except generated content, which is not part of the text.
                for (var i = first + 1; i < last; i++)
                {
                    var middle = leaves[i];
                    if (middle.IsPseudoElement || middle is CssBoxMarker) continue;

                    Touch(middle);
                    middle.Text = string.Empty;
                    emptied.Add(middle);
                }

                end.Box.Text = end.Box.Text!.Substring(end.Index);
                Touch(end.Box);
                if (end.Box.Text.Length == 0) emptied.Add(end.Box);
            }

            // Just after what was inserted: inside the box the range began in.
            var caret = new SourcePoint(start.Box, first == last ? start.Index + cleaned.Length : start.Box.Text!.Length);

            // A box with nothing left is gone. When the caret was in it, it falls back to what is next to it.
            foreach (var box in emptied)
                box.ParentBox = null;

            if (emptied.Contains(caret.Box))
                caret = CaretAfterRemoval(leaves, first, last, emptied, end, start);

            if (RedoText(paragraphs).Contains(caret.Box))
                caret = caret with { Index = caret.Index + 1 };

            return caret;
        }

        // The caret when the box it was in has been removed: the start of the next surviving text box, else the end of the previous one.
        private static SourcePoint CaretAfterRemoval(List<CssBox> leaves, int first, int last, List<CssBox> emptied, SourcePoint end, SourcePoint start)
        {
            for (var i = last; i < leaves.Count; i++)
            {
                if (!emptied.Contains(leaves[i]) && leaves[i].Text is { Length: > 0 } && ReferenceEquals(leaves[i], end.Box))
                    return new SourcePoint(leaves[i], 0);

                if (i > last && !emptied.Contains(leaves[i]) && leaves[i].Text is { Length: > 0 } && !leaves[i].IsPseudoElement)
                    return new SourcePoint(leaves[i], 0);
            }

            for (var i = first; i >= 0; i--)
            {
                if (!emptied.Contains(leaves[i]) && leaves[i].Text is { Length: > 0 } text && !leaves[i].IsPseudoElement)
                    return new SourcePoint(leaves[i], text.Length);
            }

            return new SourcePoint(start.Box, 0);
        }

        /// <summary>
        /// Inserts <paramref name="text"/> at the end of <paramref name="element"/>'s own text - into its last text box when it has one, else
        /// into a new one - so an element with no text, whose text positions do not exist, can be given some.
        /// </summary>
        public static SourcePoint? InsertInto(CssBox element, string text, out string? reason)
        {
            reason = null;
            if (element.HtmlTag is null) { reason = "The element is not an element of the document."; return null; }
            if (element.IsPseudoElement || element is CssBoxSvg or CssBoxMath or CssBoxImage or CssBoxFormField) { reason = "Text cannot be put in that kind of element."; return null; }
            if (element.IsWithinForeignContent) { reason = "The element is inside SVG or MathML."; return null; }
            if (element.DerivedStyle.ActualDisplay == Keywords.None) { reason = "The element is not displayed."; return null; }

            foreach (var child in element.Boxes)
            {
                // Mixed block and inline children would need the inline text wrapped in an anonymous block, which loading does.
                if (!child.IsInline && child.Text is null) { reason = "The element contains blocks; put the text in one of them."; return null; }
            }

            var cleaned = FixNewLines(text);
            CssBox? target = null;
            for (var i = element.Boxes.Count - 1; i >= 0; i--)
            {
                if (element.Boxes[i].Text is not null && !element.Boxes[i].IsPseudoElement && !element.Boxes[i].IsBrElement)
                {
                    target = element.Boxes[i];
                    break;
                }

                if (!element.Boxes[i].IsPseudoElement) break;
            }

            if (target is null)
            {
                target = CssBox.CreateBox(element);
                target.Text = cleaned;
            }
            else
            {
                target.Text += cleaned;
            }

            RedoText([CssBidiParagraphResolver.ParagraphRootOf(target)]);
            return new SourcePoint(target, target.Text!.Length);
        }

        /// <summary>
        /// Whether <paramref name="block"/> is a plain paragraph the structure edits can split or join: a block box in a block container,
        /// holding only inline content, with no generated content, marker, float or positioning of its own.
        /// </summary>
        private static bool IsPlainBlock(CssBox block, out string? reason)
        {
            reason = null;
            if (block.ParentBox is not { } parent ||
                block.IsInline || block.IsFloated || block.IsAbsolutelyPositioned
                || block.DerivedStyle.ActualDisplay is not (Keywords.Block or Keywords.ListItem) || parent.DerivedStyle.ActualDisplay != Keywords.Block
                || block is CssBoxSvg or CssBoxMath or CssBoxImage or CssBoxFormField || block.IsWithinForeignContent)
            {
                reason = "The text is not in a plain paragraph (a block in a block, with only inline content).";
                return false;
            }

            foreach (var child in block.Boxes)
            {
                if (child.IsMarkerPseudoElement) continue;
                if (child.IsPseudoElement || child is CssBoxMarker || (!child.IsInline && child.Text is null))
                {
                    reason = "The paragraph holds generated content or blocks of its own.";
                    return false;
                }
            }

            return true;
        }

        // The tag of a copy of an element: the same, except for the id, which names the element it was copied from (as when a browser splits one).
        private static HtmlTag? WithoutId(HtmlTag? tag)
        {
            if (tag?.Attributes is null || !tag.Attributes.ContainsKey("id")) return tag;

            var attributes = new Dictionary<string, string>(tag.Attributes, StringComparer.OrdinalIgnoreCase);
            attributes.Remove("id");
            return new HtmlTag(tag.Name, tag.IsSingle, attributes);
        }

        // A box like <paramref name="original"/> (same tag, same style), as the next sibling of it.
        private static CssBox CloneAfter(CssBox original)
        {
            var parent = original.ParentBox!;
            var index = parent.Boxes.IndexOf(original);
            var clone = CssBox.CreateBox(parent, WithoutId(original.HtmlTag));
            clone.AdoptComputedStyle(original.ComputedStyle);
            parent.Boxes.Remove(clone);
            parent.Boxes.Insert(index + 1, clone);
            return clone;
        }

        /// <summary>
        /// Splits the paragraph holding <paramref name="at"/> in two there, the way Enter does: the text after it, and the inline elements it
        /// is inside, go to a new paragraph with the same tag and style after this one.
        /// </summary>
        /// <returns>the start of the new paragraph's text (null when it has none), and the new paragraph, or null with <paramref name="reason"/></returns>
        public static (SourcePoint? Caret, CssBox? Block)? Split(HtmlContainerInt container, SourcePoint at, out string? reason)
        {
            var paragraph = CssBidiParagraphResolver.ParagraphRootOf(at.Box);
            if (!IsPlainBlock(paragraph, out reason)) return null;
            if (at.Index > at.Box.Text!.Length) { reason = "The position is outside the text it names."; return null; }

            var emptyBefore = new HashSet<CssBox>();
            CollectEmptyInlines(paragraph, emptyBefore);

            // The first box that moves: the rest of the text, or the box itself when the split is at its start.
            CssBox moving;
            var leaf = at.Box;
            if (at.Index == 0)
            {
                moving = leaf;
            }
            else if (at.Index >= leaf.Text!.Length)
            {
                moving = NextSibling(leaf)!;
            }
            else
            {
                var tail = CssBox.CreateBox(leaf.ParentBox!);
                tail.Text = leaf.Text.Substring(at.Index);
                leaf.Text = leaf.Text.Substring(0, at.Index);
                var siblings = leaf.ParentBox!.Boxes;
                siblings.Remove(tail);
                siblings.Insert(siblings.IndexOf(leaf) + 1, tail);
                moving = tail;
            }

            // Climb out of the inline elements the split is inside, cloning each so the tail keeps its style.
            var level = moving?.ParentBox ?? leaf.ParentBox!;
            while (!ReferenceEquals(level, paragraph))
            {
                var clone = CloneAfter(level);
                if (moving is not null)
                {
                    foreach (var child in level.Boxes.SkipWhile(b => !ReferenceEquals(b, moving)).ToList())
                        child.ParentBox = clone;
                }

                moving = clone;
                level = level.ParentBox!;
            }

            var block = CloneAfter(paragraph);
            if (moving is not null)
            {
                foreach (var child in paragraph.Boxes.SkipWhile(b => !ReferenceEquals(b, moving)).ToList())
                    child.ParentBox = block;
            }

            RemoveEmptyInlines(paragraph, emptyBefore);
            RecascadeAround(container, paragraph, block);
            RedoText([paragraph, block]);

            var leaves = new List<CssBox>();
            CollectLeaves(block, leaves);
            SourcePoint? caret = leaves.Count > 0 ? new SourcePoint(leaves[0], 0) : null;
            return (caret, block);
        }

        // The boxes a structural edit changes the position of: the paragraphs it made or kept, and every sibling after them, whose style may depend
        // on what comes before it (:first-child, :nth-child(), :nth-last-child(), adjacent and general sibling combinators).
        private static void RecascadeAround(HtmlContainerInt container, CssBox first, CssBox? second)
        {
            var last = second ?? first;
            var siblings = last.ParentBox!.Boxes;

            DomParser.RecascadeSubtree(container, first);
            if (second is not null) DomParser.RecascadeSubtree(container, second);
            for (var i = siblings.IndexOf(last) + 1; i < siblings.Count; i++)
                DomParser.RecascadeSubtree(container, siblings[i]);
            DomParser.RefreshListMarkers(last.ParentBox!);
        }

        /// <summary>The inline elements a range of text can be wrapped in.</summary>
        private static readonly HashSet<string> WrappingTags = new(StringComparer.OrdinalIgnoreCase)
        {
            "a", "abbr", "b", "bdi", "bdo", "cite", "code", "del", "dfn", "em", "i", "ins", "kbd", "mark", "q", "s", "samp", "small", "span", "strong", "sub",
            "sup", "time", "u", "var"
        };

        // The text box after <paramref name="leaf"/> that takes everything of its text from <paramref name="index"/> on.
        private static CssBox SplitLeaf(CssBox leaf, int index)
        {
            var parent = leaf.ParentBox!;
            var tail = CssBox.CreateBox(parent);
            tail.Text = leaf.Text!.Substring(index);
            leaf.Text = leaf.Text.Substring(0, index);
            parent.Boxes.Remove(tail);
            parent.Boxes.Insert(parent.Boxes.IndexOf(leaf) + 1, tail);
            return tail;
        }

        /// <summary>
        /// Wraps the text from <paramref name="start"/> to <paramref name="end"/> in a new inline element: the text boxes at either end are cut where the
        /// range ends, and the boxes it covers move into the element, which is cascaded as if it had been in the markup. The range must lie in one
        /// run of text boxes with the same parent (not across inline elements or paragraphs), and the element must be one of <see cref="WrappingTags"/>.
        /// </summary>
        /// <returns>the end of the wrapped text and the new element, or null with <paramref name="reason"/></returns>
        public static (SourcePoint? Caret, CssBox? Element)? Wrap(
            HtmlContainerInt container, SourcePoint start, SourcePoint end, string tagName, IReadOnlyDictionary<string, string>? attributes, out string? reason)
        {
            reason = null;
            if (!WrappingTags.Contains(tagName)) { reason = $"<{tagName}> is not an element a range of text can be wrapped in."; return null; }

            if (attributes is not null)
            {
                foreach (var name in attributes.Keys)
                {
                    if (name.Length == 0 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') || name.StartsWith("on", StringComparison.OrdinalIgnoreCase))
                    {
                        reason = $"The attribute \"{name}\" cannot be set.";
                        return null;
                    }
                }
            }

            var leaves = new List<CssBox>();
            CollectLeaves(container.Root!, leaves);
            var first = IndexOfBox(leaves, start.Box);
            var last = IndexOfBox(leaves, end.Box);
            if (first < 0 || last < 0) { reason = "The text is no longer in the document."; return null; }

            if (first > last || (first == last && start.Index > end.Index))
            {
                (start, end) = (end, start);
                (first, last) = (last, first);
            }

            var parent = start.Box.ParentBox!;
            if (!ReferenceEquals(parent, end.Box.ParentBox) || parent.IsWithinForeignContent)
            {
                reason = "The range crosses the boundary of an element; wrap text that is in one run of text.";
                return null;
            }

            // Everything between the two ends must be plain text of that parent too, not another element's.
            for (var i = first; i <= last; i++)
            {
                if (!ReferenceEquals(leaves[i].ParentBox, parent) || !IsPlainText(leaves[i]))
                {
                    reason = "The range crosses the boundary of an element; wrap text that is in one run of text.";
                    return null;
                }
            }

            if (start.Index > start.Box.Text!.Length || end.Index > end.Box.Text!.Length) { reason = "The position is outside the text it names."; return null; }
            if (first == last && start.Index == end.Index) { reason = "The range is empty."; return null; }

            // Cut the ends: the covered text boxes are then exactly the ones to move.
            var coveredFirst = start.Box;
            var coveredLast = end.Box;
            if (end.Index < end.Box.Text.Length) SplitLeaf(end.Box, end.Index);
            if (start.Index > 0)
            {
                var middle = SplitLeaf(start.Box, start.Index);
                coveredFirst = middle;
                if (first == last) coveredLast = middle;
            }

            var covered = new List<CssBox>();
            var siblings = parent.Boxes;
            for (var i = siblings.IndexOf(coveredFirst); i <= siblings.IndexOf(coveredLast); i++)
                covered.Add(siblings[i]);

            var attributesCopy = attributes?.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
            var element = CssBox.CreateBox(new HtmlTag(tagName.ToLowerInvariant(), false, attributesCopy), parent);
            siblings.Remove(element);
            siblings.Insert(siblings.IndexOf(covered[0]), element);
            // New text boxes, not the old ones moved: a box keeps what it derived from its style (its font, for one) and a move changes the style.
            foreach (var box in covered)
            {
                var copy = CssBox.CreateBox(element);
                copy.Text = box.Text;
                box.ParentBox = null;
            }

            DomParser.RecascadeSubtree(container, element);
            var paragraph = CssBidiParagraphResolver.ParagraphRootOf(element.Boxes[0]);
            var kept = RedoText([paragraph]);

            var tailBox = element.Boxes[^1];
            var caret = new SourcePoint(tailBox, tailBox.Text!.Length + (kept.Contains(tailBox) ? 1 : 0));
            return (caret, element);
        }

        /// <summary>
        /// Takes <paramref name="element"/> (an inline element that holds only text and other inline elements) out of the tree, leaving what was inside
        /// it in its place; text that then touches text is joined, as the markup without the element would have it.
        /// </summary>
        /// <returns>the start of what was inside it (null when it held no text), or null with <paramref name="reason"/></returns>
        public static (SourcePoint? Caret, bool Unwrapped)? Unwrap(HtmlContainerInt container, CssBox element, out string? reason)
        {
            reason = null;
            if (element.HtmlTag is null || element.ParentBox is not { } parent || !element.IsInline || element.IsPseudoElement
                || element is CssBoxSvg or CssBoxMath or CssBoxImage or CssBoxFormField || element.IsWithinForeignContent
                || !WrappingTags.Contains(element.HtmlTag.Name))
            {
                reason = "Only an inline element that wraps text (strong, em, a, span and the like) can be unwrapped.";
                return null;
            }

            var paragraph = CssBidiParagraphResolver.ParagraphRootOf(element);
            foreach (var child in element.Boxes)
            {
                if (!IsPlainText(child))
                {
                    reason = "The element holds other elements or generated content; unwrap the inner ones first.";
                    return null;
                }
            }

            var index = parent.Boxes.IndexOf(element);

            // New text boxes in the element's place (see Wrap); the text next to them may now be one text.
            var moved = new List<CssBox>();
            foreach (var child in element.Boxes)
            {
                var copy = CssBox.CreateBox(parent);
                copy.Text = child.Text;
                parent.Boxes.Remove(copy);
                parent.Boxes.Insert(index++, copy);
                moved.Add(copy);
            }

            element.ParentBox = null;

            for (var i = parent.Boxes.Count - 2; i >= 0; i--)
            {
                if (IsPlainText(parent.Boxes[i]) && IsPlainText(parent.Boxes[i + 1]))
                {
                    parent.Boxes[i].Text += parent.Boxes[i + 1].Text;
                    parent.Boxes[i + 1].ParentBox = null;
                }
            }

            RedoText([paragraph]);
            var firstMoved = moved.FirstOrDefault(m => m.ParentBox is not null && m.Text is not null);
            return (firstMoved is null ? null : new SourcePoint(firstMoved, 0), true);
        }

        private static bool IsPlainText(CssBox box) =>
            box.Text is not null && box.Boxes.Count == 0 && box.HtmlTag is null && !box.IsPseudoElement && box is not CssBoxMarker;

        private static CssBox? NextSibling(CssBox box)
        {
            var siblings = box.ParentBox!.Boxes;
            var index = siblings.IndexOf(box);
            return index + 1 < siblings.Count ? siblings[index + 1] : null;
        }

        // Inline elements with nothing in them before a split (link targets, empty spans): they are not the split's to remove.
        private static void CollectEmptyInlines(CssBox box, HashSet<CssBox> into)
        {
            foreach (var child in box.Boxes)
            {
                CollectEmptyInlines(child, into);
                if (child.Boxes.Count == 0 && child.Text is null) into.Add(child);
            }
        }

        // Inline elements a split left with nothing in them.
        private static void RemoveEmptyInlines(CssBox box, HashSet<CssBox> keep)
        {
            for (var i = box.Boxes.Count - 1; i >= 0; i--)
            {
                var child = box.Boxes[i];
                RemoveEmptyInlines(child, keep);
                if (!keep.Contains(child) && child.Boxes.Count == 0 && child.Text is null && child.IsInline && child.HtmlTag is { } tag
                    && child is not (CssBoxImage or CssBoxSvg or CssBoxMath or CssBoxFormField) && !child.IsBrElement
                    && tag.Name is not (HtmlConstants.Img or "br"))
                {
                    child.ParentBox = null;
                }
            }
        }

        /// <summary>
        /// Joins the paragraph holding <paramref name="at"/> with the one after it, the way Delete at the end of a paragraph (or Backspace at the
        /// start of the next) does: the next paragraph's content moves to the end of this one and the paragraph is removed.
        /// </summary>
        /// <returns>the place where the two met (null when the second had no text), or null with <paramref name="reason"/></returns>
        public static (SourcePoint? Caret, bool Joined)? Join(HtmlContainerInt container, SourcePoint at, out string? reason)
        {
            var first = CssBidiParagraphResolver.ParagraphRootOf(at.Box);
            if (!IsPlainBlock(first, out reason)) return null;

            var next = NextSibling(first);
            if (next is null || !IsPlainBlock(next, out reason))
            {
                reason ??= "There is no paragraph after it to join.";
                return null;
            }

            if (first.HtmlTag?.Name != next.HtmlTag?.Name)
            {
                reason = "The two paragraphs are different kinds of element.";
                return null;
            }

            var firstLeaves = new List<CssBox>();
            CollectLeaves(first, firstLeaves);
            var nextLeaves = new List<CssBox>();
            CollectLeaves(next, nextLeaves);
            var last = firstLeaves.Count > 0 ? firstLeaves[^1] : null;
            var lastLength = last?.Text?.Length ?? 0;

            // A list item's marker belongs to the item, so the second one's goes with it.
            var moved = next.Boxes.Where(b => !b.IsMarkerPseudoElement).ToList();
            foreach (var child in moved)
                child.ParentBox = first;

            next.ParentBox = null;

            // Two plain texts that now touch are one text node in the markup this describes (and shape as one, across the join).
            if (last is not null && moved.Count > 0 && ReferenceEquals(last.ParentBox, first) && IsPlainText(last) && IsPlainText(moved[0]))
            {
                last.Text += moved[0].Text;
                moved[0].ParentBox = null;
            }

            RecascadeAround(container, first, null);
            var kept = RedoText([first]);
            if (last is not null && kept.Contains(last)) lastLength++;

            SourcePoint? caret = last is { Text: not null } && last.ParentBox is not null ? new SourcePoint(last, lastLength)
                : nextLeaves.Count > 0 ? new SourcePoint(nextLeaves[0], 0) : null;
            return (caret, true);
        }

        private static string FixNewLines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

        // Everything loading does to text that depends on its neighbours, redone for each paragraph a change touched: the bidi levels (a text
        // box's depend on the whole paragraph), the collapsing of a space run that crosses an inline boundary, and the split into words.
        private static HashSet<CssBox> RedoText(List<CssBox> paragraphs)
        {
            // Loading removed the leading space of a text box whose run continued from the box before it. An edit may have ended that run, so
            // each such space is put back first and the collapse below decides again; the ones that stay are returned (their indices moved by one).
            var kept = new HashSet<CssBox>();
            var restored = new List<(CssBox Box, int Length)>();

            foreach (var paragraph in paragraphs)
            {
                var all = new List<CssBox>();
                CollectLeaves(paragraph, all);
                foreach (var leaf in all)
                {
                    if (!leaf.LeadingWhitespaceCollapsed || leaf.Text is null) continue;

                    restored.Add((leaf, leaf.Text.Length));
                    leaf.LeadingWhitespaceCollapsed = false;
                    leaf.Text = " " + leaf.Text;
                }
            }

            foreach (var paragraph in paragraphs)
            {
                CssBidiParagraphResolver.AssignBidiLevels(paragraph);

                var leaves = new List<CssBox>();
                CollectLeaves(paragraph, leaves);
                foreach (var leaf in leaves)
                {
                    if (!leaf.IsPseudoElement && leaf is not CssBoxMarker && leaf.Text is not null)
                        leaf.ParseToWords();
                }

                DomParser.CollapseWhitespaceAcrossInlineBoundaries(paragraph);
            }

            foreach (var (box, length) in restored)
            {
                if (box.Text!.Length > length) kept.Add(box);
            }

            return kept;
        }

        private static int IndexOfBox(List<CssBox> boxes, CssBox box)
        {
            for (var i = 0; i < boxes.Count; i++)
            {
                if (ReferenceEquals(boxes[i], box)) return i;
            }

            return -1;
        }

        /// <summary>The text boxes under <paramref name="box"/> in document order.</summary>
        private static void CollectLeaves(CssBox box, List<CssBox> leaves)
        {
            if (box is CssBoxSvg or CssBoxMath) return;
            if (box.DerivedStyle.ActualDisplay == Keywords.None) return;

            if (box.Text is not null && box.Boxes.Count == 0)
            {
                leaves.Add(box);
                return;
            }

            foreach (var child in box.Boxes)
                CollectLeaves(child, leaves);
        }
    }
}
