using PeachBrackets.Css;
using PeachBrackets.Html.Fragmentation;
using PeachBrackets.Html.Parse;
using PeachBrackets.Html.Entities;
using PeachBrackets.Html.Utils;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachBrackets.Html.Dom
{
    internal partial class CssBox
    {
        /// <summary>
        /// The inline-axis extent (physical height) of the multi-column strip this box's content is being
        /// laid out in, set by <see cref="TryLayoutVerticalColumnsAsync"/> on a vertical-writing-mode
        /// multi-column container and passed down to the vertical block descendants it stacks.
        /// </summary>
        /// <remarks>
        /// A vertical box with an auto <c>height</c> normally wraps against one full page's depth and then
        /// shrinks to what it holds. Inside a column that is wrong on both counts: the column is the line
        /// length, and a block-level box stretches across it
        /// (<see href="https://www.w3.org/TR/css-multicol-1/#column-gaps-and-rules">css-multicol-1</see> §3 -
        /// a column is the containing block of what it holds). A box carrying this value treats its
        /// inline extent as definite and equal to it, exactly as if its <c>height</c> had been that length.
        /// Null everywhere else, which keeps every vertical box outside a column laid out as it always was.
        /// </remarks>
        internal double? StripInlineExtent
        {
            get => _rare?.StripInlineExtent;
            set
            {
                if (_rare is null)
                {
                    if (value is null) return;
                    _rare = new BoxRareState();
                }

                _rare.StripInlineExtent = value;
            }
        }

        /// <summary>
        /// The whole inline extent of a vertical multi-column container while it lays out its columns: what a
        /// <c>column-span: all</c> child stretches across, where its other children stretch across one column.
        /// </summary>
        internal double? ColumnsFullInlineExtent
        {
            get => _rare?.ColumnsFullInlineExtent;
            set
            {
                if (_rare is null)
                {
                    if (value is null) return;
                    _rare = new BoxRareState();
                }

                _rare.ColumnsFullInlineExtent = value;
            }
        }

        /// <summary>
        /// The rules between the strips of a vertical-writing-mode multi-column container: each is a
        /// horizontal line at <c>Y</c> from <c>Left</c> to <c>Right</c>, in document space. The
        /// counterpart of <see cref="ColumnRuleSegments"/>, whose lines are vertical.
        /// </summary>
        internal List<(double Y, double Left, double Right)>? BlockAxisColumnRuleSegments
        {
            get => _rare?.BlockAxisColumnRuleSegments;
            set
            {
                if (_rare is null)
                {
                    if (value is null) return;
                    _rare = new BoxRareState();
                }

                _rare.BlockAxisColumnRuleSegments = value;
            }
        }

        /// <summary>
        /// Lays out this vertical-writing-mode multi-column container's content in its columns, or returns
        /// <see langword="false"/> - having touched nothing - when it resolves to fewer than two columns.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Columns run along the container's inline axis (<see href="https://www.w3.org/TR/css-multicol-1/#column-gaps-and-rules">
        /// css-multicol-1</see>), which under <c>vertical-rl</c>/<c>vertical-lr</c> is physical Y, so the columns
        /// are horizontal strips stacked top to bottom; the content of each flows along the block axis
        /// (physical X: right to left for <c>vertical-rl</c>, left to right for <c>vertical-lr</c>) and is
        /// <i>fragmented</i> along it, so a column is as wide as the container's block size and a longer
        /// flow continues in the next strip.
        /// </para>
        /// <para>
        /// The flow is laid out once as one long strip with the column's inline size, by the same
        /// vertical line and block flow every other vertical box uses, and is then cut at the break
        /// points css-break-3 allows - between line boxes and between block siblings, honouring
        /// <c>break-before</c>/<c>break-after</c>/<c>break-inside</c> and <c>orphans</c>/<c>widows</c>.
        /// Cutting after layout instead of resuming a layout pass is what this engine can do here: a
        /// vertical box's fragmentation axis is its block axis, the one axis the page-grid fragmentainer
        /// bands do not describe, so the cut pieces are stated to the fragment emitter as nested
        /// fragmentainers (<c>RecordCapturedInstance</c>) with the geometry each piece ended up with.
        /// </para>
        /// </remarks>
        internal async ValueTask<bool> TryLayoutVerticalColumnsAsync(Canvas g)
        {
            var container = HtmlContainer;
            if (container is null) return false;

            var heightIsAuto = !CssValueParser.IsValidLength(Height);
            var inline = heightIsAuto
                ? Math.Max(1, container.PageSize.Height)
                : Math.Max(1, CssLayoutEngine.DefiniteContentHeight(this));

            var gap = CssLayoutEngineColumns.ColumnGap(this, inline);

            var (count, columnSize) = CssLayoutEngineColumns.ResolveColumns(this, inline, gap);
            if (count < 2 || columnSize <= 0) return false;

            var widthIsAuto = !CssValueParser.IsValidLength(Width);
            var frame = WritingModeFrame.For(this);
            var clientTop = ClientTop;
            var clientLeft = ClientLeft;
            var clientRight = ClientRight;
            var available = Math.Max(0, clientRight - clientLeft);
            var originalLocationX = Location.X;
            var originalRight = ActualRight;
            var sizeBefore = container.ActualSize;

            // One long strip with the column's inline size: the container stands in for the column, which is
            // what every vertical block descendant measures its own inline size against.
            ActualBottom = clientTop + columnSize + ActualPaddingBottom + ActualBorderBottomWidth;
            StripInlineExtent = columnSize;
            ColumnsFullInlineExtent = inline;

            var detached = container.DetachFragmentainer();
            var previousSuppress = container.SuppressWordPageBreaks;
            container.SuppressWordPageBreaks = true;

            try
            {
                if (DomUtils.ContainsInlinesOnly(this))
                {
                    await CssLayoutEngine.CreateVerticalLineBoxes(g, this);
                }
                else
                {
                    await LayoutVerticalBlockChildren(g);
                }
            }
            finally
            {
                container.RestoreFragmentainer(detached);
                container.SuppressWordPageBreaks = previousSuppress;
            }

            // The long strip is not a real extent of the document, and the box's own width is settled below
            // from the strips actually used - not from the length of the long one.
            container.ActualSize = sizeBefore;
            Location = Location with { X = originalLocationX };
            ActualRight = originalRight;

            ResolvePendingVerticalStateWithinStrip(clientTop, columnSize);

            var axis = new VerticalColumnFlow.BlockAxis(clientLeft, clientRight, frame.BlockStartIsRight);
            var root = VerticalColumnFlow.Build(this, axis);

            var balances = ColumnFill.Value != ColumnFillMode.Auto;
            var (strips, used) = VerticalColumnFlow.PackAll(root, count, available, balances);

            var pitch = columnSize + gap;
            var inlineStartIsBottom = frame.InlineStartIsBottom;

            foreach (var strip in strips)
            {
                // A spanning child covers the whole inline axis; a column sits at its place along it, counted from
                // the inline start (the bottom edge under direction: rtl).
                strip.Height = strip.IsSpan ? inline : columnSize;
                strip.Dy = strip.IsSpan ? 0
                    : inlineStartIsBottom ? inline - columnSize - strip.Index * pitch
                    : strip.Index * pitch;
            }

            VerticalColumnFlow.Move(root, axis, strips);

            if (widthIsAuto)
            {
                CssLayoutEngine.ShrinkAutoWidthTo(this, frame, used);
            }

            ActualBottom = clientTop + inline + ActualPaddingBottom + ActualBorderBottomWidth;
            StripInlineExtent = null;
            ColumnsFullInlineExtent = null;

            RecordVerticalColumnRules(strips, clientTop, inline, gap, inlineStartIsBottom, axis);

            // Every strip is a fragmentainer (css-break-3 §2: "a column in multi-column layout"). A box cut
            // across strips has the geometry of its last piece alone, so each strip states what it holds.
            container.ClearCapturedInstances(this);

            if (root.Kids.Count > 0 && root.Kids[0].Line is null)
            {
                var lineStrips = VerticalColumnFlow.LineStrips(root);
                var parentContext = container.CurrentFragmentainer;

                for (var j = 0; j < strips.Count; j++)
                {
                    var strip = strips[j];
                    var top = clientTop + strip.Dy;
                    var band = (top, top + strip.Height);
                    var slot = container.HasRealPageGrid ? container.SlotStartingAt(top) : 0;
                    var (snapshot, continuing) = VerticalColumnFlow.SnapshotOf(root, j, axis, strips, lineStrips);
                    var (stripLeft, stripRight) = axis.Physical(strip.Origin, strip.Origin + strip.Extent);
                    var stripContext = new FragmentainerContext(container, this, slot, band, inheritsSuppression: true);

                    container.RecordCapturedInstance(
                        this, slot, band, (stripLeft, stripRight), snapshot, continuing,
                        stripContext, parentContext, blockAxisIsHorizontal: true);
                }
            }

            // Absolutely positioned children resolve against the container once it has its final size.
            await LayoutOutOfFlowChildrenAgain(g);

            return true;
        }

        /// <summary>
        /// The state a vertical box's layout leaves for its epilogue - the right-to-left reflection of its
        /// stacked children, its line-right floats, and its lines' text alignment - settled against the
        /// strip it was laid out in. The epilogue would settle them against the container's final height,
        /// which is the whole stack of strips and not the one these were laid out in.
        /// </summary>
        private void ResolvePendingVerticalStateWithinStrip(double clientTop, double stripSize)
        {
            var stripBottom = clientTop + stripSize;

            if (_pendingCrossAxisRtlReflection is { Count: > 0 } stacked)
            {
                foreach (var childBox in stacked)
                {
                    var delta = (clientTop + stripBottom - childBox.ActualBottom) - childBox.Location.Y;
                    if (delta != 0) childBox.OffsetTop(delta);
                }
            }

            _pendingCrossAxisRtlReflection = null;

            if (_pendingBottomFloats is { Count: > 0 } bottomFloats)
            {
                foreach (var (floated, inlineFromBottom) in bottomFloats)
                {
                    var height = floated.ActualBottom - floated.Location.Y;
                    var target = stripBottom - inlineFromBottom - floated.ActualMarginBottom - height;
                    target = Math.Max(target, clientTop + floated.ActualMarginTop);
                    if (target != floated.Location.Y) floated.OffsetTop(target - floated.Location.Y);
                }
            }

            _pendingBottomFloats = null;

            if (_pendingVerticalInlineFinalize)
            {
                var stripFrame = WritingModeFrame.ForContentBox(
                    ClientLeft, clientTop, ClientRight, stripBottom, WritingMode.Value, Direction.Value);
                CssLayoutEngine.FinalizeVerticalLineBoxes(this, stripFrame, clientTop, stripBottom);
                _pendingVerticalInlineFinalize = false;
            }
        }

        private void RecordVerticalColumnRules(
            List<VerticalColumnFlow.Strip> strips, double clientTop, double inline, double gap, bool inlineStartIsBottom,
            VerticalColumnFlow.BlockAxis axis)
        {
            BlockAxisColumnRuleSegments = null;
            if (ActualColumnRuleWidth <= 0 || ColumnRuleStyle.Value is LineStyle.None or LineStyle.Hidden) return;

            List<(double Y, double Left, double Right)>? rules = null;

            foreach (var strip in strips)
            {
                // A rule runs across the content of its run of columns, in the middle of the gap between a strip and
                // the one before it; a spanning child has none.
                if (strip.IsSpan || strip.Index == 0) continue;

                var run = strips.Where(other => other.Segment == strip.Segment).ToList();
                var runExtent = run.Max(other => other.Extent);
                var (left, right) = axis.Physical(strip.Origin, strip.Origin + runExtent);
                var pitch = strip.Height + gap;

                var y = inlineStartIsBottom
                    ? clientTop + (inline - strip.Height - (strip.Index - 1) * pitch) - gap / 2
                    : clientTop + strip.Index * pitch - gap / 2;

                (rules ??= []).Add((y, left, right));
            }

            BlockAxisColumnRuleSegments = rules;
        }
    }
}
