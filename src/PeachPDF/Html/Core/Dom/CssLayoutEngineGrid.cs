using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// Lays out a CSS Grid container (<c>display: grid</c> / <c>inline-grid</c>,
    /// <see href="https://www.w3.org/TR/css-grid-2/">CSS Grid Layout Module Level 1/2</see>).
    ///
    /// v1 scope (issue #232): explicit and auto track sizing (lengths, %, <c>fr</c>, <c>auto</c>,
    /// <c>min-content</c>/<c>max-content</c>, <c>minmax()</c>, <c>fit-content()</c>, <c>repeat()</c>
    /// including <c>auto-fill</c>/<c>auto-fit</c>), line-based placement with spans, auto-placement
    /// (<c>grid-auto-flow</c>), implicit tracks, gaps, and box/content alignment. Named lines,
    /// <c>grid-template-areas</c>, subgrid, masonry, baseline alignment, and the <c>grid</c>/
    /// <c>grid-template</c> shorthands are out of scope — see docs/html-css-support.md.
    ///
    /// The engine mirrors <see cref="CssLayoutEngineFlex"/>: it resolves the container's content width via
    /// <see cref="CssLayoutEngine.GetBoxWidth"/>, lays each item out at a provisional origin, then translates
    /// it into its final cell with <see cref="CssBox.OffsetLeft"/>/<see cref="CssBox.OffsetTop"/>.
    /// </summary>
    internal sealed class CssLayoutEngineGrid
    {
        private readonly CssBox _gridBox;

        private CssLayoutEngineGrid(CssBox gridBox)
        {
            _gridBox = gridBox;
        }

        public static async ValueTask PerformLayout(RGraphics g, CssBox gridBox)
        {
            try
            {
                await new CssLayoutEngineGrid(gridBox).Layout(g);
            }
            catch (Exception ex)
            {
                gridBox.HtmlContainer?.ReportError(HtmlRenderErrorType.Layout, "Failed grid layout", ex);
            }
        }

        /// <summary>A resolved grid track: its authored definition, its used size, and its start position.</summary>
        private sealed class Track
        {
            public required GridTrackSize Def { get; init; }
            public double Size { get; set; }
            public double Position { get; set; }
        }

        private async ValueTask Layout(RGraphics g)
        {
            // Container inline size — resolved exactly like any other block box's width (this also carries
            // the per-page reflow seam via GetBoxWidth).
            var containerWidth = await CssLayoutEngine.GetBoxWidth(g, _gridBox);
            _gridBox.ActualRight = _gridBox.Location.X + containerWidth + _gridBox.ActualBoxSizeIncludedWidth;
            _gridBox.ActualBottom = _gridBox.Location.Y;

            // Pre-apply a definite height so ClientBottom is valid for percentage row tracks / alignment.
            var hasDefiniteHeight = CssValueParser.IsValidLength(_gridBox.Height)
                                    || CssLayoutEngine.TryGetAspectRatioHeight(_gridBox, out _);
            if (hasDefiniteHeight)
            {
                var fullHeight = CssLayoutEngine.GetBoxHeight(_gridBox) ?? 0;
                _gridBox.ActualBottom = _gridBox.Location.Y + fullHeight;
            }

            var items = _gridBox.Boxes
                .Where(b => b.Display != CssConstants.None && !b.IsOutOfFlow
                            && (b.HtmlTag != null || !b.IsSpaceOrEmpty))
                .ToList();

            if (items.Count == 0)
            {
                if (!hasDefiniteHeight)
                    _gridBox.ActualBottom = _gridBox.Location.Y + _gridBox.ActualBoxSizeIncludedHeight;
                return;
            }

            var contentWidth = _gridBox.ClientRight - _gridBox.ClientLeft;
            var columnGap = ParseGap(_gridBox.FlexColumnGap, contentWidth);
            var rowGap = ParseGap(_gridBox.FlexRowGap, contentWidth);

            // ── Explicit column tracks (v1 Stage 2: length/%/auto; fr and intrinsic sizes land in later
            // stages, and default to auto here). No explicit template → a single implicit auto column.
            var colDefs = ExpandTemplate(_gridBox.GridTemplateColumns);
            if (colDefs.Count == 0) colDefs = [GridTrackSize.Auto];
            var colCount = colDefs.Count;

            var columns = SizeColumnTracks(colDefs, contentWidth, columnGap);
            AssignPositions(columns, _gridBox.ClientLeft, columnGap);

            // ── Row-major auto placement (explicit placement + spanning arrive in Stage 4).
            var rowCount = (int)Math.Ceiling(items.Count / (double)colCount);
            var placements = new (int Col, int Row)[items.Count];
            for (var k = 0; k < items.Count; k++)
                placements[k] = (k % colCount, k / colCount);

            // ── Row tracks: explicit grid-template-rows sizes where present, otherwise auto rows sized to
            // the tallest item they contain (measured at the item's column width).
            var rowDefs = ExpandTemplate(_gridBox.GridTemplateRows);
            var rows = new Track[rowCount];
            for (var r = 0; r < rowCount; r++)
            {
                var def = r < rowDefs.Count ? rowDefs[r] : GridTrackSize.Auto;
                rows[r] = new Track { Def = def, Size = ResolveFixedRow(def, hasDefiniteHeight) };
            }

            for (var k = 0; k < items.Count; k++)
            {
                var (col, row) = placements[k];
                if (!IsFixed(rows[row].Def))
                {
                    var natural = await MeasureItemHeight(g, items[k], columns[col].Size);
                    rows[row].Size = Math.Max(rows[row].Size, natural);
                }
            }

            AssignPositions(rows, _gridBox.ClientTop, rowGap);

            // ── Place each item into its cell (stretch to the cell by default — justify/align-self default
            // to stretch; explicit alignment lands in Stage 6).
            for (var k = 0; k < items.Count; k++)
            {
                var (col, row) = placements[k];
                await PlaceItemInCell(g, items[k], columns[col].Position, rows[row].Position,
                    columns[col].Size, rows[row].Size);
            }

            // ── Container size when auto.
            var gridHeight = rows.Sum(t => t.Size) + (rowCount > 1 ? rowGap * (rowCount - 1) : 0);
            if (!hasDefiniteHeight)
            {
                _gridBox.ActualBottom = _gridBox.ClientTop + gridHeight
                    + _gridBox.ActualPaddingBottom + _gridBox.ActualBorderBottomWidth;
            }

            // Inline-grid shrinks to the content extent in the inline axis (like inline-block).
            if (_gridBox.Display == CssConstants.InlineGrid && !CssValueParser.IsValidLength(_gridBox.Width) && columns.Length > 0)
            {
                var contentRight = columns[^1].Position + columns[^1].Size;
                _gridBox.ActualRight = contentRight + _gridBox.ActualPaddingRight + _gridBox.ActualBorderRightWidth;
            }
        }

        // ─── Track sizing ─────────────────────────────────────────────────────────

        /// <summary>Sizes the explicit column tracks against the container's content width. Fixed lengths
        /// and percentages resolve directly; <c>auto</c> (and, until their stages, other non-fixed sizes)
        /// share the leftover space equally.</summary>
        private Track[] SizeColumnTracks(IReadOnlyList<GridTrackSize> defs, double contentWidth, double gap)
        {
            var tracks = defs.Select(d => new Track { Def = d }).ToArray();

            double fixedTotal = 0;
            var autoCount = 0;
            foreach (var t in tracks)
            {
                if (IsFixed(t.Def))
                {
                    t.Size = ResolveFixedLength(t.Def, contentWidth);
                    fixedTotal += t.Size;
                }
                else
                {
                    autoCount++;
                }
            }

            var totalGap = tracks.Length > 1 ? gap * (tracks.Length - 1) : 0;
            var free = Math.Max(0, contentWidth - fixedTotal - totalGap);
            if (autoCount > 0)
            {
                var share = free / autoCount;
                foreach (var t in tracks)
                    if (!IsFixed(t.Def))
                        t.Size = share;
            }

            return tracks;
        }

        private static void AssignPositions(Track[] tracks, double start, double gap)
        {
            var pos = start;
            foreach (var t in tracks)
            {
                t.Position = pos;
                pos += t.Size + gap;
            }
        }

        private double ResolveFixedLength(GridTrackSize def, double contentBase) =>
            def.Kind switch
            {
                GridTrackKind.Length => CssValueParser.ParseLength(def.Value, contentBase, _gridBox),
                GridTrackKind.Percent => CssValueParser.ParseLength(def.Value, contentBase, _gridBox),
                _ => 0
            };

        private double ResolveFixedRow(GridTrackSize def, bool hasDefiniteHeight)
        {
            // A length row resolves directly; a percentage row resolves against a definite container height,
            // else it is treated as auto (indefinite — content-sized).
            if (def.Kind == GridTrackKind.Length)
                return CssValueParser.ParseLength(def.Value, 0, _gridBox);
            if (def.Kind == GridTrackKind.Percent && hasDefiniteHeight)
                return CssValueParser.ParseLength(def.Value, _gridBox.ClientBottom - _gridBox.ClientTop, _gridBox);
            return 0;
        }

        /// <summary>Whether a track has a fixed used size known before content measurement (a length, or a
        /// percentage against a definite base).</summary>
        private static bool IsFixed(GridTrackSize def) =>
            def.Kind is GridTrackKind.Length or GridTrackKind.Percent;

        // ─── Item measurement / placement (mirrors CssLayoutEngineFlex) ─────────────

        /// <summary>Measures an item's natural border-box height at a given content width, without leaving it
        /// placed (the caller translates it later). Mirrors the flex ResizeItem "poke Width → layout →
        /// restore" idiom.</summary>
        private async ValueTask<double> MeasureItemHeight(RGraphics g, CssBox box, double columnWidth)
        {
            var cssWidth = Math.Max(0, columnWidth - HorizontalMarginBorderPadding(box));
            var savedWidth = box.Width;
            box.Width = FormatLayoutUnits(cssWidth);

            box.Location = new RPoint(_gridBox.ClientLeft, _gridBox.ClientTop);
            box.ActualBottom = box.Location.Y;
            box.RectanglesReset();
            await PerformLayoutBlockified(g, box);

            box.Width = savedWidth;
            return box.ActualBoxSizingHeight + box.ActualMarginTop + box.ActualMarginBottom;
        }

        /// <summary>Sizes an item to its cell (stretch, the default) and translates it to the cell origin.</summary>
        private async ValueTask PlaceItemInCell(RGraphics g, CssBox box, double cellX, double cellY,
            double cellWidth, double cellHeight)
        {
            var stretchWidth = box.Width == CssConstants.Auto;
            var stretchHeight = box.Height == CssConstants.Auto;

            var savedWidth = box.Width;
            var savedHeight = box.Height;

            if (stretchWidth)
                box.Width = FormatLayoutUnits(Math.Max(0, cellWidth - HorizontalMarginBorderPadding(box)));
            if (stretchHeight)
                box.Height = FormatLayoutUnits(Math.Max(0, cellHeight - VerticalMarginBorderPadding(box)));

            box.Location = new RPoint(_gridBox.ClientLeft, _gridBox.ClientTop);
            box.ActualBottom = box.Location.Y;
            box.RectanglesReset();
            await PerformLayoutBlockified(g, box);

            if (stretchWidth) box.Width = savedWidth;
            if (stretchHeight) box.Height = savedHeight;

            // Translate the item's margin box so it sits at the cell origin.
            var targetX = cellX + box.ActualMarginLeft;
            var targetY = cellY + box.ActualMarginTop;
            var dx = targetX - box.Location.X;
            var dy = targetY - box.Location.Y;
            if (Math.Abs(dx) > 0.01) box.OffsetLeft(dx);
            if (Math.Abs(dy) > 0.01) box.OffsetTop(dy);
        }

        private static double HorizontalMarginBorderPadding(CssBox box) =>
            box.ActualMarginLeft + box.ActualMarginRight
            + box.ActualPaddingLeft + box.ActualPaddingRight
            + box.ActualBorderLeftWidth + box.ActualBorderRightWidth;

        private static double VerticalMarginBorderPadding(CssBox box) =>
            box.ActualMarginTop + box.ActualMarginBottom
            + box.ActualPaddingTop + box.ActualPaddingBottom
            + box.ActualBorderTopWidth + box.ActualBorderBottomWidth;

        // ─── Value helpers ──────────────────────────────────────────────────────────

        private List<GridTrackSize> ExpandTemplate(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Isi(CssConstants.None))
                return [];
            var template = GridTrackListGrammar.TryParse(CssValueParser.GetCssTokens(value));
            // Stage 2: fixed tracks only; the auto-repeat section is resolved in Stage 7.
            return template?.Tracks.ToList() ?? [];
        }

        private double ParseGap(string value, double contentBase) =>
            CssValueParser.ParseLength(value, contentBase, _gridBox);

        private static string FormatLayoutUnits(double value) =>
            value.ToString("F4", CultureInfo.InvariantCulture) + "pt";

        private static async ValueTask PerformLayoutBlockified(RGraphics g, CssBox box)
        {
            string? savedDisplay = null;
            if (box.IsInline)
            {
                savedDisplay = box.Display;
                box.Display = CssConstants.Block;
            }

            var container = box.HtmlContainer;
            var previousSuppress = container?.SuppressWordPageBreaks ?? false;
            if (container is not null)
                container.SuppressWordPageBreaks = true;

            try
            {
                await box.PerformLayout(g);
            }
            finally
            {
                if (container is not null)
                    container.SuppressWordPageBreaks = previousSuppress;
            }

            if (savedDisplay != null)
                box.Display = savedDisplay;
        }
    }
}
