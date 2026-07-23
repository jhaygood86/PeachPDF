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

            // ── Explicit tracks + grid-auto-flow. Row flow (default) fixes the column count and grows rows;
            // column flow fixes the row count and grows columns. Implicit tracks cycle grid-auto-columns/rows.
            var explicitColDefs = ExpandTemplate(_gridBox.GridTemplateColumns);
            var explicitRowDefs = ExpandTemplate(_gridBox.GridTemplateRows);
            var autoColDefs = ExpandTrackSizes(_gridBox.GridAutoColumns);
            var autoRowDefs = ExpandTrackSizes(_gridBox.GridAutoRows);

            var flow = _gridBox.GridAutoFlow ?? CssConstants.Row;
            var isColumnFlow = flow.IndexOf(CssConstants.Column, StringComparison.OrdinalIgnoreCase) >= 0;
            var isDense = flow.IndexOf(CssConstants.Dense, StringComparison.OrdinalIgnoreCase) >= 0;

            // The fixed-axis (explicit) track count: columns for row flow, rows for column flow.
            var fixedCount = Math.Max(1, isColumnFlow ? explicitRowDefs.Count : explicitColDefs.Count);

            var placements = PlaceItems(items, isColumnFlow, isDense, fixedCount);
            var colCount = Math.Max(1, Math.Max(explicitColDefs.Count,
                placements.Count == 0 ? 0 : placements.Max(p => p.ColStart + p.ColSpan)));
            var rowCount = Math.Max(1, Math.Max(explicitRowDefs.Count,
                placements.Count == 0 ? 0 : placements.Max(p => p.RowStart + p.RowSpan)));

            // ── Column tracks: explicit sizes, then implicit columns cycling grid-auto-columns.
            var colDefs = BuildTrackDefs(explicitColDefs, autoColDefs, colCount);
            var columns = SizeColumnTracks(colDefs, contentWidth, columnGap);
            AssignPositions(columns, _gridBox.ClientLeft, columnGap);

            // ── Row tracks: explicit sizes, then implicit rows cycling grid-auto-rows; auto rows are sized
            // to the tallest item they contain (measured at the item's column-span width).
            var rowDefs = BuildTrackDefs(explicitRowDefs, autoRowDefs, rowCount);
            var rows = new Track[rowCount];
            for (var r = 0; r < rowCount; r++)
                rows[r] = new Track { Def = rowDefs[r], Size = ResolveFixedRow(rowDefs[r], hasDefiniteHeight) };

            // Single-row items size their auto row directly.
            foreach (var p in placements.Where(p => p.RowSpan == 1 && !IsFixed(rows[p.RowStart].Def)))
            {
                var natural = await MeasureItemHeight(g, p.Box, ColumnSpanWidth(columns, p.ColStart, p.ColSpan, columnGap));
                rows[p.RowStart].Size = Math.Max(rows[p.RowStart].Size, natural);
            }

            // Row-spanning items grow the last auto row they cover if their content exceeds the spanned rows.
            foreach (var p in placements.Where(p => p.RowSpan > 1))
            {
                var natural = await MeasureItemHeight(g, p.Box, ColumnSpanWidth(columns, p.ColStart, p.ColSpan, columnGap));
                var spanned = 0.0;
                for (var r = p.RowStart; r < p.RowStart + p.RowSpan; r++) spanned += rows[r].Size;
                spanned += rowGap * (p.RowSpan - 1);
                if (natural > spanned)
                {
                    var lastAuto = -1;
                    for (var r = p.RowStart; r < p.RowStart + p.RowSpan; r++)
                        if (!IsFixed(rows[r].Def)) lastAuto = r;
                    if (lastAuto >= 0) rows[lastAuto].Size += natural - spanned;
                }
            }

            AssignPositions(rows, _gridBox.ClientTop, rowGap);

            // ── Place each item into its (possibly spanned) cell (stretch to the cell by default —
            // justify/align-self default to stretch; explicit alignment lands in Stage 6).
            foreach (var p in placements)
            {
                var cellX = columns[p.ColStart].Position;
                var cellWidth = ColumnSpanWidth(columns, p.ColStart, p.ColSpan, columnGap);
                var cellY = rows[p.RowStart].Position;
                var cellHeight = RowSpanHeight(rows, p.RowStart, p.RowSpan, rowGap);
                await PlaceItemInCell(g, p.Box, cellX, cellY, cellWidth, cellHeight);
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

        // ─── Placement ──────────────────────────────────────────────────────────────

        /// <summary>A resolved grid item placement in 0-based track coordinates (end exclusive via span).</summary>
        private sealed class Placement
        {
            public required CssBox Box { get; init; }
            public int ColStart { get; set; }
            public int ColSpan { get; set; } = 1;
            public int RowStart { get; set; }
            public int RowSpan { get; set; } = 1;
        }

        /// <summary>
        /// Resolves every item's grid position (CSS Grid §8): explicit line-based placement and spans first,
        /// then auto-placement of the remaining items into the first free cell. The <b>minor</b> axis (the
        /// one with a fixed track count, <paramref name="fixedCount"/>) is columns for row flow and rows for
        /// column flow; the <b>major</b> axis grows implicitly. <c>dense</c> restarts the search from the
        /// origin for each auto item; sparse advances a forward cursor.
        /// </summary>
        private List<Placement> PlaceItems(List<CssBox> items, bool isColumnFlow, bool dense, int fixedCount)
        {
            var placements = new List<Placement>(items.Count);
            var occupancy = new List<bool[]>(); // [major][minor]

            bool Occupied(int major, int minor)
            {
                if (minor < 0 || minor >= fixedCount) return true;
                return major < occupancy.Count && occupancy[major][minor];
            }

            bool Fits(int major, int minor, int majorSpan, int minorSpan)
            {
                if (minor < 0 || minor + minorSpan > fixedCount) return false;
                for (var m = major; m < major + majorSpan; m++)
                    for (var n = minor; n < minor + minorSpan; n++)
                        if (Occupied(m, n)) return false;
                return true;
            }

            void Mark(int major, int minor, int majorSpan, int minorSpan)
            {
                for (var m = major; m < major + majorSpan; m++)
                {
                    while (occupancy.Count <= m) occupancy.Add(new bool[fixedCount]);
                    for (var n = minor; n < minor + minorSpan && n < fixedCount; n++)
                        occupancy[m][n] = true;
                }
            }

            // Map each item to (minorStart, minorSpan, majorStart, majorSpan). The minor axis is the fixed
            // one: columns for row flow, rows for column flow.
            var resolved = items.Select(box =>
            {
                var (colStart, colSpan) = ResolveAxis(box.GridColumnStart, box.GridColumnEnd, fixedCount);
                var (rowStart, rowSpan) = ResolveAxis(box.GridRowStart, box.GridRowEnd, fixedCount);
                return isColumnFlow
                    ? new { box, MinorStart = rowStart, MinorSpan = Math.Min(rowSpan, fixedCount), MajorStart = colStart, MajorSpan = colSpan }
                    : new { box, MinorStart = colStart, MinorSpan = Math.Min(colSpan, fixedCount), MajorStart = rowStart, MajorSpan = rowSpan };
            }).ToList();

            void Commit(CssBox box, int major, int minor, int majorSpan, int minorSpan)
            {
                Mark(major, minor, majorSpan, minorSpan);
                var p = isColumnFlow
                    ? new Placement { Box = box, ColStart = major, ColSpan = majorSpan, RowStart = minor, RowSpan = minorSpan }
                    : new Placement { Box = box, ColStart = minor, ColSpan = minorSpan, RowStart = major, RowSpan = majorSpan };
                placements.Add(p);
            }

            // Pass 1: items definite on both axes.
            foreach (var r in resolved.Where(x => x.MinorStart >= 0 && x.MajorStart >= 0))
                Commit(r.box, r.MajorStart, ClampMinor(r.MinorStart, r.MinorSpan, fixedCount), r.MajorSpan, r.MinorSpan);

            // Pass 2: the rest, in DOM order.
            var cursorMajor = 0;
            var cursorMinor = 0;
            foreach (var r in resolved.Where(x => x.MinorStart < 0 || x.MajorStart < 0))
            {
                if (r.MinorStart >= 0)
                {
                    // Definite minor, auto major: first major line (from the top) where it fits.
                    var minor = ClampMinor(r.MinorStart, r.MinorSpan, fixedCount);
                    var major = 0;
                    while (!Fits(major, minor, r.MajorSpan, r.MinorSpan)) major++;
                    Commit(r.box, major, minor, r.MajorSpan, r.MinorSpan);
                }
                else if (r.MajorStart >= 0)
                {
                    // Definite major, auto minor: first free minor position on that major line.
                    var minor = 0;
                    while (!Fits(r.MajorStart, minor, r.MajorSpan, r.MinorSpan)) minor++;
                    Commit(r.box, r.MajorStart, minor, r.MajorSpan, r.MinorSpan);
                }
                else
                {
                    // Fully auto. dense restarts from origin each time; sparse keeps a forward cursor.
                    var startMajor = dense ? 0 : cursorMajor;
                    var startMinor = dense ? 0 : cursorMinor;
                    var (major, minor) = FindFreeCell(Fits, startMajor, startMinor, r.MajorSpan, r.MinorSpan, fixedCount);
                    Commit(r.box, major, minor, r.MajorSpan, r.MinorSpan);
                    if (!dense) { cursorMajor = major; cursorMinor = minor + r.MinorSpan; }
                }
            }

            // Restore DOM order so callers/tests see items in source order.
            return placements.OrderBy(p => items.IndexOf(p.Box)).ToList();
        }

        private static (int Major, int Minor) FindFreeCell(
            Func<int, int, int, int, bool> fits, int startMajor, int startMinor, int majorSpan, int minorSpan, int fixedCount)
        {
            var major = startMajor;
            var minor = startMinor;
            while (true)
            {
                if (minor + minorSpan > fixedCount) { minor = 0; major++; continue; }
                if (fits(major, minor, majorSpan, minorSpan)) return (major, minor);
                minor++;
            }
        }

        private static int ClampMinor(int minorStart, int minorSpan, int fixedCount) =>
            Math.Max(0, Math.Min(minorStart, Math.Max(0, fixedCount - minorSpan)));

        /// <summary>
        /// Resolves one axis (<c>grid-column</c> or <c>grid-row</c>) into a 0-based start line and span.
        /// A start of -1 means "auto — assign during placement". Line numbers are 1-based in CSS; negative
        /// numbers count from the explicit end edge.
        /// </summary>
        private static (int Start, int Span) ResolveAxis(string startValue, string endValue, int explicitCount)
        {
            var start = GridLineGrammar.TryParse(CssValueParser.GetCssTokens(startValue)) ?? GridLine.Auto;
            var end = GridLineGrammar.TryParse(CssValueParser.GetCssTokens(endValue)) ?? GridLine.Auto;

            int? startLine = LineNumber(start, explicitCount);
            int? endLine = LineNumber(end, explicitCount);

            var span = 1;
            if (start.IsSpan) span = Math.Max(1, start.Value);
            else if (end.IsSpan) span = Math.Max(1, end.Value);

            if (startLine.HasValue && endLine.HasValue)
            {
                var a = Math.Min(startLine.Value, endLine.Value);
                var b = Math.Max(startLine.Value, endLine.Value);
                if (b == a) b = a + 1;
                return (a - 1, b - a);
            }

            if (startLine.HasValue)
                return (startLine.Value - 1, span);

            if (endLine.HasValue)
            {
                var s = endLine.Value - 1 - span;
                return s < 0 ? (-1, span) : (s, span);
            }

            return (-1, span); // auto position
        }

        /// <summary>The 1-based line number for a non-span, non-auto grid-line, resolving negatives from the
        /// explicit end edge; null for <c>auto</c> or a <c>span</c>.</summary>
        private static int? LineNumber(GridLine line, int explicitCount)
        {
            if (line.IsAuto || line.IsSpan) return null;
            if (line.Value < 0) return explicitCount + 1 + line.Value + 1; // -1 == last edge
            return line.Value;
        }

        private static double ColumnSpanWidth(Track[] columns, int start, int span, double gap)
        {
            var last = Math.Min(start + span - 1, columns.Length - 1);
            return columns[last].Position + columns[last].Size - columns[start].Position;
        }

        private static double RowSpanHeight(Track[] rows, int start, int span, double gap)
        {
            var last = Math.Min(start + span - 1, rows.Length - 1);
            return rows[last].Position + rows[last].Size - rows[start].Position;
        }

        // ─── Track sizing ─────────────────────────────────────────────────────────

        /// <summary>
        /// Sizes the explicit column tracks against the container's definite content width (CSS Grid §11):
        /// resolve each track's base and growth-limit, then either distribute the free space to <c>fr</c>
        /// tracks (§11.7) or, when there is no flex, maximize the intrinsic (auto) tracks toward their
        /// growth limits. Intrinsic min/max-content contributions from items land in Stage 7; until then an
        /// <c>auto</c>/intrinsic track has a zero base and shares leftover space.
        /// </summary>
        private Track[] SizeColumnTracks(IReadOnlyList<GridTrackSize> defs, double contentWidth, double gap)
        {
            var n = defs.Count;
            var tracks = defs.Select(d => new Track { Def = d }).ToArray();
            var bases = new double[n];
            var limits = new double[n];    // double.PositiveInfinity for an unbounded growth limit
            var flexes = new double[n];

            for (var i = 0; i < n; i++)
                InitTrack(defs[i], contentWidth, out bases[i], out limits[i], out flexes[i]);

            var totalGap = n > 1 ? gap * (n - 1) : 0;
            var totalFlex = flexes.Sum();

            if (totalFlex > 0)
            {
                // fr tracks absorb the leftover after the non-flex tracks' bases and the gaps.
                double nonFlexBase = 0;
                for (var i = 0; i < n; i++)
                    if (flexes[i] <= 0)
                        nonFlexBase += bases[i];

                var spaceToFill = Math.Max(0, contentWidth - nonFlexBase - totalGap);
                var frSize = FindFlexFraction(bases, flexes, spaceToFill);

                for (var i = 0; i < n; i++)
                    tracks[i].Size = flexes[i] > 0 ? Math.Max(bases[i], flexes[i] * frSize) : bases[i];
            }
            else
            {
                // No flex: start each track at its base, then grow the intrinsic (unbounded-limit) tracks
                // to share the remaining free space.
                double baseTotal = 0;
                for (var i = 0; i < n; i++) { tracks[i].Size = bases[i]; baseTotal += bases[i]; }

                var free = Math.Max(0, contentWidth - baseTotal - totalGap);
                var growable = Enumerable.Range(0, n).Where(i => double.IsPositiveInfinity(limits[i])).ToArray();
                if (growable.Length > 0 && free > 0)
                {
                    var share = free / growable.Length;
                    foreach (var i in growable)
                        tracks[i].Size += share;
                }
            }

            return tracks;
        }

        /// <summary>Resolves a track's base size, growth limit, and flex factor (CSS Grid §11.4).</summary>
        private void InitTrack(GridTrackSize def, double contentBase, out double baseSize, out double limit, out double flex)
        {
            baseSize = 0;
            limit = double.PositiveInfinity;
            flex = 0;

            switch (def.Kind)
            {
                case GridTrackKind.Length:
                case GridTrackKind.Percent:
                    baseSize = limit = ResolveFixedLength(def, contentBase);
                    break;
                case GridTrackKind.Flex:
                    flex = def.Flex;
                    break;
                case GridTrackKind.Minmax:
                    baseSize = IsFixed(def.Min) ? ResolveFixedLength(def.Min, contentBase) : 0;
                    if (def.Max.Kind == GridTrackKind.Flex)
                        flex = def.Max.Flex;
                    else
                        limit = IsFixed(def.Max) ? ResolveFixedLength(def.Max, contentBase) : double.PositiveInfinity;
                    if (!double.IsPositiveInfinity(limit) && limit < baseSize) limit = baseSize;
                    break;
                case GridTrackKind.FitContent:
                    // fit-content(L): a fixed upper cap; intrinsic base lands in Stage 7.
                    limit = ResolveFixedLength(def, contentBase);
                    break;
                case GridTrackKind.Auto:
                case GridTrackKind.MinContent:
                case GridTrackKind.MaxContent:
                    // Intrinsic — real min/max-content contributions arrive in Stage 7.
                    break;
            }
        }

        /// <summary>
        /// The CSS Grid §11.7 "find the size of an fr" loop: distributes <paramref name="spaceToFill"/>
        /// across the flex tracks in proportion to their flex factors, freezing any flex track whose base
        /// floor (e.g. the min of <c>minmax(100px, 1fr)</c>) exceeds its proportional share.
        /// </summary>
        private static double FindFlexFraction(double[] bases, double[] flexes, double spaceToFill)
        {
            var n = bases.Length;
            var frozen = new bool[n];

            while (true)
            {
                double remainingSpace = spaceToFill;
                double remainingFlex = 0;
                for (var i = 0; i < n; i++)
                {
                    if (flexes[i] <= 0) continue;
                    if (frozen[i]) remainingSpace -= bases[i];
                    else remainingFlex += flexes[i];
                }

                if (remainingFlex <= 0) return 0;

                var frSize = Math.Max(0, remainingSpace) / remainingFlex;

                var anyNewlyFrozen = false;
                for (var i = 0; i < n; i++)
                {
                    if (flexes[i] <= 0 || frozen[i]) continue;
                    if (bases[i] > flexes[i] * frSize)
                    {
                        frozen[i] = true;
                        anyNewlyFrozen = true;
                    }
                }

                if (!anyNewlyFrozen) return frSize;
            }
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
            // The auto-repeat (auto-fill/auto-fit) section is resolved in Stage 7.
            return template?.Tracks.ToList() ?? [];
        }

        /// <summary>Parses a <c>grid-auto-columns</c>/<c>grid-auto-rows</c> value into its track-size list
        /// (default a single <c>auto</c> track), used to size implicit tracks.</summary>
        private static List<GridTrackSize> ExpandTrackSizes(string value)
        {
            if (string.IsNullOrEmpty(value)) return [GridTrackSize.Auto];
            var list = GridTrackListGrammar.TryParseTrackSizeList(CssValueParser.GetCssTokens(value));
            return list is { Count: > 0 } ? list.ToList() : [GridTrackSize.Auto];
        }

        /// <summary>Builds <paramref name="count"/> track definitions: the explicit tracks, then implicit
        /// tracks cycling through <paramref name="autoDefs"/> (CSS Grid §7.5).</summary>
        private static IReadOnlyList<GridTrackSize> BuildTrackDefs(
            IReadOnlyList<GridTrackSize> explicitDefs, IReadOnlyList<GridTrackSize> autoDefs, int count)
        {
            var defs = new List<GridTrackSize>(count);
            for (var i = 0; i < count; i++)
            {
                if (i < explicitDefs.Count) defs.Add(explicitDefs[i]);
                else defs.Add(autoDefs[(i - explicitDefs.Count) % autoDefs.Count]);
            }
            return defs;
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
