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
    /// The parent-grid track geometry threaded into a <c>subgrid</c> grid item so it lays its own items out
    /// against the parent's tracks (CSS Grid Level 2 §9). Set transiently on <see cref="CssBoxProperties.SubgridContext"/>
    /// by the parent engine immediately before laying the child out, then cleared. Sizes (not absolute
    /// positions) are adopted so the parent's cell translation lands the child's tracks on the parent lines.
    /// </summary>
    internal sealed class GridSubgridContext
    {
        /// <summary>The parent's spanned column-track sizes the child adopts; null ⇒ columns are not
        /// subgridded (the child sizes its own columns).</summary>
        public IReadOnlyList<double>? ColumnSizes { get; set; }

        /// <summary>Each adopted column track's start position <b>relative to the first spanned track</b>, so the
        /// child reproduces the parent's exact line positions (including any <c>justify-content</c> space
        /// distribution) once the parent translates its cell into place.</summary>
        public IReadOnlyList<double>? ColumnOffsets { get; set; }

        /// <summary>The number of subgridded row tracks (= the item's row span in the parent).</summary>
        public int RowCount { get; set; }

        /// <summary>The parent's spanned row-track sizes the child adopts; null ⇒ <b>report mode</b> — the child
        /// self-sizes its rows and writes them to <see cref="ReportedRowSizes"/> for the parent to consume.</summary>
        public IReadOnlyList<double>? RowSizes { get; set; }

        /// <summary>Each adopted row track's start position relative to the first spanned row (see
        /// <see cref="ColumnOffsets"/>).</summary>
        public IReadOnlyList<double>? RowOffsets { get; set; }

        /// <summary>Report-mode output: the child's self-sized per-subrow heights (one per adopted row).</summary>
        public double[]? ReportedRowSizes { get; set; }
    }

    /// <summary>
    /// Lays out a CSS Grid container (<c>display: grid</c> / <c>inline-grid</c>,
    /// <see href="https://www.w3.org/TR/css-grid-2/">CSS Grid Layout Module Level 1/2</see>).
    ///
    /// Scope: explicit and auto track sizing (lengths, %, <c>fr</c>, <c>auto</c>,
    /// <c>min-content</c>/<c>max-content</c>, <c>minmax()</c>, <c>fit-content()</c>, <c>repeat()</c>
    /// including <c>auto-fill</c>/<c>auto-fit</c>), line-based placement with spans, named lines and
    /// <c>grid-template-areas</c>, auto-placement (<c>grid-auto-flow</c>), implicit tracks, gaps,
    /// box/content alignment (including block-axis <c>baseline</c> item alignment), and subgrid (Level 2 §9,
    /// on either or both axes, via <see cref="GridSubgridContext"/>). Masonry and inline-axis (justify)
    /// baseline alignment are out of scope — see docs/html-css-support.md.
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

            // The templates were parsed once at cascade time (CssProperty<GridTemplate>); read them directly.
            var colTemplate = _gridBox.GridTemplateColumns.Value;
            var rowTemplate = _gridBox.GridTemplateRows.Value;

            // ── Subgrid (CSS Grid Level 2 §9): a grid item whose column/row template is `subgrid` adopts the
            // parent grid's tracks along that axis. The parent threads the spanned track geometry in through
            // _gridBox.SubgridContext; with no parent grid (context null / no column sizes) `subgrid` behaves
            // as `none` (falls through to the normal implicit-auto-grid path).
            var subgrid = _gridBox.SubgridContext;
            var colSubgrid = colTemplate?.IsSubgrid == true && subgrid?.ColumnSizes is not null;
            var rowSubgrid = rowTemplate?.IsSubgrid == true && subgrid is not null;

            // ── Explicit tracks + grid-auto-flow. Row flow (default) fixes the column count and grows rows;
            // column flow fixes the row count and grows columns. Implicit tracks cycle grid-auto-columns/rows.
            // A column template's repeat(auto-fill|auto-fit, …) is resolved to a concrete count here. A
            // subgridded axis uses one auto placeholder per adopted track so the count/placement logic is
            // unchanged; the real (adopted) sizes replace the tracks after placement.
            List<GridTrackSize> explicitColDefs;
            (int Start, int Length)? autoFitRange;
            if (colSubgrid && subgrid?.ColumnSizes is { } adoptedColumns)
            {
                explicitColDefs = Enumerable.Repeat(GridTrackSize.Auto, adoptedColumns.Count).ToList();
                autoFitRange = null;
            }
            else
            {
                (explicitColDefs, autoFitRange) = ResolveColumnTemplate(colTemplate, contentWidth, columnGap);
            }

            var explicitRowDefs = rowSubgrid && subgrid is not null
                ? Enumerable.Repeat(GridTrackSize.Auto, subgrid.RowCount).ToList()
                : ExpandTemplate(rowTemplate);
            var autoColDefs = ExpandTrackSizes(_gridBox.GridAutoColumns);
            var autoRowDefs = ExpandTrackSizes(_gridBox.GridAutoRows);

            var flow = _gridBox.GridAutoFlow ?? CssConstants.Row;
            var isColumnFlow = flow.IndexOf(CssConstants.Column, StringComparison.OrdinalIgnoreCase) >= 0;
            var isDense = flow.IndexOf(CssConstants.Dense, StringComparison.OrdinalIgnoreCase) >= 0;

            // Per-axis named-line tables: [name] from the templates, plus name-start/name-end lines each
            // grid-template-areas area contributes.
            var colLineNames = BuildLineNames(colTemplate);
            var rowLineNames = BuildLineNames(rowTemplate);

            var areas = string.IsNullOrEmpty(_gridBox.GridTemplateAreas) || _gridBox.GridTemplateAreas.Isi(CssConstants.None)
                ? null
                : GridTemplateAreasGrammar.TryParse(CssValueParser.GetCssTokens(_gridBox.GridTemplateAreas));
            if (areas is not null)
            {
                foreach (var (name, b) in areas.Areas)
                {
                    AddLine(colLineNames, name + "-start", b.C1 + 1);
                    AddLine(colLineNames, name + "-end", b.C2 + 2);
                    AddLine(rowLineNames, name + "-start", b.R1 + 1);
                    AddLine(rowLineNames, name + "-end", b.R2 + 2);
                }
                // The area grid establishes the explicit grid size; pad with auto tracks (areas never
                // resize authored tracks). This drives fixedCount/colCount/rowCount below.
                while (explicitColDefs.Count < areas.ColCount) explicitColDefs.Add(GridTrackSize.Auto);
                while (explicitRowDefs.Count < areas.RowCount) explicitRowDefs.Add(GridTrackSize.Auto);
            }

            // The fixed-axis (explicit) track count: columns for row flow, rows for column flow.
            var fixedCount = Math.Max(1, isColumnFlow ? explicitRowDefs.Count : explicitColDefs.Count);

            var placements = PlaceItems(items, isColumnFlow, isDense, fixedCount, colLineNames, rowLineNames);
            var colCount = Math.Max(1, Math.Max(explicitColDefs.Count,
                placements.Count == 0 ? 0 : placements.Max(p => p.ColStart + p.ColSpan)));
            var rowCount = Math.Max(1, Math.Max(explicitRowDefs.Count,
                placements.Count == 0 ? 0 : placements.Max(p => p.RowStart + p.RowSpan)));

            // ── Column tracks. A subgridded column axis adopts the parent's spanned track sizes directly
            // (positioned from this grid's own client origin so the parent's cell translation lands them on the
            // parent lines); otherwise size them normally.
            Track[] columns;
            if (colSubgrid && subgrid is { ColumnSizes: { } adoptedColumnSizes, ColumnOffsets: { } adoptedColumnOffsets })
            {
                columns = BuildAdoptedTracks(adoptedColumnSizes, adoptedColumnOffsets, _gridBox.ClientLeft, colCount);
            }
            else
            {
                var colDefs = BuildTrackDefs(explicitColDefs, autoColDefs, colCount);

                // auto-fit collapses any repeated column that ended up with no items in it.
                var collapsed = new bool[colCount];
                if (autoFitRange is var (rangeStart, rangeLen) && rangeLen > 0)
                {
                    var usedCol = new bool[colCount];
                    foreach (var p in placements)
                        for (var c = p.ColStart; c < p.ColStart + p.ColSpan && c < colCount; c++)
                            usedCol[c] = true;
                    for (var c = rangeStart; c < rangeStart + rangeLen && c < colCount; c++)
                        if (!usedCol[c]) collapsed[c] = true;
                }

                // Intrinsic (min/max-content) contributions for auto/min-content/max-content/fit-content columns.
                var (colMinContent, colMaxContent) =
                    await MeasureColumnIntrinsics(g, placements, colDefs, colCount, contentWidth, columnGap);

                var stretchColumns = IsStretch(_gridBox.JustifyContent);
                columns = SizeColumnTracks(colDefs, contentWidth, columnGap, collapsed,
                    colMinContent, colMaxContent, stretchColumns);
                PositionTracks(columns, _gridBox.ClientLeft, contentWidth, columnGap, _gridBox.JustifyContent);
            }

            // ── Row tracks. A subgridded row axis adopts the parent's spanned row sizes; otherwise size the
            // explicit/implicit rows to their tallest item (measured at the item's column-span width).
            Track[] rows;
            if (rowSubgrid && subgrid is { RowSizes: { } adoptedRowSizes, RowOffsets: { } adoptedRowOffsets })
            {
                rows = BuildAdoptedTracks(adoptedRowSizes, adoptedRowOffsets, _gridBox.ClientTop, rowCount);
            }
            else
            {
                // A percentage row track resolves only against a definite container height; against an
                // indefinite height it behaves as `auto` (content-sized) per §7.2.1 — so demote percentage
                // rows to auto here rather than seeding them to 0 and leaving them fixed (which collapsed
                // them). Definite height keeps percentage rows resolving normally.
                var effectiveRowDefs = hasDefiniteHeight ? explicitRowDefs : explicitRowDefs.Select(PercentToAuto).ToList();
                var effectiveAutoRowDefs = hasDefiniteHeight ? autoRowDefs : autoRowDefs.Select(PercentToAuto).ToList();
                var rowDefs = BuildTrackDefs(effectiveRowDefs, effectiveAutoRowDefs, rowCount);
                rows = new Track[rowCount];
                for (var r = 0; r < rowCount; r++)
                    rows[r] = new Track { Def = rowDefs[r], Size = ResolveFixedRow(rowDefs[r], hasDefiniteHeight) };

                // Subgrid children are measured separately (below) since their per-row content must feed the
                // parent tracks; skip them in the normal item-height passes.
                var normalItems = placements.Where(p => !IsSubgridItem(p.Box)).ToList();

                // Single-row items size their auto row directly.
                foreach (var p in normalItems.Where(p => p.RowSpan == 1 && !IsFixed(rows[p.RowStart].Def)))
                {
                    var natural = await MeasureItemHeight(g, p.Box, ColumnSpanWidth(columns, p.ColStart, p.ColSpan, columnGap));
                    rows[p.RowStart].Size = Math.Max(rows[p.RowStart].Size, natural);
                }

                // Row-spanning items grow the last auto row they cover if their content exceeds the spanned rows.
                foreach (var p in normalItems.Where(p => p.RowSpan > 1))
                {
                    var natural = await MeasureItemHeight(g, p.Box, ColumnSpanWidth(columns, p.ColStart, p.ColSpan, columnGap));
                    GrowSpannedAutoRows(rows, p, natural, rowGap);
                }

                // Subgrid children: a col-only subgrid is measured as one item (with adopted columns); a
                // row-subgrid child reports its own per-subrow heights, which grow the parent's spanned auto rows
                // so the shared tracks size to fit every subgrid.
                foreach (var p in placements.Where(p => IsSubgridItem(p.Box)))
                    await MeasureSubgridItem(g, p, columns, rows, columnGap, rowGap);

                var rowContainerSize = hasDefiniteHeight ? _gridBox.ClientBottom - _gridBox.ClientTop
                    : rows.Sum(t => t.Size) + (rowCount > 1 ? rowGap * (rowCount - 1) : 0);
                PositionTracks(rows, _gridBox.ClientTop, rowContainerSize, rowGap, _gridBox.AlignContent);

                // Report mode: this grid is itself a row-subgrid being measured by its parent — hand back the
                // per-subrow sizes it just computed (one per adopted row) so the parent can grow its tracks.
                if (rowSubgrid && subgrid is not null)
                    subgrid.ReportedRowSizes = rows.Take(subgrid.RowCount).Select(t => t.Size).ToArray();
            }

            // ── Place each item into its (possibly spanned) cell, aligned per justify-self/align-self
            // (defaulting to justify-items/align-items, then to stretch). A subgrid child is given the adopted
            // track geometry for its subgridded axes so its own items snap to the parent lines.
            foreach (var p in placements)
            {
                var cellX = columns[p.ColStart].Position;
                var cellWidth = ColumnSpanWidth(columns, p.ColStart, p.ColSpan, columnGap);
                var cellY = rows[p.RowStart].Position;
                var cellHeight = RowSpanHeight(rows, p.RowStart, p.RowSpan, rowGap);
                var justify = ResolveSelfAlignment(p.Box.JustifySelf, _gridBox.JustifyItems);
                var align = ResolveSelfAlignment(p.Box.AlignSelf, _gridBox.AlignItems);

                var context = BuildChildSubgridContext(p, columns, rows, adoptRows: true);
                p.Box.SubgridContext = context;
                try
                {
                    await PlaceItemInCell(g, p.Box, cellX, cellY, cellWidth, cellHeight, justify, align);
                }
                finally
                {
                    p.Box.SubgridContext = null;
                }
            }

            // ── Baseline row alignment (CSS Box Alignment §9.3): items in the same row whose used
            // block-axis alignment is `baseline` were placed start-aligned above; shift each down so all
            // baseline items in the row share a common first baseline.
            AlignRowBaselines(placements);

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
        /// then auto-placement of the remaining items into the first free cell. The <b>minor</b> axis is
        /// columns for row flow and rows for column flow; the <b>major</b> axis grows implicitly. The minor
        /// axis starts at the explicit track count (<paramref name="fixedCount"/>) but grows implicit tracks
        /// to fit an item explicitly placed past it or whose span exceeds it (§8.3/§8.5); auto-placement then
        /// flows within that grid. <c>dense</c> restarts the search from the origin for each auto item;
        /// sparse advances a forward cursor.
        /// </summary>
        private List<Placement> PlaceItems(List<CssBox> items, bool isColumnFlow, bool dense, int fixedCount,
            IReadOnlyDictionary<string, List<int>> colLineNames, IReadOnlyDictionary<string, List<int>> rowLineNames)
        {
            var placements = new List<Placement>(items.Count);

            // Map each item to (minorStart, minorSpan, majorStart, majorSpan). Line numbers (incl. negatives)
            // resolve against the explicit track count (fixedCount); spans are kept unclamped so the implicit
            // grid can grow to hold them.
            var resolved = items.Select(box =>
            {
                var (colStart, colSpan) = ResolveAxis(box.GridColumnStart, box.GridColumnEnd, fixedCount, colLineNames);
                var (rowStart, rowSpan) = ResolveAxis(box.GridRowStart, box.GridRowEnd, fixedCount, rowLineNames);
                return isColumnFlow
                    ? new { box, MinorStart = rowStart, MinorSpan = Math.Max(1, rowSpan), MajorStart = colStart, MajorSpan = colSpan }
                    : new { box, MinorStart = colStart, MinorSpan = Math.Max(1, colSpan), MajorStart = rowStart, MajorSpan = rowSpan };
            }).ToList();

            // The minor track count grows beyond the explicit count for an item explicitly placed past the
            // grid (e.g. grid-column: 5 in a 3-column grid) or whose span alone is wider than the grid — both
            // generate implicit minor tracks rather than being clamped into range (which would overlap
            // another item). colCount/rowCount in Layout already grow from the placement extent to match.
            var minorCount = fixedCount;
            foreach (var r in resolved)
            {
                minorCount = Math.Max(minorCount, r.MinorSpan);
                if (r.MinorStart >= 0) minorCount = Math.Max(minorCount, r.MinorStart + r.MinorSpan);
            }

            var occupancy = new List<bool[]>(); // [major][minor]

            bool Occupied(int major, int minor)
            {
                if (minor < 0 || minor >= minorCount) return true;
                return major < occupancy.Count && occupancy[major][minor];
            }

            bool Fits(int major, int minor, int majorSpan, int minorSpan)
            {
                if (minor < 0 || minor + minorSpan > minorCount) return false;
                for (var m = major; m < major + majorSpan; m++)
                    for (var n = minor; n < minor + minorSpan; n++)
                        if (Occupied(m, n)) return false;
                return true;
            }

            void Mark(int major, int minor, int majorSpan, int minorSpan)
            {
                for (var m = major; m < major + majorSpan; m++)
                {
                    while (occupancy.Count <= m) occupancy.Add(new bool[minorCount]);
                    for (var n = minor; n < minor + minorSpan && n < minorCount; n++)
                        occupancy[m][n] = true;
                }
            }

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
                Commit(r.box, r.MajorStart, ClampMinor(r.MinorStart, r.MinorSpan, minorCount), r.MajorSpan, r.MinorSpan);

            // Pass 2: the rest, in DOM order.
            var cursorMajor = 0;
            var cursorMinor = 0;
            foreach (var r in resolved.Where(x => x.MinorStart < 0 || x.MajorStart < 0))
            {
                if (r.MinorStart >= 0)
                {
                    // Definite minor, auto major: first major line (from the top) where it fits.
                    var minor = ClampMinor(r.MinorStart, r.MinorSpan, minorCount);
                    var major = 0;
                    while (!Fits(major, minor, r.MajorSpan, r.MinorSpan)) major++;
                    Commit(r.box, major, minor, r.MajorSpan, r.MinorSpan);
                }
                else if (r.MajorStart >= 0)
                {
                    // Definite major, auto minor: first free minor position on that major line. If the line
                    // is already full (no in-range position fits), place at the start (overflow) rather than
                    // looping forever — the minor axis cannot grow to hold an auto-placed item here.
                    var minor = 0;
                    while (minor + r.MinorSpan <= minorCount && !Fits(r.MajorStart, minor, r.MajorSpan, r.MinorSpan))
                        minor++;
                    if (minor + r.MinorSpan > minorCount) minor = 0;
                    Commit(r.box, r.MajorStart, minor, r.MajorSpan, r.MinorSpan);
                }
                else
                {
                    // Fully auto. dense restarts from origin each time; sparse keeps a forward cursor.
                    var startMajor = dense ? 0 : cursorMajor;
                    var startMinor = dense ? 0 : cursorMinor;
                    var (major, minor) = FindFreeCell(Fits, startMajor, startMinor, r.MajorSpan, r.MinorSpan, minorCount);
                    Commit(r.box, major, minor, r.MajorSpan, r.MinorSpan);
                    if (!dense) { cursorMajor = major; cursorMinor = minor + r.MinorSpan; }
                }
            }

            // Restore DOM order so callers/tests see items in source order.
            return placements.OrderBy(p => items.IndexOf(p.Box)).ToList();
        }

        private static (int Major, int Minor) FindFreeCell(
            Func<int, int, int, int, bool> fits, int startMajor, int startMinor, int majorSpan, int minorSpan, int minorCount)
        {
            var major = startMajor;
            var minor = startMinor;
            while (true)
            {
                if (minor + minorSpan > minorCount) { minor = 0; major++; continue; }
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
        private static (int Start, int Span) ResolveAxis(string startValue, string endValue, int explicitCount,
            IReadOnlyDictionary<string, List<int>> lineNames)
        {
            var start = GridLineGrammar.TryParse(CssValueParser.GetCssTokens(startValue)) ?? GridLine.Auto;
            var end = GridLineGrammar.TryParse(CssValueParser.GetCssTokens(endValue)) ?? GridLine.Auto;

            // A named-line reference resolves to a concrete line number against this axis's name table
            // (honoring the §8.3.1 -start/-end suffix rule for area names) before numeric handling.
            int? startLine = ResolveNamedEdge(start, lineNames, isStart: true) ?? LineNumber(start, explicitCount);
            int? endLine = ResolveNamedEdge(end, lineNames, isStart: false) ?? LineNumber(end, explicitCount);

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
            if (line.IsAuto || line.IsSpan || line.Name != null) return null;
            if (line.Value < 0) return explicitCount + 1 + line.Value + 1; // -1 == last edge
            return line.Value;
        }

        /// <summary>
        /// Resolves a named-line reference (CSS Grid §8.3): the Nth (1-based, clamped) line labeled
        /// <see cref="GridLine.Name"/>, or — when no such explicit line exists — the implicit
        /// <c>name-start</c>/<c>name-end</c> line an <c>grid-template-areas</c> area produced, chosen by the
        /// edge side (<paramref name="isStart"/>). Returns null when the line isn't named or the name is
        /// unknown (falls through to auto).
        /// </summary>
        private static int? ResolveNamedEdge(GridLine line, IReadOnlyDictionary<string, List<int>> lineNames, bool isStart)
        {
            if (line.Name is null) return null;

            if (lineNames.TryGetValue(line.Name, out var direct) && direct.Count > 0)
                return NthClamp(direct, line.Value);

            var suffixed = line.Name + (isStart ? "-start" : "-end");
            if (lineNames.TryGetValue(suffixed, out var edge) && edge.Count > 0)
                return NthClamp(edge, line.Value);

            return null;
        }

        /// <summary>The Nth line number among a sorted list of same-named lines: 1-based from the start for a
        /// positive index, or counting back from the end for a negative one (<c>-1</c> = the last), clamped to
        /// the list (v1: no synthesis of implicit lines beyond the named ones).</summary>
        private static int NthClamp(List<int> lines, int nth)
        {
            var index = nth < 0 ? lines.Count + nth : nth - 1;   // -1 → last; 1 → first
            return lines[Math.Min(Math.Max(0, index), lines.Count - 1)];
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
        /// Sizes the column tracks against the container's definite content width (CSS Grid §11): resolve
        /// each track's base and growth-limit (auto/min-content/max-content/fit-content taking their base
        /// from the measured item content in <paramref name="minContent"/>/<paramref name="maxContent"/>),
        /// then either distribute the free
        /// space to <c>fr</c> tracks (§11.7) or, when there is no flex, grow the intrinsic tracks to share
        /// the remainder. Collapsed (empty <c>auto-fit</c>) tracks are forced to zero and drop their gap.
        /// </summary>
        private Track[] SizeColumnTracks(IReadOnlyList<GridTrackSize> defs, double contentWidth, double gap,
            bool[] collapsed, double[] minContent, double[] maxContent, bool stretchTracks)
        {
            var n = defs.Count;
            var tracks = defs.Select(d => new Track { Def = d }).ToArray();
            var bases = new double[n];
            var limits = new double[n];    // double.PositiveInfinity for an unbounded growth limit
            var flexes = new double[n];

            for (var i = 0; i < n; i++)
            {
                if (collapsed[i]) { bases[i] = 0; limits[i] = 0; flexes[i] = 0; continue; }
                InitTrack(defs[i], contentWidth, minContent[i], maxContent[i], out bases[i], out limits[i], out flexes[i]);
            }

            var nonCollapsed = collapsed.Count(c => !c);
            var totalGap = nonCollapsed > 1 ? gap * (nonCollapsed - 1) : 0;
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
                // Grow the unbounded (auto) tracks to fill only under a normal/stretch content-distribution;
                // an explicit justify-content leaves the free space for PositionTracks to distribute.
                var growable = stretchTracks
                    ? Enumerable.Range(0, n).Where(i => !collapsed[i] && double.IsPositiveInfinity(limits[i])).ToArray()
                    : [];
                if (growable.Length > 0 && free > 0)
                {
                    var share = free / growable.Length;
                    foreach (var i in growable)
                        tracks[i].Size += share;
                }
            }

            return tracks;
        }

        /// <summary>Resolves a track's base size, growth limit, and flex factor (CSS Grid §11.4).
        /// <paramref name="minContent"/>/<paramref name="maxContent"/> are the measured min-content and
        /// max-content contributions for an intrinsic track — a min-content/<c>fit-content</c> track and the
        /// intrinsic sides of a <c>minmax()</c> now size to the appropriate one, so a min-content track is no
        /// longer over-sized to its max-content width (§11.5).</summary>
        private void InitTrack(GridTrackSize def, double contentBase, double minContent, double maxContent,
            out double baseSize, out double limit, out double flex)
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
                    // The min track-sizing function feeds the base; the max function feeds the growth limit.
                    // An intrinsic min uses min-content (auto/min-content) or max-content (max-content);
                    // an intrinsic max uses max-content (auto/max-content) or min-content (min-content).
                    baseSize = IsFixed(def.Min)
                        ? ResolveFixedLength(def.Min, contentBase)
                        : IntrinsicMin(def.Min, minContent, maxContent);
                    if (def.Max.Kind == GridTrackKind.Flex)
                        flex = def.Max.Flex;
                    else
                        limit = IsFixed(def.Max)
                            ? ResolveFixedLength(def.Max, contentBase)
                            : IntrinsicMax(def.Max, minContent, maxContent);
                    if (!double.IsPositiveInfinity(limit) && limit < baseSize) limit = baseSize;
                    break;
                case GridTrackKind.FitContent:
                    // fit-content(L): the measured max-content, capped at L (def.Value holds the L argument);
                    // content wider than L overflows the track, matching browser cap behavior.
                    baseSize = limit = Math.Min(maxContent, CssValueParser.ParseLength(def.Value, contentBase, _gridBox));
                    break;
                case GridTrackKind.Auto:
                    // Max-content is the floor; an unbounded limit lets a normal/stretch content-distribution
                    // grow the track to share leftover space.
                    baseSize = maxContent;
                    limit = double.PositiveInfinity;
                    break;
                case GridTrackKind.MaxContent:
                    baseSize = limit = maxContent;   // content-sized, does not stretch
                    break;
                case GridTrackKind.MinContent:
                    baseSize = limit = minContent;   // narrowest unbreakable content, does not stretch
                    break;
            }
        }

        /// <summary>The base-side intrinsic size of a <c>minmax()</c> min breadth: min-content for
        /// <c>auto</c>/<c>min-content</c>, max-content for <c>max-content</c>.</summary>
        private static double IntrinsicMin(GridTrackSize min, double minContent, double maxContent) =>
            min.Kind == GridTrackKind.MaxContent ? maxContent : minContent;

        /// <summary>The limit-side intrinsic size of a <c>minmax()</c> max breadth: max-content for
        /// <c>auto</c>/<c>max-content</c>, min-content for <c>min-content</c>.</summary>
        private static double IntrinsicMax(GridTrackSize max, double minContent, double maxContent) =>
            max.Kind == GridTrackKind.MinContent ? minContent : maxContent;

        /// <summary>Measures each intrinsic (auto/min-content/max-content/fit-content) column's min-content and
        /// max-content contributions (outer, incl. margin/border/padding). Single-column items contribute
        /// directly; a multi-column item distributes the part of its contribution the spanned tracks don't
        /// already cover across the intrinsic tracks it spans (CSS Grid §11.5, simplified), so a spanned
        /// intrinsic column no longer collapses to zero.</summary>
        private async ValueTask<(double[] MinContent, double[] MaxContent)> MeasureColumnIntrinsics(
            RGraphics g, List<Placement> placements, IReadOnlyList<GridTrackSize> colDefs, int colCount,
            double contentWidth, double gap)
        {
            var min = new double[colCount];
            var max = new double[colCount];
            if (!colDefs.Any(IsIntrinsic)) return (min, max);

            // Single-column items contribute directly to their column.
            // A subgrid item is skipped: measuring it here (with no SubgridContext) would lay it out as an
            // ordinary grid and feed a bogus contribution into an auto parent column. Its contribution to the
            // parent's column sizing is the (accepted) column-axis gap, #276.
            foreach (var p in placements.Where(p => p.ColSpan == 1 && IsIntrinsic(colDefs[p.ColStart]) && !IsSubgridItem(p.Box)))
            {
                var (mn, mx) = await MeasureItemContribution(g, p.Box);
                min[p.ColStart] = Math.Max(min[p.ColStart], mn);
                max[p.ColStart] = Math.Max(max[p.ColStart], mx);
            }

            // Spanning items: distribute the contribution the spanned tracks don't already cover across the
            // intrinsic columns they span. Single-column contributions are settled first (loop above) so the
            // "already covered" term reflects them.
            foreach (var p in placements.Where(p => p.ColSpan > 1 && !IsSubgridItem(p.Box)))
            {
                var lastCol = Math.Min(p.ColStart + p.ColSpan, colCount);
                var intrinsicCols = new List<int>();
                for (var c = p.ColStart; c < lastCol; c++)
                    if (IsIntrinsic(colDefs[c])) intrinsicCols.Add(c);
                if (intrinsicCols.Count == 0) continue;

                var (mn, mx) = await MeasureItemContribution(g, p.Box);
                DistributeSpanContribution(min, colDefs, p, lastCol, intrinsicCols, mn, contentWidth, gap);
                DistributeSpanContribution(max, colDefs, p, lastCol, intrinsicCols, mx, contentWidth, gap);
            }

            return (min, max);
        }

        /// <summary>An item's outer min-content and max-content width contributions (content width, plus its
        /// own margin/border/padding). An explicit width supplies both.</summary>
        private static async ValueTask<(double Min, double Max)> MeasureItemContribution(RGraphics g, CssBox box)
        {
            double minContent, maxContent;
            if (CssValueParser.IsValidLength(box.Width))
            {
                minContent = maxContent = CssValueParser.ParseLength(box.Width, 0, box);
            }
            else
            {
                minContent = await CssLayoutEngine.GetMinContentWidth(g, box);
                maxContent = await CssLayoutEngine.GetMaxContentWidth(g, box);
            }

            var outerExtra = box.ActualMarginLeft + box.ActualMarginRight
                + box.ActualPaddingLeft + box.ActualPaddingRight
                + box.ActualBorderLeftWidth + box.ActualBorderRightWidth;
            return (minContent + outerExtra, maxContent + outerExtra);
        }

        /// <summary>Adds a spanning item's uncovered contribution equally to the intrinsic columns it spans:
        /// the part of <paramref name="contribution"/> not already met by the span's fixed-track sizes, its
        /// interior gaps, and the spanned columns' current contributions is shared across
        /// <paramref name="intrinsicCols"/> (§11.5.1, simplified — no per-span-count ordering or growth-limit
        /// distinction).</summary>
        private void DistributeSpanContribution(double[] arr, IReadOnlyList<GridTrackSize> colDefs, Placement p,
            int lastCol, List<int> intrinsicCols, double contribution, double contentWidth, double gap)
        {
            double covered = gap * (p.ColSpan - 1);
            for (var c = p.ColStart; c < lastCol; c++)
                covered += IsFixed(colDefs[c]) ? ResolveFixedLength(colDefs[c], contentWidth) : arr[c];

            var extra = contribution - covered;
            if (extra <= 0) return;

            var share = extra / intrinsicCols.Count;
            foreach (var c in intrinsicCols) arr[c] += share;
        }

        private static bool IsIntrinsic(GridTrackSize def) =>
            def.Kind is GridTrackKind.Auto or GridTrackKind.MinContent or GridTrackKind.MaxContent
                or GridTrackKind.FitContent
            || (def.Kind == GridTrackKind.Minmax && (IsIntrinsic(def.Min) || IsIntrinsic(def.Max)));

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

        /// <summary>Maps a top-level <c>&lt;percentage&gt;</c> row track to <c>auto</c> (used when the container
        /// height is indefinite, so the percentage behaves as auto per §7.2.1); every other track is returned
        /// unchanged.</summary>
        private static GridTrackSize PercentToAuto(GridTrackSize def) =>
            def.Kind == GridTrackKind.Percent ? GridTrackSize.Auto : def;

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

        /// <summary>Sizes an item into its cell and translates it there, honoring the resolved
        /// <paramref name="justify"/> (inline axis) and <paramref name="align"/> (block axis) — <c>stretch</c>
        /// fills the cell (when the dimension is auto); <c>start</c>/<c>end</c>/<c>center</c> position the
        /// intrinsically-sized item within the cell.</summary>
        private async ValueTask PlaceItemInCell(RGraphics g, CssBox box, double cellX, double cellY,
            double cellWidth, double cellHeight, string justify, string align)
        {
            var autoWidth = box.Width == CssConstants.Auto;
            var autoHeight = box.Height == CssConstants.Auto;
            var stretchWidth = IsStretch(justify) && autoWidth;
            var stretchHeight = IsStretch(align) && autoHeight;

            var savedWidth = box.Width;
            var savedHeight = box.Height;

            var cellContentWidth = Math.Max(0, cellWidth - HorizontalMarginBorderPadding(box));
            if (autoWidth)
            {
                // An auto-width block would fill its containing block, so a grid item's width must be pinned:
                // stretch → the cell's content width; otherwise its fit-content size clamped to the cell.
                var used = stretchWidth
                    ? cellContentWidth
                    : await CssLayoutEngine.GetFitContentWidth(g, box, cellContentWidth);
                box.Width = FormatLayoutUnits(used);
            }
            if (stretchHeight)
                box.Height = FormatLayoutUnits(Math.Max(0, cellHeight - VerticalMarginBorderPadding(box)));

            box.Location = new RPoint(_gridBox.ClientLeft, _gridBox.ClientTop);
            box.ActualBottom = box.Location.Y;
            box.RectanglesReset();
            await PerformLayoutBlockified(g, box);

            if (autoWidth) box.Width = savedWidth;
            if (stretchHeight) box.Height = savedHeight;

            // Position the item's margin box within the cell along each axis per the alignment keyword.
            var outerWidth = box.ActualBoxSizingWidth + box.ActualMarginLeft + box.ActualMarginRight;
            var outerHeight = box.ActualBoxSizingHeight + box.ActualMarginTop + box.ActualMarginBottom;
            var targetX = cellX + AlignmentOffset(justify, cellWidth, outerWidth) + box.ActualMarginLeft;
            var targetY = cellY + AlignmentOffset(align, cellHeight, outerHeight) + box.ActualMarginTop;

            var dx = targetX - box.Location.X;
            var dy = targetY - box.Location.Y;
            if (Math.Abs(dx) > 0.01) box.OffsetLeft(dx);
            if (Math.Abs(dy) > 0.01) box.OffsetTop(dy);
        }

        /// <summary>Resolves a grid item's used self-alignment: <c>auto</c>/<c>normal</c> defer to the
        /// container's <c>*-items</c> value, which itself defaults to <c>stretch</c>.</summary>
        private static string ResolveSelfAlignment(string self, string items)
        {
            var value = self;
            if (string.IsNullOrEmpty(value) || value.Isi(CssConstants.Auto) || value.Isi(CssConstants.Normal))
                value = items;
            if (string.IsNullOrEmpty(value) || value.Isi(CssConstants.Normal) || value.Isi(CssConstants.Auto))
                value = CssConstants.Stretch;
            return value;
        }

        private static bool IsStretch(string value) =>
            value.Isi(CssConstants.Stretch) || value.Isi(CssConstants.Normal);

        /// <summary>Whether a used alignment value is a baseline alignment (<c>baseline</c> /
        /// <c>first baseline</c> / <c>last baseline</c> — all treated as first-baseline here).</summary>
        private static bool IsBaselineAlign(string value) =>
            !string.IsNullOrEmpty(value)
            && value.IndexOf(CssConstants.Baseline, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Aligns the first baselines of the single-row items in each row whose used <c>align-items</c>/
        /// <c>align-self</c> is <c>baseline</c> (they were placed start-aligned by <see cref="PlaceItemInCell"/>,
        /// since <see cref="AlignmentOffset"/> maps baseline to start). Each baseline item is shifted down by
        /// the difference between the row's maximum baseline offset and its own, so every baseline item in the
        /// row rests on a common baseline. Items with no discoverable baseline stay start-aligned.
        /// Accepted gap (#280): this shifts within the already-sized row but does not grow the row to the
        /// baseline group's max-ascent + max-descent, so an item with a disproportionately large descent
        /// below its baseline could overflow — standard text with a normal line-height always fits.
        /// </summary>
        private void AlignRowBaselines(List<Placement> placements)
        {
            var baselineItems = placements.Where(p =>
                p.RowSpan == 1 && IsBaselineAlign(ResolveSelfAlignment(p.Box.AlignSelf, _gridBox.AlignItems)));

            foreach (var row in baselineItems.GroupBy(p => p.RowStart))
            {
                var offsets = new List<(CssBox Box, double Offset)>();
                var maxBaseline = 0.0;
                foreach (var p in row)
                {
                    var offset = BaselineAlignment.GetItemBaselineOffset(p.Box);
                    if (offset is null) continue;   // no line-box content — stays start-aligned
                    offsets.Add((p.Box, offset.Value));
                    maxBaseline = Math.Max(maxBaseline, offset.Value);
                }

                if (offsets.Count < 2) continue;   // a lone baseline item already sits on its own baseline

                foreach (var (box, offset) in offsets)
                {
                    var dy = maxBaseline - offset;
                    if (dy > 0.01) box.OffsetTop(dy);
                }
            }
        }

        /// <summary>The start offset of an item within its cell for a start/end/center alignment (a stretched
        /// or start-aligned item is at 0). Baseline is placed at start here, then <see cref="AlignRowBaselines"/>
        /// applies the block-axis baseline shift as a post-pass; inline-axis (justify) baseline stays start.</summary>
        private static double AlignmentOffset(string value, double cellSize, double itemSize)
        {
            var free = cellSize - itemSize;
            if (free <= 0) return 0;
            if (value.Isi(CssConstants.Center)) return free / 2;
            if (value.Isi(CssConstants.End) || value.Isi(CssConstants.FlexEnd) || value.Isi("self-end") || value.Isi("right"))
                return free;
            return 0; // start / flex-start / self-start / left / stretch / baseline
        }

        /// <summary>
        /// Assigns each track's position, distributing any leftover container space per a content-alignment
        /// value (<c>justify-content</c> for columns, <c>align-content</c> for rows): start/end/center and
        /// the space-* distributions. <c>normal</c>/<c>stretch</c> pack at the start (auto-track stretch is a
        /// v1 gap).
        /// </summary>
        private static void PositionTracks(Track[] tracks, double start, double containerSize, double gap, string value)
        {
            var n = tracks.Length;
            if (n == 0) return;

            // A zero-size track (a collapsed auto-fit column, or an empty row) drops its adjoining gap.
            var gapCount = Math.Max(0, tracks.Count(t => t.Size > 0.01) - 1);
            var used = tracks.Sum(t => t.Size) + gap * gapCount;
            var free = containerSize - used;

            double offset = 0;
            double between = gap;

            if (free > 0.01 && gapCount >= 0)
            {
                if (value.Isi(CssConstants.Center)) offset = free / 2;
                else if (value.Isi(CssConstants.End) || value.Isi(CssConstants.FlexEnd) || value.Isi("right")) offset = free;
                else if (value.Isi(CssConstants.SpaceBetween) && gapCount > 0) between = gap + free / gapCount;
                else if (value.Isi(CssConstants.SpaceAround)) { between = gap + free / (gapCount + 1); offset = free / (gapCount + 1) / 2; }
                else if (value.Isi(CssConstants.SpaceEvenly)) { between = gap + free / (gapCount + 2); offset = free / (gapCount + 2); }
            }

            var pos = start + offset;
            foreach (var t in tracks)
            {
                t.Position = pos;
                pos += t.Size;
                if (t.Size > 0.01) pos += between;
            }
        }

        private static double HorizontalMarginBorderPadding(CssBox box) =>
            box.ActualMarginLeft + box.ActualMarginRight
            + box.ActualPaddingLeft + box.ActualPaddingRight
            + box.ActualBorderLeftWidth + box.ActualBorderRightWidth;

        private static double VerticalMarginBorderPadding(CssBox box) =>
            box.ActualMarginTop + box.ActualMarginBottom
            + box.ActualPaddingTop + box.ActualPaddingBottom
            + box.ActualBorderTopWidth + box.ActualBorderBottomWidth;

        // ─── Subgrid (CSS Grid Level 2 §9) ─────────────────────────────────────────

        /// <summary>Whether a grid item is a subgrid on at least one axis — a nested grid whose
        /// <c>grid-template-columns</c> and/or <c>grid-template-rows</c> is <c>subgrid</c>.</summary>
        private static bool IsSubgridItem(CssBox box) =>
            (box.Display == CssConstants.Grid || box.Display == CssConstants.InlineGrid)
            && (ChildColSubgrid(box) || ChildRowSubgrid(box));

        private static bool ChildColSubgrid(CssBox box) => box.GridTemplateColumns.Value?.IsSubgrid == true;
        private static bool ChildRowSubgrid(CssBox box) => box.GridTemplateRows.Value?.IsSubgrid == true;

        /// <summary>The used sizes of the tracks a placement spans (clamped to the built track array).</summary>
        private static List<double> SpannedSizes(Track[] tracks, int start, int span)
        {
            var sizes = new List<double>(span);
            for (var i = start; i < start + span && i < tracks.Length; i++)
                sizes.Add(tracks[i].Size);
            return sizes;
        }

        /// <summary>Each spanned track's start position relative to the first spanned track — this captures the
        /// parent's exact line positions (uniform gaps, or the wider spacing a <c>space-*</c> content-alignment
        /// produces) so the adopted tracks reproduce them, not just a raw-gap reconstruction.</summary>
        private static List<double> SpannedOffsets(Track[] tracks, int start, int span)
        {
            var offsets = new List<double>(span);
            var origin = start < tracks.Length ? tracks[start].Position : 0;
            for (var i = start; i < start + span && i < tracks.Length; i++)
                offsets.Add(tracks[i].Position - origin);
            return offsets;
        }

        /// <summary>Builds <paramref name="totalCount"/> tracks: the first <paramref name="sizes"/>.Count use the
        /// adopted parent sizes at their adopted relative <paramref name="offsets"/> (measured from
        /// <paramref name="start"/>), reproducing the parent's line positions exactly once the parent translates
        /// the child's cell into place; any extra tracks beyond the adopted count are zero-width and abut the
        /// last adopted track (overflow).</summary>
        private static Track[] BuildAdoptedTracks(IReadOnlyList<double> sizes, IReadOnlyList<double> offsets, double start, int totalCount)
        {
            var tracks = new Track[totalCount];
            var overflowPos = start + (sizes.Count > 0 ? offsets[^1] + sizes[^1] : 0);
            for (var i = 0; i < totalCount; i++)
            {
                if (i < sizes.Count)
                    tracks[i] = new Track { Def = GridTrackSize.Auto, Size = sizes[i], Position = start + offsets[i] };
                else
                    tracks[i] = new Track { Def = GridTrackSize.Auto, Size = 0, Position = overflowPos };
            }
            return tracks;
        }

        /// <summary>Builds the <see cref="GridSubgridContext"/> handed to a subgrid child — the parent track
        /// geometry it adopts for each subgridded axis. Returns null for a non-subgrid item (no-op). When
        /// <paramref name="adoptRows"/> is false the row sizes are omitted (report/measure mode: the child
        /// self-sizes its rows and reports them back).</summary>
        private static GridSubgridContext? BuildChildSubgridContext(
            Placement p, Track[] columns, Track[] rows, bool adoptRows)
        {
            if (!IsSubgridItem(p.Box)) return null;

            var context = new GridSubgridContext();
            if (ChildColSubgrid(p.Box))
            {
                context.ColumnSizes = SpannedSizes(columns, p.ColStart, p.ColSpan);
                context.ColumnOffsets = SpannedOffsets(columns, p.ColStart, p.ColSpan);
            }
            if (ChildRowSubgrid(p.Box))
            {
                context.RowCount = p.RowSpan;
                if (adoptRows)
                {
                    context.RowSizes = SpannedSizes(rows, p.RowStart, p.RowSpan);
                    context.RowOffsets = SpannedOffsets(rows, p.RowStart, p.RowSpan);
                }
            }
            return context;
        }

        /// <summary>Grows the last auto row a placement spans by the amount its content exceeds the spanned
        /// rows + gaps (a no-op if it already fits, or if every spanned row is fixed).</summary>
        private static void GrowSpannedAutoRows(Track[] rows, Placement p, double natural, double rowGap)
        {
            var spanned = 0.0;
            for (var r = p.RowStart; r < p.RowStart + p.RowSpan && r < rows.Length; r++) spanned += rows[r].Size;
            spanned += rowGap * (p.RowSpan - 1);
            if (natural <= spanned) return;

            var lastAuto = -1;
            for (var r = p.RowStart; r < p.RowStart + p.RowSpan && r < rows.Length; r++)
                if (!IsFixed(rows[r].Def)) lastAuto = r;
            if (lastAuto >= 0) rows[lastAuto].Size += natural - spanned;
        }

        /// <summary>
        /// Measures a subgrid item's contribution to this (parent) grid's row tracks. A row-subgrid child is laid
        /// out in <b>report mode</b> (adopted columns, self-sized rows) so its per-subrow heights can grow the
        /// parent's spanned auto rows one-for-one — this is what makes every subgrid's rows share a common,
        /// content-fitting size. A column-only subgrid is measured as an ordinary item (with adopted columns).
        /// </summary>
        private async ValueTask MeasureSubgridItem(RGraphics g, Placement p, Track[] columns, Track[] rows,
            double columnGap, double rowGap)
        {
            var box = p.Box;
            var columnWidth = ColumnSpanWidth(columns, p.ColStart, p.ColSpan, columnGap);

            if (ChildRowSubgrid(box))
            {
                var context = new GridSubgridContext { RowCount = p.RowSpan };
                if (ChildColSubgrid(box))
                {
                    context.ColumnSizes = SpannedSizes(columns, p.ColStart, p.ColSpan);
                    context.ColumnOffsets = SpannedOffsets(columns, p.ColStart, p.ColSpan);
                }

                box.SubgridContext = context;
                try { await MeasureItemHeight(g, box, columnWidth); }
                finally { box.SubgridContext = null; }

                var reported = context.ReportedRowSizes;
                if (reported is null) return;
                for (var k = 0; k < p.RowSpan && p.RowStart + k < rows.Length && k < reported.Length; k++)
                {
                    var r = p.RowStart + k;
                    if (!IsFixed(rows[r].Def))
                        rows[r].Size = Math.Max(rows[r].Size, reported[k]);
                }
            }
            else
            {
                // Column-only subgrid: adopt columns while measuring so the reported height is correct.
                var context = new GridSubgridContext
                {
                    ColumnSizes = SpannedSizes(columns, p.ColStart, p.ColSpan),
                    ColumnOffsets = SpannedOffsets(columns, p.ColStart, p.ColSpan)
                };
                box.SubgridContext = context;
                double natural;
                try { natural = await MeasureItemHeight(g, box, columnWidth); }
                finally { box.SubgridContext = null; }

                if (p.RowSpan == 1)
                {
                    if (!IsFixed(rows[p.RowStart].Def))
                        rows[p.RowStart].Size = Math.Max(rows[p.RowStart].Size, natural);
                }
                else
                {
                    GrowSpannedAutoRows(rows, p, natural, rowGap);
                }
            }
        }

        // ─── Value helpers ──────────────────────────────────────────────────────────

        /// <summary>Extracts the <c>[name]</c> named lines declared in a track template into a mutable
        /// name → sorted 1-based line-number table (into which <c>grid-template-areas</c> merges its implicit
        /// area lines).</summary>
        private static Dictionary<string, List<int>> BuildLineNames(GridTemplate? template)
        {
            var table = new Dictionary<string, List<int>>();
            if (template?.LineNames is { Count: > 0 } names)
                foreach (var (name, lines) in names)
                    table[name] = lines.ToList();
            return table;
        }

        /// <summary>Adds a 1-based line number to a name's entry, keeping the list sorted and deduped.</summary>
        private static void AddLine(Dictionary<string, List<int>> table, string name, int line)
        {
            if (!table.TryGetValue(name, out var lines))
                table[name] = lines = [];
            var idx = lines.BinarySearch(line);
            if (idx < 0) lines.Insert(~idx, line);
        }

        private static List<GridTrackSize> ExpandTemplate(GridTemplate? template)
        {
            // The auto-repeat (auto-fill/auto-fit) section is resolved in Stage 7.
            return template?.Tracks.ToList() ?? [];
        }

        /// <summary>
        /// Resolves a <c>grid-template-columns</c> template into concrete track definitions, expanding a
        /// <c>repeat(auto-fill|auto-fit, …)</c> section to the number of repetitions that fit the container
        /// width (CSS Grid §7.2.3.2). Returns the tracks and, for <c>auto-fit</c>, the [start, length) index
        /// range of the repeated tracks so empty ones can be collapsed after placement.
        /// </summary>
        private (List<GridTrackSize> Defs, (int Start, int Length)? AutoFitRange) ResolveColumnTemplate(
            GridTemplate? template, double contentWidth, double gap)
        {
            if (template is null)
                return ([], null);
            if (template.AutoRepeat == GridAutoRepeatKind.None)
                return (template.Tracks.ToList(), null);

            var fixedDefs = template.Tracks;
            var repeated = template.AutoRepeatTracks;

            double FloorSum(IEnumerable<GridTrackSize> ts) => ts.Sum(FloorSize);

            var repFloor = FloorSum(repeated) + (repeated.Count > 1 ? gap * (repeated.Count - 1) : 0);
            int count;
            if (repFloor <= 0)
            {
                count = 1; // no definite repeated size → a single repetition (spec's fallback)
            }
            else
            {
                var available = contentWidth - FloorSum(fixedDefs);
                // repFloor already includes the group's internal gaps, so one more repetition costs the
                // group's floor plus a single separating gap (not one gap per repeated track).
                var perRepetition = repFloor + gap;
                count = Math.Max(1, (int)Math.Floor((available + gap) / perRepetition));
            }

            var defs = new List<GridTrackSize>();
            defs.AddRange(fixedDefs.Take(template.AutoRepeatInsertIndex));
            var rangeStart = defs.Count;
            for (var r = 0; r < count; r++) defs.AddRange(repeated);
            var rangeLen = count * repeated.Count;
            defs.AddRange(fixedDefs.Skip(template.AutoRepeatInsertIndex));

            var autoFitRange = template.AutoRepeat == GridAutoRepeatKind.AutoFit
                ? ((int, int)?)(rangeStart, rangeLen)
                : null;
            return (defs, autoFitRange);
        }

        /// <summary>The definite floor size of a track for auto-fill/auto-fit repetition counting: a length/
        /// percentage resolves directly, a minmax() uses its min, and flex/intrinsic tracks contribute 0.</summary>
        private double FloorSize(GridTrackSize def) => def.Kind switch
        {
            GridTrackKind.Length or GridTrackKind.Percent => ResolveFixedLength(def, 0),
            GridTrackKind.Minmax => FloorSize(def.Min),
            _ => 0
        };

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
