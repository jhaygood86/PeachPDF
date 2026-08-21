// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to anend,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
// 
// - Sun Tsu,
// "The Art of War"

using PeachPDF;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Fragmentation;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// Layout engine for tables executing the complex layout of tables with rows/columns/headers/etc.
    /// </summary>
    internal sealed class CssLayoutEngineTable
    {
        /// <summary>
        /// the main box of the table
        /// </summary>
        private readonly CssBox _tableBox;

        // ─── Writing-mode axis mapping ──────────────────────────────────────────
        //
        // Unlike Flexbox's row/column (which flex-direction can point at either logical axis), a
        // table's rows ALWAYS stack along the block axis and its columns ALWAYS run along the inline
        // axis (css-tables-3) - there is no "reverse" keyword that swaps which end either starts at,
        // so this table only ever needs two flags rather than Flex's three. Columns (inline axis,
        // assumed ltr - this engine has no direction: rtl support for either axis, matching Flex's
        // and the vertical-line-flow engine's own scope) always run from physical-min forward, so
        // only the row axis's start side needs a flag at all: LogicalPropertyResolver.InlineStart
        // under ltr is Left (horizontal-tb) or Top (vertical-rl/vertical-lr) - never Right/Bottom.
        //
        // CURRENT STATE: column/row sizing, cell/row/caption/thead-tfoot placement, collapsed-border
        // resolution (topology and paint geometry alike), vertical-align's own content alignment
        // within a cell, and a rowspan cell's own row-axis sizing are all axis-aware now. A
        // vertical-rl table's own row-axis-max-anchored growth (a row's own row-axis thickness isn't
        // knowable until after its cells are laid out, which conflicts with placing it at a
        // physical-max-anchored start before that) is solved by ReflectRowAxisForVerticalRl: every
        // row/caption/header-footer-proxy grows forward as if vertical-lr, then the whole assembly is
        // mirrored once its own final row-axis bounds are known - see that method's own remarks. What
        // remains out of scope: colspan straddling the row axis (reviewed and found already
        // axis-correct by construction - see GetCellWidth's own remarks) is not itself a gap, but real
        // per-row pagination of a vertical table's own content is - it stays monolithic
        // (MonolithicContent.IsUnresumableVerticalTable) because the page-fragmentation system's own
        // primitives (FragmentainerContext, HtmlContainerInt.SlotStartingAt/PageTopOf/PageBottomOf)
        // are physical-Y-only at the type level, the same architectural wall #764 (Multi-column) hits
        // for the identical reason. See .claude/accepted-gaps/no-vertical-writing-mode-layout.md.
        private readonly bool _isVertical;
        private readonly bool _rowAxisStartIsAtMax;

        // The four logical table edges' resolved physical Border sides, computed once from the same
        // writing-mode LogicalPropertyResolver call _isVertical/_rowAxisStartIsAtMax already derive from
        // - reused by ApplyCollapsedUsedBorderWidths and threaded into CollapsedBorderModel so neither
        // hand-rolls a second logical-to-physical mapper (CLAUDE.md's "don't write two independent
        // mappers for the same grammar" convention). No RTL column/inline axis in this engine (see the
        // remarks above), so DirectionMode.Ltr is always the right direction to resolve inline-start/end
        // against, matching every other column-axis site in this file.
        private readonly Border _blockStartBorder;
        private readonly Border _blockEndBorder;
        private readonly Border _inlineStartBorder;
        private readonly Border _inlineEndBorder;

        private static Border ToBorder(PhysicalSide side) => side switch
        {
            PhysicalSide.Top => Border.Top,
            PhysicalSide.Right => Border.Right,
            PhysicalSide.Bottom => Border.Bottom,
            _ => Border.Left,
        };

        /// <summary>A cell's own CSS property that constrains its column-axis (inline) extent.</summary>
        private string CellInlineSize(CssBox cell) => _isVertical ? cell.Height : cell.Width;

        private string CellInlineMaxSize(CssBox cell) => _isVertical ? cell.MaxHeight : cell.MaxWidth;

        private string CellInlineMinSize(CssBox cell) => _isVertical ? cell.MinHeight : cell.MinWidth;

        /// <summary>The table's own border width consumed at the column axis's start/end edge.</summary>
        private double TableInlineBorderStart => _isVertical ? _tableBox.ActualBorderTopWidth : _tableBox.ActualBorderLeftWidth;

        private double TableInlineBorderEnd => _isVertical ? _tableBox.ActualBorderBottomWidth : _tableBox.ActualBorderRightWidth;

        private CssBox? _headerBox;

        /// <summary>Where <see cref="_headerBox"/> sat among the table's children before it was detached.</summary>
        private int _headerIndex = -1;

        private CssBox? _footerBox;

        /// <summary>Where <see cref="_footerBox"/> sat among the table's children before it was detached.</summary>
        private int _footerIndex = -1;

        /// <summary>
        /// collection of all rows boxes
        /// </summary>
        private readonly List<CssBox> _bodyRows = [];

        /// <summary>
        /// For each row kept in <see cref="_bodyRows"/>, its ordinal position among the table's rows in
        /// source order - counting <c>visibility: collapse</c> rows that <see cref="AssignBoxKinds"/>
        /// left out of <see cref="_bodyRows"/> (CSS 2.1 §17.6.1) too. A cell's <c>rowspan</c> counts rows
        /// of that original grid, not of the shorter filtered list, so mapping a span onto
        /// <see cref="_bodyRows"/> needs both numbers - see <see cref="GetEffectiveEndRowIndex(int, int)"/>.
        /// </summary>
        private readonly List<int> _bodyRowOriginalIndices = [];

        /// <summary>
        /// collection of all columns boxes
        /// </summary>
        private readonly List<CssBox> _columns = [];

        /// <summary>
        ///
        /// </summary>
        private readonly List<CssBox> _allRows = [];

        /// <summary>
        /// <see cref="_allRows"/>'s own grid-row-space twin of <see cref="_bodyRowOriginalIndices"/>: for
        /// each of the header's rows (real per-row count, not <see cref="_bodyRowOriginalIndices"/>'s own
        /// "the header counts as one unit" numbering) followed by each of <see cref="_bodyRows"/>'s own
        /// rows, the original (collapsed-rows-included) index within its own group - one continuous,
        /// unbroken numbering spanning the header/body boundary. Only the header and body portions are
        /// populated (the footer is provably unreachable from a header-opened span, since <c>_allRows</c>
        /// always places footer rows last regardless of source order) - see
        /// <see cref="ComputeAllRowsOriginalIndices"/>.
        /// </summary>
        private readonly List<int> _allRowsOriginalIndices = [];

        /// <summary>
        /// Every header cell whose <c>rowspan</c> reaches past the header's own last row, into
        /// <see cref="_bodyRows"/> - keyed to the body-local row index (an index into
        /// <see cref="_bodyRows"/>) its span actually ends on. See
        /// <see cref="ComputeHeaderRowSpansCrossingIntoBody"/> (issue #788).
        /// </summary>
        private readonly Dictionary<CssBox, int> _headerRowSpansCrossingIntoBody = [];

        /// <summary>
        /// Every <c>table-caption</c> box among the table's direct children, in source order -
        /// populated by <see cref="AssignBoxKinds"/> and split by each box's own <c>caption-side</c>
        /// into <see cref="_topCaptions"/>/<see cref="_bottomCaptions"/>.
        /// </summary>
        private readonly List<CssBox> _captionBoxes = [];

        /// <summary>Captions with <c>caption-side: top</c> (the initial value) - laid out above the row grid.</summary>
        private readonly List<CssBox> _topCaptions = [];

        /// <summary>Captions with <c>caption-side: bottom</c> - laid out below the row grid.</summary>
        private readonly List<CssBox> _bottomCaptions = [];

        /// <summary>
        /// The combined height <see cref="_topCaptions"/> occupies above the row grid - set once by
        /// <see cref="LayoutCells"/> on the pass that lays them out. Left at 0 on a continuation pass,
        /// which is harmless: every use of it feeds a <c>Math.Max</c> against the resumed page's own
        /// top (<see cref="ResumedRowTop"/>), which always wins over a stale/zero caption height.
        /// </summary>
        private double _topCaptionsHeight;

        private int _columnCount;

        private bool _widthSpecified;

        private double[]? _columnWidths;

        private double[]? _columnMinWidths;

        /// <summary>Cache for <see cref="CollapsedColumnCount"/>.</summary>
        private int? _collapsedColumnCount;

        /// <summary>
        /// The table's logical row×column grid, built (and <see cref="_collapsedBorders"/> resolved from
        /// it) only when <c>border-collapse: collapse</c> is set - null for a <c>separate</c> table,
        /// which needs neither.
        /// </summary>
        private TableGrid? _grid;

        /// <summary>CSS 2.1 §17.6.2's resolution of <see cref="_grid"/> - see <see cref="_grid"/>.</summary>
        private CollapsedBorderModel? _collapsedBorders;

        /// <summary>
        /// Every real cell's grid column - unlike <see cref="_grid"/>, populated for every table
        /// regardless of <c>border-collapse</c>, since <see cref="GetCellRealColumnIndex"/>'s callers are
        /// cell positioning/width distribution, not border resolution. See
        /// <see cref="ComputeColumnPlacements"/> for when this is populated.
        /// </summary>
        private Dictionary<CssBox, CellPlacement>? _columnPlacements;

        /// <summary>
        /// The column count <see cref="_columnPlacements"/> itself needed - every real cell's rowspan-
        /// occupancy-aware placement, not just what any single row's own <c>Boxes</c> list happens to
        /// declare. <see cref="DetermineColumnCount"/> folds this in as a floor.
        /// </summary>
        private int _columnPlacementsColumnCount;

        // Header/Footer repetition fields
        private double _headerHeight;
        private double _footerHeight;

        /// <summary>
        /// Whether the table has a <c>&lt;thead&gt;</c> this engine took out of the child list and stands
        /// proxies in for — which is a different question from whether it <i>repeats</i>
        /// (<see cref="_headerRepeats"/>).
        /// </summary>
        /// <remarks>
        /// A detached group is laid out once to measure it, occupies room at the table's top, and is drawn
        /// there whether or not §6.2 lets it repeat. Three things are keyed to detachment rather than to
        /// repetition and must stay that way: the measurement steps, the orphan pre-check that moves a
        /// table whose header fits but whose first body row does not, and the headerless whole-table
        /// pre-check — which is gated on the *absence* of both groups, so reading repetition here would
        /// send a non-repeating table down a relocation path a table with a <c>&lt;thead&gt;</c> never takes.
        /// </remarks>
        private bool HeaderIsDetached => _headerBox != null && _headerBox.DerivedStyle.ActualDisplay == Keywords.TableHeaderGroup;

        /// <summary>
        /// Whether the table has a <c>&lt;tfoot&gt;</c> this engine took out of the child list. See
        /// <see cref="HeaderIsDetached"/>; <see cref="_footerRepeats"/> is the other question.
        /// </summary>
        private bool FooterIsDetached => _footerBox != null && _footerBox.DerivedStyle.ActualDisplay == Keywords.TableFooterGroup;

        /// <summary>
        /// Whether css-tables-3 §6.2 lets the detached <c>&lt;thead&gt;</c> be repeated on every band the
        /// table spans, rather than laid out once at its top. Settled once per table by
        /// <see cref="SettleWhetherTheGroupsRepeat"/> and inherited by a continuation through
        /// <see cref="TableSetup"/>.
        /// </summary>
        private bool _headerRepeats;

        /// <summary>
        /// Whether css-tables-3 §6.2 lets the detached <c>&lt;tfoot&gt;</c> be repeated at the foot of every
        /// band the table spans. See <see cref="_headerRepeats"/>.
        /// </summary>
        private bool _footerRepeats;

        /// <summary>
        /// The bands a row overflowed <i>through</i> on this pass, which
        /// <see cref="RepeatTheGroupsOnEveryBandTheTableSpans"/> owes the repeated groups to.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Written by <see cref="SliceARowAcrossTheBandsItOverflows"/>, which is the only thing that knows
        /// a band was covered without a break falling on it. Cleared per pass, because a band an abandoned
        /// run overflowed into is not a band this one does.
        /// </para>
        /// <para>
        /// <b>Why this rather than "every band between the table's top and its bottom".</b> A table can
        /// span a boundary without fragmenting at all, and that table is about to be moved whole by §4.3's
        /// epilogue mover — so repeating groups onto those bands draws on pages the table will not occupy,
        /// and writing a slice bottom for them tells
        /// <c>CssBox.PaginatedItsOwnContentWithoutBreaking</c> the table fragmented, which stops the move
        /// from happening at all. Measured directly: a footer-only table that should have moved to page 2
        /// stayed at Y=500. The bands here are the ones no move can help with, which is exactly §4.3's
        /// last rung and exactly the set §6.2 is unserved on.
        /// </para>
        /// </remarks>
        private readonly HashSet<int> _bandsARowOverflowedInto = [];

        /// <summary>
        /// What a band owes the footer this table repeats at its foot — nothing where §6.2 declined the
        /// repetition, since no footer is drawn there to leave room for.
        /// </summary>
        /// <remarks>
        /// The four sites that subtract this were written when <c>_footerHeight</c> was zero for exactly
        /// the tables that draw no repeated footer. That is no longer the same set: a <c>&lt;tfoot&gt;</c>
        /// §6.2 declines is still measured, so subtracting the raw height would keep charging every band
        /// for a footer that is not there — which is the whole cost §6.2's conditions exist to remove.
        /// <para>
        /// Clamped at zero because an empty <c>&lt;tfoot&gt;&lt;/tfoot&gt;</c> measures a negative
        /// <see cref="VerticalSpacingAt"/>, and a negative reservation would <i>add</i> room to a band
        /// rather than take it. The expression this replaced carried the same guard.
        /// </para>
        /// </remarks>
        private double RepeatedFooterHeight => _footerRepeats && _footerHeight > 0 ? _footerHeight : 0;

        /// <summary>
        /// What a repeated <c>&lt;thead&gt;</c> takes from the top of every band the table covers, spacing
        /// included, or zero where nothing repeats.
        /// </summary>
        private double RepeatedHeaderRoom =>
            _headerRepeats && _headerHeight > 0 ? _headerHeight + VerticalSpacingAt(HeaderRowCountInGrid) : 0;

        /// <summary>
        /// Whether this run continues a table layout an earlier fragmentainer pass began, rather than
        /// laying the table out from the start.
        /// </summary>
        /// <remarks>
        /// Every other reason this engine runs again over the same box — the per-page-width reflow loop,
        /// <c>ShrinkToFit</c>, a §4.3 relocation laying the subtree out again at its destination — is a
        /// fresh layout and has to start from the markup, which is why the question is asked of the
        /// resumption record. It is asked of the settled <see cref="TableSetup"/> as well: a record for a
        /// table that has settled nothing names a continuation there is nothing to continue, and starting
        /// from the markup is both the safe reading and the total one.
        /// </remarks>
        private readonly bool _continuesAPreviousPass;

        /// <summary>
        /// What this table settled once: inherited whole on a continuation, and built by this run
        /// otherwise. See <see cref="TableSetup"/> for what re-deciding each part would destroy.
        /// </summary>
        private readonly TableSetup _setup;

        /// <summary>
        /// Where the pass this run continues left its row loop, or null when the row loop starts from the
        /// first body row — which is every run today, and also a continuation whose record does not name a
        /// row (see <see cref="TableBreakToken"/>).
        /// </summary>
        private readonly TableBreakToken? _carried;

        /// <summary>
        /// Init.
        /// </summary>
        /// <param name="tableBox"></param>
        /// <param name="resume">
        /// how this table resumes on the current fragmentainer pass, or null when it is being laid out
        /// from the start.
        /// </param>
        private CssLayoutEngineTable(CssBox tableBox, BreakToken? resume)
        {
            _tableBox = tableBox;

            var writingMode = tableBox.WritingMode.Value;
            _isVertical = writingMode is WritingMode.VerticalRl or WritingMode.VerticalLr;
            _rowAxisStartIsAtMax = LogicalPropertyResolver.BlockStart(writingMode) is PhysicalSide.Right or PhysicalSide.Bottom;

            _blockStartBorder = ToBorder(LogicalPropertyResolver.BlockStart(writingMode));
            _blockEndBorder = ToBorder(LogicalPropertyResolver.BlockEnd(writingMode));
            _inlineStartBorder = ToBorder(LogicalPropertyResolver.InlineStart(writingMode, DirectionMode.Ltr));
            _inlineEndBorder = ToBorder(LogicalPropertyResolver.InlineEnd(writingMode, DirectionMode.Ltr));

            // Cleared before anything can throw, for the reason the setup is: a run that dies part-way
            // must leave no answer rather than the previous run's. A record naming a row of a layout that
            // no longer exists is worse than none - it carries CssBox references into a tree that has
            // moved on. Safe on a continuation too, since the record this run continues is _carried.
            tableBox.TableContinuation = null;

            if (resume is not null && tableBox.TableSetup is { } carried)
            {
                _setup = carried;
                _continuesAPreviousPass = true;
                _carried = resume as TableBreakToken;
            }
            else
            {
                // A fresh layout discards whatever the last one settled - the markup is the input again -
                // and does not publish its own until Layout has finished. Publishing here instead would
                // leave a run that threw part-way with a non-null but *empty* setup on a table whose
                // <thead> is already detached, and a later continuation would inherit nothing while
                // skipping the restore, which is the only way back to that group through its proxies.
                // Same shape, and the same reason, as the TableContinuation clear above.
                _setup = new TableSetup();
                tableBox.TableSetup = null;
            }
        }

        /// <summary>
        /// Get the table cells spacing for all the cells in the table.<br/>
        /// Used to calculate the spacing the table has in addition to regular padding and borders.
        /// </summary>
        /// <param name="tableBox">the table box to calculate the spacing for</param>
        /// <returns>the calculated spacing</returns>
        public static double GetTableSpacing(CssBox tableBox)
        {
            var count = 0;
            var columns = 0;

            foreach (var box in tableBox.Boxes)
            {
                switch (box.DerivedStyle.ActualDisplay)
                {
                    case Keywords.TableColumn:
                        columns += GetSpan(box);
                        break;
                    case Keywords.TableRowGroup:
                        {
                            foreach (var cr in tableBox.Boxes)
                            {
                                count++;
                                if (cr.DerivedStyle.ActualDisplay == Keywords.TableRow)
                                    columns = Math.Max(columns, cr.Boxes.Count);
                            }

                            break;
                        }
                    case Keywords.TableRow:
                        count++;
                        columns = Math.Max(columns, box.Boxes.Count);
                        break;
                }

                // limit the amount of rows to process for performance
                if (count > 30)
                    break;
            }

            // Collapse: no grid exists yet at this pre-layout estimation point (this is a static
            // estimator with no CssLayoutEngineTable instance to build one), so there is nothing to
            // resolve against - 0 is a strictly better estimate than a flat per-boundary guess would be,
            // and every real caller re-derives the true spacing once the engine actually runs.
            if (tableBox.BorderCollapse == Keywords.Collapse) return 0;

            // +1 columns because padding is between the cell and table borders
            return (columns + 1) * tableBox.ActualBorderSpacingHorizontal;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="g"></param>
        /// <param name="tableBox"> </param>
        /// <param name="resume">
        /// how <paramref name="tableBox"/> resumes on the current fragmentainer pass, or null when it is
        /// being laid out from the start. A continuation inherits what the table already settled rather
        /// than settling it again — see <see cref="TableSetup"/>.
        /// </param>
        public static async ValueTask PerformLayout(RGraphics g, CssBox tableBox, BreakToken? resume)
        {
            ArgumentNullException.ThrowIfNull(g);
            ArgumentNullException.ThrowIfNull(tableBox);

            try
            {
                var table = new CssLayoutEngineTable(tableBox, resume);
                await table.Layout(g);
            }
            catch (Exception ex)
            {
                if (tableBox.HtmlContainer is { } container)
                    throw container.RenderError(HtmlRenderErrorType.Layout, "Failed table layout", ex);
            }
        }


        #region Private Methods

        /// <summary>
        /// Analyzes the Table and assigns values to this CssTable object.
        /// To be called from the constructor
        /// </summary>
        private async ValueTask Layout(RGraphics g)
        {
            await MeasureWords(_tableBox, g);

            // This engine may be run again over the same table - the per-page-width reflow loop,
            // ShrinkToFit, a §4.3 relocation - and it does not start from the markup unless the last run's
            // output is undone first. A resumed pass is the one run that is not starting from the markup:
            // it continues the table this output belongs to, and the proxies it would drop here are the
            // only surviving reference to the detached group every earlier page repeats.
            if (!_continuesAPreviousPass) RestoreStructureFromAnyPreviousRun();

            // get the table boxes into the proper fields
            AssignBoxKinds();

            // A header-opened rowspan crossing into the body (issue #788) needs both: the grid-row-space
            // numbering to find such a cell, and which cell(s) those are, before InsertEmptyBoxes below
            // places their continuation placeholders.
            ComputeAllRowsOriginalIndices();
            ComputeHeaderRowSpansCrossingIntoBody();

            // Every real cell's grid column, needed before InsertEmptyBoxes' own first call to
            // GetCellRealColumnIndex - see ComputeColumnPlacements' own remarks.
            ComputeColumnPlacements();

            // Before anything below captures an index into _tableBox.Boxes (RemoveHeaderFooterFromTree's
            // _headerIndex/_footerIndex, baked into a proxy and consumed by a later pass's
            // RestoreStructureFromAnyPreviousRun) - see EnsureGridDecorationBoxStructure's own remarks
            // for why the ordering matters.
            EnsureGridDecorationBoxStructure();

            // Insert EmptyBoxes for vertical cell spanning.
            InsertEmptyBoxes();

            // Determine Row and Column Count
            DetermineColumnCount();

            // CSS 2.1 §17.6.2's border-conflict resolution needs only topology (the grid) and computed
            // border styles, not geometry - so it can run here, before any width/height math, and its
            // output (HorizontalLineWidth/VerticalLineWidth) then feeds that math instead of the old
            // flat border-spacing constant. Table-topology-only cost for a `separate` table: none, since
            // neither field is ever set.
            if (_tableBox.BorderCollapse == Keywords.Collapse)
            {
                _grid = BuildTableGrid();
                _collapsedBorders = CollapsedBorderModel.Resolve(
                    _grid, _tableBox, IsColumnCollapsed, IsLeftToRight(),
                    _blockStartBorder, _blockEndBorder, _inlineStartBorder, _inlineEndBorder);
            }
            else
            {
                _grid = null;
                _collapsedBorders = null;
            }

            _tableBox.CollapsedBorderGrid = _grid;
            _tableBox.CollapsedBorders = _collapsedBorders;

            // Must run before any cell is measured/laid out (CalculateColumnWidths/GetAvailableCellWidth
            // read the table's own used border widths; cell content insets read each cell's own).
            ApplyCollapsedUsedBorderWidths();

            // Determine ColumnWidths
            var availCellSpace = CalculateColumnWidths();

            DetermineMissingColumnWidths(availCellSpace);

            // Check for minimum sizes (increment widths if necessary)
            EnforceMaximumSize();

            // While table width is larger than it should, and width is reducible
            EnforceMinimumSize();

            // A collapsed <col>/<colgroup> (CSS 2.1 §17.6.1) must not compete for space with the
            // rest of the table, so this runs last - after every other step that could size a
            // column up from its content has already run - and nothing after it may spread width
            // back into a column this zeroes.
            CollapseColumnWidths();

            // CssBox.PerformLayoutImp's Static/Relative branch already positioned this box at
            // ClientLeft + ActualMarginLeft before dispatching here (ActualMarginLeft calls
            // GetActualMarginLeft with boxWidth: null). For a fixed (non-auto) margin-left that
            // call already returns the final pixel value, so re-adding it below would double-count
            // it - Acid2's own teeth row ("ul { margin: -1em 7em 0; }") landed 63pt too far right
            // because of exactly this. Only 'margin-left: auto' (table centering, e.g.
            // 'margin: 0 auto') genuinely needs a second pass here: GetActualMarginLeft
            // intentionally returns 0 for an auto-margin table when boxWidth is null (the table's
            // own shrink-to-fit width isn't known yet during the earlier pass), deferring the real
            // centering offset - now that GetWidthSum() is known - to this point.
            //
            // Once per table, for the same reason the whole-table pre-checks below are: the offset is
            // derived from the containing block rather than from where the table currently is, so a
            // continuation adding it again would center an already-centered table a second time.
            if (!_continuesAPreviousPass && _tableBox.MarginLeft.Value.IsKeyword)
            {
                _tableBox.Location = _tableBox.Location with
                {
                    X = _tableBox.Location.X + CssLayoutEngine.GetActualMarginLeft(_tableBox, GetWidthSum())
                };
            }

            // Ensure there's no padding
            _tableBox.PaddingLeft = _tableBox.PaddingTop = _tableBox.PaddingRight = _tableBox.PaddingBottom = "0";

            //Actually layout cells!
            await LayoutCells(g);

            // Issue #735's actual fix: every collapse participant's own border paint is suppressed here,
            // and CollapsedBorderSegments - built from this pass's real row/cell geometry, after it is
            // final - is what FragmentPainter draws instead, once, after every table-internal background.
            // Boxes paint in tree order, so a later row's opaque cell background otherwise lands on top of
            // (and erases) an earlier row's border - suppressing the independent per-box draws and
            // painting the resolved borders late is what stops that regardless of paint order elsewhere.
            SuppressParticipantBorderPaint();
            EmitCollapsedBorderSegments();
            EmitHeaderFooterBorderSegments();

            // Published only now that the run has finished - see the constructor for what a half-settled
            // setup would cost a later continuation. A continuation re-publishes the instance it inherited.
            _tableBox.TableSetup = _setup;
        }

        /// <summary>
        /// States every collapse participant's <i>used</i> border widths - half the resolved grid-line
        /// width per edge - via <see cref="DerivedStyle.SetCollapsedUsedBorderWidths"/>, so
        /// <c>ClientLeft</c>/<c>ClientTop</c>/content insets and the width-sum math all agree with the
        /// spacing model <see cref="HorizontalSpacingAt"/>/<see cref="VerticalSpacingAt"/> implement. A
        /// row/row-group/column/column-group owns no box-model space of its own under this model - the
        /// grid line's room is charged entirely to the cells that actually border it - so those get all
        /// zeros. A cell spanning multiple segments takes the max resolved width across its own span on
        /// each edge, so its inset clears the thickest segment it touches.
        /// </summary>
        private void ApplyCollapsedUsedBorderWidths()
        {
            if (_grid is not { } grid || _collapsedBorders is not { } model)
            {
                // Not (or no longer) a collapsed table - undo whatever an earlier pass over this same
                // table (ShrinkToFit, a §4.3 relocation, a per-page-width reflow) may have stated.
                _tableBox.DerivedStyle.ClearCollapsedUsedBorderWidths();
                foreach (var box in _tableBox.Boxes)
                {
                    if (box.DerivedStyle.ActualDisplay == Keywords.TableRowGroup)
                        box.DerivedStyle.ClearCollapsedUsedBorderWidths();
                }
                _headerBox?.DerivedStyle.ClearCollapsedUsedBorderWidths();
                _footerBox?.DerivedStyle.ClearCollapsedUsedBorderWidths();
                foreach (var column in _columns) column.DerivedStyle.ClearCollapsedUsedBorderWidths();
                foreach (var box in _tableBox.Boxes)
                {
                    if (box.DerivedStyle.ActualDisplay == Keywords.TableColumnGroup)
                        box.DerivedStyle.ClearCollapsedUsedBorderWidths();
                }
                foreach (var row in _allRows)
                {
                    row.DerivedStyle.ClearCollapsedUsedBorderWidths();
                    foreach (var cell in row.Boxes) cell.DerivedStyle.ClearCollapsedUsedBorderWidths();
                }
                return;
            }

            var rowCount = grid.RowCount;
            var columnCount = grid.ColumnCount;

            {
                var (top, right, bottom, left) = ResolvePhysicalBorderWidths(
                    blockStartWidth: model.HorizontalLineWidth[0] / 2,
                    blockEndWidth: model.HorizontalLineWidth[rowCount] / 2,
                    inlineStartWidth: model.VerticalLineWidth[0] / 2,
                    inlineEndWidth: model.VerticalLineWidth[columnCount] / 2);
                _tableBox.DerivedStyle.SetCollapsedUsedBorderWidths(top, right, bottom, left);
            }

            foreach (var box in _tableBox.Boxes)
            {
                if (box.DerivedStyle.ActualDisplay == Keywords.TableRowGroup)
                    box.DerivedStyle.SetCollapsedUsedBorderWidths(0, 0, 0, 0);
            }
            _headerBox?.DerivedStyle.SetCollapsedUsedBorderWidths(0, 0, 0, 0);
            _footerBox?.DerivedStyle.SetCollapsedUsedBorderWidths(0, 0, 0, 0);
            foreach (var column in _columns) column.DerivedStyle.SetCollapsedUsedBorderWidths(0, 0, 0, 0);
            foreach (var box in _tableBox.Boxes)
            {
                if (box.DerivedStyle.ActualDisplay == Keywords.TableColumnGroup)
                    box.DerivedStyle.SetCollapsedUsedBorderWidths(0, 0, 0, 0);
            }

            var seen = new HashSet<CssBox>(ReferenceEqualityComparer.Instance);

            foreach (var row in _allRows)
            {
                row.DerivedStyle.SetCollapsedUsedBorderWidths(0, 0, 0, 0);

                foreach (var cell in row.Boxes)
                {
                    if (cell is CssSpacingBox spacer)
                    {
                        spacer.DerivedStyle.SetCollapsedUsedBorderWidths(0, 0, 0, 0);
                        continue;
                    }

                    if (!seen.Add(cell)) continue;
                    if (!grid.TryGetSpan(cell, out var span)) continue;

                    double blockStartWidth = 0, blockEndWidth = 0, inlineStartWidth = 0, inlineEndWidth = 0;

                    for (var c = span.Column; c <= span.LastColumn && c < columnCount; c++)
                    {
                        blockStartWidth = Math.Max(blockStartWidth, model.Horizontal(span.Row, c).UsedWidth / 2);
                        blockEndWidth = Math.Max(blockEndWidth, model.Horizontal(span.LastRow + 1, c).UsedWidth / 2);
                    }
                    for (var r = span.Row; r <= span.LastRow && r < rowCount; r++)
                    {
                        inlineStartWidth = Math.Max(inlineStartWidth, model.Vertical(r, span.Column).UsedWidth / 2);
                        inlineEndWidth = Math.Max(inlineEndWidth, model.Vertical(r, span.LastColumn + 1).UsedWidth / 2);
                    }

                    var (top, right, bottom, left) =
                        ResolvePhysicalBorderWidths(blockStartWidth, blockEndWidth, inlineStartWidth, inlineEndWidth);
                    cell.DerivedStyle.SetCollapsedUsedBorderWidths(top, right, bottom, left);
                }
            }
        }

        /// <summary>
        /// Routes a table participant's own block-start/block-end/inline-start/inline-end used
        /// half-border-widths to the four physical (top/right/bottom/left) slots
        /// <see cref="DerivedStyle.SetCollapsedUsedBorderWidths"/> takes, via <see cref="_blockStartBorder"/>/
        /// <see cref="_blockEndBorder"/>/<see cref="_inlineStartBorder"/>/<see cref="_inlineEndBorder"/> -
        /// the same resolved mapping <see cref="CollapsedBorderModel"/>'s own candidate collection is
        /// given, reused here rather than a second, independently-derived mapper.
        /// </summary>
        private (double Top, double Right, double Bottom, double Left) ResolvePhysicalBorderWidths(
            double blockStartWidth, double blockEndWidth, double inlineStartWidth, double inlineEndWidth)
        {
            double top = 0, right = 0, bottom = 0, left = 0;

            Assign(_blockStartBorder, blockStartWidth);
            Assign(_blockEndBorder, blockEndWidth);
            Assign(_inlineStartBorder, inlineStartWidth);
            Assign(_inlineEndBorder, inlineEndWidth);

            return (top, right, bottom, left);

            void Assign(Border side, double value)
            {
                switch (side)
                {
                    case Border.Top: top = value; break;
                    case Border.Right: right = value; break;
                    case Border.Bottom: bottom = value; break;
                    default: left = value; break;
                }
            }
        }

        /// <summary>
        /// Suppresses every collapse participant's own independent border paint - see the call site's
        /// own remarks. Idempotent and safe to call unconditionally: a <c>separate</c> table (or one that
        /// somehow changed collapse mode since a previous pass) is explicitly cleared back to
        /// <see cref="BorderEdges.None"/> rather than left however an earlier pass set it.
        /// </summary>
        private void SuppressParticipantBorderPaint()
        {
            var edges = _tableBox.BorderCollapse == Keywords.Collapse ? BorderEdges.All : BorderEdges.None;

            _tableBox.SuppressedBorderEdges = edges;

            foreach (var box in _tableBox.Boxes)
            {
                if (box.DerivedStyle.ActualDisplay == Keywords.TableRowGroup)
                    box.SuppressedBorderEdges = edges;
            }

            if (_headerBox is not null) _headerBox.SuppressedBorderEdges = edges;
            if (_footerBox is not null) _footerBox.SuppressedBorderEdges = edges;

            // <col>/<colgroup> - their own border paint (they are never painted at all otherwise; see
            // SetColumnBoxDimensions) is suppressed the same way once they participate in resolution.
            foreach (var column in _columns) column.SuppressedBorderEdges = edges;
            foreach (var box in _tableBox.Boxes)
            {
                if (box.DerivedStyle.ActualDisplay == Keywords.TableColumnGroup)
                    box.SuppressedBorderEdges = edges;
            }

            foreach (var row in _allRows)
            {
                row.SuppressedBorderEdges = edges;

                // Includes CssSpacingBox - harmless, it paints nothing of its own regardless.
                foreach (var cell in row.Boxes) cell.SuppressedBorderEdges = edges;
            }
        }

        /// <summary>
        /// Builds <see cref="CssBox.CollapsedBorderSegments"/> from this pass's real, final row/cell
        /// geometry - one merged run per grid line per contiguous stretch of identically-resolved
        /// segments (a colspan/rowspan cell's own differing per-column/per-row resolution, from
        /// <see cref="CollapsedBorderModel"/> resolving on the unit grid, is what makes the runs
        /// naturally split where the resolved border actually changes, with no special-casing here).
        /// </summary>
        /// <remarks>
        /// Excludes every line inside a detached header's/footer's own row range (including its own
        /// outer edge and its boundary to the body) - a repeated group is shown through one
        /// <see cref="CssProxyBox"/> per page, each repositioning the <i>same</i> shared row objects
        /// (<see cref="CssProxyBox.PerformLayoutImp"/>), so by the time this runs those rows' own
        /// <c>Location</c>/<c>ActualBottom</c> reflect only whichever proxy happened to lay out last -
        /// not any particular page. <see cref="EmitHeaderFooterBorderSegments"/> is what emits those
        /// lines instead, once per proxy, from that proxy's own captured
        /// <see cref="CssProxyBox.SourceGeometry"/> snapshot.
        /// </remarks>
        private void EmitCollapsedBorderSegments()
        {
            if (_grid is not { } grid || _collapsedBorders is not { } model)
            {
                _tableBox.CollapsedBorderSegments = null;
                return;
            }

            var segments = new List<CollapsedBorderSegment>();

            var headerRowCount = _headerBox != null ? HeaderRowCountInGrid : 0;
            var footerRowCount = _footerBox != null ? FooterRowCountInGrid : 0;

            var lineStart = _headerBox != null ? headerRowCount + 1 : 0;
            var lineEnd = _footerBox != null ? grid.RowCount - footerRowCount - 1 : grid.RowCount;

            for (var line = lineStart; line <= lineEnd; line++)
            {
                if (model.HorizontalLineWidth[line] <= 0) continue;
                var rowAxisCenter = GetGridLineY(grid, line, model);

                EmitRuns(grid.ColumnCount, col => model.Horizontal(line, col), (start, end, border) =>
                {
                    var colAxisStart = GetGridLineX(grid, start, model);
                    var colAxisEnd = GetGridLineX(grid, end, model);
                    if (colAxisStart is null || colAxisEnd is null) return;

                    // !_isVertical: this loop resolves row-axis (block-axis) boundaries, which paint as a
                    // physically horizontal stripe for horizontal-tb but a physically vertical one for a
                    // vertical table (rows stack along physical X there) - see RowBoundaryRect's own remarks.
                    segments.Add(new CollapsedBorderSegment(!_isVertical,
                        RowBoundaryRect(rowAxisCenter, colAxisStart.Value, colAxisEnd.Value, border.Width),
                        border.Style, border.Width, border.Color));
                });
            }

            var rowStart = headerRowCount;
            var rowEnd = grid.RowCount - footerRowCount;

            for (var line = 0; line <= grid.ColumnCount; line++)
            {
                if (model.VerticalLineWidth[line] <= 0) continue;
                var colAxisCenter = GetGridLineX(grid, line, model);
                if (colAxisCenter is null) continue;

                EmitRuns(rowEnd - rowStart, i => model.Vertical(rowStart + i, line), (start, end, border) =>
                {
                    var rowAxisStart = GetGridLineY(grid, rowStart + start, model);
                    var rowAxisEnd = GetGridLineY(grid, rowStart + end, model);

                    segments.Add(new CollapsedBorderSegment(_isVertical,
                        ColumnBoundaryRect(colAxisCenter.Value, rowAxisStart, rowAxisEnd, border.Width),
                        border.Style, border.Width, border.Color));
                });
            }

            _tableBox.CollapsedBorderSegments = segments;
        }

        /// <summary>
        /// The physical rect a row-axis (block-axis) grid-line segment paints as: a wide, thin stripe
        /// spanning <paramref name="colAxisStart"/>..<paramref name="colAxisEnd"/> along the column axis
        /// and centered on <paramref name="rowAxisCenter"/>, <paramref name="width"/> thick, along the row
        /// axis - physical Y for horizontal-tb (a visually horizontal stripe), physical X for a vertical
        /// table (a visually vertical one, since rows stack along physical X there).
        /// </summary>
        /// <remarks>
        /// <paramref name="colAxisStart"/>/<paramref name="colAxisEnd"/> (from <see cref="GetGridLineX"/>,
        /// which has no <c>_rowAxisStartIsAtMax</c> equivalent) are always given in increasing physical
        /// order. <paramref name="rowAxisCenter"/> alone can't invert a single point, but the two-argument
        /// overload below normalizes its own row-axis pair regardless, since <see cref="GetGridLineY"/>
        /// runs <i>decreasing</i> with grid-line index for a <c>vertical-rl</c> table (row 0 sits at the
        /// physical-max edge there) - a bare <c>end - start</c> would otherwise hand a negative
        /// width/height to <see cref="RRect"/>, whose own <c>IsEmpty</c>/painting consumers assume
        /// non-negative extents.
        /// </remarks>
        private RRect RowBoundaryRect(double rowAxisCenter, double colAxisStart, double colAxisEnd, double width) =>
            _isVertical
                ? new RRect(rowAxisCenter - width / 2, colAxisStart, width, colAxisEnd - colAxisStart)
                : new RRect(colAxisStart, rowAxisCenter - width / 2, colAxisEnd - colAxisStart, width);

        /// <summary>
        /// The physical rect a column-axis (inline-axis) grid-line segment paints as: a wide, thin stripe
        /// spanning <paramref name="rowAxisStart"/>..<paramref name="rowAxisEnd"/> along the row axis and
        /// centered on <paramref name="colAxisCenter"/>, <paramref name="width"/> thick, along the column
        /// axis - physical X for horizontal-tb (a visually vertical stripe), physical Y for a vertical
        /// table (a visually horizontal one, since columns run top-to-bottom along physical Y there for
        /// both writing modes). Normalizes <paramref name="rowAxisStart"/>/<paramref name="rowAxisEnd"/>'s
        /// order itself - see <see cref="RowBoundaryRect"/>'s own remarks for why a vertical-rl table can
        /// hand these in decreasing physical order.
        /// </summary>
        private RRect ColumnBoundaryRect(double colAxisCenter, double rowAxisStart, double rowAxisEnd, double width)
        {
            var rowAxisMin = Math.Min(rowAxisStart, rowAxisEnd);
            var rowAxisExtent = Math.Abs(rowAxisEnd - rowAxisStart);

            return _isVertical
                ? new RRect(rowAxisMin, colAxisCenter - width / 2, rowAxisExtent, width)
                : new RRect(colAxisCenter - width / 2, rowAxisMin, width, rowAxisExtent);
        }

        /// <summary>
        /// A box's own row-axis-start-facing (block-start-facing) physical edge - <c>Location.Y</c> for
        /// horizontal-tb, and for a vertical table whichever of <c>Location.X</c>/<c>ActualRight</c>
        /// actually faces block-start, per <see cref="_rowAxisStartIsAtMax"/> (<c>ActualRight</c> for
        /// <c>vertical-rl</c>, since row-axis-start is the physical-max edge there; <c>Location.X</c> for
        /// <c>vertical-lr</c>, matching horizontal-tb's own physical-min-is-near-edge shape). Generalizes
        /// <see cref="GetGridLineY"/>'s own near/far-edge reasoning for
        /// <see cref="EmitHeaderFooterBorderSegments"/>'s own boundary-row geometry, which reads from a
        /// live <see cref="CssBox"/> (the proxy) or a captured <see cref="BoxGeometrySnapshot.BoxGeometry"/>
        /// (a header/footer's own snapshotted rows) - both overloads exist for that reason.
        /// </summary>
        private double RowAxisNearEdge(CssBox box) => RowAxisNearEdge(box.Location.Y, box.Location.X, box.ActualRight);

        /// <summary>The row-axis-end-facing (block-end-facing) physical edge - see <see cref="RowAxisNearEdge(CssBox)"/>.</summary>
        private double RowAxisFarEdge(CssBox box) => RowAxisFarEdge(box.ActualBottom, box.Location.X, box.ActualRight);

        private double RowAxisNearEdge(BoxGeometrySnapshot.BoxGeometry geometry) =>
            RowAxisNearEdge(geometry.Location.Y, geometry.Location.X, geometry.ActualRight);

        private double RowAxisFarEdge(BoxGeometrySnapshot.BoxGeometry geometry) =>
            RowAxisFarEdge(geometry.ActualBottom, geometry.Location.X, geometry.ActualRight);

        // The one core the four overloads above unpack their box/geometry's Location.Y/Location.X/
        // ActualRight into - CssBox and BoxGeometrySnapshot.BoxGeometry expose the same three physical
        // fields this needs but share no common interface, so the overloads exist only to bridge that,
        // not to duplicate the axis-selection rule itself.
        private double RowAxisNearEdge(double locationY, double locationX, double actualRight) =>
            !_isVertical ? locationY : _rowAxisStartIsAtMax ? actualRight : locationX;

        private double RowAxisFarEdge(double actualBottom, double locationX, double actualRight) =>
            !_isVertical ? actualBottom : _rowAxisStartIsAtMax ? locationX : actualRight;

        /// <summary>
        /// The sign a row-axis-far-edge reading needs to recover a shared boundary's true center from the
        /// overlap band it names (<see cref="GetGridLineY"/>'s own remarks: <c>+1</c> for <c>vertical-rl</c>,
        /// <c>-1</c> otherwise). A row-axis-near-edge reading needs the <i>opposite</i> sign - by the
        /// overlap-symmetry argument <see cref="EmitHeaderFooterBorderSegments"/>'s own remarks make for
        /// its footer-boundary branch (the block-end-ward neighbor's own near edge sits exactly as far past
        /// the true center, on the opposite side, as the block-start-ward neighbor's far edge does).
        /// </summary>
        private int RowAxisFarEdgeCorrectionSign => _rowAxisStartIsAtMax ? 1 : -1;

        /// <summary>
        /// Builds each detached header's/footer's own <see cref="CssBox.CollapsedBorderSegments"/> -
        /// once per <see cref="CssProxyBox"/>, i.e. once per page it is shown on (every occurrence goes
        /// through a proxy, including a group's only appearance when it does not repeat - see
        /// <see cref="CreateHeaderProxy"/>/<see cref="CreateFooterProxy"/>'s call sites). Two things a
        /// repeated group needs that a plain body row never does:
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Its own internal grid lines translate per page</b>, read from the proxy's own captured
        /// <see cref="CssProxyBox.SourceGeometry"/> rather than the shared source rows' live geometry -
        /// see <see cref="EmitCollapsedBorderSegments"/>'s remarks for why the live geometry is not
        /// trustworthy here. A group's own topology (which cells/rows exist) is page-invariant, so
        /// <see cref="TableGrid"/>/<see cref="CollapsedBorderModel"/>'s already-resolved values are still
        /// used for style/width/color - only position is re-read per proxy. X does not need the same
        /// treatment: a repeat's <c>startX</c> is always <see cref="_tableBox"/>'s own <c>ClientLeft</c>,
        /// identical on every page, so the existing live-geometry <see cref="GetGridLineX"/> is safe to
        /// reuse for every segment this method emits, including the boundary one below.
        /// </para>
        /// <para>
        /// <b>The line where the group meets the body is genuinely different per page</b> - CSS 2.1
        /// §17.6.2 resolves a border against whichever row is visually adjacent, and a repeat's neighbor
        /// is whatever row starts/ends <i>that page</i>, not the header/footer's original DOM neighbor.
        /// This is why that one line is excluded from the "own outer edge" loop below and resolved fresh
        /// via <see cref="CollapsedBorderModel.ResolveRepeatedGroupBoundary"/> against the real adjacent
        /// row instead of <see cref="_collapsedBorders"/>'s single, DOM-order-based resolution for it.
        /// </para>
        /// </remarks>
        private void EmitHeaderFooterBorderSegments()
        {
            if (_headerBox is not null) _headerBox.CollapsedBorderSegments = null;
            if (_footerBox is not null) _footerBox.CollapsedBorderSegments = null;

            if (_grid is not { } grid || _collapsedBorders is not { } model) return;

            // Accumulated across every proxy of the same source, exactly like _tableBox's own segments
            // span every page it's on: FragmentEmitter gives a repeated group's fragment the *source*
            // box's identity on every page (ChildrenOf yields proxy.SourceBox, not the proxy itself), so
            // paint's box.CollapsedBorderSegments check reads _headerBox/_footerBox - one property, not
            // one per proxy - and relies on each page's own fragment.OriginY/clip to show only the
            // segments actually on that page (see PaintCollapsedTableBorders' IsRectVisible culling).
            var headerSegments = _headerBox is null ? null : new List<CollapsedBorderSegment>();
            var footerSegments = _footerBox is null ? null : new List<CollapsedBorderSegment>();

            foreach (var proxy in _tableBox.Boxes.OfType<CssProxyBox>())
            {
                var isHeader = ReferenceEquals(proxy.SourceBox, _headerBox);
                var isFooter = !isHeader && ReferenceEquals(proxy.SourceBox, _footerBox);
                if (!isHeader && !isFooter) continue;

                var snapshot = proxy.SourceGeometry;
                if (snapshot is null) continue;

                var sourceStart = isHeader ? 0 : grid.RowCount - FooterRowCountInGrid;
                var sourceCount = isHeader ? HeaderRowCountInGrid : FooterRowCountInGrid;
                if (sourceCount <= 0) continue;

                var segments = isHeader ? headerSegments! : footerSegments!;

                // boundaryLine/boundaryIsInterior need to be known before SnapshotLineY is defined, since
                // SnapshotLineY's own two "outer" branches also cover the group's boundary-to-body line
                // (whichever of them is reached when line == boundaryLine) - see the remarks below.
                var boundaryLine = isHeader ? sourceStart + sourceCount : sourceStart;

                // Whether the boundary line is genuinely interior to the whole table (a real opposing row
                // exists on the other side somewhere) - as opposed to the table's own true outer edge. Only
                // an interior line's reported position needs the overlap-band-to-center correction; see
                // GetGridLineY's remarks.
                var boundaryIsInterior = boundaryLine > 0 && boundaryLine < grid.RowCount;
                var boundaryHalfWidth = boundaryIsInterior ? model.HorizontalLineWidth[boundaryLine] / 2.0 : 0.0;

                // The sign a row-axis-far-edge reading needs to recover the true center - see
                // RowAxisFarEdgeCorrectionSign's own remarks. A near-edge reading needs the opposite sign.
                var farSign = RowAxisFarEdgeCorrectionSign;
                var nearSign = -farSign;

                double? SnapshotLine(int line)
                {
                    if (line <= sourceStart)
                    {
                        if (!snapshot.TryGetGeometry(grid.RowAt(sourceStart), out var top)) return null;
                        // The group's own near edge is the table's true outer edge (header) or the boundary
                        // to the body (footer) - only the latter needs the correction, and only when line is
                        // actually that boundary (line == boundaryLine can only hold for a footer here,
                        // since a header's boundary sits at its own *far* edge, the other branch below).
                        var near = RowAxisNearEdge(top);
                        return line == boundaryLine ? near + nearSign * boundaryHalfWidth : near;
                    }
                    if (line >= sourceStart + sourceCount)
                    {
                        if (!snapshot.TryGetGeometry(grid.RowAt(sourceStart + sourceCount - 1), out var bottom)) return null;
                        var far = RowAxisFarEdge(bottom);
                        return line == boundaryLine ? far + farSign * boundaryHalfWidth : far;
                    }
                    // Interior to the group's own row range - see GetGridLineY's remarks: the block-start-
                    // ward neighbor's own far edge names the overlap band's own far edge, not its center, so
                    // half the resolved line width has to come back off it (the sign GetGridLineY's own
                    // interior branch uses, reused here via farSign for exactly the same reason).
                    return snapshot.TryGetGeometry(grid.RowAt(line - 1), out var above)
                        ? RowAxisFarEdge(above) + farSign * model.HorizontalLineWidth[line] / 2.0
                        : null;
                }

                // The group's own internal lines and whichever of its two outer edges is not the
                // boundary to the body (the other is resolved fresh, below).
                var ownLineStart = isHeader ? sourceStart : sourceStart + 1;
                var ownLineEnd = isHeader ? sourceStart + sourceCount - 1 : sourceStart + sourceCount;

                for (var line = ownLineStart; line <= ownLineEnd; line++)
                {
                    if (model.HorizontalLineWidth[line] <= 0) continue;
                    var rowAxisCenter = SnapshotLine(line);
                    if (rowAxisCenter is null) continue;

                    EmitRuns(grid.ColumnCount, col => model.Horizontal(line, col), (start, end, border) =>
                    {
                        var colAxisStart = GetGridLineX(grid, start, model);
                        var colAxisEnd = GetGridLineX(grid, end, model);
                        if (colAxisStart is null || colAxisEnd is null) return;

                        segments.Add(new CollapsedBorderSegment(!_isVertical,
                            RowBoundaryRect(rowAxisCenter.Value, colAxisStart.Value, colAxisEnd.Value, border.Width),
                            border.Style, border.Width, border.Color));
                    });
                }

                for (var line = 0; line <= grid.ColumnCount; line++)
                {
                    if (model.VerticalLineWidth[line] <= 0) continue;
                    var colAxisCenter = GetGridLineX(grid, line, model);
                    if (colAxisCenter is null) continue;

                    EmitRuns(sourceCount, i => model.Vertical(sourceStart + i, line), (start, end, border) =>
                    {
                        var rowAxisStart = SnapshotLine(sourceStart + start);
                        var rowAxisEnd = SnapshotLine(sourceStart + end);
                        if (rowAxisStart is null || rowAxisEnd is null) return;

                        segments.Add(new CollapsedBorderSegment(_isVertical,
                            ColumnBoundaryRect(colAxisCenter.Value, rowAxisStart.Value, rowAxisEnd.Value, border.Width),
                            border.Style, border.Width, border.Color));
                    });
                }

                var proxyNear = RowAxisNearEdge(proxy);
                var proxyFar = RowAxisFarEdge(proxy);
                var groupRow = isHeader ? sourceStart + sourceCount - 1 : sourceStart;

                // The row whose *span* reaches this boundary, not the first one whose own near edge is
                // past it: border-collapse overlaps adjacent boxes by (up to) the resolved line width, so
                // a row with a thick border can legitimately start a little before the proxy's own far edge
                // while still being the row the header sits directly above - filtering on the raw edge
                // alone would skip it and wrongly land on the row after. The comparison direction (and the
                // epsilon's sign) flips with farSign/nearSign: a vertical-rl table's row-axis physical order
                // runs opposite its topological order (row 0 sits at the physical-max edge), so "the first
                // row topologically after the header" is the first row whose own far edge has fallen *below*
                // (not risen above) the header's own far edge.
                var adjacentRowIndex = isHeader
                    ? _bodyRows.FindIndex(r => farSign > 0 ? RowAxisFarEdge(r) <= proxyFar + 0.5 : RowAxisFarEdge(r) >= proxyFar - 0.5)
                    : _bodyRows.FindLastIndex(r => farSign > 0 ? RowAxisNearEdge(r) >= proxyNear - 0.5 : RowAxisNearEdge(r) <= proxyNear + 0.5);

                if (adjacentRowIndex >= 0)
                {
                    var groupRowGroup = isHeader ? _headerBox : _footerBox;

                    var resolved = CollapsedBorderModel.ResolveRepeatedGroupBoundary(
                        grid, groupRow, groupRowGroup, HeaderRowCountInGrid + adjacentRowIndex,
                        groupIsAbove: isHeader, IsLeftToRight(), _blockStartBorder, _blockEndBorder);

                    // proxyFar/proxyNear each name one edge of the overlap band, exactly like GetGridLineY's
                    // own far/near-edge reasoning - so the same correction applies here. This has to agree
                    // exactly with SnapshotLine(boundaryLine)'s own now-corrected value (used by the
                    // vertical-divider loop above for any run spanning the group's full row range) - both
                    // read from the same proxyFar/bottom-far-edge (equivalently proxyNear/top-near-edge)
                    // and the same model.HorizontalLineWidth[boundaryLine], so a divider that reaches this
                    // line still meets the boundary segment exactly, not offset by half its width.
                    var boundaryPos = isHeader ? proxyFar + farSign * boundaryHalfWidth : proxyNear + nearSign * boundaryHalfWidth;

                    EmitRuns(grid.ColumnCount, col => resolved[col], (start, end, border) =>
                    {
                        var colAxisStart = GetGridLineX(grid, start, model);
                        var colAxisEnd = GetGridLineX(grid, end, model);
                        if (colAxisStart is null || colAxisEnd is null) return;

                        segments.Add(new CollapsedBorderSegment(!_isVertical,
                            RowBoundaryRect(boundaryPos, colAxisStart.Value, colAxisEnd.Value, border.Width),
                            border.Style, border.Width, border.Color));
                    });
                }
                else if (model.HorizontalLineWidth[boundaryLine] > 0)
                {
                    // No opposing row exists at all on either side of this line (a <thead>-only/<tfoot>-only
                    // table, or a header immediately followed by a footer with no body rows in between) -
                    // this boundary is then either the table's own true outer edge or a fixed header/footer
                    // adjacency that doesn't vary per page either way, both of which the whole-table static
                    // resolution already models correctly (Column/ColumnGroup/Table origins apply exactly
                    // at line 0/RowCount - see CollectHorizontal), unlike the fresh per-page resolution
                    // above, which deliberately excludes those origins because they don't apply to a
                    // genuinely interior line. SnapshotLine(boundaryLine) already carries the correction
                    // (see its own definition above), so its value is used directly with no further
                    // adjustment here.
                    var rowAxisCenter = SnapshotLine(boundaryLine);
                    if (rowAxisCenter is not null)
                    {
                        EmitRuns(grid.ColumnCount, col => model.Horizontal(boundaryLine, col), (start, end, border) =>
                        {
                            var colAxisStart = GetGridLineX(grid, start, model);
                            var colAxisEnd = GetGridLineX(grid, end, model);
                            if (colAxisStart is null || colAxisEnd is null) return;

                            segments.Add(new CollapsedBorderSegment(!_isVertical,
                                RowBoundaryRect(rowAxisCenter.Value, colAxisStart.Value, colAxisEnd.Value, border.Width),
                                border.Style, border.Width, border.Color));
                        });
                    }
                }
            }

            if (_headerBox is not null) _headerBox.CollapsedBorderSegments = headerSegments;
            if (_footerBox is not null) _footerBox.CollapsedBorderSegments = footerSegments;
        }

        /// <summary>
        /// Walks <paramref name="count"/> unit segments (columns for a horizontal line, rows for a
        /// vertical one), merging consecutive ones with an identical resolved border into one run and
        /// reporting each run via <paramref name="emit"/> - the [start, end) index range and the shared
        /// <see cref="CollapsedBorder"/>. A segment that doesn't paint (<see cref="CollapsedBorder.IsPainted"/>
        /// false) ends whatever run precedes it without starting a new one.
        /// </summary>
        private static void EmitRuns(int count, Func<int, CollapsedBorder> resolvedAt, Action<int, int, CollapsedBorder> emit)
        {
            var runStart = -1;
            var current = CollapsedBorder.None;

            for (var i = 0; i <= count; i++)
            {
                var resolved = i < count ? resolvedAt(i) : CollapsedBorder.None;
                var continuesRun = runStart >= 0 && resolved.IsPainted &&
                    resolved.Style == current.Style && resolved.Width == current.Width && resolved.Color == current.Color;

                if (continuesRun) continue;

                if (runStart >= 0) emit(runStart, i, current);

                if (i < count && resolved.IsPainted)
                {
                    runStart = i;
                    current = resolved;
                }
                else
                {
                    runStart = -1;
                }
            }
        }

        /// <summary>
        /// The document-space row-axis position (physical Y for a horizontal-tb table, physical X for a
        /// vertical one) of horizontal grid line <paramref name="line"/>'s <i>center</i> (0..RowCount),
        /// read from this pass's real, laid-out row geometry (post-<see cref="ReflectRowAxisForVerticalRl"/>
        /// for <c>vertical-rl</c>, which this method runs after) rather than derived arithmetically. At an
        /// outer edge (<c>line &lt;= 0</c> or <c>line &gt;= RowCount</c>) a row's own half-border
        /// reservation and the table's own separately-applied half sit on non-overlapping sides of one
        /// point, so that row's own row-axis-start edge already <i>is</i> the center. At an <b>interior</b>
        /// line the two neighboring rows instead overlap by the <i>whole</i> resolved line width
        /// (border-collapse's overlap-then-paint-border-last model - see <see cref="VerticalSpacingAt"/>),
        /// so the block-start-ward neighbor's own far edge names only the overlap band's own far edge, not
        /// its center - halving <paramref name="model"/>'s resolved width back off it is what recovers the
        /// true center a segment must be built around.
        /// </summary>
        /// <remarks>
        /// Row <paramref name="line"/><c>-1</c> is always the <i>topologically</i> block-start-ward
        /// neighbor - grid indices are a pure topology fact, unaffected by which physical side either
        /// writing mode's block-start actually is. Which physical edge is its own "far" (overlap-facing)
        /// edge, and which direction recovers the center from it, is exactly what
        /// <see cref="_rowAxisStartIsAtMax"/> flips: for <c>horizontal-tb</c>/<c>vertical-lr</c> (row 0
        /// grows from the physical-min edge forward, matching topological order), that neighbor's far edge
        /// faces the physical-max direction (<c>ActualBottom</c>/<c>ActualRight</c>), so the center is
        /// <i>behind</i> it (subtract half). For <c>vertical-rl</c> (mirrored so row 0 sits at the
        /// physical-max edge instead), the same topologically-block-start-ward neighbor now sits physically
        /// closer to the table's max edge than the row after it, so its far edge instead faces the
        /// physical-min direction (<c>Location.X</c>), and the center is reached by <i>adding</i> half.
        /// </remarks>
        private double GetGridLineY(TableGrid grid, int line, CollapsedBorderModel model)
        {
            if (line <= 0) return RowAxisNearEdge(grid.RowAt(0));
            if (line >= grid.RowCount) return RowAxisFarEdge(grid.RowAt(grid.RowCount - 1));

            return RowAxisFarEdge(grid.RowAt(line - 1))
                   + RowAxisFarEdgeCorrectionSign * model.HorizontalLineWidth[line] / 2.0;
        }

        /// <summary>
        /// The document-space column-axis position (physical X for a horizontal-tb table, physical Y for
        /// a vertical one) of vertical grid line <paramref name="line"/>'s <i>center</i> (0..ColumnCount) -
        /// read off the first real cell anywhere in the grid whose own edge is that line, since a ragged
        /// row can leave some rows with no cell there at all. Null only for a column with no real cell in
        /// any row on either side of it (a fully empty column), which has no geometry to draw a segment at.
        /// </summary>
        /// <remarks>
        /// See <see cref="GetGridLineY"/>'s own remarks for why an outer edge needs no adjustment while an
        /// interior one does. Unlike the row axis, an interior line's two branches here return <i>opposite</i>
        /// edges of the overlap band - the first branch (a real cell starting at this column) names the
        /// band's own column-axis-min edge, so recovering the center means <i>adding</i> half the resolved
        /// width; the fallback (a cell ending at this column, found only when no row starts one here - e.g.
        /// a colspan crossing the line from the column-axis-min side) names the band's own column-axis-max
        /// edge, so recovering the center means <i>subtracting</i> it instead. No <c>_rowAxisStartIsAtMax</c>
        /// equivalent here - this engine has no <c>direction: rtl</c> column/inline axis for either writing
        /// mode, so the column axis always grows physical-min-forward regardless of orientation.
        /// </remarks>
        private double? GetGridLineX(TableGrid grid, int line, CollapsedBorderModel model)
        {
            var halfWidth = line > 0 && line < grid.ColumnCount ? model.VerticalLineWidth[line] / 2.0 : 0.0;

            for (var r = 0; r < grid.RowCount; r++)
            {
                if (line < grid.ColumnCount && grid.CellAt(r, line) is { } right)
                    return (_isVertical ? right.Location.Y : right.Location.X) + halfWidth;
                if (line > 0 && grid.CellAt(r, line - 1) is { } left)
                    return (_isVertical ? left.ActualBottom : left.ActualRight) - halfWidth;
            }

            return null;
        }

        /// <summary>
        /// Get the table boxes into the proper fields.
        /// </summary>
        private void AssignBoxKinds()
        {
            // A continuation's header/footer are not in the table's child list to be found: an earlier
            // pass detached them, and skipping the restore is what keeps every earlier page's repeated
            // copy alive. They come from what that pass settled instead.
            //
            // Explicitly only on a continuation, though a fresh run's setup is empty anyway: seeding
            // _headerBox from a setup a fresh run had somehow inherited would run *after* the restore put
            // the real <thead> back, and AssignBoxKinds pushes a second header group onto _bodyRows -
            // which is exactly #353's double-count.
            if (_continuesAPreviousPass)
            {
                if (_setup.Header is { } header)
                    (_headerBox, _headerIndex, _headerHeight, _headerRepeats) = header;

                if (_setup.Footer is { } footer)
                    (_footerBox, _footerIndex, _footerHeight, _footerRepeats) = footer;
            }

            // Counts every row of the table's own grid in source order, collapsed ones included - the
            // original-index half of the pair InsertEmptyBoxes/LayoutBodyRow need to map a rowspan onto
            // _bodyRows correctly (see _bodyRowOriginalIndices).
            var originalRowIndex = 0;

            foreach (var box in _tableBox.Boxes)
            {
                // A proxy standing in for a detached group is this engine's own output rather than
                // markup, and it inherits the source's Display - so on a continuation, where the restore
                // that drops proxies is deliberately skipped, the first one would be taken for the header
                // itself and the rest classified as body rows. A proxy has no cells, so positioning one
                // as a row throws on an empty sequence (issue #353, from the other direction).
                if (box is CssProxyBox) continue;

                switch (box.DerivedStyle.ActualDisplay)
                {
                    case Keywords.TableCaption:
                        _captionBoxes.Add(box);
                        break;
                    case Keywords.TableRow:
                        if (!IsRowCollapsed(box))
                        {
                            _bodyRows.Add(box);
                            _bodyRowOriginalIndices.Add(originalRowIndex);
                        }
                        originalRowIndex++;
                        break;
                    case Keywords.TableRowGroup:
                        foreach (CssBox childBox in box.Boxes)
                        {
                            if (childBox.DerivedStyle.ActualDisplay != Keywords.TableRow) continue;

                            if (!IsRowCollapsed(childBox))
                            {
                                _bodyRows.Add(childBox);
                                _bodyRowOriginalIndices.Add(originalRowIndex);
                            }
                            originalRowIndex++;
                        }
                        break;
                    case Keywords.TableHeaderGroup:
                        if (_headerBox != null)
                        {
                            _bodyRows.Add(box);
                            _bodyRowOriginalIndices.Add(originalRowIndex);
                        }
                        else
                            _headerBox = box;
                        originalRowIndex++;
                        break;
                    case Keywords.TableFooterGroup:
                        if (_footerBox != null)
                        {
                            _bodyRows.Add(box);
                            _bodyRowOriginalIndices.Add(originalRowIndex);
                        }
                        else
                            _footerBox = box;
                        originalRowIndex++;
                        break;
                    case Keywords.TableColumn:
                        for (int i = 0; i < GetSpan(box); i++)
                            _columns.Add(box);
                        break;
                    case Keywords.TableColumnGroup:
                        if (box.Boxes.Count == 0)
                        {
                            int gspan = GetSpan(box);
                            for (int i = 0; i < gspan; i++)
                            {
                                _columns.Add(box);
                            }
                        }
                        else
                        {
                            foreach (CssBox bb in box.Boxes)
                            {
                                int bbspan = GetSpan(bb);
                                for (int i = 0; i < bbspan; i++)
                                {
                                    _columns.Add(bb);
                                }
                            }
                        }
                        break;
                }
            }

            if (_headerBox != null)
                _allRows.AddRange(_headerBox.Boxes.Where(r => !IsRowCollapsed(r)));

            _allRows.AddRange(_bodyRows);

            if (_footerBox != null)
                _allRows.AddRange(_footerBox.Boxes.Where(r => !IsRowCollapsed(r)));

            // CSS 2.1 §17.4: caption-side's only two values split the table's caption(s) into a group
            // stacked above the row grid and a group stacked below it - each caption goes by its own
            // computed value, not the table's or the first caption's.
            foreach (var caption in _captionBoxes)
            {
                (caption.CaptionSide == Keywords.Bottom ? _bottomCaptions : _topCaptions).Add(caption);
            }
        }

        /// <summary>
        /// Populates <see cref="_allRowsOriginalIndices"/>: <see cref="_headerBox"/>'s own real per-row
        /// numbering (<see cref="ComputeRowGroupOriginalIndices"/>), continued by one more unbroken counter
        /// across <see cref="_tableBox"/>'s remaining children - mirroring <see cref="AssignBoxKinds"/>'s
        /// own <c>TableRow</c>/<c>TableRowGroup</c> cases exactly, so the result stays index-aligned with
        /// <see cref="_bodyRows"/>, but never incrementing for <see cref="_footerBox"/> itself.
        /// </summary>
        /// <remarks>
        /// A first cut rebased <see cref="_bodyRowOriginalIndices"/>'s own values (which count the header
        /// as one unit, and may have a footer's own unit mixed in) by a single constant, reasoning that
        /// only each body row's original index *relative to the first body row* is used. That reasoning
        /// holds only when a <c>&lt;tfoot&gt;</c> sits before every body row - real, HTML-table-content-
        /// model-legal markup can place one *between* two <c>&lt;tbody&gt;</c> groups instead, where the
        /// footer's one-unit slot perturbs only the body rows *after* it, not by a constant every body
        /// row shares. A fresh, independent walk has no such position-dependent term to get wrong: the
        /// footer is simply never counted, wherever it sits, which is what "no unit for a box that isn't
        /// part of this numbering" already means for the header's own leading contribution too.
        /// </remarks>
        private void ComputeAllRowsOriginalIndices()
        {
            _allRowsOriginalIndices.Clear();
            _allRowsOriginalIndices.AddRange(ComputeRowGroupOriginalIndices(_headerBox));

            var originalRowIndex = _headerBox?.Boxes.Count(r => r.DerivedStyle.ActualDisplay == Keywords.TableRow) ?? 0;

            foreach (var box in _tableBox.Boxes)
            {
                if (box is CssProxyBox) continue;
                if (ReferenceEquals(box, _headerBox) || ReferenceEquals(box, _footerBox)) continue;

                switch (box.DerivedStyle.ActualDisplay)
                {
                    case Keywords.TableRow:
                        if (!IsRowCollapsed(box)) _allRowsOriginalIndices.Add(originalRowIndex);
                        originalRowIndex++;
                        break;
                    case Keywords.TableRowGroup:
                        foreach (var childBox in box.Boxes)
                        {
                            if (childBox.DerivedStyle.ActualDisplay != Keywords.TableRow) continue;

                            if (!IsRowCollapsed(childBox)) _allRowsOriginalIndices.Add(originalRowIndex);
                            originalRowIndex++;
                        }
                        break;
                    case Keywords.TableHeaderGroup or Keywords.TableFooterGroup:
                        // A second <thead>/<tfoot> - AssignBoxKinds folds it into _bodyRows as one whole
                        // entry, with no collapse check of its own (mirrored here unchanged, not newly
                        // introduced) - needs the same one-unit counting to stay index-aligned with it.
                        _allRowsOriginalIndices.Add(originalRowIndex);
                        originalRowIndex++;
                        break;
                }
            }
        }

        /// <summary>
        /// Populates <see cref="_headerRowSpansCrossingIntoBody"/>: every header cell whose <c>rowspan</c>
        /// reaches past the header's own last row (issue #788). <see cref="TableGrid"/>/column-placement
        /// (<see cref="ComputeColumnPlacements"/>, built over the whole-grid <see cref="_allRows"/>) already
        /// treats such a span as reaching into the body and reserves a column for it there - this is the
        /// other half, finding which cells those are so the rest of this engine can agree.
        /// </summary>
        /// <remarks>
        /// A footer-opened span crossing into the body is not possible to detect the same way and does not
        /// need to be: <see cref="_allRows"/> always places footer rows after every body row, so a footer
        /// row's own grid index is never small enough for <see cref="GetEffectiveEndRowIndex(int, int, IReadOnlyList{int}, int)"/>
        /// to walk past it into anything - there is nothing after the footer in the grid to cross into.
        /// </remarks>
        private void ComputeHeaderRowSpansCrossingIntoBody()
        {
            _headerRowSpansCrossingIntoBody.Clear();

            if (_headerBox is null || _bodyRows.Count == 0) return;

            var headerRowCountInGrid = HeaderRowCountInGrid;
            var headerRowIndex = 0;

            foreach (var row in _headerBox.Boxes)
            {
                if (row.DerivedStyle.ActualDisplay != Keywords.TableRow) continue;
                if (IsRowCollapsed(row)) continue;

                foreach (var cell in row.Boxes)
                {
                    var rowSpan = GetRowSpan(cell);
                    if (rowSpan <= 1) continue;

                    var gridEndRow = GetEffectiveEndRowIndex(
                        headerRowIndex, rowSpan, _allRowsOriginalIndices, _allRowsOriginalIndices.Count);

                    if (gridEndRow < headerRowCountInGrid) continue;

                    _headerRowSpansCrossingIntoBody[cell] =
                        Math.Min(gridEndRow - headerRowCountInGrid, _bodyRows.Count - 1);
                }

                headerRowIndex++;
            }
        }

        /// <summary>
        /// Whether <paramref name="row"/> is a table row (or a row inside a collapsed row group -
        /// <c>visibility</c> is an inherited property, so a <c>&lt;tbody&gt;</c>/<c>&lt;thead&gt;</c>/
        /// <c>&lt;tfoot&gt;</c> marked <c>collapse</c> already gives every row inside it the same
        /// computed value without this engine having to walk up to the group itself) that CSS 2.1
        /// <see href="https://www.w3.org/TR/CSS21/tables.html#dynamic-effects">§17.6.1</see> removes
        /// from the table's rendering entirely - as if it had <c>display: none</c>, so it takes no
        /// layout space and the rows after it shift up to fill the gap. Distinct from
        /// <c>visibility: hidden</c>, which this engine still lays out normally (its space stays
        /// reserved) and only <see cref="PeachPDF.Html.Core.Paint.FragmentPainter"/> skips painting.
        /// </summary>
        private static bool IsRowCollapsed(CssBox row) => row.Visibility.Value == Visibility.Collapse;

        /// <summary>
        /// Insert EmptyBoxes for vertical cell spanning.
        /// </summary>
        private void InsertEmptyBoxes()
        {
            if (_tableBox._tableFixed) return;

            for (var currentRow = 0; currentRow < _bodyRows.Count; currentRow++)
            {
                var row = _bodyRows[currentRow];

                for (var k = 0; k < row.Boxes.Count; k++)
                {
                    var cell = row.Boxes[k];
                    var rowSpan = GetRowSpan(cell);
                    var realColumnIndex = GetCellRealColumnIndex(cell); //Real column of the cell
                    var endRowIndex = GetEffectiveEndRowIndex(currentRow, rowSpan);

                    InsertSpacingBoxesForSpan(cell, realColumnIndex, currentRow, endRowIndex);
                }
            }

            // A header cell whose rowspan crosses into the body (issue #788) isn't reached by the loop
            // above - it isn't itself a member of any _bodyRows entry's Boxes - so it needs its own
            // placeholder pass, opening at the sentinel row index TableRowCursor.RowIndex already uses
            // for "opened outside the body's own numbering" (-1) rather than any real _bodyRows index.
            foreach (var (cell, endBodyRow) in _headerRowSpansCrossingIntoBody)
            {
                InsertSpacingBoxesForSpan(cell, GetCellRealColumnIndex(cell), -1, endBodyRow);
            }

            _tableBox._tableFixed = true;
        }

        /// <summary>
        /// Places a <see cref="CssSpacingBox"/> continuation placeholder for <paramref name="cell"/>'s own
        /// <c>rowspan</c> into every <see cref="_bodyRows"/> entry between <paramref name="openingBodyRow"/>
        /// (exclusive) and <paramref name="endRowIndex"/> (inclusive), at <paramref name="realColumnIndex"/> -
        /// the shared body of <see cref="InsertEmptyBoxes"/>'s own loop, reused for a header-opened cell
        /// crossing into the body (issue #788), whose own opening row isn't a member of <see cref="_bodyRows"/>
        /// at all.
        /// </summary>
        /// <remarks>
        /// <paramref name="realColumnIndex"/> is deliberately mutated across the outer loop's own
        /// iterations rather than reset to <see cref="GetCellRealColumnIndex(CssBox)"/>'s own value per
        /// row - matching this method's pre-extraction shape exactly, since re-deriving whether that was
        /// itself a latent bug is out of scope for whichever change last touched this loop.
        /// </remarks>
        private void InsertSpacingBoxesForSpan(CssBox cell, int realColumnIndex, int openingBodyRow, int endRowIndex)
        {
            for (var i = openingBodyRow + 1; i <= endRowIndex; i++)
            {
                var columnCount = 0;
                var inserted = false;
                for (var j = 0; j < _bodyRows[i].Boxes.Count; j++)
                {
                    if (columnCount == realColumnIndex)
                    {
                        _bodyRows[i].Boxes.Insert(columnCount, new CssSpacingBox(_tableBox, ref cell, openingBodyRow, endRowIndex));
                        inserted = true;
                        break;
                    }
                    columnCount++;
                    realColumnIndex -= GetColSpan(_bodyRows[i].Boxes[j]) - 1;
                }

                // The loop above only ever compares columnCount against a later row's *existing*
                // cells, so a rowspan whose column sits at or past that row's last cell (the
                // common case: a rowspan in the table's last column) never matches inside the
                // loop and falls through here with no placeholder inserted at all - the row's
                // column count then silently undercounts and CalculateCountAndWidth's tally
                // (below) misses this row's contribution to it (issue #522).
                if (!inserted && columnCount == realColumnIndex)
                {
                    _bodyRows[i].Boxes.Add(new CssSpacingBox(_tableBox, ref cell, openingBodyRow, endRowIndex));
                }
            }
        }

        /// <summary>
        /// Maps a cell's <c>rowspan</c> - a count of rows in the table's own row grid, collapsed ones
        /// included - to the index of the last row in <see cref="_bodyRows"/> it actually reaches once
        /// <c>visibility: collapse</c> rows (CSS 2.1 §17.6.1) are excluded from that list. Walking the
        /// same number of steps through the shorter filtered list instead, as a raw
        /// <c>currentRow + rowSpan - 1</c> would, lands a span that opens before a collapsed row and
        /// extends into or past it one row too far for every collapsed row it crosses (issue #665).
        /// </summary>
        /// <param name="startRowIndex">
        /// the cell's own row, as an index into <see cref="_bodyRows"/> - or <c>-1</c>, the
        /// <see cref="TableRowCursor.RowIndex"/> sentinel for a <c>&lt;thead&gt;</c>/<c>&lt;tfoot&gt;</c>
        /// group being measured on its own cursor, whose rows are not in <see cref="_bodyRows"/> at all.
        /// </param>
        /// <param name="rowSpan">the cell's raw <c>rowspan</c> value</param>
        private int GetEffectiveEndRowIndex(int startRowIndex, int rowSpan) =>
            GetEffectiveEndRowIndex(startRowIndex, rowSpan, _bodyRowOriginalIndices, _bodyRows.Count);

        /// <summary>
        /// The general form of the mapping above, parameterized over <paramref name="originalIndices"/>/
        /// <paramref name="rowCount"/> rather than reaching for <see cref="_bodyRowOriginalIndices"/>/
        /// <see cref="_bodyRows"/> directly, so <see cref="DetachAndMeasureRepeatedRowGroups"/>'s
        /// header/footer loops can apply the identical collapsed-row remapping against their own
        /// row-group-local numbering (<see cref="ComputeRowGroupOriginalIndices"/>) instead of the body's
        /// - a header/footer rowspan crossing one of the group's own <c>visibility: collapse</c> rows has
        /// exactly the same off-by-one-per-collapsed-row failure mode issue #665 fixed for body rows, and
        /// issue #742's own review is what found it unfixed here.
        /// </summary>
        private static int GetEffectiveEndRowIndex(
            int startRowIndex, int rowSpan, IReadOnlyList<int> originalIndices, int rowCount)
        {
            // Nothing here to map collapsed rows against - fall back to the raw span arithmetic
            // CssSpacingBox used before this mapping existed, which is also correct whenever rowSpan is 1
            // or startRowIndex is otherwise out of range.
            if (startRowIndex < 0 || rowSpan <= 1 || startRowIndex >= originalIndices.Count)
                return startRowIndex + rowSpan - 1;

            var targetOriginalIndex = originalIndices[startRowIndex] + rowSpan - 1;

            var endRowIndex = startRowIndex;
            for (var i = startRowIndex + 1; i < rowCount && originalIndices[i] <= targetOriginalIndex; i++)
                endRowIndex = i;

            return endRowIndex;
        }

        /// <summary>
        /// <paramref name="groupBox"/>'s own row-group-local twin of <see cref="_bodyRowOriginalIndices"/>:
        /// for each of its rows kept after <c>visibility: collapse</c> filtering, in order, the raw index
        /// (collapsed rows counted too) it had among <paramref name="groupBox"/>'s own children alone - a
        /// detached header's/footer's own rows are never part of <see cref="_bodyRows"/>' numbering, so
        /// they need this same mapping computed fresh, against only their own group's rows.
        /// </summary>
        private static List<int> ComputeRowGroupOriginalIndices(CssBox? groupBox)
        {
            var indices = new List<int>();
            if (groupBox is null) return indices;

            var originalRowIndex = 0;
            foreach (var row in groupBox.Boxes)
            {
                if (row.DerivedStyle.ActualDisplay != Keywords.TableRow) continue;

                if (!IsRowCollapsed(row)) indices.Add(originalRowIndex);
                originalRowIndex++;
            }

            return indices;
        }

        /// <summary>How many of <see cref="_allRows"/>'s leading entries are the (filtered) header rows - the offset into it <see cref="_bodyRows"/> starts at.</summary>
        private int HeaderRowCountInGrid => _headerBox?.Boxes.Count(r => !IsRowCollapsed(r)) ?? 0;

        /// <summary>How many of <see cref="_allRows"/>'s trailing entries are the (filtered) footer rows.</summary>
        private int FooterRowCountInGrid => _footerBox?.Boxes.Count(r => !IsRowCollapsed(r)) ?? 0;

        /// <summary>
        /// The last grid row (inclusive, 0-based into <see cref="_allRows"/>) a cell starting at grid row
        /// <paramref name="gridRow"/> with the given <paramref name="rowSpan"/> actually reaches - needs
        /// the same body-row visibility:collapse remapping (<see cref="GetEffectiveEndRowIndex(int, int)"/>,
        /// issue #665) whether the caller wants it for the collapsed-border grid or for
        /// <see cref="GetCellRealColumnIndex"/>'s own cache, so <see cref="ComputeColumnPlacements"/> (the
        /// one caller left - <see cref="BuildTableGrid"/> reuses its output rather than asking again) is
        /// the single place this has to be right. A header row's rowspan needs the identical remapping
        /// too (issue #788: a rowspan crossing a <c>visibility: collapse</c> header row, or reaching past
        /// the header into the body, otherwise lands one row short or long per collapsed row it crosses -
        /// the same failure mode issue #665 already fixed for a body row's own rowspan), via
        /// <see cref="_allRowsOriginalIndices"/> rather than <see cref="_bodyRowOriginalIndices"/>, since a
        /// header-opened span's own start index is in the header's portion of the whole-grid numbering, not
        /// the body's. A footer row's rowspan needs no remapping (<see cref="_allRows"/> always places
        /// footer rows last, so a footer-opened span has nothing after it in the grid to cross into or
        /// reach past - the raw arithmetic fallback below is already correct for it).
        /// </summary>
        private int GetLastRowInGrid(int gridRow, CssBox cell, int rowSpan)
        {
            var headerRowCount = HeaderRowCountInGrid;
            var bodyRowCount = _bodyRows.Count;

            if (gridRow >= headerRowCount && gridRow < headerRowCount + bodyRowCount)
            {
                var bodyIndex = gridRow - headerRowCount;
                return GetEffectiveEndRowIndex(bodyIndex, rowSpan) + headerRowCount;
            }

            if (gridRow < headerRowCount)
            {
                return GetEffectiveEndRowIndex(gridRow, rowSpan, _allRowsOriginalIndices, _allRowsOriginalIndices.Count);
            }

            return gridRow + rowSpan - 1;
        }

        /// <summary>
        /// Builds <see cref="_grid"/> from <see cref="_allRows"/>/<see cref="_columns"/> and this table's
        /// own <see cref="_columnPlacements"/>/<see cref="_columnPlacementsColumnCount"/>
        /// (<see cref="ComputeColumnPlacements"/> has already computed both by the time this runs) rather
        /// than asking <see cref="TableGrid.Build(IReadOnlyList{CssBox}, IReadOnlyList{CssBox}, Dictionary{CssBox, CellPlacement}, int)"/>'s
        /// other overload to recompute them - the placement algorithm running twice per layout pass for
        /// every collapsed-border table was real, avoidable work.
        /// </summary>
        private TableGrid BuildTableGrid() =>
            TableGrid.Build(_allRows, _columns, _columnPlacements!, _columnPlacementsColumnCount);

        /// <summary>
        /// Populates <see cref="_columnPlacements"/>/<see cref="_columnPlacementsColumnCount"/> - every
        /// real cell's grid column via <see cref="TableGrid.ComputeColumnPlacements"/> - so
        /// <see cref="GetCellRealColumnIndex"/> no longer has to sum a row's own preceding <c>Boxes</c>,
        /// which - for a detached header's/footer's own row - under-counts past a rowspan gap
        /// <see cref="InsertEmptyBoxes"/> never pads (issue #740). <see cref="BuildTableGrid"/> reuses this
        /// same output for the collapsed-border grid rather than recomputing it. Must run after
        /// <see cref="AssignBoxKinds"/> (needs <see cref="_allRows"/>/
        /// <see cref="_headerBox"/>/<see cref="_bodyRows"/>) and before <see cref="InsertEmptyBoxes"/>,
        /// whose own first call to <see cref="GetCellRealColumnIndex"/> depends on it - computed once
        /// regardless of <c>border-collapse</c>, since cell positioning (unlike <see cref="_grid"/>) isn't
        /// a collapse-only concern.
        /// </summary>
        private void ComputeColumnPlacements()
        {
            var initialColumnCount = 0;
            foreach (var _ in _columns) initialColumnCount++;

            var (placements, columnCount) = TableGrid.ComputeColumnPlacements(
                _allRows, initialColumnCount, GetRowSpan, GetColSpan, GetLastRowInGrid);

            _columnPlacements = placements;
            _columnPlacementsColumnCount = columnCount;
        }

        /// <summary>The table's own <c>direction</c> (not any cell's) - CSS 2.1 §17.6.2's position tiebreak reads this, not the writing direction of whatever happens to be adjacent.</summary>
        private bool IsLeftToRight() => _tableBox.Direction.Value == DirectionMode.Ltr;

        /// <summary>
        /// Determines <see cref="_columnCount"/> alone - split out from column-width calculation so the
        /// column count (and so <see cref="_allRows"/>/<see cref="_columns"/>) is known before
        /// <see cref="BuildTableGrid"/> needs it, without the width math running first.
        /// </summary>
        private void DetermineColumnCount()
        {
            if (_columns.Count > 0)
            {
                _columnCount = _columns.Count;
            }
            else
            {
                foreach (var b in _allRows)
                {
                    var rowColumnCount = b.Boxes.Sum(GetColSpan);
                    _columnCount = Math.Max(_columnCount, rowColumnCount);
                }
            }

            // Neither branch above can be narrower than every real cell's own rowspan-occupancy-aware
            // column - a header/footer row following a rowspan gap has fewer Boxes entries (so a smaller
            // naive sum) than real columns it needs, and a <col>-declared count can likewise under-declare
            // relative to actual cell content (the same "take the wider of the two" TableGrid.Build's own
            // column-count computation already applies). Without this floor, GetCellRealColumnIndex could
            // return a column _columnWidths (sized off _columnCount) is too short to index - LayoutBodyRow's
            // column-skip loop has no bound check of its own, unlike GetCellWidth's (issue #740).
            _columnCount = Math.Max(_columnCount, _columnPlacementsColumnCount);
        }

        /// <summary>
        /// Determine ColumnWidths, once <see cref="_columnCount"/> is already known.
        /// </summary>
        /// <returns></returns>
        private double CalculateColumnWidths()
        {
            //Initialize column widths array with NaNs
            _columnWidths = new double[_columnCount];
            for (var i = 0; i < _columnWidths.Length; i++)
                _columnWidths[i] = double.NaN;

            var availCellSpace = GetAvailableCellWidth();

            if (_columns.Count > 0)
            {
                // Fill ColumnWidths array by scanning column widths
                for (var i = 0; i < _columns.Count; i++)
                {
                    var columnInlineSize = CellInlineSize(_columns[i]);
                    CssLength len = new(columnInlineSize); //Get specified width

                    if (!(len.Number > 0)) continue; //If some width specified

                    if (len.IsPercentage) //Get width as a percentage
                    {
                        _columnWidths[i] = CssValueParser.ParseNumber(columnInlineSize, availCellSpace);
                    }
                    else if (len.Unit is CssUnit.Pixels or CssUnit.None)
                    {
                        // px (and unitless HTML width attributes, which map to CSS px) convert to
                        // layout points via the shared spec-correct factor.
                        _columnWidths[i] = len.Number * Length.PointsPerPx;
                    }
                }
            }
            else
            {
                // Fill ColumnWidths array by scanning width in table-cell definitions
                foreach (var row in _allRows)
                {
                    //Check for column width in table-cell definitions
                    for (var i = 0; i < _columnCount; i++)
                    {
                        if (i >= 20 && !double.IsNaN(_columnWidths[i])) continue; // limit column width check

                        if (i >= row.Boxes.Count || row.Boxes[i].DerivedStyle.ActualDisplay != Keywords.TableCell) continue;

                        var len = CssValueParser.ParseLength(CellInlineSize(row.Boxes[i]), availCellSpace, row.Boxes[i]);

                        if (!(len > 0)) continue; //If some width specified

                        var colspan = GetColSpan(row.Boxes[i]);
                        len /= Convert.ToSingle(colspan);

                        for (var j = i; j < i + colspan; j++)
                        {
                            _columnWidths[j] = double.IsNaN(_columnWidths[j]) ? len : Math.Max(_columnWidths[j], len);
                        }
                    }
                }
            }
            return availCellSpace;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="availCellSpace"></param>
        private void DetermineMissingColumnWidths(double availCellSpace)
        {
            double occupiedSpace = 0f;

            if (_widthSpecified) //If a width was specified,
            {
                //Assign NaNs equally with space left after gathering not-NaNs
                var numOfNans = 0;

                //Calculate number of NaNs and occupied space
                foreach (var colWidth in _columnWidths!)
                {
                    if (double.IsNaN(colWidth))
                        numOfNans++;
                    else
                        occupiedSpace += colWidth;
                }
                var orgNumOfNans = numOfNans;

                double[]? orgColWidths = null;
                if (numOfNans < _columnWidths.Length)
                {
                    orgColWidths = new double[_columnWidths.Length];
                    for (var i = 0; i < _columnWidths.Length; i++)
                        orgColWidths[i] = _columnWidths[i];
                }

                if (numOfNans > 0)
                {
                    // Determine the max width for each column
                    GetColumnsMinMaxWidthByContent(true, out _, out var maxFullWidths);

                    // set the columns that can fulfill by the max width in a loop because it changes the nanWidth
                    int oldNumOfNans;
                    do
                    {
                        oldNumOfNans = numOfNans;

                        for (var i = 0; i < _columnWidths.Length; i++)
                        {
                            var nanWidth = (availCellSpace - occupiedSpace) / numOfNans;
                            if (!double.IsNaN(_columnWidths[i]) || !(nanWidth > maxFullWidths[i])) continue;

                            _columnWidths[i] = maxFullWidths[i];
                            numOfNans--;
                            occupiedSpace += maxFullWidths[i];
                        }
                    } while (oldNumOfNans != numOfNans);

                    if (numOfNans > 0)
                    {
                        // Determine width that will be assigned to un assigned widths
                        var nanWidth = (availCellSpace - occupiedSpace) / numOfNans;

                        for (var i = 0; i < _columnWidths.Length; i++)
                        {
                            if (double.IsNaN(_columnWidths[i]))
                                _columnWidths[i] = nanWidth;
                        }
                    }
                }

                if (numOfNans != 0 || !(occupiedSpace < availCellSpace)) return;
                {
                    if (orgNumOfNans > 0)
                    {
                        // Spread extra width between all non width specified columns, but never
                        // past a column's own explicit CSS max-width (unset columns are uncapped,
                        // matching the normal "auto columns fill remaining space" behavior).
                        var explicitMaxWidths = GetColumnExplicitMaxWidths();
                        var extWidth = (availCellSpace - occupiedSpace) / orgNumOfNans;
                        for (var i = 0; i < _columnWidths.Length; i++)
                            if (orgColWidths == null || double.IsNaN(orgColWidths[i]))
                                _columnWidths[i] = Math.Min(_columnWidths[i] + extWidth, explicitMaxWidths[i]);
                    }
                    else
                    {
                        // spread extra width between all columns with respect to relative sizes
                        for (var i = 0; i < _columnWidths.Length; i++)
                            _columnWidths[i] += (availCellSpace - occupiedSpace) * (_columnWidths[i] / occupiedSpace);
                    }
                }
            }
            else
            {
                //Get the minimum and maximum full length of NaN boxes
                GetColumnsMinMaxWidthByContent(true, out var minFullWidths, out var maxFullWidths);

                for (var i = 0; i < _columnWidths!.Length; i++)
                {
                    if (double.IsNaN(_columnWidths[i]))
                        _columnWidths[i] = minFullWidths[i];
                    occupiedSpace += _columnWidths[i];
                }

                // spread extra width between all columns - only when there is a genuine surplus
                // (availCellSpace > occupiedSpace) to distribute. Without this guard, an indefinite/zero
                // availCellSpace - most commonly a vertical table with no definite height anywhere up its
                // containing-block chain, since availCellSpace maps to the column axis's available space
                // there (GetAvailableCellWidth) - made (availCellSpace - occupiedSpace) negative, and the
                // loop below silently SHRANK a column already sized to its own content's explicit width
                // toward zero. This loop's only job is to grow a column toward maxFullWidths when there is
                // real spare room; it must never shrink one below the content-based width already computed
                // above it.
                if (availCellSpace > occupiedSpace)
                {
                    for (var i = 0; i < _columnWidths.Length; i++)
                    {
                        if (!(maxFullWidths[i] > _columnWidths[i])) continue;

                        var temp = _columnWidths[i];
                        _columnWidths[i] = Math.Min(_columnWidths[i] + (availCellSpace - occupiedSpace) / Convert.ToSingle(_columnWidths.Length - i), maxFullWidths[i]);
                        occupiedSpace = occupiedSpace + _columnWidths[i] - temp;
                    }
                }
            }
        }

        /// <summary>
        /// While table width is larger than it should, and width is reducible.<br/>
        /// If table max width is limited by we need to lower the columns width even if it will result in clipping<br/>
        /// </summary>
        private void EnforceMaximumSize()
        {
            ShrinkColumnsToFitAvailableWidth();

            // if table max width is limited by we need to lower the columns width even if it will result in clipping
            var maxWidth = GetMaxTableWidth();
            if (!(maxWidth < 90999)) return;

            var widthSum = GetWidthSum();
            if (!(maxWidth < widthSum)) return;

            //Get the minimum and maximum full length of NaN boxes
            GetColumnsMinMaxWidthByContent(false, out var minFullWidths, out var maxFullWidths);

            // lower all the columns to the minimum
            for (var i = 0; i < _columnWidths!.Length; i++)
                _columnWidths[i] = minFullWidths[i];

            // either min for all column is not enought and we need to lower it more resulting in clipping
            // or we now have extra space so we can give it to columns than need it
            widthSum = GetWidthSum();
            if (maxWidth < widthSum)
                ClipColumnsToMaxWidth(maxWidth);
            else
                SpreadExtraWidthToColumns(maxWidth, maxFullWidths);
        }

        /// <summary>
        /// While table width is larger than it should, and width is reducible.
        /// </summary>
        /// <remarks>
        /// Provably unreachable under the current (pre-existing, out-of-scope) <see cref="CanReduceWidth()"/>
        /// - see .claude/accepted-gaps/table-max-width-clip-branch-coverage.md.
        /// </remarks>
        [ExcludeFromCodeCoverage]
        private void ShrinkColumnsToFitAvailableWidth()
        {
            int curCol = 0;
            var widthSum = GetWidthSum();
            while (widthSum > GetAvailableTableWidth() && CanReduceWidth())
            {
                while (!CanReduceWidth(curCol))
                    curCol++;

                _columnWidths![curCol] -= 1f;

                curCol++;

                if (curCol >= _columnWidths.Length)
                    curCol = 0;
            }
        }

        /// <summary>
        /// Lowers the width of columns, starting from the largest one, until the max width is satisfied.
        /// Columns are already at their content minimum, so this results in clipping.
        /// </summary>
        /// <remarks>
        /// Its caller's guard (<c>maxWidth &lt; widthSum</c>, after columns are already at their content
        /// minimum) is live code, not provably dead like <see cref="ShrinkColumnsToFitAvailableWidth"/> -
        /// but no HTML/CSS input tried actually reaches it; see
        /// .claude/accepted-gaps/table-max-width-clip-branch-coverage.md for the investigation.
        /// </remarks>
        [ExcludeFromCodeCoverage]
        private void ClipColumnsToMaxWidth(double maxWidth)
        {
            var widthSum = GetWidthSum();
            for (var a = 0; a < 15 && maxWidth < widthSum - 0.1; a++) // limit iteration so bug won't create infinite loop
            {
                var nonMaxedColumns = 0;
                double largeWidth = 0f, secLargeWidth = 0f;
                foreach (var columnWidth in _columnWidths!)
                {
                    if (columnWidth > largeWidth + 0.1)
                    {
                        secLargeWidth = largeWidth;
                        largeWidth = columnWidth;
                        nonMaxedColumns = 1;
                    }
                    else if (columnWidth > largeWidth - 0.1)
                    {
                        nonMaxedColumns++;
                    }
                }

                var decrease = secLargeWidth > 0 ? largeWidth - secLargeWidth : (widthSum - maxWidth) / _columnWidths.Length;
                if (decrease * nonMaxedColumns > widthSum - maxWidth)
                    decrease = (widthSum - maxWidth) / nonMaxedColumns;
                for (var i = 0; i < _columnWidths.Length; i++)
                    if (_columnWidths[i] > largeWidth - 0.1)
                        _columnWidths[i] -= decrease;

                widthSum = GetWidthSum();
            }
        }

        /// <summary>
        /// Spreads extra width to columns that haven't reached their content maximum yet, trying to
        /// spread it between all columns.
        /// </summary>
        private void SpreadExtraWidthToColumns(double maxWidth, double[] maxFullWidths)
        {
            var widthSum = GetWidthSum();
            for (var a = 0; a < 15 && maxWidth > widthSum + 0.1; a++) // limit iteration so bug won't create infinite loop
            {
                var nonMaxedColumns = 0;
                for (var i = 0; i < _columnWidths!.Length; i++)
                    if (_columnWidths[i] + 1 < maxFullWidths[i])
                        nonMaxedColumns++;
                if (nonMaxedColumns == 0)
                    nonMaxedColumns = _columnWidths.Length;

                var hit = false;
                var minIncrement = (maxWidth - widthSum) / nonMaxedColumns;
                for (var i = 0; i < _columnWidths.Length; i++)
                {
                    if (!(_columnWidths[i] + 0.1 < maxFullWidths[i])) continue;

                    minIncrement = Math.Min(minIncrement, maxFullWidths[i] - _columnWidths[i]);
                    hit = true;
                }

                for (var i = 0; i < _columnWidths.Length; i++)
                    if (!hit || _columnWidths[i] + 1 < maxFullWidths[i])
                        _columnWidths[i] += minIncrement;

                widthSum = GetWidthSum();
            }
        }

        /// <summary>
        /// Check for minimum sizes (increment widths if necessary)
        /// </summary>
        private void EnforceMinimumSize()
        {
            //Get the minimum length
            GetColumnsMinMaxWidthByContent(false, out var minFullWidths, out _);

            for (var i = 0; i < _columnWidths!.Length; i++)
            {
                _columnWidths[i] = Math.Max(_columnWidths[i], minFullWidths[i]);
            }

            foreach (var row in _allRows)
            {
                foreach (var cell in row.Boxes)
                {
                    var colspan = GetColSpan(cell);
                    var col = GetCellRealColumnIndex(cell);
                    var affectColumn = col + colspan - 1;

                    if (_columnWidths!.Length <= col || !(_columnWidths[col] < GetColumnMinWidths()[col])) continue;
                    var diff = GetColumnMinWidths()[col] - _columnWidths[col];
                    _columnWidths[affectColumn] = GetColumnMinWidths()[affectColumn];

                    if (col < _columnWidths.Length - 1)
                    {
                        _columnWidths[col + 1] -= diff;
                    }

                }
            }
        }

        /// <summary>
        /// Zeroes the width of every column whose originating <c>&lt;col&gt;</c>/<c>&lt;colgroup&gt;</c>
        /// is <c>visibility: collapse</c> (CSS 2.1 <see href="https://www.w3.org/TR/CSS21/tables.html#dynamic-effects">§17.6.1</see>),
        /// so it takes no width and the table shrinks by that column rather than reserving space for
        /// it. Only a column with an explicit <c>&lt;col&gt;</c>/<c>&lt;colgroup&gt;</c> can be
        /// collapsed this way - a table with no column elements has no box to read the value from,
        /// so <see cref="_columns"/> is empty and this is a no-op.
        /// </summary>
        /// <remarks>
        /// Deliberately last in the width-determination pipeline: every earlier step
        /// (<see cref="DetermineMissingColumnWidths"/>, <see cref="EnforceMaximumSize"/>,
        /// <see cref="EnforceMinimumSize"/>) can size a column up from its own content or spread
        /// spare width into it, and none of them know about collapse - so the only way to guarantee
        /// a collapsed column stays at zero is to zero it after all of them have run. A cell that
        /// spans out of a collapsed column into visible ones still gets the visible columns' full
        /// width via <see cref="GetCellWidth"/>, which simply sums <see cref="_columnWidths"/> across
        /// the span - the collapsed entry contributes zero automatically, with no separate case
        /// needed for the cell itself.
        /// </remarks>
        private void CollapseColumnWidths()
        {
            if (_columnWidths is null) return;

            for (var i = 0; i < _columnWidths.Length; i++)
            {
                if (IsColumnCollapsed(i))
                    _columnWidths[i] = 0;
            }
        }

        /// <summary>
        /// Whether column <paramref name="columnIndex"/>'s originating <c>&lt;col&gt;</c>/
        /// <c>&lt;colgroup&gt;</c> is <c>visibility: collapse</c>. False for an index past
        /// <see cref="_columns"/> - a table with no column elements (the common case) has no box to
        /// read the value from, so no column of it can ever be collapsed this way.
        /// </summary>
        private bool IsColumnCollapsed(int columnIndex) =>
            columnIndex < _columns.Count && _columns[columnIndex].Visibility.Value == Visibility.Collapse;

        /// <summary>
        /// How many columns <see cref="IsColumnCollapsed"/> answers true for, cached because
        /// <see cref="GetWidthSum"/> - which needs this count to leave out the collapsed columns'
        /// own border-spacing slot - runs many times over a single layout (every iteration of
        /// <see cref="ShrinkColumnsToFitAvailableWidth"/>, <see cref="ClipColumnsToMaxWidth"/>, and
        /// <see cref="SpreadExtraWidthToColumns"/>), and <see cref="_columns"/> never changes after
        /// <see cref="AssignBoxKinds"/> has run.
        /// </summary>
        private int CollapsedColumnCount()
        {
            if (_collapsedColumnCount is { } cached) return cached;

            var count = 0;
            for (var i = 0; i < _columnWidths!.Length; i++)
            {
                if (IsColumnCollapsed(i)) count++;
            }

            return (_collapsedColumnCount = count).Value;
        }

        /// <summary>
        /// Whether every column a cell spans, starting at <paramref name="columnIndex"/> for
        /// <paramref name="colspan"/> columns, is collapsed - used to skip a cell's own (invisible)
        /// content from sizing a column it is entirely confined to (<see cref="GetColumnsMinMaxWidthByContent"/>,
        /// <see cref="GetColumnMinWidths"/>). False past the last known column, since
        /// <see cref="IsColumnCollapsed"/> is false there too.
        /// </summary>
        private bool CellOccupiesOnlyCollapsedColumns(int columnIndex, int colspan)
        {
            for (var i = columnIndex; i < columnIndex + colspan; i++)
            {
                if (!IsColumnCollapsed(i)) return false;
            }

            return true;
        }

        /// <summary>
        /// Sums the border-spacing owed strictly between a cell's own spanned columns (not before its
        /// first column or after its last - those are the previous/next cell's own concern). Column
        /// <paramref name="columnIndex"/> + i is skipped when it's collapsed, matching <see cref="GetWidthSum"/>'s
        /// one-slot-per-collapsed-column removal and <see cref="LayoutBodyRow"/>'s trailing-slot check:
        /// the slot immediately after a collapsed column never exists, whether that boundary falls
        /// inside a colspan cell's own span (issue #667: a cell straddling a collapsed and a visible
        /// column) or between two separate cells.
        /// </summary>
        private double GetInteriorSpacing(int columnIndex, int colspan)
        {
            double spacing = 0;

            for (var i = columnIndex; i < columnIndex + colspan - 1; i++)
            {
                if (!IsColumnCollapsed(i)) spacing += HorizontalSpacingAt(i + 1);
            }

            return spacing;
        }

        /// <summary>
        /// Remove header and footer from document tree for proxy-based repetition
        /// </summary>
        private void RemoveHeaderFooterFromTree()
        {
            if (_headerBox != null)
            {
                _headerIndex = _tableBox.Boxes.IndexOf(_headerBox);
                _tableBox.Boxes.Remove(_headerBox);
                _headerBox.ParentBox = null;
                _headerBox.DomParentBox = _tableBox;
            }

            if (_footerBox != null)
            {
                _footerIndex = _tableBox.Boxes.IndexOf(_footerBox);
                _tableBox.Boxes.Remove(_footerBox);
                _footerBox.ParentBox = null;
                _footerBox.DomParentBox = _tableBox;
            }
        }

        /// <summary>
        /// Undoes what a previous run of this engine did to the table's own child list, so this run sees
        /// the markup's structure rather than the last run's output.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="RemoveHeaderFooterFromTree"/> detaches a repeating <c>&lt;thead&gt;</c>/<c>&lt;tfoot&gt;</c>
        /// and puts one <see cref="CssProxyBox"/> per page in its place, and nothing removed the proxies.
        /// A second run therefore found the markup's own group gone — and, because a proxy inherits the
        /// source's style, its <c>Display</c> <i>is</i> <c>table-header-group</c>, so
        /// <see cref="AssignBoxKinds"/> took the first stale proxy as the header and classified the rest as
        /// body rows. A proxy has no cells, so the row-positioning step then threw on an empty sequence.
        /// </para>
        /// <para>
        /// The proxies are the only surviving reference to the detached group — the engine is constructed
        /// afresh per layout and detaching nulls the group's <see cref="CssBox.ParentBox"/> — which is why
        /// the restore goes through them and why each carries the index it was taken from.
        /// </para>
        /// </remarks>
        private void RestoreStructureFromAnyPreviousRun()
        {
            List<CssProxyBox>? proxies = null;

            foreach (var box in _tableBox.Boxes)
            {
                if (box is CssProxyBox proxy) (proxies ??= []).Add(proxy);
            }

            if (proxies is null) return;

            foreach (var proxy in proxies)
            {
                _tableBox.Boxes.Remove(proxy);
            }

            // Lowest index first, so each group lands where it was rather than being displaced by one
            // restored before it. Several proxies share one source (one per page), hence the parent check.
            foreach (var proxy in proxies.OrderBy(p => p.SourceIndex))
            {
                var source = proxy.SourceBox;

                if (source.ParentBox is not null) continue;

                // The ParentBox setter is what re-parents, and it *appends* to the new parent's
                // children - so the index is applied afterwards by moving the box. Inserting first and
                // then setting the parent adds the group twice, which is exactly one header's worth of
                // extra height on the second run and was the whole of this issue's "extra height".
                source.ParentBox = _tableBox;

                // DomParentBox exists only to stand in for a null ParentBox (see its own doc comment) -
                // now that ParentBox is real again, clear it rather than leave it pointing at _tableBox
                // forever. Harmless today (every reader already prefers ParentBox), but a stale non-null
                // DomParentBox on a box that's genuinely back in the live tree is a footgun for whatever
                // reads it next.
                source.DomParentBox = null;

                if (proxy.SourceIndex >= 0 && proxy.SourceIndex < _tableBox.Boxes.Count - 1)
                {
                    _tableBox.Boxes.Remove(source);
                    _tableBox.Boxes.Insert(proxy.SourceIndex, source);
                }
            }
        }

        /// <summary>
        /// Create a proxy box for the header at the specified row-axis position (physical Y for a
        /// horizontal-tb table, physical X - <paramref name="yPosition"/>'s value at every call site
        /// already is one - for a vertical one).
        /// </summary>
        private CssProxyBox? CreateHeaderProxy(double yPosition)
        {
            if (_headerBox == null)
                return null;

            var proxy = new CssProxyBox(_tableBox, _headerBox, _headerIndex);
            var columnAxisStart = Math.Max((_isVertical ? _tableBox.ClientTop : _tableBox.ClientLeft) + StartXSpacing(), 0);
            proxy.Location = _isVertical ? new RPoint(yPosition, columnAxisStart) : new RPoint(columnAxisStart, yPosition);
            return proxy;
        }

        /// <summary>
        /// Create a proxy box for the footer at the specified row-axis position - see
        /// <see cref="CreateHeaderProxy"/>'s own remark.
        /// </summary>
        private CssProxyBox? CreateFooterProxy(double yPosition)
        {
            if (_footerBox == null)
                return null;

            var proxy = new CssProxyBox(_tableBox, _footerBox, _footerIndex);
            var columnAxisStart = Math.Max((_isVertical ? _tableBox.ClientTop : _tableBox.ClientLeft) + StartXSpacing(), 0);
            proxy.Location = _isVertical ? new RPoint(yPosition, columnAxisStart) : new RPoint(columnAxisStart, yPosition);
            return proxy;
        }

        /// <summary>
        /// Lays <paramref name="captions"/> out stacked in source order along the table's own row axis
        /// (physical Y for horizontal-tb, physical X for a vertical table), starting at
        /// <paramref name="rowAxisCursor"/> and sized to the table's full column-axis extent
        /// (<paramref name="columnAxisStart"/>/<paramref name="columnAxisExtent"/>) - CSS 2.1 §17.4: a
        /// caption box takes the full width of the table, reinterpreted through css-tables-3's axis
        /// mapping for a vertical writing mode. Each is placed at its own assigned position
        /// (<see cref="CssBox.LayoutContentAtItsAssignedPosition"/>) rather than through the generic
        /// block-flow frame, since this engine - not a block-flow cursor a table box does not otherwise
        /// keep - owns where it goes.
        /// </summary>
        /// <remarks>
        /// Like a row, a caption grows forward from the row-axis-min edge (the <c>vertical-lr</c> shape)
        /// regardless of orientation - its own final row-axis-max edge isn't known until the whole grid is
        /// laid out, so a <c>vertical-rl</c> table mirrors it once, together with the rows, via
        /// <see cref="ReflectRowAxisForVerticalRl"/> rather than here.
        /// </remarks>
        /// <returns><paramref name="rowAxisCursor"/> unchanged when <paramref name="captions"/> is empty,
        /// otherwise the row-axis position after the last caption's own trailing margin.</returns>
        private async ValueTask<double> LayoutCaptionGroup(
            RGraphics g, IReadOnlyList<CssBox> captions, double columnAxisStart, double rowAxisCursor, double columnAxisExtent)
        {
            var currentPos = rowAxisCursor;

            foreach (var caption in captions)
            {
                currentPos += _isVertical ? caption.ActualMarginLeft : caption.ActualMarginTop;

                caption.Location = _isVertical
                    ? new RPoint(currentPos, columnAxisStart)
                    : new RPoint(columnAxisStart, currentPos);

                if (_isVertical)
                {
                    caption.ActualBottom = columnAxisStart + columnAxisExtent;

                    // A caption bypasses the ordinary block-flow frame (this method's own remarks), so
                    // nothing else ever resolves its box model before LayoutContentAtItsAssignedPosition
                    // runs - unlike an ordinary block child, ResolveOwnInlineSize never sees it. For
                    // horizontal-tb that's fine: ActualRight above is exactly what CreateLineBoxes/FlowBox
                    // consult for wrap width. But a caption inherits the table's own writing-mode too, so
                    // for a vertical table its content dispatches to CreateVerticalLineBoxes, which reads
                    // the CSS Height *string* (not ActualBottom) for its own wrap limit - CSS 2.1 §17.4's
                    // "full width of the table" is the caption's own inline axis under this reinterpretation
                    // (physical Y here), so it has to be stated as a real Height value or the caption
                    // silently falls back to an auto one-page-tall wrap limit and then shrinks to content
                    // instead of spanning the column axis. Formatted as "pt" (the identity unit for this
                    // engine's own internal layout units) rather than "px", which resolves at 0.75pt and
                    // would silently shrink this to 75% of the intended extent.
                    caption.Height = columnAxisExtent.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + "pt";

                    await caption.LayoutContentAtItsAssignedPosition(g);

                    // CreateVerticalLineBoxes' own auto-width shrink (issue #761) is anchored to whichever
                    // physical edge is THIS caption's own real block-start - ActualRight for vertical-rl
                    // (Location.X, set above, is left free to move instead), Location.X for vertical-lr
                    // (matching the Location assignment above, a no-op delta below). That can disagree with
                    // the Location.X anchor this method itself wants, since - like a row - a caption grows
                    // forward as if vertical-lr regardless of the table's real orientation, deferring the
                    // real vertical-rl direction to the whole-table reflection pass rather than to this
                    // per-caption shrink. Re-anchoring to currentPos here, via a real subtree translation
                    // (OffsetLeft - the shrink already positioned nested word content, a bare field rewrite
                    // would leave it behind exactly the way a bare row/cell rewrite once did), is what keeps
                    // every caption's own near edge at currentPos regardless of which edge the shrink itself
                    // preserved.
                    var forwardGrowthDelta = currentPos - caption.Location.X;
                    if (forwardGrowthDelta != 0) caption.OffsetLeft(forwardGrowthDelta);
                }
                else
                {
                    caption.ActualRight = columnAxisStart + columnAxisExtent;
                    await caption.LayoutContentAtItsAssignedPosition(g);
                }

                currentPos = _isVertical
                    ? caption.ActualRight + caption.ActualMarginRight
                    : caption.ActualBottom + caption.ActualMarginBottom;
            }

            return currentPos;
        }

        /// <summary>
        /// CSS 2.1 §17.4 gives a table with a caption an anonymous "table wrapper box" that owns
        /// margin/position while border/background/padding stay with the row grid alone - PeachPDF has
        /// no such wrapper (see .claude/accepted-gaps entry / issue #721: the practical effect is a
        /// bordered/filled &lt;table&gt; visually enclosing its own caption). Introducing a real wrapper
        /// ancestor is the larger structural change the issue itself scopes out; this instead gives a
        /// captioned table's grid a dedicated leaf decoration box - <see cref="CssBox.TableGridDecorationBox"/>,
        /// the first of <see cref="_tableBox"/>'s own children - that carries a copy of _tableBox's own
        /// border/background (<see cref="CssBox.AdoptBorderAndBackgroundFrom"/>) and is sized, once
        /// layout knows where the grid itself starts and ends, to the grid's own border-box rect by
        /// <see cref="FinalizeGridDecorationBoxGeometry"/>. _tableBox's own border/background paint is
        /// suppressed (<see cref="CssBox.SuppressOwnBorderPaint"/>/<see cref="CssBox.SuppressOwnBackgroundPaint"/>)
        /// so the two don't double-paint; _tableBox's own <see cref="CssBox.Location"/>/
        /// <c>ActualBottom</c> are untouched and keep spanning the combined
        /// grid+caption assembly, exactly as every other consumer of this box (block-flow siblings,
        /// §4.3 page-break relocation, etc.) already expects.
        /// <para>
        /// Called right after <see cref="AssignBoxKinds"/>, before anything in this pass captures an
        /// index into <see cref="_tableBox"/>'s own <c>Boxes</c> list - <see cref="RemoveHeaderFooterFromTree"/>'s
        /// <c>_headerIndex</c>/<c>_footerIndex</c>, baked into a <see cref="CssProxyBox"/>'s own
        /// <c>SourceIndex</c> and consumed by a later pass's <see cref="RestoreStructureFromAnyPreviousRun"/>.
        /// Positioning the decoration box here, ahead of that capture, on every pass (idempotently - a
        /// captioned table keeps exactly one, reused and just repositioned to the front on every later
        /// pass) means the index a header/footer restore later replays against is always relative to a
        /// list that already accounts for this box, on every past and future pass alike. Doing this
        /// later - once real geometry is known, at Step 7 - would insert it after an index an earlier
        /// pass already captured and baked into a proxy, silently shifting every subsequent restore by
        /// one slot.
        /// </para>
        /// A captionless table (the overwhelming majority - most real-world tables put borders on cells
        /// rather than on &lt;table&gt; itself) never creates this box: zero cost, zero behavior change.
        /// </summary>
        private void EnsureGridDecorationBoxStructure()
        {
            if (_captionBoxes.Count == 0) return;

            var decorationBox = _tableBox.TableGridDecorationBox;
            if (decorationBox is null)
            {
                decorationBox = new CssBox(_tableBox, tag: null)
                {
                    Display = CssProperty<DisplayMode>.FromValue(Keywords.Block, DisplayMode.Block),
                    IsTableGridDecorationBox = true
                };
                decorationBox.AdoptBorderAndBackgroundFrom(_tableBox);

                _tableBox.SuppressOwnBorderPaint = true;
                _tableBox.SuppressOwnBackgroundPaint = true;
                _tableBox.TableGridDecorationBox = decorationBox;
            }

            // Kept first in _tableBox.Boxes on every pass rather than trusted to stay there - header/
            // footer detachment and proxy restoration both mutate this same list, and painting the grid's
            // background after real row/cell content would draw over content it should sit behind. Cheap
            // to check first: nothing after this method's own first run ever displaces it (every
            // RemoveHeaderFooterFromTree/RestoreStructureFromAnyPreviousRun index is captured or consumed
            // relative to a list that already has this box at 0), so the common case - every pass after
            // the first, on every table with a caption - is an O(1) reference check rather than an O(n)
            // remove-and-shift on a large table's own child list.
            if (_tableBox.Boxes.Count == 0 || !ReferenceEquals(_tableBox.Boxes[0], decorationBox))
            {
                _tableBox.Boxes.Remove(decorationBox);
                _tableBox.Boxes.Insert(0, decorationBox);
            }
        }

        /// <summary>
        /// Sizes the grid decoration box <see cref="EnsureGridDecorationBoxStructure"/> created to the
        /// row grid's own border-box - see that method's remarks, and the Step 7 call site for
        /// <paramref name="gridBorderBoxBottom"/>. Runs every pass, mirroring how _tableBox's own
        /// ActualRight/ActualBottom are (re)published every pass rather than only once the table
        /// completes. A captionless table never has a decoration box to size; a no-op then.
        /// </summary>
        /// <param name="gridBorderBoxBottom">
        /// the grid's own border-box row-axis-end edge (physical Y for horizontal-tb, physical X for a
        /// vertical table) - already computed by the caller as part of settling _tableBox's own row-axis
        /// dimension.
        /// </param>
        private void FinalizeGridDecorationBoxGeometry(double gridBorderBoxBottom)
        {
            if (_tableBox.TableGridDecorationBox is not { } decorationBox) return;

            // The top caption group's own (persistent, set once by the pass that laid it out and never
            // touched again - and, for vertical-rl, already mirrored by the caller's own reflection pass
            // before this runs) geometry, rather than _topCaptionsHeight - a per-engine-instance field
            // that resets to 0 on a continuation's fresh instance even though the caption above it was
            // laid out by an earlier one. Reading the caption box itself is correct on every pass alike.
            if (_isVertical)
            {
                var gridBorderBoxTop = _topCaptions.Count > 0
                    ? _topCaptions[^1].ActualRight + _topCaptions[^1].ActualMarginRight
                    : _tableBox.Location.X;

                decorationBox.Location = new RPoint(gridBorderBoxTop, _tableBox.Location.Y);
                decorationBox.ActualRight = gridBorderBoxBottom;
                decorationBox.ActualBottom = _tableBox.ActualBottom;
            }
            else
            {
                var gridBorderBoxTop = _topCaptions.Count > 0
                    ? _topCaptions[^1].ActualBottom + _topCaptions[^1].ActualMarginBottom
                    : _tableBox.Location.Y;

                decorationBox.Location = new RPoint(_tableBox.Location.X, gridBorderBoxTop);
                decorationBox.ActualRight = _tableBox.ActualRight;
                decorationBox.ActualBottom = gridBorderBoxBottom;
            }

            decorationBox.PageBreakBottoms = _tableBox.PageBreakBottoms;
        }

        /// <summary>
        /// Layout the cells by the calculated table layout
        /// </summary>
        /// <param name="g"></param>
        private async ValueTask LayoutCells(RGraphics g)
        {
            // Column-axis start: always physical-min-forward (no rtl support - see the axis-mapping
            // fields' own remarks), so ClientTop for a vertical table (columns run along physical Y),
            // ClientLeft otherwise.
            var startX = Math.Max((_isVertical ? _tableBox.ClientTop : _tableBox.ClientLeft) + StartXSpacing(), 0);

            // Lay the top caption(s) out above the row grid before anything else claims this
            // coordinate - once, on the pass that starts the row loop from the top. A continuation
            // resumes rows on a later fragmentainer and must not redo this: the caption already sits
            // where the first pass put it, and _topCaptionsHeight staying 0 on that pass is harmless
            // (see its own remarks).
            if (!_continuesAPreviousPass && _topCaptions.Count > 0)
            {
                // CSS 2.1 §17.4: a top caption sits flush above the row grid's own border-box, with
                // nothing (no border, no background) above it - anchored at the table's own row-axis-min
                // edge (Location.Y for horizontal-tb, Location.X for vertical - the combined assembly's
                // true row-axis-start) rather than the client edge (inside the grid's own border), which
                // would otherwise leave a border-width gap of nothing above the caption where the border
                // used to visually sit before this box's own border paint was suppressed (issue #721; see
                // EnsureGridDecorationBoxStructure/FinalizeGridDecorationBoxGeometry). Note startY below
                // stays anchored at the client edge unchanged - the two anchors differ by exactly the
                // table's own row-axis-start border width, which startY's own formula already adds back,
                // so the row grid's own position is unaffected by this. Grows forward from the row-axis-min
                // edge regardless of orientation, like a row - LayoutCaptionGroup's own remarks explain why.
                var topCaptionsBottom = await LayoutCaptionGroup(
                    g,
                    _topCaptions,
                    _isVertical ? _tableBox.Location.Y : _tableBox.Location.X,
                    _isVertical ? _tableBox.Location.X : _tableBox.Location.Y,
                    GetWidthSum());
                _topCaptionsHeight = topCaptionsBottom - (_isVertical ? _tableBox.Location.X : _tableBox.Location.Y);
            }

            // Row-axis start: forward-growing (vertical-lr's own shape) even for vertical-rl, whose rows
            // actually grow from the opposite (physical-max) edge - ReflectRowAxisForVerticalRl applies
            // that correction in one pass once every row's final position is known, rather than solving
            // it here where a row's own row-axis thickness isn't yet knowable (see the axis-mapping
            // fields' own remarks). ClientLeft for a vertical table (rows run along physical X), ClientTop
            // otherwise. _topCaptionsHeight is now a real row-axis-thickness scalar for a vertical table
            // too (LayoutCaptionGroup above is writing-mode-aware), so including it unconditionally here
            // is correct for both orientations, not merely harmless.
            var startY = Math.Max((_isVertical ? _tableBox.ClientLeft : _tableBox.ClientTop) + _topCaptionsHeight + StartYSpacing(), 0);

            var container = _tableBox.HtmlContainer;
            // A vertical table's own rows advance along physical X, not physical Y, so they have no
            // relationship to the page's own (always physical-Y) fragmentation bands - "does this row
            // cross a page boundary" is not a question a vertical table's row loop can answer. Reporting
            // an unbounded page height here routes the whole row loop through its own pre-existing
            // unpaginated fallback path (every page-break decision point below is gated on
            // `pageHeight < double.MaxValue - 1`), the same way a measurement pass with no live
            // fragmentainer already does - not a new bypass, reuse of an already-tested path. The table's
            // own box is separately made monolithic (MonolithicContent) so the *whole* table still moves
            // to a later page as a unit if it doesn't fit; real per-cell pagination of a vertical table's
            // content is tracked as a follow-up (#762).
            var pageHeight = _isVertical ? double.MaxValue : container?.PageSize.Height ?? double.MaxValue;

            // Which fragmentainer the table begins in is a question about the table's own top edge, not
            // about startY: startY is a row cursor, and VerticalSpacingAt(0) is negative (half the
            // table's own resolved top border) for a collapsed-border table, so it sits above the box for
            // exactly the tables whose top lands flush on a page boundary. Reading it there names the
            // page the table just left, and the pre-check below then nudges the table onto the next one -
            // which is what a table relocated by CssBox's own §4.3 mover, and so placed exactly at a page
            // top, does every time.
            //
            // A continuation resumes the cursor the pass before it left; every other run starts one. The
            // two differ in what only the earlier pass knows - which row to re-enter and which of its
            // cells to continue, the rowspan map keyed by absolute row index, and the widest edge the
            // table has reached.
            //
            // Where its rows go is the other half, and it is not startY. A box that spans fragmentainers
            // keeps the one Location it was placed at (CssBox.ResumeInTheNextFragmentainer moves only a
            // box inside a fragmentainer with a band of its own - a column - and does nothing on the page
            // grid), so startY still names the top of the page the table *began* on. The rows this pass
            // places belong to the fragmentainer the break fell in, which is what the record names.
            var cursor = ContinuedRowLoop is { } carried
                ? TableRowCursor.Continuing(carried, ResumedRowTop(container, carried, startY, pageHeight), _isVertical)
                : new TableRowCursor(
                    startY, startX,
                    pageHeight < double.MaxValue - 1 ? container!.SlotStartingAt(_tableBox.ClientTop) : 0,
                    _isVertical);

            // Reset page-break tracking so re-layout doesn't accumulate stale entries. A resumed pass is
            // not a re-layout: where the table's slice ended on the pages earlier passes filled is what
            // clips its borders there, so those entries have to accumulate rather than be thrown away.
            if (!_continuesAPreviousPass) _tableBox.PageBreakBottoms = null;

            // Steps 1-3, once per table: take the repeating groups out of the tree and lay each out once
            // to learn its height. A continuation inherits both from what settled them (AssignBoxKinds
            // reads them back off the setup) - the source subtree is shared by every proxy of it, so
            // laying it out again re-positions a subtree whose earlier snapshots are already frozen in
            // the fragment emitter.
            if (!_continuesAPreviousPass)
            {
                await DetachAndMeasureRepeatedRowGroups(g, cursor, startX);

                // A header-opened rowspan cell crossing into the body (issue #788) is registered here,
                // before the body row loop below ever calls TableRowCursor.BeginRow - the same shape
                // TableRowCursor.Continuing already uses to pre-seed RowSpannedBoxes for a cell an
                // earlier *pass* left open, reused for a cell an earlier *row-group* left open instead.
                // Once seeded, CloseSpanningCell/straddle-correction/Continuation/Continuing all treat it
                // as an indistinguishable, ordinary already-open rowspan cell - no changes needed there.
                SeedCrossBoundaryRowSpans(cursor);
            }

            // Step 4: Layout body rows with page break detection.
            await LayoutBodyRows(g, cursor, startX, startY, container, pageHeight);
        }

        /// <summary>
        /// Where a continuation's first row goes: the content top of the fragmentainer
        /// <paramref name="carried"/> names, never above the table's own content top.
        /// </summary>
        /// <param name="container">the container whose page grid resolves the slot, or null when there is none</param>
        /// <param name="carried">the record this run continues</param>
        /// <param name="startY">
        /// the table's own content top, which is where a run with no page grid to resolve the slot
        /// against puts everything — a document with no pagination has one fragmentainer, so a record
        /// naming any other names nothing.
        /// </param>
        /// <param name="pageHeight">the page band's height, or <see cref="double.MaxValue"/> when unpaged</param>
        private static double ResumedRowTop(
            HtmlContainerInt? container, TableBreakToken carried, double startY, double pageHeight) =>
            container is null || pageHeight >= double.MaxValue - 1
                ? startY
                : Math.Max(startY, container.PageTopOf(carried.ResumeSlotIndex));

        /// <summary>
        /// Lays a row out with its fragmentainer told what the groups this table repeats have already
        /// claimed of it — <paramref name="headerRoom"/> below the band's content edge, and
        /// <paramref name="footerRoom"/> above its foot — so a flow inside the row begins below the one and
        /// stops above the other.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see href="https://www.w3.org/TR/css-tables-3/#repeated-headers">css-tables-3 §6.2</see> is
        /// explicit that repeating a header means <i>leaving room</i> for it — "the same applies for footer
        /// rows and the table bottom border" — and the row cursor cannot state that on its own. It reserves
        /// the header's height by advancing <c>CurrentY</c>, and the footer's by subtracting it from the
        /// room a row is measured against, which between them position the rows this pass places and the
        /// cells it enters fresh. Neither reaches a flow <i>inside</i> a cell: a cell continuing an earlier
        /// fragmentainer deliberately keeps the one <c>Location</c> its first fragment was built from and
        /// starts its content at <see cref="FragmentainerContext.ResumeContentTop"/>, and any cell's own
        /// lines then run down toward the band's foot regardless of which pass entered it.
        /// </para>
        /// <para>
        /// Both halves were measured as content drawn <i>under</i> the group that repeats over it. The
        /// header's was <see href="https://github.com/jhaygood86/PeachPDF/issues/439">#439</see>: six words
        /// hidden under the header on each of two pages of <c>paged_media_table_row_continuation</c>. The
        /// footer's is its mirror (<see href="https://github.com/jhaygood86/PeachPDF/issues/493">#493</see>),
        /// and in both cases every word is still claimed by exactly one fragmentainer, so no count-based
        /// check can see either.
        /// </para>
        /// <para>
        /// Both insets <b>compose</b> — each is added to whatever is already reserved and restored in a
        /// <c>finally</c> — so a sibling flow elsewhere in the same fragmentainer owes nothing. That is not
        /// the same as nested repeating headers working: an inner table's own proxy is placed at the band
        /// top the outer header occupies, which this does not address.
        /// </para>
        /// <para>
        /// The two reservations do <b>not</b> mirror each other in scope, and the difference is argued at
        /// <see cref="FragmentainerContext.ReserveBandEnd"/>: the header is drawn into one fragmentainer,
        /// the footer into every one the table covers.
        /// </para>
        /// </remarks>
        /// <param name="g">the graphics context layout is running against</param>
        /// <param name="row">the body row to lay out</param>
        /// <param name="startX">the table's own content left, as for <see cref="LayoutBodyRow"/></param>
        /// <param name="cursor">this pass's row cursor</param>
        /// <param name="slot">the band the row is being placed in, which the footer's claim is keyed from</param>
        /// <param name="headerRoom">
        /// the height the repeated header took plus the vertical spacing after it, or zero where this row
        /// owes it nothing
        /// </param>
        /// <param name="footerRoom">the height the repeated footer holds at the band's foot, or zero</param>
        private async ValueTask LayoutBodyRowInsideTheRepeatedGroups(
            RGraphics g, CssBox row, double startX, TableRowCursor cursor,
            int slot, double headerRoom, double footerRoom)
        {
            // Null on an unpaginated or measurement pass, where nothing resumes and CreateLineBoxes reads
            // the block's own ClientTop rather than any fragmentainer's content edge.
            var fragmentainer = _tableBox.HtmlContainer?.CurrentFragmentainer;

            if (fragmentainer is null || (headerRoom <= 0 && footerRoom <= 0))
            {
                await LayoutBodyRow(g, row, startX, cursor);
                return;
            }

            // Reserved and restored only where there is something to reserve. Restoring the null a skipped
            // Reserve would have returned clears a reservation an enclosing scope still owns - and
            // reserving zero is not a no-op either, since it re-keys the reservation to this call's slot.
            var previousTop = headerRoom > 0 ? fragmentainer.ReserveResumeContent(headerRoom) : null;
            var previousEnd = footerRoom > 0 ? fragmentainer.ReserveBandEnd(slot, footerRoom) : null;

            try
            {
                await LayoutBodyRow(g, row, startX, cursor);
            }
            finally
            {
                if (footerRoom > 0) fragmentainer.RestoreBandEnd(previousEnd);
                if (headerRoom > 0) fragmentainer.RestoreResumeContent(previousTop);
            }
        }

        /// <summary>
        /// The record whose row loop this run continues, or null when there is none — including a record
        /// naming a row this table does not have, which belongs to a layout that no longer exists and so
        /// has nothing to continue. Reading it as "start from the markup" is both the safe answer and the
        /// total one, and it is the same reading a record for a table with no settled setup gets.
        /// </summary>
        /// <remarks>
        /// Valid only once <see cref="AssignBoxKinds"/> has filled <c>_bodyRows</c>, which is every use.
        /// </remarks>
        private TableBreakToken? ContinuedRowLoop =>
            _carried is { ResumeRowIndex: var row } && row >= 0 && row < _bodyRows.Count ? _carried : null;

        /// <summary>The body row this run's loop starts at.</summary>
        private int ResumeRowIndex => ContinuedRowLoop?.ResumeRowIndex ?? 0;

        /// <summary>
        /// Steps 1 to 3: detaches the repeating <c>&lt;thead&gt;</c>/<c>&lt;tfoot&gt;</c> and lays each out
        /// once to measure it, recording both in <see cref="_setup"/> so a resumed pass inherits them
        /// rather than doing either again.
        /// </summary>
        private async ValueTask DetachAndMeasureRepeatedRowGroups(RGraphics g, TableRowCursor cursor, double startX)
        {
            // Step 1: Remove header/footer from document tree
            RemoveHeaderFooterFromTree();

            // Step 2: Layout header rows ONCE to calculate height. Proxy creation is deferred
            // until after the header pre-check below (Step 4) has settled the header's final
            // position - CssProxyBox.PerformLayoutImp captures a paint-time snapshot of the
            // header at whatever position it's laid out at, so creating the proxy here (before a
            // possible page-break relocation) would bake in a stale, pre-relocation snapshot.
            if (HeaderIsDetached && _headerBox != null)
            {
                // Layout header rows directly using table layout logic
                var headerRowsLayoutY = cursor.CurrentY;
                var headerCursor = cursor.ForRowGroupMeasurement(headerRowsLayoutY);

                // Header rows are always _allRows' own leading entries (AssignBoxKinds), so this loop's
                // own ordinal is that row's grid row index too - line rowIndex+1 is the boundary right
                // after it.
                var rowIndex = 0;
                var headerRows = new List<CssBox>();
                var headerOriginalRowIndices = ComputeRowGroupOriginalIndices(_headerBox);
                var headerSpanningCellsEndingOnRow = new Dictionary<int, List<CssBox>>();

                foreach (var row in _headerBox.Boxes)
                {
                    if (row.DerivedStyle.ActualDisplay != Keywords.TableRow)
                        continue;
                    if (IsRowCollapsed(row))
                        continue;

                    headerCursor.CurrentY = headerRowsLayoutY;
                    headerCursor.MaxBottom = headerRowsLayoutY;

                    await LayoutBodyRow(g, row, startX, headerCursor);

                    RegisterRowSpanCellsEndingRow(
                        row, rowIndex, headerOriginalRowIndices, headerSpanningCellsEndingOnRow,
                        _headerRowSpansCrossingIntoBody);
                    headerCursor.MaxBottom = GrowForClosingRowSpanCells(
                        rowIndex, headerSpanningCellsEndingOnRow, headerCursor.MaxBottom, _isVertical);

                    headerRowsLayoutY = headerCursor.MaxBottom + VerticalSpacingAt(rowIndex + 1);

                    // Unlike the regular body-row loop below, this never set the row's own
                    // Location/ActualRight/ActualBottom (only each cell's) - left it at a
                    // degenerate (0,0,0,0) Bounds, which the paint-time visibility-culling
                    // optimization (see SetRowGroupBoxDimensions's call-site comment for the same
                    // bug at the row-group level) then silently drops from painting entirely.
                    //
                    // headerCursor.MaxBottom is the row axis - see AssignRowActualBounds for why it (not
                    // Boxes.Max) is the safe source for that field on a vertical table.
                    row.Location = new RPoint(row.Boxes.Min(x => x.Location.X), row.Boxes.Min(x => x.Location.Y));
                    AssignRowActualBounds(row, headerCursor.MaxBottom);

                    CloseRowSpanCellsEndingOnRow(
                        g, rowIndex, headerSpanningCellsEndingOnRow,
                        _isVertical ? row.ActualRight : row.ActualBottom, _isVertical);

                    rowIndex++;
                    headerRows.Add(row);
                }

                cursor.MaxRight = headerCursor.MaxRight;

                // Set header box dimensions
                _headerBox.Location = _isVertical ? new RPoint(cursor.CurrentY, startX) : new RPoint(startX, cursor.CurrentY);
                if (_isVertical)
                {
                    _headerBox.ActualBottom = cursor.MaxRight;
                    _headerBox.ActualRight = headerRowsLayoutY - VerticalSpacingAt(rowIndex);
                    _headerHeight = _headerBox.ActualRight - _headerBox.Location.X;
                }
                else
                {
                    _headerBox.ActualRight = cursor.MaxRight;
                    _headerBox.ActualBottom = headerRowsLayoutY - VerticalSpacingAt(rowIndex);
                    _headerHeight = _headerBox.ActualBottom - _headerBox.Location.Y;
                }
            }

            // Step 3: Layout footer rows once to get dimensions (if needed)
            if (FooterIsDetached && _footerBox != null)
            {
                // Layout footer rows directly
                var footerRowsLayoutY = 0d;
                var footerCursor = cursor.ForRowGroupMeasurement(footerRowsLayoutY);

                // Footer rows are always _allRows' own trailing entries, after every header and body row -
                // see the header loop above for why the ordinal doubles as the grid row index.
                var footerRowIndex = HeaderRowCountInGrid + _bodyRows.Count;
                var footerRows = new List<CssBox>();
                var footerOriginalRowIndices = ComputeRowGroupOriginalIndices(_footerBox);
                var footerSpanningCellsEndingOnRow = new Dictionary<int, List<CssBox>>();

                foreach (var row in _footerBox.Boxes)
                {
                    if (row.DerivedStyle.ActualDisplay != Keywords.TableRow)
                        continue;
                    if (IsRowCollapsed(row))
                        continue;

                    footerCursor.CurrentY = footerRowsLayoutY;
                    footerCursor.MaxBottom = footerRowsLayoutY;

                    await LayoutBodyRow(g, row, startX, footerCursor);

                    // The row-group-local row index this closing pass keys against restarts at 0 here,
                    // deliberately independent of footerRowIndex (the whole-grid ordinal used for
                    // VerticalSpacingAt just below) - footerSpanningCellsEndingOnRow only ever has to be
                    // internally consistent with itself across this one loop, and never needs to agree
                    // with any other row-numbering scheme.
                    var footerLocalRowIndex = footerRows.Count;
                    RegisterRowSpanCellsEndingRow(row, footerLocalRowIndex, footerOriginalRowIndices, footerSpanningCellsEndingOnRow);
                    footerCursor.MaxBottom = GrowForClosingRowSpanCells(
                        footerLocalRowIndex, footerSpanningCellsEndingOnRow, footerCursor.MaxBottom, _isVertical);

                    footerRowsLayoutY = footerCursor.MaxBottom + VerticalSpacingAt(footerRowIndex + 1);
                    footerRowIndex++;

                    // See the identical fix in the header-rows loop above for why this is needed.
                    row.Location = new RPoint(row.Boxes.Min(x => x.Location.X), row.Boxes.Min(x => x.Location.Y));
                    AssignRowActualBounds(row, footerCursor.MaxBottom);

                    CloseRowSpanCellsEndingOnRow(
                        g, footerLocalRowIndex, footerSpanningCellsEndingOnRow,
                        _isVertical ? row.ActualRight : row.ActualBottom, _isVertical);

                    footerRows.Add(row);
                }

                cursor.MaxRight = footerCursor.MaxRight;

                // Unlike Location/ActualBottom above, ActualRight is a computed property derived
                // from Size.Width, which is never otherwise set on the footer row-group box itself
                // (only its row/cell children get real sizes) - leaving it out here left every
                // CssProxyBox created from _footerBox (see CreateFooterProxy) with a zero-width
                // Bounds, which CssBox.Paint's visibility-culling check (the Rectangles.Count==0/
                // Bounds-intersect-clip branch, active whenever the document has no float/absolute/
                // fixed content anywhere) then silently treated as never visible - the footer never
                // painted on any page. Mirrors the identical `_headerBox.ActualRight = maxRight`
                // assignment above. See GitHub issue #124.
                _footerBox.Location = _isVertical ? new RPoint(0, startX) : new RPoint(startX, 0);
                if (_isVertical)
                {
                    _footerBox.ActualBottom = cursor.MaxRight;
                    _footerBox.ActualRight = footerRowsLayoutY - VerticalSpacingAt(footerRowIndex);
                    _footerHeight = _footerBox.ActualRight - _footerBox.Location.X;
                }
                else
                {
                    _footerBox.ActualRight = cursor.MaxRight;
                    _footerBox.ActualBottom = footerRowsLayoutY - VerticalSpacingAt(footerRowIndex);
                    _footerHeight = _footerBox.ActualBottom - _footerBox.Location.Y;
                }
            }

            SettleWhetherTheGroupsRepeat();

            _setup.Header = _headerBox is null
                ? null
                : new DetachedRowGroup(_headerBox, _headerIndex, _headerHeight, _headerRepeats);
            _setup.Footer = _footerBox is null
                ? null
                : new DetachedRowGroup(_footerBox, _footerIndex, _footerHeight, _footerRepeats);
        }

        /// <summary>
        /// Registers each of <see cref="_headerRowSpansCrossingIntoBody"/>'s cells into <paramref name="cursor"/>'s
        /// own <see cref="TableRowCursor.RowSpannedBoxes"/>, at the body-local row its span actually ends
        /// on - before the body row loop places its first row, so the cell looks, to every consumer of
        /// that map, exactly like an ordinary rowspan cell some earlier body row opened (issue #788).
        /// </summary>
        private void SeedCrossBoundaryRowSpans(TableRowCursor cursor)
        {
            foreach (var (cell, bodyEndRow) in _headerRowSpansCrossingIntoBody)
            {
                if (!cursor.RowSpannedBoxes.TryGetValue(bodyEndRow, out var boxes))
                    cursor.RowSpannedBoxes[bodyEndRow] = boxes = [];

                boxes.Add(cell);
            }
        }

        /// <summary>
        /// Re-syncs every header proxy created so far against <paramref name="cell"/>'s own now-final
        /// (post-<see cref="CloseSpanningCell"/>) geometry - see <see cref="BoxGeometrySnapshot.Resync"/>
        /// for why this is needed at all for a header cell crossing into the body (issue #788).
        /// </summary>
        private void ResyncHeaderProxiesFor(CssBox cell)
        {
            foreach (var proxy in _tableBox.Boxes.OfType<CssProxyBox>())
            {
                if (proxy.DerivedStyle.ActualDisplay != Keywords.TableHeaderGroup) continue;

                proxy.SourceGeometry?.Resync(cell);
            }
        }

        /// <summary>
        /// Closes every <c>rowSpan &gt; 1</c> cell in a just-measured detached header's/footer's own rows
        /// the same way <see cref="LayoutBodyRow"/>'s own vertical-alignment loop closes an ordinary body
        /// row's rowspan cell (<c>cell.ActualBottom = rowMaxBottom; CssLayoutEngine.ApplyCellVerticalAlignment(g, cell);</c>,
        /// itself reached via a row that grew to fit the cell first) - except that loop keys both the
        /// growth and the close off <see cref="TableRowCursor.RowIndex"/>, which stays pinned at <c>-1</c>
        /// for every row of a row-group measurement pass (<see cref="TableRowCursor.ForRowGroupMeasurement"/>'s
        /// own remarks), so neither ever engages there. Deliberately not reached by giving that cursor real
        /// per-row indices instead: the close also runs through <see cref="CloseSpanningCell"/>, whose own
        /// bookkeeping (straddle correction, fragmentainer band geometry) is a pagination concept a
        /// row-group's own one-shot, never-resumed measurement pass has no analogue for.
        /// <see cref="DetachAndMeasureRepeatedRowGroups"/>'s header/footer loops instead keep this
        /// bookkeeping themselves, in a dictionary scoped to that one loop and keyed by a row-group-local
        /// row index with no meaning outside it, via the three methods below (issue #742). A <c>rowSpan</c>
        /// declared past the group's own last row (e.g. <c>rowspan="99"</c> in a two-row group) needs no
        /// special handling here either: <see cref="GetEffectiveEndRowIndex(int, int, IReadOnlyList{int}, int)"/>
        /// itself never returns past <c>originalRowIndices.Count - 1</c>, so such a cell simply registers
        /// against the group's own actual last row already.
        /// </summary>
        /// <param name="row">the row <see cref="LayoutBodyRow"/> just placed</param>
        /// <param name="rowIndex">that row's own row-group-local index (0-based, the caller's own counter)</param>
        /// <param name="originalRowIndices">
        /// <paramref name="row"/>'s own row-group's <see cref="ComputeRowGroupOriginalIndices"/> - a
        /// rowspan crossing one of the group's own <c>visibility: collapse</c> rows needs the identical
        /// remapping <see cref="GetEffectiveEndRowIndex(int, int, IReadOnlyList{int}, int)"/> already
        /// applies for a body row (issue #665's failure mode, reachable here too).
        /// </param>
        /// <param name="spanningCellsEndingOnRow">
        /// every <c>rowSpan &gt; 1</c> cell seen so far this loop, keyed by the row-group-local row index
        /// its span ends on - shared by every method here and scoped to one header/footer loop
        /// </param>
        /// <param name="crossingCells">
        /// <see cref="_headerRowSpansCrossingIntoBody"/>, when <paramref name="row"/> belongs to the
        /// header - a cell in it closes in the body instead (issue #788), so it must not also be
        /// registered here. Null for the footer loop, which has no equivalent (a footer-opened span has
        /// nothing after it in the grid to cross into).
        /// </param>
        private static void RegisterRowSpanCellsEndingRow(
            CssBox row, int rowIndex, IReadOnlyList<int> originalRowIndices,
            Dictionary<int, List<CssBox>> spanningCellsEndingOnRow,
            IReadOnlyDictionary<CssBox, int>? crossingCells = null)
        {
            foreach (var cell in row.Boxes)
            {
                var rowSpan = GetRowSpan(cell);
                if (rowSpan <= 1) continue;

                // A cell crossing into the body (issue #788) closes there instead - registering it here
                // too would stretch/align it against the header's own last row first, leaving it in a
                // state CloseSpanningCell's later close (from the body row it actually ends on) would
                // incorrectly compose on top of rather than replace.
                if (crossingCells?.ContainsKey(cell) == true) continue;

                var endRow = GetEffectiveEndRowIndex(rowIndex, rowSpan, originalRowIndices, originalRowIndices.Count);
                if (!spanningCellsEndingOnRow.TryGetValue(endRow, out var endingHere))
                    spanningCellsEndingOnRow[endRow] = endingHere = [];
                endingHere.Add(cell);
            }
        }

        /// <summary>
        /// Before <paramref name="rowIndex"/>'s own bottom is finalized, grows <paramref name="rowMaxBottom"/>
        /// to fit every <c>rowSpan &gt; 1</c> cell ending on it, using each one's own natural (pre-stretch)
        /// <c>ActualBottom</c> from the row that opened it - the same fold-back <c>LayoutBodyRow</c>'s own
        /// <c>sb.EndRow == rowIndex</c> branch does for an ordinary body row's <c>CssSpacingBox</c>.
        /// Without this, a cell taller than every row it spans combined left the header's/footer's own
        /// total height too short for its own tallest content - the table body (or the next row-group)
        /// then started overlapping it.
        /// </summary>
        private static double GrowForClosingRowSpanCells(
            int rowIndex, Dictionary<int, List<CssBox>> spanningCellsEndingOnRow, double rowMaxBottom, bool isVertical)
        {
            if (!spanningCellsEndingOnRow.TryGetValue(rowIndex, out var endingHere)) return rowMaxBottom;

            foreach (var cell in endingHere)
                rowMaxBottom = Math.Max(rowMaxBottom, isVertical ? cell.ActualRight : cell.ActualBottom);

            return rowMaxBottom;
        }

        /// <summary>
        /// Stretches every <c>rowSpan &gt; 1</c> cell ending on <paramref name="rowIndex"/> to
        /// <paramref name="rowAxisExtent"/> - that row's own now-final row-axis extent (<c>ActualBottom</c>
        /// for a horizontal-tb table, <c>ActualRight</c> for a vertical one), already grown to fit them by
        /// <see cref="GrowForClosingRowSpanCells"/> if any needed it.
        /// </summary>
        private static void CloseRowSpanCellsEndingOnRow(
            RGraphics g, int rowIndex, Dictionary<int, List<CssBox>> spanningCellsEndingOnRow, double rowAxisExtent,
            bool isVertical)
        {
            if (!spanningCellsEndingOnRow.TryGetValue(rowIndex, out var endingHere)) return;

            foreach (var cell in endingHere)
            {
                if (isVertical) cell.ActualRight = rowAxisExtent;
                else cell.ActualBottom = rowAxisExtent;
                CssLayoutEngine.ApplyCellVerticalAlignment(g, cell, isVertical);
            }
        }

        /// <summary>
        /// Answers css-tables-3 §6.2's two conditions on repeating a <c>&lt;thead&gt;</c>/<c>&lt;tfoot&gt;</c>
        /// at all, once for the table.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see href="https://www.w3.org/TR/css-tables-3/#repeated-headers">§6.2</see> repeats a group on
        /// each page a table spans <i>“if the header/footer has avoid <c>break-inside</c> applied to it”</i>
        /// and <i>“if the height required to do so is inferior to two quarters of the page height (up to
        /// one quarter for header rows, and up to one quarter for footer rows)”</i>. So: an avoiding
        /// <c>break-inside</c>, and each group's own quarter, independently. <c>&lt;</c> rather than
        /// <c>&lt;=</c>, because “inferior to” is strict.
        /// </para>
        /// <para>
        /// The <c>break-inside</c> half is a style read, and on its own it would stop every existing
        /// <c>&lt;thead&gt;</c> from repeating — which is not what any print engine does. The UA stylesheet
        /// supplies <c>thead, tfoot { break-inside: avoid }</c> under <c>@media print</c>, so the condition
        /// is behaviour-preserving and reads as what it is: an opt-<i>out</i> an author can take with
        /// <c>break-inside: auto</c>.
        /// </para>
        /// <para>
        /// Called from the measurement step, after both heights are known and before they are published:
        /// the cap needs a measured height, and the measurement is what a repetition flag would have to
        /// gate, so asking the question any earlier is circular. Once per table rather than once per band
        /// — the answer is carried on <see cref="DetachedRowGroup"/> and a continuation inherits it. Both
        /// groups repeat unconditionally with no real page grid to measure a quarter of.
        /// </para>
        /// <para>
        /// The quarter is taken of <see cref="HtmlContainerInt.PageSheetHeight"/> — the page box, margins
        /// included — because §6.2 says "the page height" and means the page rather than the content area.
        /// That is deliberately <i>not</i> the band a repeated group costs its height out of, which is the
        /// smaller of the two: measuring against the band would decline a group between a quarter of the
        /// band and a quarter of the sheet, where §6.2's <c>must</c> requires it to repeat. It is also why
        /// this takes no slot — only the margins vary per page, and the sheet they come out of does not.
        /// </para>
        /// </remarks>
        private void SettleWhetherTheGroupsRepeat()
        {
            var container = _tableBox.HtmlContainer;
            var quarterOfThePage = container is { HasRealPageGrid: true }
                ? container.PageSheetHeight / 4
                : double.MaxValue;

            // A vertical table is placed as a monolithic unit rather than paginated per-row (real per-row
            // pagination of a vertical table is out of scope for #762 - tracked as #783), so it never
            // actually spans a page boundary the way this repetition machinery assumes. _headerHeight/
            // _footerHeight are row-axis (physical-X) quantities for a vertical table, not the physical-Y
            // thickness quarterOfThePage measures against - comparing them here would be comparing
            // different axes by coincidence of magnitude, not by any meaningful relationship. Keeping both
            // flags false is what keeps every downstream consumer (RepeatedHeaderRoom/RepeatedFooterHeight,
            // and everything gated on them - SliceARowAcrossTheBandsItOverflows,
            // RepeatTheGroupsOnEveryBandTheTableSpans, RoomForARowIn) a correct no-op for a vertical table,
            // without needing an _isVertical check re-added at each of those separately.
            _headerRepeats = !_isVertical
                             && HeaderIsDetached
                             && _headerBox is { } header
                             && BreakValues.AvoidsBreak(header.BreakInside, FragmentationContext.Page)
                             && _headerHeight < quarterOfThePage;

            _footerRepeats = !_isVertical
                             && FooterIsDetached
                             && _footerBox is { } footer
                             && BreakValues.AvoidsBreak(footer.BreakInside, FragmentationContext.Page)
                             && _footerHeight < quarterOfThePage;
        }

        /// <summary>
        /// Step 4 onwards: places the body rows, breaking between them as the page runs out, and settles
        /// the table's own dimensions.
        /// </summary>
        private async ValueTask LayoutBodyRows(
            RGraphics g, TableRowCursor cursor, double startX, double startY,
            HtmlContainerInt? container, double pageHeight)
        {
            // The two whole-table pre-checks below move the table's own Location, so both are once per
            // table: a continuation's earlier rows are already emitted at coordinates the table's own
            // position is what makes sense of, and moving it now moves only the rows still to come.
            //
            // Pre-check: move the entire table to the next page when the first body row
            // would cross a page boundary AND the full table body fits on one page.
            // The per-row page-break check uses `i > 0` so it never fires for single-row
            // tables; this pre-check handles that case by adjusting the table's location.
            // Restricted to tables without repeating headers/footers to avoid repositioning
            // proxy boxes that were already placed above.
            if (_bodyRows.Count > 0
                && !_continuesAPreviousPass
                && pageHeight < double.MaxValue - 1
                && _tableBox.HtmlContainer != null
                && !HeaderIsDetached
                && !FooterIsDetached)
            {
                var slot = cursor.SlotIndex;
                var firstRowHeight = EstimateRowHeight(_bodyRows[0]);
                // The band height already excludes both margins (PdfGenerator.SetContent) -
                // subtracting them again here double-counted a marginTop+marginBottom-sized band
                // out of every page's real capacity.
                var availableHeight = container!.PageBandHeightOf(slot) - RepeatedFooterHeight;
                var estimatedBodyHeight = _bodyRows.Sum(EstimateRowHeight);

                if (WillCrossPageBoundary(container, cursor.CurrentY + firstRowHeight, availableHeight, slot, pageHeight)
                    && estimatedBodyHeight <= availableHeight)
                {
                    var pageBreakOffset = CalculatePageBreakOffset(container, cursor.CurrentY, slot);
                    pageBreakOffset = PullKeepWithNextRun(container, cursor.CurrentY, pageBreakOffset,
                        slot, availableHeight, estimatedBodyHeight);

                    _tableBox.Location = _tableBox.Location with { Y = _tableBox.Location.Y + pageBreakOffset };
                    foreach (var caption in _topCaptions) caption.OffsetTop(pageBreakOffset);
                    startY = Math.Max(_tableBox.ClientTop + _topCaptionsHeight + StartYSpacing(), 0);
                    // startY is a fresh restart point at the table's own content-top edge on the new
                    // page, not an arbitrary interior coordinate - the same "top edge flush on a
                    // boundary" case HtmlContainerInt.SlotStartingAt exists for (see the identical fix in
                    // CssLayoutEngineColumns.Layout, #573's investigation).
                    cursor.RestartAt(startY, container.SlotStartingAt(startY), container);
                }
            }

            // Pre-check: a detached <thead> is laid out above (Step 2) before any body row is
            // attempted - if the header itself fits on the current page but no body row does, the
            // per-row break check below can't catch it (it requires `i > 0`, since it exists to
            // detect breaks *between* rows) and the whole header (plus any keep-with-next heading)
            // would strand alone on the current page while every body row starts on the next.
            // Mirrors the headerless pre-check above - "first body row after the header" instead
            // of "first body row after the table's own top" - but deliberately NOT gated on the
            // whole body fitting one page: a long repeating-header table should still start fresh
            // on the next page rather than orphan its header, and fragments normally via the
            // per-row check from there.
            //
            // Keyed to the header being detached, not to it repeating: a group §6.2 declines to repeat
            // is still drawn once, here, and can strand here just the same.
            if (_bodyRows.Count > 0
                && !_continuesAPreviousPass
                && pageHeight < double.MaxValue - 1
                && container != null
                && HeaderIsDetached && _headerBox != null)
            {
                var slot = cursor.SlotIndex;
                var firstRowHeight = EstimateRowHeight(_bodyRows[0]);
                var availableHeight = container.PageBandHeightOf(slot) - RepeatedFooterHeight;
                var afterHeaderY = startY + _headerHeight + VerticalSpacingAt(HeaderRowCountInGrid);

                if (WillCrossPageBoundary(container, afterHeaderY + firstRowHeight, availableHeight, slot, pageHeight))
                {
                    var pageBreakOffset = CalculatePageBreakOffset(container, startY, slot);
                    pageBreakOffset = PullKeepWithNextRun(container, startY, pageBreakOffset,
                        slot, availableHeight, _headerHeight + firstRowHeight);

                    _tableBox.Location = _tableBox.Location with { Y = _tableBox.Location.Y + pageBreakOffset };
                    _headerBox.OffsetTop(pageBreakOffset);
                    foreach (var caption in _topCaptions) caption.OffsetTop(pageBreakOffset);

                    startY = Math.Max(_tableBox.ClientTop + _topCaptionsHeight + StartYSpacing(), 0);
                    // startY is a fresh restart point at the table's own content-top edge on the new
                    // page, not an arbitrary interior coordinate - the same "top edge flush on a
                    // boundary" case HtmlContainerInt.SlotStartingAt exists for (see the identical fix in
                    // CssLayoutEngineColumns.Layout, #573's investigation).
                    cursor.RestartAt(startY, container.SlotStartingAt(startY), container);
                }
            }

            // Create the header proxy that references the already-laid-out header, now that its
            // final (possibly page-break-adjusted) position is settled. CreateHeaderProxy's
            // CssProxyBox constructor already appends itself to _tableBox.Boxes (see the base
            // CssBox(parentBox, tag) constructor) - an explicit second Add here duplicated the
            // same proxy instance in the list, causing every header row to be painted (and, once
            // tagged, MCID-tagged) twice at identical coordinates - invisible on the page (exact
            // overlap) but wasted content-stream bytes and duplicate structure-tree entries.
            //
            // How much of this fragmentainer the header took, which a continuation owes to the one flow
            // the cursor cannot speak for - see ReserveHeaderRoomForResumedCells.
            //
            // This is the one header block a group §6.2 declines to repeat still gets, and only on the
            // pass that starts the table: the group is laid out once, in flow, at the table's own top,
            // which is what "not repeated" means - not "not drawn". Every other header site below is
            // about a *later* band and reads _headerRepeats.
            var resumedHeaderRoom = 0d;

            if (HeaderIsDetached && _headerBox != null && (_headerRepeats || !_continuesAPreviousPass))
            {
                var headerProxy = CreateHeaderProxy(cursor.CurrentY);
                if (headerProxy != null)
                {
                    await headerProxy.PerformLayout(g);

                    var headerRoom = _headerHeight + VerticalSpacingAt(HeaderRowCountInGrid);
                    if (_continuesAPreviousPass) resumedHeaderRoom = headerRoom;

                    cursor.CurrentY += headerRoom;
                    cursor.MaxBottom = cursor.CurrentY;
                }
            }

            // The body rows this table has reached, this pass or an earlier one - which is every body
            // row unless the loop below stops. Steps 5 and 6 close the table over them rather than over
            // its markup, because a row no pass has placed has no geometry to close over.
            var placedRows = _bodyRows.Count;

            // What every row owes the footer this table repeats at the foot of the band it is placed in.
            // Every row, not only the one a continuation re-enters as the header's room is: the cursor
            // positions a row's *top*, and any row's cell can be the one whose own flow runs down into the
            // footer's strip - which row that is cannot be known before it is placed, since
            // EstimateRowHeight is one line of text per cell and blind to block content.
            var repeatedFooterRoom = RepeatedFooterHeight;

            // The band the loop was filling when it stopped, or null where it did not. Taken from the row's
            // own iteration rather than re-derived after: cursor.BandReached follows CurrentY past the band
            // it was filling, so a row that overflowed would name the band after the one this pass is
            // leaving, and this is also the band a break between two rows would have keyed its slice bottom
            // to - which keeps the footer sites saying the same thing.
            int? bandTheStopLeaves = null;

            // This run re-decides where every finished cell's box sits, so what an earlier one stated over
            // the same slots goes first. Swept here, once, rather than per cell in the loop below: a cell
            // the loop never reaches - because it stops at a cell that did not finish, or runs out of
            // columns - would otherwise keep a statement no pass still stands behind. A run continuing an
            // earlier pass sweeps only from the slot it resumes in, because the slots before it were
            // settled by the passes that filled them and are still true, which is what lets a row spanning
            // three fragmentainers hold a fragment in each.
            DiscardContinuationShells(container, _continuesAPreviousPass ? cursor.SlotIndex : null);

            // Per pass: step 5b reads it at the end of this one, and a band an abandoned run overflowed
            // into is not a band this one does.
            _bandsARowOverflowedInto.Clear();

            // A continuation re-enters the row that did not finish, not the one after it: only some of
            // that row's cells stopped, and the cells of a row are §2.1 parallel flows, so the row is
            // where the record points. Every other run starts at the first body row.
            for (var i = ResumeRowIndex; i < _bodyRows.Count; i++)
            {
                var row = _bodyRows[i];
                cursor.RowIndex = i;

                // The band the cursor has actually reached, which is the band every question below is
                // asked of. It is the counter's floor raised to where CurrentY is, so a row that
                // overflowed the band it was placed in does not leave the loop asking about a band the
                // content has already passed - which is what made the break offset come out negative and
                // put the rows after a too-tall row back inside it (issue #432).
                var slot = container is not null && pageHeight < double.MaxValue - 1
                    ? cursor.BandReached(container)
                    : cursor.SlotIndex;
                var estimatedRowHeight = EstimateRowHeight(row);
                // See the identical fix/comment on the pre-check's availableHeight above.
                var availableHeight = (container?.PageBandHeightOf(slot) ?? pageHeight) - RepeatedFooterHeight;

                // Check for page break: either the row does not fit, or a break value says the break
                // falls here regardless (css-break-3 §3.1's class-A break point between two rows).
                // Not at the row a continuation re-enters: that break point was decided by the pass that
                // stopped there, and re-deciding it takes a forced break a second time - pushing the row
                // a further page down, adding a second header and footer proxy, and writing this pass's
                // MaxBottom (the band top it has just started at) over the slice bottom the earlier pass
                // recorded for that page, which is what clips the table's borders there. §4.4's "no
                // empty fragmentainer" says the same thing from the other side: the resumed row begins
                // this fragmentainer, so there is nothing before it here to break from.
                //
                // The prediction is EstimateRowHeight's, which undershoots a row holding block content by
                // roughly 2x - so this arm is the cheap one that catches an ordinary text row before it
                // is ever placed straddling, and the correction below is what makes the answer right.
                // A vertical table's row loop never takes a page break of its own - TakeBreakBeforeRow
                // repositions the cursor against container.PageTopOf, a physical-Y page-top, but this
                // table's own cursor.CurrentY tracks the row axis (physical X - see the pageHeight
                // override's own remarks above), so honoring ForcedBreakFallsBeforeRow here as well
                // (not just the WillCrossPageBoundary estimate) would feed a physical-Y value into a
                // physical-X accumulator. The whole table already moves as a unit if it doesn't fit
                // (MonolithicContent.IsUnresumableVerticalTable); an explicit break-before/-after on a row
                // inside one is deferred along with the rest of real per-row vertical-table pagination
                // (#762) rather than partially honored here.
                if (pageHeight < double.MaxValue - 1
                    && i > ResumeRowIndex && container != null
                    && (ForcedBreakFallsBeforeRow(i)
                        || WillCrossPageBoundary(container, cursor.CurrentY + estimatedRowHeight, availableHeight, slot, pageHeight)))
                {
                    slot = await TakeBreakBeforeRow(g, container, cursor, slot);
                }

                // Layout body row. Only the row a continuation re-enters can hold a cell whose own flow
                // resumes, so only that row owes the repeated header the room it took - while every row
                // owes the repeated footer the room it holds at the foot of the band.
                var placement = cursor.BeginRow();
                var rowTop = cursor.CurrentY;

                await LayoutBodyRowInsideTheRepeatedGroups(
                    g, row, startX, cursor, slot,
                    headerRoom: i == ResumeRowIndex ? resumedHeaderRoom : 0,
                    footerRoom: repeatedFooterRoom);

                // The question the estimate could only guess at, now that the row has been placed and its
                // real bottom is readable: did it cross out of the band it began in? Where it did, the
                // break belongs before it (§4.3), so the placement is taken back and the row is placed
                // again on the other side of it. At most one correction per row, so this cannot loop.
                if (StraddleCorrectionAppliesTo(i, container, cursor, rowTop, slot, pageHeight))
                {
                    // A placement that closed a spanning cell also stated the geometry that cell occupies
                    // in the bands after its own (CloseSpanningCell), and that is keyed by (box, slot) -
                    // so a re-placement stating fewer slots than the abandoned one would leave the
                    // difference behind to be painted. Swept before the retraction, which is what still
                    // knows which cells the row wrote to.
                    foreach (var spanning in cursor.ForeignCellsWritten)
                    {
                        container!.ClearContinuationShells(spanning);
                    }

                    // And the strips the abandoned placement stated for the row itself: the re-placement
                    // decides them again from the band the break just opened, and a state left behind
                    // would displace the row by a gap that placement no longer opens.
                    container!.ClearFragmentDisplacements(row);

                    cursor.Retract(placement);
                    PassRewind.RollBackTo(null, row.Boxes);

                    slot = await TakeBreakBeforeRow(g, container!, cursor, slot);

                    // Re-clears FinishedCells, which the retracted placement filled in. The footer's room is
                    // owed again, and against the *new* slot: this row is being placed in the band the
                    // break just opened. The header's is not - this arm never runs at ResumeRowIndex.
                    cursor.RowIndex = i;
                    await LayoutBodyRowInsideTheRepeatedGroups(
                        g, row, startX, cursor, slot, headerRoom: 0, footerRoom: repeatedFooterRoom);
                }

                // A row no band could hold was left where it is (§4.3), so it overflows into bands this
                // table also spans - and §6.2 owes the groups it repeats their room on every one of them.
                // The only way to pay that is to slice the row's own graphical representation, which is
                // §4.3's last rung in as many words. Stated after the correction, because a row the
                // correction moved fits the band it was moved to and has nothing to slice.
                var slicedRowBottom = SliceARowAcrossTheBandsItOverflows(container, row, cursor, rowTop);

                cursor.CurrentY = cursor.MaxBottom + VerticalSpacingAt(HeaderRowCountInGrid + i + 1);

                row.Location = new RPoint(row.Boxes.Min(x => x.Location.X), row.Boxes.Min(x => x.Location.Y));
                AssignRowActualBounds(row, cursor.MaxBottom, slicedRowBottom);

                // A cell of this row ran out of fragmentainer before it ran out of content, so the rows
                // after it belong to the fragmentainer this table resumes in - laying them out here would
                // place them above content that has not been placed yet. The row itself stays: its
                // finished cells are complete and its unfinished ones continue, which is what
                // css-tables-3 §6.1 asks of a fragmented row.
                //
                // Reachable from markup exactly once in the suite today, and there the row is the last
                // one, so nothing is skipped. What makes the table resumable from outside is publishing
                // the record below as this box's PendingBreakToken, which this step deliberately does not
                // do - see CssBox.TableContinuation.
                if (cursor.Stopped)
                {
                    placedRows = i + 1;
                    bandTheStopLeaves = slot;
                    break;
                }
            }

            // Step 5a: a pass that ran out of fragmentainer *inside a cell* still owes the band it is
            // leaving the footer that band repeats. Neither of the two sites that write one reaches it -
            // the per-row break block only runs where the loop goes on to place another row, and step 5's
            // closing footer belongs under the table's last row, which this pass has not reached.
            // css-tables-3 §6.2 asks for the footer at the foot of every page the table covers, and a page
            // a mid-cell continuation leaves is one of them (#493). A third case rather than a relaxation
            // of either gate: both are load-bearing, and what makes this one different is that its footer
            // closes a *page* rather than the table.
            //
            // The paginated guard is not decorative: CalculateFooterPositionAtPageBottom hands back
            // currentY unchanged for an unpaginated container, which is exactly the mid-table position
            // step 5's gate exists to prevent.
            if (bandTheStopLeaves is { } leaving && container != null
                && pageHeight < double.MaxValue - 1
                && _footerRepeats && _footerHeight > 0)
            {
                var pageFooterProxy = CreateFooterProxy(
                    CalculateFooterPositionAtPageBottom(container, cursor.CurrentY, leaving));

                if (pageFooterProxy != null)
                {
                    await pageFooterProxy.PerformLayout(g);

                    // Same record, and for the same reason, as TakeBreakBeforeRow's: this is where the
                    // table's slice on that page ends, and FragmentPainter clips the table's bottom border
                    // to it. Without the entry the border is drawn above the footer just placed under it.
                    //
                    // Deliberately inside the footer arm. Writing it for every stopping pass would tell
                    // CssBox.PaginatedItsOwnContentWithoutBreaking that every mid-cell-continuing table
                    // fragmented, which is a far wider change than this one.
                    _tableBox.PageBreakBottoms ??= new Dictionary<int, double>();
                    _tableBox.PageBreakBottoms[leaving] = pageFooterProxy.ActualBottom;

                    GrowMaxRightFor(cursor, pageFooterProxy);
                }
            }

            // Step 5b: css-tables-3 §6.2 repeats the groups on every page the table *spans*, and the three
            // sites above are all keyed to a break being *taken* - so a band the table merely covers, with
            // no break falling on it, got neither. That is a row overflowing through a whole band, and the
            // strips SliceARowAcrossTheBandsItOverflows states are what leave the room to put them in.
            await RepeatTheGroupsOnEveryBandTheTableSpans(g, container, cursor);

            // Step 5: Create final footer proxy. Not on a pass that stopped: the closing footer sits
            // under the table's last row, and a pass that has not reached the last row would put it in
            // the middle of the table on the page it is leaving. The footer for that page is step 5a's
            // above - which is a different footer, closing that page rather than the table - or the
            // per-row break block's, and the pass that finishes the table writes this one.
            //
            // Keyed to the footer being detached rather than to it repeating: this footer closes the
            // *table*, so it is drawn under the last row whether or not §6.2 let it repeat - the same way
            // the header block above still draws a non-repeating <thead> once at the table's top.
            if (!cursor.Stopped && FooterIsDetached && _footerHeight > 0)
            {
                await MoveTheClosingFooterOffABoundaryItWouldStraddle(g, container, cursor);

                var finalFooterProxy = CreateFooterProxy(cursor.CurrentY);
                if (finalFooterProxy != null)
                {
                    await finalFooterProxy.PerformLayout(g);
                    cursor.CurrentY += _footerHeight + VerticalSpacingAt(_grid?.RowCount ?? 0);

                    // cursor.MaxBottom is the row-axis tracker (physical X for a vertical table) - the
                    // proxy's own row-axis far edge is ActualRight there, ActualBottom otherwise. Found
                    // reading ActualBottom unconditionally: a vertical table with a non-empty <tfoot> had
                    // its own final row-axis extent computed without the footer's own extent folded in at
                    // all, leaving the footer positioned entirely outside the table's own settled bounds.
                    cursor.MaxBottom = Math.Max(cursor.MaxBottom, _isVertical ? finalFooterProxy.ActualRight : finalFooterProxy.ActualBottom);
                    GrowMaxRightFor(cursor, finalFooterProxy);
                }
            }

            // Step 6: Set row-group (<tbody>) box dimensions. Unlike <thead>/<tfoot> (always
            // explicitly positioned above via _headerBox/_footerBox, since any <thead>/<tfoot>
            // present is unconditionally treated as repeatable), a <tbody>'s own rows are flattened
            // straight into _bodyRows by AssignBoxKinds and laid out directly - the <tbody> box
            // itself is never otherwise touched, leaving its Location/ActualRight/ActualBottom at
            // their unset defaults (an empty/degenerate Bounds). That's harmless for layout itself
            // (nothing sizes against a row-group's own box), but CssBox.Paint's visibility-culling
            // optimization intersects a Rectangles.Count==0 box's own Bounds against the current
            // clip whenever the document has no floated/absolute/fixed content anywhere - a <tbody>
            // with a never-set (0,0,0,0) Bounds fails that intersection and gets silently culled
            // along with its entire row/cell subtree, even though every row/cell inside it has a
            // perfectly valid, already-computed position. Give every row-group box a real bounding
            // rect spanning its own row children so it participates in that check correctly.
            SetRowGroupBoxDimensions(placedRows);
            SetColumnBoxDimensions();

            // Step 7: Set final table dimensions. cursor.MaxRight tracks the column axis (physical Y for
            // a vertical table, per its own tracking in LayoutBodyRow), so it settles ActualBottom there
            // instead of ActualRight - the column-axis twin of the row-axis settling below.
            if (_isVertical)
            {
                var tableBottom = Math.Max(cursor.MaxRight, _tableBox.Location.Y + _tableBox.ActualHeight);
                _tableBox.ActualBottom = tableBottom + HorizontalSpacingAt(_columnCount) + TableInlineBorderEnd;
            }
            else
            {
                var tableRight = Math.Max(cursor.MaxRight, _tableBox.Location.X + _tableBox.ActualWidth);
                _tableBox.ActualRight = tableRight + HorizontalSpacingAt(_columnCount) + TableInlineBorderEnd;
            }

            // Computed ahead of ActualBottom rather than only assigned to TableContinuation below,
            // because a bottom caption's placement depends on it: the caption belongs after the
            // table's very last row, never stitched into the middle of a table still spanning
            // fragmentainers, so it may only be laid out on the pass that finishes the row loop.
            var tableContinuation = cursor.Continuation(_tableBox);
            // cursor.MaxBottom/startY are already row-axis (physical X for a vertical table, per their
            // own tracking above) - only the final assignment target and the border-width term below need
            // the axis swap.
            var contentBottom = Math.Max(cursor.MaxBottom, startY) + VerticalSpacingAt(_grid?.RowCount ?? 0);

            // CSS 2.1 §17.4: the row grid's own border-box bottom - where FinalizeGridDecorationBoxGeometry
            // sizes the grid's own paint rect to (issue #721) - sits here, before any bottom caption is
            // added below it. A bottom caption is positioned from this edge (flush beneath the grid's own
            // border, mirroring the top caption's own flush-above positioning in LayoutCells) rather than
            // from the bare contentBottom a caption used to start from, which put the caption where the
            // grid's own border still had to be drawn beneath it.
            var gridBorderBoxBottom = contentBottom + TableRowAxisBorderEnd;

            if (tableContinuation is null && _bottomCaptions.Count > 0)
            {
                // The caption's own returned position (its own far row-axis edge plus its own trailing
                // margin) already includes the grid's own row-axis-end border width once, via
                // gridBorderBoxBottom above - adding it again below would double-count it, extending
                // _tableBox's combined extent (and so a following sibling's position) past the caption's
                // own visible far edge by one border width. gridBorderBoxBottom is already a row-axis
                // scalar (see its own remark above), so it's exactly LayoutCaptionGroup's rowAxisCursor -
                // no further axis conversion needed for that argument.
                var columnAxisStart = _isVertical ? _tableBox.Location.Y : _tableBox.Location.X;
                contentBottom = await LayoutCaptionGroup(g, _bottomCaptions, columnAxisStart, gridBorderBoxBottom, GetWidthSum());

                // Row-axis-max edge (see ReflectRowAxisForVerticalRl, which reads _tableBox.ActualRight
                // as exactly that for a vertical table) vs. the ordinary horizontal-tb row axis
                // (ActualBottom). The vertical table's own column-axis extent (ActualBottom there) was
                // already settled above, at Step 7's first dimension arm, and a bottom caption never
                // changes the column axis, so it is deliberately left alone here.
                if (_isVertical) _tableBox.ActualRight = contentBottom;
                else _tableBox.ActualBottom = contentBottom;
            }
            else if (_isVertical)
            {
                _tableBox.ActualRight = gridBorderBoxBottom;
            }
            else
            {
                _tableBox.ActualBottom = gridBorderBoxBottom;
            }

            // vertical-rl's rows actually grow from the physical-max (right) edge, not the physical-min
            // (left) edge every row/cell above was just placed against (see the axis-mapping fields' own
            // remarks on why this pass, rather than solving it during placement, is the correct fix) - now
            // that the table's own final row-axis bounds are settled (_tableBox.ActualRight above), mirror
            // every row's (and its cells', and their content's) row-axis position within them, then
            // recompute each row-group's bounding box from the rows' new positions - SetRowGroupBoxDimensions
            // above ran before the reflection and so captured each row-group's pre-reflection bounds.
            //
            // Captions and header/footer proxies share the exact same "grow forward from the row-axis-min
            // edge, mirror once the table's own final row-axis bounds are known" chicken-and-egg problem
            // rows have, and are laid out the same way (LayoutCaptionGroup; CreateHeaderProxy/
            // CreateFooterProxy), so they join the same sweep rather than getting a second reflection
            // formula. A header/footer's own PAINTED content is reflected through its proxy (OffsetLeft is
            // a real subtree translation, and CssProxyBox.OnTranslated keeps its own captured paint
            // snapshot in sync with exactly this uniform, group-wide shift - see #437) - but
            // _headerBox/_footerBox's own detached row objects join the sweep too, even though nothing
            // paints them directly, because GetGridLineY/GetGridLineX (collapsed-border geometry) read
            // them straight off TableGrid.RowAt/CellAt, not off the proxy's own snapshot. Left unreflected,
            // a border segment whose row-axis span touches the header/footer-adjacent grid line read the
            // detached header's still-forward-grown (pre-mirror) position against the body's
            // already-mirrored one - two coordinate spaces with no relationship to each other, producing a
            // degenerate or wildly wrong span (found by rendering a real header+collapsed-border+rowspan
            // vertical-rl table and looking at the result, not by a token/count assertion - this repo's own
            // stated pitfall for exactly this class of bug).
            //
            // A group with two or more rows needs more than that one uniform shift: ReflectRowAxisForVerticalRl
            // itself now also reverses each such group's own internal row order (issue #784), applying the
            // extra per-row residual to both the detached rows above AND directly into the proxy's own
            // captured snapshot (OnTranslated's uniform shift alone cannot express two rows moving by
            // different amounts) - see that method's own remarks for the full reasoning.
            //
            // FinalizeGridDecorationBoxGeometry (below, not before this block) deliberately runs after the
            // reflection: it reads the top caption's own final ActualRight/ActualMarginRight to place the
            // decoration box's row-axis-start edge, and pre-reflection that value is still the caption's
            // forward-grown (near the table's physical-min edge) position, not its final vertical-rl one.
            if (_isVertical && _rowAxisStartIsAtMax)
            {
                IEnumerable<CssBox> boxesToReflect = _topCaptions
                    .Concat(_bodyRows.Take(placedRows))
                    .Concat(_bottomCaptions)
                    .Concat(_tableBox.Boxes.OfType<CssProxyBox>());
                if (_headerBox is not null) boxesToReflect = boxesToReflect.Append(_headerBox);
                if (_footerBox is not null) boxesToReflect = boxesToReflect.Append(_footerBox);
                ReflectRowAxisForVerticalRl(_tableBox, boxesToReflect);
                SetRowGroupBoxDimensions(placedRows);
            }

            FinalizeGridDecorationBoxGeometry(gridBorderBoxBottom);

            // Publish where the row loop stopped, as a copy of the cursor's own state rather than the
            // cursor: a caller that kept one alive past this point would otherwise mutate what it has
            // already handed out. Null when the loop reached the end of the body rows.
            //
            // Published twice, on purpose. TableContinuation is the engine's own channel and the one a
            // later run of *this* engine reads back. PendingBreakToken is what says "I did not finish" to
            // everything else at once - CssBox.PerformLayoutImp returns early on it,
            // PublishBreakToTheContextRoot hands it to the fragmentation context, and the parent's child
            // loop stops and wraps it into a link naming this table - which is the whole of how a second
            // fragmentainer pass is opened for the rows after the stop (issue #464). The engine cannot use
            // one field for both: BeginLayoutPass clears PendingBreakToken at the top of the table's next
            // layout, and the record has to survive that in order to be handed back to the run that
            // continues it.
            _tableBox.TableContinuation = tableContinuation;

            if (_tableBox.TableContinuation is { } stopped)
            {
                _tableBox.SetPendingBreakToken(stopped);
            }

            // What the pre-checks above decide from EstimateRowHeight - a one-line-of-text heuristic
            // that can grossly undershoot a row whose cells hold tall block content - only real layout
            // can settle. When the estimate misses, the laid-out table straddles a page boundary with no
            // per-row break recorded and would paint sliced across two pages. Correcting that is a
            // question about a box that has finished laying out and knows its own height, which is what
            // CssBox.PerformLayoutEpilogue's §4.3 mover already asks of every other such box: this table
            // recorded no break inside itself, so it did not fragment, and a box that did not fragment
            // is moved whole or left alone. The engine states the fact (PageBreakBottoms) and the
            // epilogue takes the decision - see CssBox.PaginatedItsOwnContentWithoutBreaking.

            // Real per-column pagination (issue #783), for a vertical table only. Run last, once every
            // row's real geometry (including the vertical-rl reflection above) has settled - see the
            // method's own remarks for why this is a relocation pass over already-correct geometry rather
            // than a change to the row loop itself.
            RelocateColumnsAcrossPageBoundaries(placedRows);
        }

        /// <summary>
        /// Relocates whole columns of a vertical table across page boundaries wherever one falls between
        /// them - issue #783's first-cut real per-column pagination, scoped to a "plain grid" table (see
        /// <see cref="MonolithicContent.HasColumnPaginationExcludedFeature"/>).
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>Why a relocation pass over already-laid-out geometry, not an interleaved column-major layout
        /// loop.</b> A vertical table's own column axis (physical Y) is genuinely paginable - unlike the
        /// row axis, physical Y is the page's own real fragmentation axis - but a *row*'s own row-axis
        /// extent still cannot be known without laying out that row's cells across every column at once
        /// (there is no per-row equivalent of <c>_columnWidths[]</c>'s own CSS-hint-driven precomputation).
        /// Interleaving column-by-column layout with page-by-page pagination would need every row's
        /// content fragmented across the very columns pagination is trying to place independently - a
        /// two-pass architecture (measure every row unpaginated, *then* paginate) genuinely more than this
        /// first cut needs. This method instead runs once, after the table's own ordinary (still-forward-
        /// grown, unpaginated) row loop has already settled every cell's real geometry: a column's own
        /// extent is entirely fixed by <c>_columnWidths[]</c> (<see cref="CellInlineSize"/>'s "cell height
        /// is the column-sizing hint" mechanism) - identical for every row's cell in that column, by
        /// construction (<c>cell.ActualBottom = cell.Location.Y + width</c> in <see cref="LayoutBodyRow"/>,
        /// <c>width</c> read straight from <c>_columnWidths[]</c>, never from that cell's own content) - so
        /// relocating a column onto a later page is a pure subtree translation
        /// (<see cref="CssBox.OffsetTop(double)"/>) of every row's cell in it, the same mover pattern
        /// already used throughout this engine (e.g. <see cref="ReflectRowAxisForVerticalRl"/>), not a new
        /// layout pass with its own continuation/resumption machinery.
        /// </para>
        /// <para>
        /// <b>Plan, then commit - never partially relocate.</b> The loop below first walks every column
        /// computing where it would land (<c>plannedDeltas</c>) and which page breaks that requires
        /// (<c>pageBreaks</c>), mutating nothing. Only if that succeeds for every column does a second loop
        /// apply the planned shifts. A single column whose own fixed extent exceeds even a fresh page's
        /// whole band cannot be helped by any relocation - discovered partway through, that would leave
        /// earlier columns already translated with no undo - so the whole pass bails out, unmodified, the
        /// instant that is found, deferring to <see cref="MonolithicContent.IsUnresumableVerticalTable"/>'s
        /// own existing "move the whole table" fallback for that one remaining sub-case. The same bail-out
        /// covers the table's own first column not fitting where the table currently starts: that is not a
        /// column-relocation question (nothing has been placed on this page yet for a relocation to
        /// preserve), so the ordinary whole-table mover already answers it correctly on its own.
        /// </para>
        /// <para>
        /// <b>No new "did this fragment" fact is needed.</b> Whether this pass found anything to relocate
        /// is answered exactly the way an ordinary <c>horizontal-tb</c> table's own row-break decision
        /// already is - <see cref="CssBox.PaginatedItsOwnContentWithoutBreaking"/> reading
        /// <see cref="CssBox.PageBreakBottoms"/> as a post-layout fact. This method states that same fact:
        /// every entry records the absolute Y the table's own slice on that page ends at, which
        /// <see cref="Paint.FragmentPainter"/> already reads to clip the table's own bottom border there.
        /// Finding nothing to relocate, or bailing out entirely, simply leaves it unset, and the table
        /// falls through to exactly today's monolithic move-whole behavior with no special-casing needed
        /// here for either outcome.
        /// </para>
        /// </remarks>
        /// <param name="placedRows">
        /// how many of <see cref="_bodyRows"/> this pass actually placed - every one of them, since a
        /// vertical table's own row loop never stops mid-table (its <c>pageHeight</c> override routes it
        /// through the unpaginated fallback path), but named to match <see cref="ReflectRowAxisForVerticalRl"/>'s
        /// own caller rather than assumed.
        /// </param>
        private void RelocateColumnsAcrossPageBoundaries(int placedRows)
        {
            if (!_isVertical || _bodyRows.Count == 0 || placedRows == 0) return;

            // A continuation pass resumes only the still-open cell content of a row already placed by an
            // earlier pass - row 0's geometry (this method's own reference for every column's position) is
            // not fresh here, and may already reflect an earlier pass's own relocation. Recomputing deltas
            // from it a second time has no established invariant behind it, so this pass simply defers to
            // whatever the fresh pass that placed row 0 already decided.
            if (_continuesAPreviousPass) return;

            var container = _tableBox.HtmlContainer;
            if (container is not { HasRealPageGrid: true }) return;

            if (MonolithicContent.HasColumnPaginationExcludedFeature(_tableBox)) return;

            var columnCount = _bodyRows[0].Boxes.Count;
            if (columnCount == 0) return;

            // A "plain grid" table (no rowspan/colspan - already excluded above) is still not guaranteed to
            // have the same cell count in every row: an author can simply write fewer <td>s in one row, or
            // give a cell display:none in some rows but not others (a ragged row is already a reachable,
            // unpadded shape elsewhere in this engine - see InsertEmptyBoxes's own remarks). Every column
            // below is read positionally (_bodyRows[r].Boxes[c]), which silently reads the wrong cell - or
            // throws - for a short row, so bail out to the existing whole-table mover instead of guessing.
            for (var r = 1; r < placedRows; r++)
            {
                if (_bodyRows[r].Boxes.Count != columnCount) return;
            }

            var plannedDeltas = new double[columnCount];
            List<(int Slot, double Bottom)> pageBreaks = [];

            var appliedDelta = 0d;
            var currentSlot = container.SlotStartingAt(_bodyRows[0].Boxes[0].Location.Y);
            var previousColumnBottom = 0d;

            for (var c = 0; c < columnCount; c++)
            {
                var referenceCell = _bodyRows[0].Boxes[c];
                var columnTop = referenceCell.Location.Y + appliedDelta;
                var columnHeight = referenceCell.ActualBottom - referenceCell.Location.Y;

                if (HtmlContainerInt.FallsPast(columnTop + columnHeight, container.BandOfSlot(currentSlot)))
                {
                    if (c == 0)
                    {
                        // The table's own first column doesn't fit where the table currently starts -
                        // nothing has been placed on this page yet for a relocation to preserve, so this
                        // is an ordinary "does the whole box fit here" question, not a column-relocation
                        // one. Leave it to the existing whole-table mover.
                        return;
                    }

                    var nextSlot = currentSlot + 1;
                    if (columnHeight > container.PageBandHeightOf(nextSlot))
                    {
                        // No relocation can help a column taller than a whole fresh page - bail out of
                        // the entire pass, unmodified, per this method's own "plan, then commit" remarks.
                        return;
                    }

                    pageBreaks.Add((currentSlot, previousColumnBottom));

                    appliedDelta += container.PageTopOf(nextSlot) - columnTop;
                    currentSlot = nextSlot;
                    columnTop = container.PageTopOf(nextSlot);
                }

                plannedDeltas[c] = appliedDelta;
                previousColumnBottom = columnTop + columnHeight;
            }

            if (pageBreaks.Count == 0) return;

            for (var c = 0; c < columnCount; c++)
            {
                if (plannedDeltas[c] == 0) continue;

                for (var r = 0; r < placedRows; r++)
                {
                    _bodyRows[r].Boxes[c].OffsetTop(plannedDeltas[c]);
                }
            }

            // OffsetTop keeps every relocated cell's own ActualBottom in sync (a computed property), but
            // the table's own column-axis extent, and each row's, were both settled by Step 7/
            // SetRowGroupBoxDimensions before this pass ran and are real, stored fields - left alone they
            // still name the pre-relocation extent, understating how far the table's content now actually
            // reaches. That is not just a cosmetic gap: a following sibling is positioned from
            // _tableBox.ActualBottom (CssBox's own in-flow placement), so a stale value lands the next box
            // on top of the relocated column(s) rather than after them. plannedDeltas is monotonically
            // non-decreasing in c (appliedDelta only ever grows), and every column's own pre-relocation
            // ActualBottom is strictly increasing in c (columns stack forward) - so the last column always
            // carries both the largest delta and the table's true final column-axis edge, making
            // plannedDeltas[columnCount - 1] exactly what _tableBox's own trailing border/spacing epilogue
            // (added after the last column's content at Step 7) needs to shift by too. Each row is
            // recomputed from its own (now-relocated) cells instead, the same way AssignRowActualBounds
            // already does for the row loop itself, rather than assumed to share the table's one delta -
            // cheap, and it costs nothing to be exact rather than lean on the same derivation twice.
            _tableBox.ActualBottom += plannedDeltas[columnCount - 1];

            for (var r = 0; r < placedRows; r++)
            {
                _bodyRows[r].ActualBottom = _bodyRows[r].Boxes.Max(x => x.ActualBottom);
            }

            SetRowGroupBoxDimensions(placedRows);

            _tableBox.PageBreakBottoms ??= [];
            foreach (var (slot, bottom) in pageBreaks)
            {
                _tableBox.PageBreakBottoms[slot] = bottom;
            }
        }

        /// <summary>
        /// Real document-space X of vertical grid line <paramref name="line"/> (0..ColumnCount), read off
        /// any real cell anywhere in the table whose own edge is that line - independent of
        /// <see cref="_grid"/> (unlike <see cref="GetGridLineX"/>), so this works for a <c>separate</c>
        /// table too, since <see cref="SetColumnBoxDimensions"/> - CSS 2.1 §17.5.1 column/column-group
        /// background layering - is not itself a collapse-only feature. Null only when no row has a real
        /// cell on either side of the line (every row is shorter than it, a legitimately empty column).
        /// </summary>
        /// <param name="line">vertical grid line index, 0..ColumnCount</param>
        private double? GetColumnLineX(int line)
        {
            foreach (var row in _allRows)
            {
                foreach (var cell in row.Boxes)
                {
                    if (cell is CssSpacingBox) continue;

                    var col = GetCellRealColumnIndex(cell);
                    var span = GetColSpan(cell);

                    if (line == col) return cell.Location.X;
                    if (line == col + span) return cell.ActualRight;
                }
            }

            return null;
        }

        /// <summary>
        /// Gives every <c>&lt;col&gt;</c>/<c>&lt;colgroup&gt;</c> box a real
        /// <c>Location</c>/<c>ActualRight</c>/<c>ActualBottom</c> spanning its own column range's
        /// X-extent and the full row grid's Y-extent - the exact twin of
        /// <see cref="SetRowGroupBoxDimensions"/> for the same reason: these boxes are otherwise never
        /// laid out at all, so <c>CssBox</c>'s paint-time visibility-culling optimization (intersecting a
        /// still-default <c>(0,0,0,0)</c> <c>Bounds</c> against the current clip) silently drops them from
        /// painting - which, since neither box has painted anything at all until now, has hidden the fact
        /// that they never got real geometry in the first place. Once they have it, their own background
        /// paints through the ordinary <c>FragmentPainter</c> path with no new mechanism (their border
        /// paint still goes through <see cref="CssBox.CollapsedBorderSegments"/> like every other collapse
        /// participant, gated on <c>border-collapse: collapse</c> the same way) - CSS 2.1 §17.5.1's
        /// column-group-then-column-then-row-group-then-row-then-cell background layering falls out of
        /// DOM tree order for free, since a <c>&lt;colgroup&gt;</c> structurally always precedes its own
        /// <c>&lt;col&gt;</c> children and both always precede <c>&lt;tbody&gt;</c>/<c>&lt;tr&gt;</c>/
        /// <c>&lt;td&gt;</c>. Runs for every table, not just <c>collapse</c> ones - unlike border
        /// participation, background layering is an ordinary CSS 2.1 feature this gap affected regardless
        /// of <c>border-collapse</c>.
        /// </summary>
        private void SetColumnBoxDimensions()
        {
            if (_columns.Count == 0 || _allRows.Count == 0) return;

            var top = _allRows[0].Location.Y;
            var bottom = _allRows[^1].ActualBottom;

            var i = 0;
            while (i < _columns.Count)
            {
                var box = _columns[i];
                var j = i;
                while (j < _columns.Count && ReferenceEquals(_columns[j], box)) j++;

                var left = GetColumnLineX(i);
                var right = GetColumnLineX(j);
                if (left is { } l && right is { } r)
                {
                    box.Location = new RPoint(l, top);
                    box.ActualRight = r;
                    box.ActualBottom = bottom;
                }

                i = j;
            }

            // A <colgroup> with real <col> children never appears in _columns itself (only its children
            // do) - it still needs its own geometry, spanning the union of its children's columns, so its
            // own background paints beneath theirs.
            foreach (var box in _tableBox.Boxes)
            {
                if (box.DerivedStyle.ActualDisplay != Keywords.TableColumnGroup) continue;

                var childCols = box.Boxes.Where(c => c.DerivedStyle.ActualDisplay == Keywords.TableColumn).ToList();
                if (childCols.Count == 0) continue;

                var firstIndex = _columns.IndexOf(childCols[0]);
                var lastIndex = _columns.LastIndexOf(childCols[^1]);
                if (firstIndex < 0 || lastIndex < 0) continue;

                var left = GetColumnLineX(firstIndex);
                var right = GetColumnLineX(lastIndex + 1);
                if (left is { } l && right is { } r)
                {
                    box.Location = new RPoint(l, top);
                    box.ActualRight = r;
                    box.ActualBottom = bottom;
                }
            }
        }

        /// <summary>
        /// Sets Location/ActualRight/ActualBottom on every direct <c>&lt;tbody&gt;</c>
        /// (table-row-group) child of the table, spanning the bounding box of its own row
        /// children - see the call site's comment for why this is needed. &lt;thead&gt;/&lt;tfoot&gt;
        /// are unaffected: they're already explicitly positioned above (as _headerBox/_footerBox),
        /// since any present header/footer group is unconditionally treated as repeatable.
        /// </summary>
        /// <param name="placedRows">
        /// how many of <see cref="_bodyRows"/> have been placed, this pass or an earlier one. Rows past
        /// that belong to a fragmentainer no pass has filled yet and still sit at the origin, so spanning
        /// them would give the group a box starting above the table.
        /// </param>
        private void SetRowGroupBoxDimensions(int placedRows)
        {
            var placed = _bodyRows.Take(placedRows).ToHashSet();

            foreach (var box in _tableBox.Boxes)
            {
                if (box.DerivedStyle.ActualDisplay != Keywords.TableRowGroup)
                    continue;

                var rows = box.Boxes.Where(b => b.DerivedStyle.ActualDisplay == Keywords.TableRow && placed.Contains(b))
                    .ToList();
                if (rows.Count == 0)
                    continue;

                box.Location = new RPoint(rows.Min(r => r.Location.X), rows.Min(r => r.Location.Y));
                box.ActualRight = rows.Max(r => r.ActualRight);
                box.ActualBottom = rows.Max(r => r.ActualBottom);
            }
        }

        /// <summary>
        /// Grows <paramref name="cursor"/>'s own <see cref="TableRowCursor.MaxRight"/> - the column-axis
        /// tracker (physical Y for a vertical table, physical X otherwise; see the header/footer-repeat
        /// call sites this centralizes) - to include <paramref name="proxy"/>'s own column-axis far edge.
        /// </summary>
        /// <remarks>
        /// Every call site that grows <c>MaxRight</c> from a header/footer proxy's own geometry needs this
        /// same axis swap - reused rather than repeated, after one such site (Step 5's closing-footer arm)
        /// was found reading <c>ActualRight</c> unconditionally, corrupting a vertical table's own final
        /// column-axis extent (and, for a table with a footer, cascading into a row-axis corruption too,
        /// since that same arm also grows <c>MaxBottom</c> from the same proxy - see its own call site).
        /// </remarks>
        private void GrowMaxRightFor(TableRowCursor cursor, CssBox proxy)
        {
            cursor.MaxRight = Math.Max(cursor.MaxRight, _isVertical ? proxy.ActualBottom : proxy.ActualRight);
        }

        // A vertical table's row axis is physical X (ActualRight), the field CloseSpanningCell/
        // CloseRowSpanCellsEndingOnRow mutates later when it closes a rowspan cell opened on this row - so
        // capturing it from Boxes.Max here, before that happens, would freeze a stale value. rowAxisExtent
        // (the tracked cursor accumulator the caller passes in) is never mutated afterward and is exactly
        // what ReflectRowAxisForVerticalRl needs. The column axis (ActualBottom) is the mirror image:
        // stable per-cell from placement (ResolveOwnInlineSize), so Boxes.Max is safe there - the same
        // reasoning horizontal-tb already relies on, just on the other physical pair. slicedColumnAxisBottom
        // only applies to the horizontal-tb column axis: a sliced row (§4.3's last-resort fragmentainer
        // slice) keeps the bottom its own content reaches instead of the cursor's, and slicing is gated off
        // entirely for vertical tables (see LayoutBodyRow's own pagination pre-pass gate), so it never
        // applies on the _isVertical arm.
        private void AssignRowActualBounds(CssBox row, double rowAxisExtent, double? slicedColumnAxisBottom = null)
        {
            if (_isVertical)
            {
                row.ActualRight = rowAxisExtent;
                row.ActualBottom = row.Boxes.Max(x => x.ActualBottom);
            }
            else
            {
                row.ActualRight = row.Boxes.Max(x => x.ActualRight);
                row.ActualBottom = slicedColumnAxisBottom ?? rowAxisExtent;
            }
        }

        /// <summary>
        /// Mirrors every placed row's row-axis (physical X) position within the table's own now-final
        /// row-axis bounds - the correction every row was placed assuming it would <i>not</i> need,
        /// growing forward from the physical-min edge the way <c>vertical-lr</c> genuinely does. Called
        /// only for <c>vertical-rl</c>, once <paramref name="tableBox"/>'s own <see cref="CssBox.ActualRight"/>
        /// names the true row-axis-max edge every reflection is taken about. See the axis-mapping fields'
        /// own remarks for why a reflection pass, rather than placing rows backward to begin with, is the
        /// tractable fix. The caller must re-run <see cref="SetRowGroupBoxDimensions"/> afterward, since
        /// its bounding box was computed from each row's pre-reflection position.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Column-axis (physical Y) geometry is untouched - reflecting swaps which end of the row axis a
        /// box's own edges face, not its column-axis position, which vertical-rl and vertical-lr already
        /// share (both grow their columns top-to-bottom).
        /// </para>
        /// <para>
        /// Each row is moved through <see cref="CssBox.OffsetLeft(double)"/> - a real subtree translation,
        /// not a bare <c>Location</c>/<c>ActualRight</c> rewrite - because a row-axis reflection is
        /// equivalent, for the row itself, to a plain shift by a fixed delta: the row's own row-axis
        /// <i>extent</i> does not change, only its position within the table does. A non-spanning cell
        /// within a row shares that same row-axis footprint exactly, so letting <c>OffsetLeft</c> cascade
        /// the one delta down through the row's own <c>Boxes</c> moves every such cell, and every cell's
        /// own content (its words, line-box rectangles, and any nested boxes), by the same amount - a bare
        /// <c>Location</c>/<c>ActualRight</c> mutation on the row and cell boxes alone left their
        /// already-laid-out content behind at its pre-reflection position, which is what actually happened
        /// here before this fix: the row/cell rectangles moved but their text did not, so cell text painted
        /// outside the reflected cell's own bounds.
        /// </para>
        /// <para>
        /// A rowspan cell's own row-axis footprint spans multiple rows and does not coincide with any one
        /// row's own footprint, so reflecting it as a side effect of its opening row's cascade (the only
        /// thing that happens to it above) uses the wrong delta except when the span is exactly one row.
        /// The real cell object lives in its opening row's own <c>Boxes</c> (later rows hold only a bare
        /// <see cref="CssSpacingBox"/> placeholder standing in for it), so the row loop's cascade always
        /// reaches it - just with the wrong shift. Since <c>OffsetLeft</c> is a pure additive translation,
        /// composing the row loop's shift with a second, residual shift that corrects for the difference
        /// between the cell's own delta and its opening row's delta reaches the same target position a
        /// (hypothetical) independent reflection of the cell's own footprint would - snapshotting each
        /// spanning cell's pre-reflection footprint up front, before the row loop mutates anything, is what
        /// makes that residual computable afterward.
        /// </para>
        /// <para>
        /// A <c>&lt;thead&gt;</c>/<c>&lt;tfoot&gt;</c> with two or more rows is exactly the same shape of
        /// problem one level up: <paramref name="placedRows"/> carries the row-GROUP (<c>_headerBox</c>/
        /// <c>_footerBox</c>) as one entry, not its individual rows, so the main loop above gives the
        /// whole group one shared delta - correct for a single-row group (the row's own delta and the
        /// group's own delta are the same number), but a multi-row group's own rows then keep their
        /// forward-grown relative order instead of reversing the way an ordinary <c>&lt;tbody&gt;</c> row
        /// does (issue <see href="https://github.com/jhaygood86/PeachPDF/issues/784">#784</see>). The fix
        /// composes exactly like the rowspan case above: each internal row gets a residual
        /// (<c>rowDelta - groupDelta</c>) on top of the uniform delta it already received by cascading
        /// down from its group's own <c>OffsetLeft</c> - the row's total received shift becomes
        /// <c>groupDelta + residual == rowDelta</c>, the same delta an independently-reflected row would
        /// get. The same widened net catches a rowspan cell nested inside such a group too - one whose
        /// span is entirely contained within the group's own rows - since the rowspan-fixup scan above
        /// also walks each multi-row group's own rows' cells, not just <paramref name="placedRows"/>'s
        /// immediate entries (a row-GROUP's immediate <c>Boxes</c> are its <c>&lt;tr&gt;</c> children, not
        /// cells, so without this widening <c>GetRowSpan</c> could never find a real rowspan cell nested
        /// inside one).
        /// </para>
        /// <para>
        /// The residual above only fixes the <i>detached</i> row objects - what <c>GetGridLineY</c>/
        /// <c>GetGridLineX</c>/<c>EmitCollapsedBorderSegments</c> read directly off <c>TableGrid.RowAt</c>/
        /// <c>CellAt</c>. A repeating group's actually-painted content instead comes from its own
        /// <see cref="CssProxyBox.SourceGeometry"/> - a frozen <see cref="BoxGeometrySnapshot"/> captured
        /// in <see cref="CssProxyBox.PerformLayoutImp"/>, necessarily before this method's own
        /// <c>min</c>/<c>max</c> (the table's final row-axis bounds) are known - so the same residual is
        /// also applied to it directly, via <see cref="BoxGeometrySnapshot.ReflectSubtree"/>, once per
        /// row, for whichever proxy(s) in <paramref name="placedRows"/> repeat that row's own group
        /// (<c>ReferenceEquals(proxy.SourceBox, group)</c>). This is safe even though a snapshot may have
        /// been captured at a different page position than the group's own current (detached) state: the
        /// residual algebraically reduces to <c>(groupRight0 + groupLoc0) - (rowRight0 + rowLoc0)</c> (the
        /// <c>min</c>/<c>max</c> terms cancel), which is invariant under adding any shared constant to all
        /// four inputs - i.e. under any uniform translation of the whole group, which is exactly what
        /// distinguishes one page's own capture of the same relative row layout from another's, since
        /// <see cref="CssProxyBox.PerformLayoutImp"/> always moves every row of a group by one shared
        /// delta per page. (A vertical table cannot actually repeat its header/footer today -
        /// <c>_headerRepeats</c>/<c>_footerRepeats</c> require <c>!_isVertical</c>, since a vertical table
        /// is monolithic - so only one proxy per group is reachable in practice; the invariance still
        /// matters for the fix to be correct in general, and costs nothing extra to rely on.)
        /// </para>
        /// </remarks>
        private static void ReflectRowAxisForVerticalRl(CssBox tableBox, IEnumerable<CssBox> placedRows)
        {
            var min = tableBox.Location.X;
            var max = tableBox.ActualRight;

            // The one mirror-about-the-table's-midline formula every delta/residual below is built from -
            // shared so a future correction to it can't be applied to one copy and missed in the others.
            double Reflect(double loc0, double right0) => (min + max - right0) - loc0;

            var rows = placedRows as IReadOnlyCollection<CssBox> ?? placedRows.ToList();

            var rowGroupFixups = new List<(CssBox Row, CssBox Group, double RowLoc0, double RowRight0, double GroupLoc0, double GroupRight0)>();
            var rowToGroup = new Dictionary<CssBox, CssBox>();
            foreach (var group in rows)
            {
                if (group is CssProxyBox) continue;
                if (group.DerivedStyle.ActualDisplay is not (Keywords.TableHeaderGroup or Keywords.TableFooterGroup)) continue;

                // !IsRowCollapsed only - matches HeaderRowCountInGrid's/_allRows' own filter, i.e. what
                // TableGrid actually indexes (GetGridLineY/GetGridLineX's grid.RowAt reads), not the
                // stricter ActualDisplay == TableRow check Step 5's own row-layout loop uses for a
                // different reason (skipping non-row children while walking .Boxes directly).
                var groupRows = group.Boxes.Where(r => !IsRowCollapsed(r)).ToList();
                if (groupRows.Count < 2) continue;

                foreach (var row in groupRows)
                {
                    rowGroupFixups.Add((row, group, row.Location.X, row.ActualRight, group.Location.X, group.ActualRight));
                    // Which repeated-group each row-group-fixup row belongs to - a rowspan cell opening on
                    // one of these rows needs its own residual (below) propagated into that group's own
                    // proxy snapshot too, on top of the row-level residual, exactly like it needs both
                    // shifts applied to the live cell (see this method's own remarks on why the two compose
                    // rather than replace one another).
                    rowToGroup[row] = group;
                }
            }

            var rowspanFixups = new List<(CssBox Cell, CssBox Row, double CellLoc0, double CellRight0, double RowLoc0, double RowRight0)>();
            foreach (var row in rows.Concat(rowGroupFixups.Select(f => f.Row)))
            {
                foreach (var cell in row.Boxes)
                {
                    if (cell is CssSpacingBox || GetRowSpan(cell) <= 1) continue;
                    rowspanFixups.Add((cell, row, cell.Location.X, cell.ActualRight, row.Location.X, row.ActualRight));
                }
            }

            foreach (var row in rows)
            {
                var delta = Reflect(row.Location.X, row.ActualRight);
                if (delta != 0) row.OffsetLeft(delta);
            }

            var rowResiduals = new List<(CssBox Row, CssBox Group, double Residual)>();
            foreach (var (row, group, rowLoc0, rowRight0, groupLoc0, groupRight0) in rowGroupFixups)
            {
                var residual = Reflect(rowLoc0, rowRight0) - Reflect(groupLoc0, groupRight0);
                rowResiduals.Add((row, group, residual));
                if (residual != 0) row.OffsetLeft(residual);
            }

            var cellResiduals = new List<(CssBox Cell, CssBox? Group, double Residual)>();
            foreach (var (cell, row, cellLoc0, cellRight0, rowLoc0, rowRight0) in rowspanFixups)
            {
                var residual = Reflect(cellLoc0, cellRight0) - Reflect(rowLoc0, rowRight0);
                cellResiduals.Add((cell, rowToGroup.GetValueOrDefault(row), residual));
                if (residual != 0) cell.OffsetLeft(residual);
            }

            if (rowResiduals.Count > 0 || cellResiduals.Any(f => f.Group is not null))
            {
                var proxiesBySource = rows.OfType<CssProxyBox>().ToLookup(p => p.SourceBox);

                foreach (var (row, group, residual) in rowResiduals)
                {
                    if (residual == 0) continue;

                    foreach (var proxy in proxiesBySource[group]) proxy.SourceGeometry?.ReflectSubtree(row, residual);
                }

                // The row-level sync above already carried a rowspan cell's opening row's own residual
                // into the snapshot (ReflectSubtree recurses into the row's own cells) - but a rowspan
                // cell's own footprint differs from its opening row's, so it needs this second, on-top
                // residual applied directly to it too, exactly like the live cell above.
                foreach (var (cell, group, residual) in cellResiduals)
                {
                    if (residual == 0 || group is null) continue;

                    foreach (var proxy in proxiesBySource[group]) proxy.SourceGeometry?.ReflectSubtree(cell, residual);
                }
            }
        }

        /// <summary>
        /// Whether <see href="https://www.w3.org/TR/css-break-3/#break-between">§3.1</see>'s forced break
        /// falls at the class-A break point immediately before body row <paramref name="index"/>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Both sides of the break point are read, and through the chains they begin and end
        /// (<see cref="BreakPropagation"/>), so a <c>break-before</c> on a <c>&lt;tbody&gt;</c> is seen at
        /// its first row and a <c>break-after</c> on one at its last. The rows themselves are flattened
        /// into <c>_bodyRows</c> by <see cref="AssignBoxKinds"/>, so the row is the only box the engine
        /// places and therefore the only one that can act on the value.
        /// </para>
        /// <para>
        /// Asked in the page context because that is the only vehicle this engine has: it moves a row to
        /// the next page's content top, exactly as it already does when the row does not fit. A
        /// <c>column</c> value names a fragmentation context the table does not establish, and is left to
        /// the multi-column container the table may sit in.
        /// </para>
        /// <para>
        /// <c>break-inside: avoid</c> on a row needs nothing here: the engine never splits a row, so a row
        /// that would cross a boundary is already moved whole by the geometric test beside this one. The
        /// value is satisfied by construction rather than by being read.
        /// </para>
        /// </remarks>
        private bool ForcedBreakFallsBeforeRow(int index)
        {
            if (index <= 0) return false;

            // Read at each side's *anchor*: the value may sit on the row group the row begins or ends
            // rather than on the row itself, and the group is not a box this engine places, so the row is
            // where it has to be acted on. ForcedBreak*At then reads back down the chain, so both the
            // group's own value and the row's are seen.
            var before = BreakPropagation.AnchorForBreakBefore(_bodyRows[index]);
            var after = BreakPropagation.AnchorForBreakAfter(_bodyRows[index - 1]);

            return BreakPropagation.ForcedBreakBeforeAt(before, FragmentationContext.Page) is not null
                   || BreakPropagation.ForcedBreakAfterAt(after, FragmentationContext.Page) is not null;
        }

        /// <summary>
        /// Closes the table's slice in the band being filled and opens the next one: the footer proxy that
        /// band repeats, where the slice ended, the move onto the band after it, and the header proxy the
        /// new band repeats. Returns the band the cursor is now filling.
        /// </summary>
        /// <remarks>
        /// Shared by the row loop's two arms — the one that predicts the row will not fit and the one that
        /// has seen that it did not — so a break decided either way leaves the same record behind.
        /// </remarks>
        /// <param name="g">the graphics context layout is running against</param>
        /// <param name="container">the container whose page grid the bands come from</param>
        /// <param name="cursor">this pass's row cursor</param>
        /// <param name="slot">the band being filled, which the break closes</param>
        private async ValueTask<int> TakeBreakBeforeRow(
            RGraphics g, HtmlContainerInt container, TableRowCursor cursor, int slot)
        {
            // Start with the last body-row bottom; may be extended by the footer below.
            var pageBreakBottomY = cursor.MaxBottom;

            // Create footer proxy for current page
            if (_footerRepeats && _footerHeight > 0)
            {
                var footerY = CalculateFooterPositionAtPageBottom(container, cursor.CurrentY, slot);
                var footerProxy = CreateFooterProxy(footerY);
                if (footerProxy != null)
                {
                    await footerProxy.PerformLayout(g);
                    // Footer is part of this page's table slice — extend clip to cover it.
                    pageBreakBottomY = footerProxy.ActualBottom;
                }
            }

            // Record after footer so the border clip includes the footer area. Keyed by the band being
            // filled, which is where this slice bottom is: FragmentPainter reads the record by the
            // fragment's own fragmentainer index, so a key naming any other band pulls a table's bottom
            // border up on a page whose slice did not end there. That was the second half of #432 - the
            // counter named band 0 while the slice bottom lay five bands below it - and it is right by
            // construction now that the band is the one the cursor reached rather than the one it counted.
            _tableBox.PageBreakBottoms ??= new Dictionary<int, double>();
            _tableBox.PageBreakBottoms[slot] = pageBreakBottomY;

            // css-break-3 §4.3: the break moves the row to the *next* fragmentainer, which is the one
            // after the band the content already placed ends in. Named rather than derived from an offset
            // against CurrentY, which is what could come out negative.
            var target = slot + 1;
            cursor.MoveToSlot(target, container.PageTopOf(target), container);

            // Create new header proxy for new page. It takes no ResumeContentInset, and does not need one:
            // only a cell whose flow continues from an earlier fragmentainer owes the header its room, only
            // the row a continuation re-enters can hold one, and this arm never runs at that row.
            if (_headerRepeats && _headerHeight > 0)
            {
                var headerProxy = CreateHeaderProxy(cursor.CurrentY);
                if (headerProxy != null)
                {
                    await headerProxy.PerformLayout(g);
                    cursor.CurrentY += _headerHeight + VerticalSpacingAt(HeaderRowCountInGrid);
                    GrowMaxRightFor(cursor, headerProxy);
                }
            }

            cursor.MaxBottom = cursor.CurrentY;

            return target;
        }

        /// <summary>
        /// Takes the break before the table's closing footer where the band it would be drawn in has no
        /// room left for it, and opens the next one with the header this table repeats.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Only a footer <see href="https://www.w3.org/TR/css-tables-3/#repeated-headers">§6.2</see>
        /// declined to repeat can get here, and the reason is the declining: the room a repeating footer
        /// needs is reserved out of every band the table spans, so its last row stops clear of the foot,
        /// while a declined one is placed straight after a last row that may have run all the way down.
        /// Measured before this existed as <b>1.2pt past the band</b> on four consecutive word counts of a
        /// 21-document sweep, and independently by Windows CI on an ordinary 40-row table, where the
        /// footer came out claimed by two fragmentainers at once
        /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/518">#518</see>).
        /// </para>
        /// <para>
        /// Moving it whole is the only alternative to drawing it across the boundary: the group carries
        /// the UA stylesheet's <c>break-inside: avoid</c>, and even where an author has set that back to
        /// <c>auto</c> this engine cannot split a row group. Deliberately scoped to the declined case —
        /// a repeating footer's room is reserved by construction, and widening this to it would put a
        /// second decision on top of #493's placement rather than beside it.
        /// </para>
        /// <para>
        /// The three declines are the row loop's, for the same three reasons. A footer taller than the
        /// next band would only straddle again there (§4.3's ladder ends in leaving content where it is);
        /// a footer already at its band's top would leave that band empty (§4.4); and inside a
        /// multi-column column the page grid does not describe the fragmentainer being filled, so its
        /// bands answer nothing.
        /// </para>
        /// </remarks>
        /// <param name="g">the graphics context layout is running against</param>
        /// <param name="container">the container whose page grid the bands come from, or null</param>
        /// <param name="cursor">this pass's row cursor, sitting under the table's last row</param>
        private async ValueTask MoveTheClosingFooterOffABoundaryItWouldStraddle(
            RGraphics g, HtmlContainerInt? container, TableRowCursor cursor)
        {
            if (_footerRepeats) return;
            if (container is null || !container.HasRealPageGrid) return;
            if (container.CurrentFragmentainer is { HasOwnBand: true }) return;

            var slot = cursor.BandReached(container);
            var room = _footerHeight + VerticalSpacingAt(_grid?.RowCount ?? 0);

            if (!HtmlContainerInt.FallsPast(cursor.CurrentY + room, container.BandOfSlot(slot))) return;
            if (room > container.PageBandHeightOf(slot + 1)) return;
            if (cursor.CurrentY - HtmlContainerInt.PageBoundaryEpsilon <= container.PageTopOf(slot)) return;

            // Where the table's slice on the band being left ends, which is under its last row - the same
            // record TakeBreakBeforeRow writes, and for the same reason: FragmentPainter clips the table's
            // bottom border to it, and without the entry that border is drawn across the rows below it.
            _tableBox.PageBreakBottoms ??= new Dictionary<int, double>();
            _tableBox.PageBreakBottoms[slot] = cursor.MaxBottom;

            var target = slot + 1;
            cursor.MoveToSlot(target, container.PageTopOf(target), container);

            // §6.2 repeats the header on every page the table spans, and the page this break opens is one
            // of them - so the band the footer lands in gets a header too, exactly as one opened by a
            // break between two rows does.
            if (_headerRepeats && _headerHeight > 0)
            {
                var headerProxy = CreateHeaderProxy(cursor.CurrentY);

                if (headerProxy != null)
                {
                    await headerProxy.PerformLayout(g);
                    cursor.CurrentY += _headerHeight + VerticalSpacingAt(HeaderRowCountInGrid);
                    GrowMaxRightFor(cursor, headerProxy);
                }
            }

            cursor.MaxBottom = cursor.CurrentY;
        }

        /// <summary>
        /// Whether the row just placed crossed out of the band it began in and should be taken back and
        /// placed in the next one instead.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The row loop's other break arm predicts from <see cref="EstimateRowHeight"/>, one line of text
        /// per cell and blind to block content, so it misses any row whose height comes from a block. This
        /// asks the same question of what the row actually measured, which is only knowable here.
        /// </para>
        /// <para>
        /// Five things make it decline, and each is a case where moving the row would be worse than
        /// leaving it:
        /// <list type="bullet">
        /// <item><description>
        /// <b>A cell stopped.</b> The row is fragmenting rather than overflowing — it took its break where
        /// its own flow put it (css-tables-3 §6.1), and retracting that would retract a record a cell has
        /// already published.
        /// </description></item>
        /// <item><description>
        /// <b>The row begins the band.</b> Moving it would leave the fragmentainer empty, which
        /// §4.4 forbids — cited bare because <c>#break-between</c> is the property section this file
        /// links elsewhere as §3.1, and the rule is not there — and the next
        /// band would only cut it in the same place. A row taller than a whole band lands here, which is
        /// exactly the case <see href="https://github.com/jhaygood86/PeachPDF/issues/432">#432</see> is
        /// about: it stays, overflows, and the rows after it are placed below it rather than inside it.
        /// </description></item>
        /// <item><description>
        /// <b>The next band could not hold it either.</b> §4.3's relaxation ladder ends in leaving content
        /// where it is; moving a row that will straddle again walks it down the document one band per row.
        /// </description></item>
        /// <item><description>
        /// <b>The fragmentainer has a band of its own.</b> Inside a multi-column column the page grid does
        /// not describe the fragmentainer being filled, so its bands answer nothing here.
        /// </description></item>
        /// <item><description>
        /// <b>A cell already took a forced break of its own.</b> A block inside a <c>&lt;td&gt;</c> is
        /// ordinary block-flow content — <see cref="MonolithicContent.PaginatesItsOwnContent"/> does not
        /// name a table cell — so its <c>break-before</c>/<c>break-after</c> already placed it at the real
        /// next fragmentainer's top the first time the cell laid it out; the row is laid out at its true
        /// row-top coordinate, not a provisional one a later translation corrects, so that placement is
        /// already right. Retracting and re-placing the row asks
        /// <see cref="CssBox.ForcedBreakTopFor"/> the same question again from the new top and takes the
        /// same break a second time, walking the content one fragmentainer further than the value asked
        /// for (<see href="https://github.com/jhaygood86/PeachPDF/issues/512">issue #512</see>). Declining
        /// leaves the row's already-correct geometry alone — the interior gap this leaves between the two
        /// halves is the break's own page skip, not room a later band could still use.
        /// </description></item>
        /// </list>
        /// </para>
        /// <para>
        /// A row that <i>ends</i> a <c>rowspan</c> used to be a fifth decline, because moving it meant
        /// taking back geometry belonging to a cell of an earlier row
        /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/511">issue #511</see>). It is now
        /// moved like any other: <see cref="CloseSpanningCell"/> records what the row wrote to that cell so
        /// <see cref="TableRowCursor.Retract"/> can put it back, and fragments the cell rather than
        /// stretching it across the boundary the moved row opens.
        /// </para>
        /// </remarks>
        /// <param name="rowIndex">the body row just placed</param>
        /// <param name="container">the container whose page grid the bands come from, or null</param>
        /// <param name="cursor">this pass's row cursor, holding the row's real bottom</param>
        /// <param name="rowTop">where the row was placed</param>
        /// <param name="slot">the band it was placed in</param>
        /// <param name="pageHeight">
        /// this table's own effective page height - see <see cref="WillCrossPageBoundary"/>'s own remarks
        /// on why this, not <c>container.PageSize.Height</c>, is the check that actually says whether this
        /// table's row loop paginates at all.
        /// </param>
        private bool StraddleCorrectionAppliesTo(
            int rowIndex, HtmlContainerInt? container, TableRowCursor cursor, double rowTop, int slot, double pageHeight)
        {
            if (rowIndex <= ResumeRowIndex || cursor.Stopped) return false;
            if (container is null || pageHeight >= double.MaxValue - 1) return false;
            if (container.CurrentFragmentainer is { HasOwnBand: true }) return false;

            if (!HtmlContainerInt.FallsPast(cursor.MaxBottom, container.BandOfSlot(slot))) return false;

            if (rowTop - HtmlContainerInt.PageBoundaryEpsilon <= container.PageTopOf(slot)) return false;

            if (RowHoldsAnInternalForcedBreak(_bodyRows[rowIndex])) return false;

            return cursor.MaxBottom - rowTop <= RoomForARowIn(container, slot + 1);
        }

        /// <summary>
        /// Whether something inside <paramref name="row"/>'s own cells took a forced break of its own —
        /// see the fifth decline on <see cref="StraddleCorrectionAppliesTo"/> for why that rules out
        /// moving the row.
        /// </summary>
        /// <remarks>
        /// Not the row-level break <see cref="ForcedBreakFallsBeforeRow"/> reads: that is a value on the
        /// row (or a row group it begins) read <i>before</i> the row is placed. This is read <i>after</i>,
        /// off <see cref="CssBox.PlacedByForcedBreak"/>, which only a box block flow actually placed can
        /// have set — a cell itself never does, since the table engine positions cells directly rather
        /// than through <c>CssBox.PlaceBlockChild</c>.
        /// </remarks>
        private static bool RowHoldsAnInternalForcedBreak(CssBox row)
        {
            foreach (var cell in row.Boxes)
            {
                if (cell is not CssSpacingBox && BoxOrDescendantPlacedByForcedBreak(cell)) return true;
            }

            return false;
        }

        private static bool BoxOrDescendantPlacedByForcedBreak(CssBox box)
        {
            if (box.PlacedByForcedBreak) return true;

            foreach (var child in box.Boxes)
            {
                if (BoxOrDescendantPlacedByForcedBreak(child)) return true;
            }

            return false;
        }

        /// <summary>
        /// What a band leaves a body row once the groups this table repeats on every page have taken
        /// theirs.
        /// </summary>
        /// <param name="container">the container whose page grid the band comes from</param>
        /// <param name="slot">the band to measure</param>
        private double RoomForARowIn(HtmlContainerInt container, int slot) =>
            container.PageBandHeightOf(slot) - RepeatedFooterHeight - RepeatedHeaderRoom;

        /// <summary>
        /// One band of a run of content that crosses more than one: which band, where in it the content
        /// resumes, how much of the content that band holds, and how far the content's own box has to be
        /// displaced to draw that strip there.
        /// </summary>
        /// <param name="Slot">the band</param>
        /// <param name="ContentTop">where this band's strip begins, below the room a repeated header takes</param>
        /// <param name="Depth">how much of the run this band holds</param>
        /// <param name="DrawShift">
        /// what the run's own box is displaced by on this band — the accumulated depth of the gaps the
        /// repeated groups have opened above it. Zero on the band the content began in.
        /// </param>
        private readonly record struct SpannedBand(int Slot, double ContentTop, double Depth, double DrawShift);

        /// <summary>
        /// How a run of content <paramref name="depth"/> tall, starting at <paramref name="top"/>, falls
        /// across the bands below it once each of them has given the groups this table repeats their room.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The one place that answers "which bands does this cover, and where does it resume in each" —
        /// <see cref="StateSpanningCellContinuation"/> and the too-tall row's own bottom are two readings of
        /// the same question, and they may not disagree about what a repeated group costs. Every band after
        /// the first opens at <c>PageTopOf(slot) + RepeatedHeaderRoom</c> and closes at
        /// <c>PageBottomOf(slot) - RepeatedFooterHeight</c>, which is <see cref="RoomForARowIn"/> read as a
        /// pair of edges rather than a length.
        /// </para>
        /// <para>
        /// <b>A band must always take some of the run</b>
        /// (<see href="https://www.w3.org/TR/css-break-3/#possible-breaks">css-break-3 §4.3</see>: "it must
        /// place at least some content on each fragmentainer … in order to guarantee progress through the
        /// content"). §6.2's two quarter caps keep the groups under half a uniform band between them, so a
        /// band that leaves nothing is not reachable from that route — but per-<c>@page</c> geometry can
        /// make one band far shorter than the page the caps were measured against, and a band that took
        /// nothing would not terminate. Such a band gives the run its whole height and no room to the
        /// groups, which is the same trade §4.3 makes everywhere else: the drawn result loses to the
        /// alternative of never finishing.
        /// </para>
        /// </remarks>
        /// <param name="container">the container whose page grid the bands come from</param>
        /// <param name="top">where the run begins, in document space</param>
        /// <param name="depth">how tall the run is</param>
        private List<SpannedBand> BandsSpannedBy(HtmlContainerInt container, double top, double depth)
        {
            var slot = container.SlotStartingAt(top);
            var contentTop = top;
            var remaining = Math.Max(depth, 0);
            var shift = 0d;

            List<SpannedBand> bands = [];

            while (true)
            {
                var bandBottom = container.PageBottomOf(slot) - RepeatedFooterHeight;
                var room = bandBottom - contentTop;

                // §4.3's progress guard, above: a band that leaves this run nothing takes it whole instead.
                if (room <= 0)
                {
                    bandBottom = container.PageBottomOf(slot);
                    room = bandBottom - contentTop;
                }

                if (remaining <= room || room <= 0)
                {
                    bands.Add(new SpannedBand(slot, contentTop, remaining, shift));
                    return bands;
                }

                bands.Add(new SpannedBand(slot, contentTop, room, shift));

                remaining -= room;
                slot++;

                var resumesAt = container.PageTopOf(slot) + RepeatedHeaderRoom;

                // What the gap between the two strips costs the box that draws them: the run's own
                // coordinates are continuous, so the displacement is what makes the second strip pick up
                // exactly where the first stopped.
                shift += resumesAt - bandBottom;
                contentTop = resumesAt;
            }
        }

        /// <summary>
        /// Draws the groups this table repeats on every band its slice covers that has not already got
        /// them — the bands no break opened, which the three per-break sites cannot reach.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see href="https://www.w3.org/TR/css-tables-3/#repeated-headers">css-tables-3 §6.2</see> repeats
        /// the groups "on each page spanned by a table", and "a break was taken here" is a different
        /// question from "does this table's slice cover this band". They agree everywhere except a band a
        /// row overflows *through*, which is the one case no pass either fills or leaves — so the set is
        /// taken from <see cref="_bandsARowOverflowedInto"/> rather than from the table's own top and
        /// bottom, for the reason recorded there.
        /// </para>
        /// <para>
        /// <b>Which bands already have theirs is read off the proxies, not counted.</b> A counter would be
        /// wrong across passes for the reason <c>TableRowCursor.SlotIndex</c> was: a continuation inherits
        /// the proxies earlier passes placed (<c>TableOncePerTableTests.AContinuation_LeavesTheProxiesAnEarlierPassPlaced</c>)
        /// but not their bookkeeping, so anything it re-derived would draw a second header on a band that
        /// already had one. The proxies are the record, and asking them by
        /// <see cref="HtmlContainerInt.SlotStartingAt"/> is asking the same question
        /// <c>FragmentRegion.Contains</c> will ask of the same rectangle later.
        /// </para>
        /// <para>
        /// <b>The room is not reserved here, because it has already been given.</b> The bands this reaches
        /// are exactly the ones whose strips <see cref="SliceARowAcrossTheBandsItOverflows"/> stated, and
        /// those strips already begin below <see cref="RepeatedHeaderRoom"/> and end above
        /// <see cref="RepeatedFooterHeight"/>. Charging the band a second time here would open a gap the
        /// width of the group with nothing in it — the failure the "a repeated group's cost is charged to
        /// every band" invariant describes, in its other direction.
        /// </para>
        /// </remarks>
        /// <param name="g">the graphics context layout is running against</param>
        /// <param name="container">the container whose page grid the bands come from, null on a measurement run</param>
        /// <param name="cursor">this pass's row cursor; only its widest edge is advanced</param>
        private async ValueTask RepeatTheGroupsOnEveryBandTheTableSpans(
            RGraphics g, HtmlContainerInt? container, TableRowCursor cursor)
        {
            if (container is not { HasRealPageGrid: true }) return;
            if (container.CurrentFragmentainer is not { HasOwnBand: false }) return;

            // Not on a pass that stopped. The bands past where a mid-cell continuation gave up are not
            // bands this table *spans* yet - they are the ones it will be resumed into, and the pass that
            // resumes opens each of them with its own groups (#493, step 5a and the first header block).
            // Drawing them here closes pages this pass never reached: measured as an eighth footer on a
            // seven-page table, on a band with no slice bottom recorded to put it at.
            if (cursor.Stopped) return;

            var drawsAHeader = _headerRepeats && _headerHeight > 0;
            var drawsAFooter = _footerRepeats && _footerHeight > 0;

            if (!drawsAHeader && !drawsAFooter) return;

            if (_bandsARowOverflowedInto.Count == 0) return;

            var last = container.SlotEndingAt(cursor.MaxBottom);

            var headerBands = new HashSet<int>();
            var footerBands = new HashSet<int>();

            foreach (var proxy in _tableBox.Boxes.OfType<CssProxyBox>())
            {
                var slot = container.SlotStartingAt(proxy.Location.Y);

                if (proxy.DerivedStyle.ActualDisplay == Keywords.TableHeaderGroup) headerBands.Add(slot);
                else if (proxy.DerivedStyle.ActualDisplay == Keywords.TableFooterGroup) footerBands.Add(slot);
            }

            foreach (var slot in _bandsARowOverflowedInto.Order())
            {
                if (drawsAHeader && headerBands.Add(slot))
                {
                    var headerProxy = CreateHeaderProxy(container.PageTopOf(slot));

                    if (headerProxy != null)
                    {
                        await headerProxy.PerformLayout(g);
                        GrowMaxRightFor(cursor, headerProxy);
                    }
                }

                // Every band but the one the table ends in. That last one is closed by step 5's footer,
                // which is drawn under the final row rather than at the page foot and runs *after* this -
                // so a footer written here would be the second on that page. The header has no such twin:
                // a band the table's last row overflows into still opens with one.
                if (drawsAFooter && slot < last && footerBands.Add(slot))
                {
                    var footerProxy = CreateFooterProxy(
                        CalculateFooterPositionAtPageBottom(container, cursor.CurrentY, slot));

                    if (footerProxy != null)
                    {
                        await footerProxy.PerformLayout(g);

                        // Where this band's slice ends, for the same reason and in the same place as the
                        // other footer sites: FragmentPainter clips the table's bottom border to this
                        // record, and without it the border is drawn above the footer just placed under it.
                        //
                        // Deliberately inside the footer arm, as step 5a's is. A header-only table writes
                        // nothing here, so a band it merely spans does not turn
                        // CssBox.PaginatedItsOwnContentWithoutBreaking into "this table fragmented" - which
                        // would send it down a relocation path it has never taken.
                        _tableBox.PageBreakBottoms ??= new Dictionary<int, double>();
                        _tableBox.PageBreakBottoms[slot] = footerProxy.ActualBottom;

                        GrowMaxRightFor(cursor, footerProxy);
                    }
                }
            }
        }

        /// <summary>
        /// Slices <paramref name="row"/> across the bands it overflows, so each of them resumes it below
        /// the room the groups this table repeats take there, and grows the cursor's bottom by the gaps
        /// that opens.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The row is not resized and not moved. Content taller than a band is drawn once, from one
        /// <see cref="CssBox.Location"/>, and each fragmentainer already shows the strip of it
        /// that falls in that band — which is why a block taller than three pages slices correctly today
        /// with no machinery at all. What it cannot do unaided is <i>resume below</i> a repeated
        /// <c>&lt;thead&gt;</c>, because that makes the strips non-contiguous in document space. Each band
        /// is therefore given a displacement (<see cref="HtmlContainerInt.RecordFragmentDisplacement"/>)
        /// that puts its strip exactly where the previous one stopped.
        /// </para>
        /// <para>
        /// <b>The cursor's bottom has to grow with it.</b> The gaps are real depth on the page, so a row
        /// sliced across three bands ends lower than its own height says — and that bottom is what places
        /// every row after it, what <c>PageBreakBottoms</c> records, and what decides how many pages the
        /// document has. Leaving it at the un-sliced value put the rows after a tall row back inside its
        /// last strip.
        /// </para>
        /// <para>
        /// <b>Only where a group actually repeats.</b> With neither group repeating there is no room to
        /// leave, every band's strip is the whole band, and the displacements would all be zero — so the
        /// early return is what keeps this change confined to tables that repeat something, and is what
        /// makes it invisible to every fixture that does not.
        /// </para>
        /// </remarks>
        /// <param name="container">the container whose page grid the bands come from, null on a measurement run</param>
        /// <param name="row">the row being sliced</param>
        /// <param name="cursor">this pass's row cursor; its bottom is grown by the gaps</param>
        /// <param name="rowTop">where the row was placed</param>
        /// <returns>
        /// the bottom the row's own content reaches, where it was sliced — which is <b>not</b> the bottom
        /// it reaches on the page, since the gaps the repeated groups open lie between its strips. The
        /// cursor carries the latter, because that is what places the next row; the row's own box keeps
        /// the former, because a <c>background-image</c> or a <c>box-shadow</c> on a <c>&lt;tr&gt;</c>
        /// resolves against its unfragmented border box, and a box grown by the gaps renders stretched —
        /// the very failure this change is otherwise built to avoid. Null where nothing was sliced.
        /// </returns>
        private double? SliceARowAcrossTheBandsItOverflows(
            HtmlContainerInt? container, CssBox row, TableRowCursor cursor, double rowTop)
        {
            // The same two guards CloseSpanningCell states page-grid geometry behind: a measurement pass
            // has no grid to name, and a multi-column column is a fragmentainer the page grid cannot
            // describe.
            if (container is not { HasRealPageGrid: true }) return null;
            if (container.CurrentFragmentainer is not { HasOwnBand: false }) return null;

            if (RepeatedHeaderRoom <= 0 && RepeatedFooterHeight <= 0) return null;

            // A pass that stopped hands the rest of the table to a continuation, which places what is left
            // and opens each band with its own groups. Stating strips here would leave room on bands step
            // 5b then declines to draw anything on - the "gate both" rule, in the direction that leaves a
            // blank strip.
            if (cursor.Stopped) return null;

            // The row's *own* cells, not cursor.MaxBottom. MaxBottom is the lowest edge any cell placed so
            // far reached, and a rowspan cell reaches it only on the row that *ends* the span - a row that
            // does not contain it. Measured with a three-row span holding a 700pt block: the depth came out
            // as the spanning cell's, so the ending row (13pt of text) was the one displaced, the rows
            // actually holding the tall cell got no strips at all, and the table finished 26.4pt taller
            // than its content. A CssSpacingBox stands in for a cell an earlier row owns, so it is skipped
            // for the same reason.
            var ownBottom = double.MinValue;

            foreach (var cell in row.Boxes)
            {
                if (cell is CssSpacingBox) continue;

                ownBottom = Math.Max(ownBottom, cell.ActualBottom);
            }

            if (ownBottom <= double.MinValue) return null;

            var depth = ownBottom - rowTop;
            if (depth <= 0) return null;

            var bands = BandsSpannedBy(container, rowTop, depth);
            if (bands.Count < 2) return null;

            // Only a row §4.3 has run out of moves for - one taller than a whole band, so no page could
            // hold it and leaving it where it is was the last rung. A row that would fit the next band is
            // moved there by the straddle correction, and a table whose *first* row straddles is moved
            // whole by the epilogue's mover; slicing either draws it across pages it is about to vacate,
            // and the slice bottom recorded below then tells CssBox.PaginatedItsOwnContentWithoutBreaking
            // the table fragmented, which stops the move happening at all. Measured as a footer-only
            // table that should have moved to page 2 staying at Y=500.
            //
            // The same quantity the correction fits a row against, so the two cannot disagree about which
            // rows are movable.
            if (depth <= RoomForARowIn(container, bands[0].Slot + 1)) return null;

            // The bands step 5b owes the repeated groups to, recorded here because this is the only thing
            // that knows a row crossed one without a break falling on it. Including the band the row
            // *began* in: no break was taken there either, so while its header came from the table's own
            // top, nothing has closed it with the footer §6.2 repeats at the foot of every page the table
            // spans. Step 5b skips whatever is already drawn, so listing it costs nothing and leaving it
            // out measured as a footer on bands 1 and 2 of three.
            foreach (var band in bands)
            {
                _bandsARowOverflowedInto.Add(band.Slot);
            }

            // This run decides the row's strips again, so what an earlier one stated over the same slots
            // goes first - the same sweep, for the same reason, as DiscardContinuationShells'.
            container.ClearFragmentDisplacements(row);

            // The confinement is a block-axis question - it stops a displaced rectangle redrawing, under
            // the repeated header, the strip an earlier band already showed. So the inline axis is opened
            // a page's width either side of the row, which is wider than anything that can be drawn on the
            // page and therefore cannot cut a cell's horizontally-overflowing content. Taking the row's
            // own width instead would, and PageSize.Width is the *content* width, so it is narrower than
            // the row's own right edge and clipped the cell short.
            var margin = Math.Max(container.PageSize.Width, 1);
            var left = row.Boxes.Count > 0 ? row.Boxes.Min(b => b.Location.X) : _tableBox.Location.X;
            var right = row.Boxes.Count > 0 ? row.Boxes.Max(b => b.ActualRight) : _tableBox.ActualRight;

            foreach (var band in bands)
            {
                container.RecordFragmentDisplacement(
                    row, band.Slot, band.DrawShift,
                    new RRect(left - margin, band.ContentTop, right - left + 2 * margin, band.Depth));
            }

            // The cursor carries where the row really ends on the page - gaps included - because that is
            // what places the next row, records the slice bottom, and decides how many pages there are.
            // The row's own box does not: see the returns doc above.
            var last = bands[^1];
            cursor.MaxBottom = last.ContentTop + last.Depth;

            return ownBottom;
        }

        /// <summary>
        /// Discards the continuation geometry an earlier run of this table's row loop stated, from
        /// <paramref name="fromSlot"/> on or — with no slot — in every slot.
        /// </summary>
        /// <param name="container">the container holding this layout's emitter, null on a measurement run</param>
        /// <param name="fromSlot">the first slot this run decides again, or null for all of them</param>
        private void DiscardContinuationShells(HtmlContainerInt? container, int? fromSlot)
        {
            if (container is null) return;

            foreach (var row in _bodyRows)
            {
                // A row's own strips are stated per band exactly as a finished cell's shell is, and are
                // re-decided by the same runs, so they are swept by the same walk rather than a parallel
                // one - see SliceARowAcrossTheBandsItOverflows.
                container.ClearFragmentDisplacements(row, fromSlot);

                foreach (var cell in row.Boxes)
                {
                    container.ClearContinuationShells(cell, fromSlot);
                }
            }
        }

        /// <summary>
        /// Lays one row's cells out at <paramref name="cursor"/>'s current position, advancing the
        /// cursor's <see cref="TableRowCursor.MaxRight"/>/<see cref="TableRowCursor.MaxBottom"/> over them.
        /// </summary>
        /// <param name="g">the graphics device to measure with</param>
        /// <param name="row">the row whose cells are being placed</param>
        /// <param name="startX">the table's own content left edge, where each row's first cell starts</param>
        /// <param name="cursor">where the row loop has got to; read and advanced</param>
        private async ValueTask LayoutBodyRow(RGraphics g, CssBox row, double startX, TableRowCursor cursor)
        {
            var currentY = cursor.CurrentY;
            var rowIndex = cursor.RowIndex;
            var rowSpannedBoxes = cursor.RowSpannedBoxes;

            var currentX = startX;
            var rowMaxBottom = cursor.MaxBottom;
            var rowMaxRight = cursor.MaxRight;

            // The next grid column this row's own Boxes list is expected to reach, absent any gap - see
            // the skip-forward below for why this can fall behind columnIndex.
            var expectedColumn = 0;

            // The cells of this row that ran out of fragmentainer, which the two steps below have to leave
            // alone: a cell whose content continues elsewhere has no leftover room in this fragment to
            // distribute, and its box does not describe its content.
            var stoppedCells = new List<CssBox>();

            // The cells of this row that an earlier fragmentainer finished. css-tables-3 §6.1 has their
            // boxes continue with the row's, so this row states the geometry they occupy here - nothing
            // can read it off them, because a continuation deliberately leaves their one Location naming
            // the fragmentainer that placed them, and a cell that finished is indistinguishable from one
            // no pass entered by geometry alone.
            var finishedCells = new List<(CssBox Cell, double Left, double Width)>();

            foreach (var cell in row.Boxes)
            {
                var rowSpan = GetRowSpan(cell);
                var columnIndex = GetCellRealColumnIndex(cell);

                if (columnIndex >= _columnWidths!.Length)
                    break;

                var colSpan = GetColSpan(cell);
                var width = GetCellWidth(columnIndex, cell);

                // A rowspan cell opened by an earlier row of this same row-group can occupy a column this
                // row's own Boxes list has no entry for at all - no CssSpacingBox placeholder stands in
                // for it, since InsertEmptyBoxes never pads a detached header's/footer's own rows (see
                // GetCellRealColumnIndex's own remarks, issue #740). currentX otherwise only ever advances
                // one Boxes-list entry at a time and would never account for that column's width, so any
                // gap between where the row loop expected to be and this cell's real column is walked here
                // first - the same width-plus-trailing-spacing a real cell in that column would have
                // advanced by, one column at a time.
                for (var skipped = expectedColumn; skipped < columnIndex; skipped++)
                {
                    currentX += _columnWidths![skipped];
                    currentX += IsColumnCollapsed(skipped) ? 0 : HorizontalSpacingAt(skipped + 1);
                }
                expectedColumn = columnIndex + colSpan;

                // A cell contributes no border-spacing slot of its own when the last column it spans
                // is collapsed - GetWidthSum leaves that same slot out of the table's own width. This
                // is keyed on the span's last column rather than CellOccupiesOnlyCollapsedColumns so a
                // colspan cell straddling a collapsed and a visible column (issue #667) is handled too:
                // the slot is owed only when the cell's own trailing edge lands on a visible column.
                var spacingAfterCell = IsColumnCollapsed(columnIndex + colSpan - 1)
                    ? 0
                    : HorizontalSpacingAt(columnIndex + colSpan);

                // A cell an earlier pass finished has its whole content in the fragment that pass emitted,
                // so this one places nothing for it: not its position, not its content, not its alignment.
                // Only the column cursor moves on, which is what keeps the cells beside it in their
                // columns. The distinction this rests on is the record's - see TableRowCursor.FinishedCells
                // for why a finished cell and one no pass entered cannot be told apart without it.
                if (cursor.FinishedOnAnEarlierPass(cell))
                {
                    // Still finished, and this pass's own record has to say so - see CarryForwardFinished
                    // for what a record that forgets costs.
                    cursor.CarryForwardFinished(cell);

                    // A rowspan placeholder reaches this arm too - LayoutBodyRow records every cell it
                    // enters as finished or not, spacers included - and states nothing here. It is
                    // constructed with a bare tag and never inherits style, so it has no border or
                    // background for a continuation to draw; the geometry §6.1 wants belongs to the cell
                    // it stands in for, which lives in an earlier row and continues on its own terms.
                    if (cell is not CssSpacingBox)
                        finishedCells.Add((cell, currentX, width));

                    rowMaxRight = Math.Max(rowMaxRight, currentX + width);
                    currentX += width + spacingAfterCell;
                    continue;
                }

                // Where a cell that continues an earlier fragment goes is the same question
                // CssBox.ResumeInTheNextFragmentainer answers for every other box, and it has the same two
                // answers. On the page grid the cell keeps the one Location its first fragment was built
                // from: a box has exactly one, so writing this pass's row top into it retracts that
                // fragment's geometry - and the emitter, told the box moved, rebuilds the fragmentainer
                // from where the box is now and finds nothing of it there. Measured as a whole table
                // disappearing from the page it began on. Where this pass's own content goes is the flow's
                // question, which CssLayoutEngine.CreateLineBoxes already answers from the fragmentainer's
                // own content top.
                //
                // Inside a fragmentainer with a band of its own - a multi-column column - the cell does
                // move, for the reason that method gives: columns differ in exactly the axis the page grid
                // holds constant, so a continuation left where it was would be laid out over the fragment
                // it just left. The row cursor is already in the column's own space there, so the ordinary
                // placement below is the right one.
                if (!cursor.ResumedFromAnEarlierPass(cell)
                    || _tableBox.HtmlContainer?.CurrentFragmentainer is { HasOwnBand: true })
                {
                    // currentX is always the column axis (physical Y for a vertical table), currentY
                    // always the row axis (physical X) - swapped into the correct RPoint slot here, the
                    // one place logical and physical coordinates actually meet. See the axis-mapping
                    // fields' own remarks for why this assumes forward growth even for vertical-rl.
                    cell.Location = _isVertical ? new RPoint(currentY, currentX) : new RPoint(currentX, currentY);
                }

                // width is the cell's column-axis extent (from _columnWidths[], physical Y for a vertical
                // table) - ActualBottom for a vertical table, ActualRight otherwise.
                if (_isVertical) cell.ActualBottom = cell.Location.Y + width;
                else cell.ActualRight = cell.Location.X + width;

                // A cell an earlier pass stopped part-way through continues from its own record rather
                // than from the start - the cells of one row are §2.1 parallel flows, so each has its own
                // stopping point and the ones that finished are simply not in the carried list.
                if (cursor.CarriedTokenFor(cell) is { } carried) cell.ResumeAt(carried, null);

                // This cell's position is this engine's own decision, not block flow's - the same #166
                // boundary that stops a break value travelling out of a cell (BreakPropagation.CanTravelOutOf)
                // - so the flag says the same thing to its epilogue that CssLayoutEngineFlex/Grid's own
                // commit pass already says to a flex/grid item's (ItemContentCommit.CommitLayout): the §4.3
                // movers (avoid/monolithic, widows/orphans, the keep-with-next retry) all answer "this box
                // does not fit, so lay it out again somewhere else" by re-running its own content layout in
                // place - CssBox.ResolveBlockChildOffset never actually moves a table-cell-display box, so
                // the retry cannot honour what it decided, only repeat the layout at the same top. Every
                // <td>'s UA-default overflow:hidden (CssDefaults) already makes MonolithicContent.IsMonolithic
                // true for it, so without this every cell whose content reaches a page boundary took that
                // retry - silently dropping a forced break inside it the second time through, since the
                // break's own one-shot latch (CssBox.PlacedByForcedBreak) was already spent by the first run
                // (issue #512).
                cell.PositionAssignedByEngine = true;
                try
                {
                    await cell.PerformLayout(g);
                }
                finally
                {
                    cell.PositionAssignedByEngine = false;
                }

                // A CssSpacingBox is built with a bare "none" tag and never inherits style (its own doc
                // comment), so its own WritingMode stays at the CSS initial value (horizontal-tb) even
                // inside a vertical table - PerformLayout's ordinary auto-height-from-empty-content
                // resolution then collapses the ActualBottom set above right back down to Location.Y,
                // since for a horizontal-tb box that field is the one auto-resolved-from-content, not the
                // engine-controlled row-axis extent it is here. Harmless for horizontal-tb, where the same
                // resolution instead targets ActualRight - a field this loop never pre-sets before layout
                // in the first place, so there is nothing for it to clobber. Re-asserting is safe: a
                // spacer's own geometry is entirely engine-controlled (it has no content of its own to
                // lay out), so there is nothing PerformLayout could have legitimately changed here. Found
                // by rendering a real vertical-rl table with a rowspan cell and looking at the result - a
                // row-axis-degenerate spacer put the row after the spanned one back at the spanned column's
                // own position instead of after it, corrupting every collapsed-border segment whose span
                // depended on that row's own geometry (GetGridLineY reads grid.RowAt/CellAt directly).
                if (_isVertical && cell is CssSpacingBox) cell.ActualBottom = cell.Location.Y + width;

                // Did this cell finish? Asked here because here is the only place the answer exists: a
                // box's record is cleared at the start of its next layout, and the engine's whole-table
                // pre-checks can restart this loop over the same cells. Recorded, not acted on - the loop
                // still places every remaining row, which is what keeps this step behaviour-neutral while
                // the engine runs with the fragmentainer detached and no cell can answer yes.
                cursor.RecordIfUnfinished(cell);

                // A cell that stopped never reached the line that would have set its own ActualBottom
                // (CssLayoutEngine.CreateLineBoxes returns on the break before it), so the box still holds
                // the pre-flow value its placement gave it - its own top. Two steps below read that as the
                // cell's height: the row's own MaxBottom, and the vertical alignment, which distributes
                // (box bottom - content bottom) and so pushes a whole fragment's worth of lines *up* out
                // of the fragmentainer being filled. Measured with the monolithic gate lifted: a 244-word
                // <td> put its first line 104pt above the document origin and emitted 121 of its words.
                //
                // What the cell's fragment is worth here is where its content actually reached, and there
                // is no leftover room to distribute at all - a cell that continues elsewhere overfills its
                // fragment by definition, which is why the alignment is skipped rather than re-based.
                if (cell.PendingBreakToken is not null)
                {
                    stoppedCells.Add(cell);
                    cell.ActualBottom = Math.Max(cell.ActualBottom, CssBox.GetMaximumBottom(cell, 0d));
                }

                // Track max bottom
                if (cell is CssSpacingBox sb)
                {
                    if (sb.EndRow == rowIndex)
                    {
                        // rowMaxBottom is the row's own row-axis extent (physical X for a vertical
                        // table, matching the non-spanning case's own _isVertical branch right below) -
                        // ActualRight, not ActualBottom, is that extent there too.
                        rowMaxBottom = Math.Max(rowMaxBottom, _isVertical ? sb.ExtendedBox.ActualRight : sb.ExtendedBox.ActualBottom);
                    }
                }
                else
                {
                    switch (rowSpan)
                    {
                        case 1:
                            // rowMaxBottom tracks the row's own row-axis extent (physical X for a
                            // vertical table, since rows run along physical X) - ActualRight, not
                            // ActualBottom, is that extent there.
                            rowMaxBottom = Math.Max(rowMaxBottom, _isVertical ? cell.ActualRight : cell.ActualBottom);
                            break;
                        case > 1:
                            {
                                // Same mapping InsertEmptyBoxes' placeholders use (GetEffectiveEndRowIndex),
                                // so a span crossing a collapsed row closes on the same row here as the
                                // CssSpacingBox that stands in for it there (issue #665).
                                var endRow = GetEffectiveEndRowIndex(rowIndex, rowSpan);
                                if (!rowSpannedBoxes.TryGetValue(endRow, out var rowSpannedBoxesForRow))
                                {
                                    rowSpannedBoxesForRow = [];
                                    rowSpannedBoxes[endRow] = rowSpannedBoxesForRow;
                                }
                                rowSpannedBoxesForRow.Add(cell);
                                break;
                            }
                    }
                }

                // rowMaxRight/currentX track the column-axis extent within this row (physical Y for a
                // vertical table) - ActualBottom, not ActualRight, is that extent there.
                var cellColumnAxisEdge = _isVertical ? cell.ActualBottom : cell.ActualRight;
                rowMaxRight = Math.Max(rowMaxRight, cellColumnAxisEdge);
                currentX = cellColumnAxisEdge + spacingAfterCell;
            }

            // Vertical alignment
            IEnumerable<CssBox> boxesToVerticallyAlign = row.Boxes;
            if (rowSpannedBoxes.TryGetValue(rowIndex, out var boxesThatEndOnRow))
            {
                boxesToVerticallyAlign = boxesToVerticallyAlign.Union(boxesThatEndOnRow);
            }

            // A spanning cell whose own content overflows into this row's own band has to raise the
            // row for every cell in it, not just itself - ordinary (unpaginated) table layout grows
            // every row a tall rowspan cell spans, and the loop below would otherwise give this row's
            // other cells only rowMaxBottom, the smaller value TableRowCursor.MaxBottom tracking
            // deliberately settled for without the spanning cell (needed so the straddle correction can
            // still move this row - see RecordForeignWrite). Asked before that loop runs, and before
            // CloseSpanningCell answers the same question for the spanning cell itself, so every cell
            // this row aligns sees the one, final rowMaxBottom.
            //
            // !_isVertical: SpanningCellBandGeometry reads cell.Location.Y/GetMaximumBottom, both the
            // physical-Y page-band axis - meaningless for a vertical table, whose own row axis is
            // physical X and has no relationship to the page grid's physical-Y bands. Same scope boundary
            // CloseSpanningCell's own pagination arm below already draws for the identical reason (#762).
            if (!_isVertical
                && boxesThatEndOnRow is { Count: > 0 }
                && _tableBox.HtmlContainer is { HasRealPageGrid: true } alignmentContainer
                && alignmentContainer.CurrentFragmentainer is { HasOwnBand: false })
            {
                var slot = cursor.SlotIndex;

                foreach (var spanningCell in boxesThatEndOnRow)
                {
                    if (cursor.FinishedOnAnEarlierPass(spanningCell)) continue;

                    var (cellSlot, contentBottom, contentSlot) =
                        SpanningCellBandGeometry(spanningCell, alignmentContainer);

                    if (cellSlot < slot && contentSlot >= slot)
                    {
                        rowMaxBottom = Math.Max(rowMaxBottom, contentBottom);
                    }
                }
            }

            foreach (var cell in boxesToVerticallyAlign)
            {
                // Nothing of this cell belongs to this fragmentainer, so neither does its geometry: giving
                // it this fragment's bottom and re-aligning against that would drag the content an earlier
                // pass emitted down onto this page. Exempted when the cell is ending its span on *this*
                // row (boxesThatEndOnRow): TableRowCursor._carriedFinished is seeded once, when a resumed
                // pass re-enters this cell's *opening* row, and is never cleared for the rest of that
                // pass - so a cell finished trivially (by its own content, or by an unrelated sibling
                // whose own resumption re-ran the whole row) on that earlier row still reads as finished
                // here, several rows later, on the one row that actually has to close it. Without the
                // exemption CloseSpanningCell is never entered for it at all (issue #593).
                if (cursor.FinishedOnAnEarlierPass(cell) && !(boxesThatEndOnRow?.Contains(cell) ?? false)) continue;

                if (cell is CssSpacingBox spacer)
                {
                    if (spacer.EndRow == rowIndex)
                    {
                        CloseSpanningCell(g, spacer.ExtendedBox, cursor, rowMaxBottom);
                    }
                }
                else if (boxesThatEndOnRow?.Contains(cell) ?? false)
                {
                    // Asked before the resumed/stopped arm below, and that order is load-bearing rather
                    // than incidental: a cell reaching here belongs to an *earlier* row, so
                    // TableRowCursor.ResumedFromAnEarlierPass can still answer true for it - matched by
                    // reference against a carried record this pass's own resumption seeded while placing
                    // that earlier, opening row, several rows before this one. The record is never cleared
                    // once that row consumes it, so asking the resumed/stopped question first read that
                    // stale match and skipped closing the cell's box entirely for the rest of the pass -
                    // measured as CloseSpanningCell never once entered for a cell whose own content took
                    // more than one resumption pass to finish (issue #521's exact shape).
                    //
                    // skipFinishedGuard: true - the guard above already exempted this cell from the same
                    // stale FinishedOnAnEarlierPass question for the same reason (issue #593); asking it
                    // again inside CloseSpanningCell would just re-apply the stale answer and silently
                    // no-op the close instead of skipping it, which is worse.
                    CloseSpanningCell(g, cell, cursor, rowMaxBottom, skipFinishedGuard: true);
                }
                else if (stoppedCells.Contains(cell) || cursor.ResumedFromAnEarlierPass(cell))
                {
                    // Only a cell whose fragment both opens and closes in this fragmentainer has leftover
                    // room of its own to distribute. One that continues elsewhere overfills its fragment by
                    // definition, and one that *continues an earlier* fragment had where its content sits
                    // settled by the pass that opened it - re-aligning that drags content an earlier
                    // fragmentainer has already emitted onto this page, which is a whole line drawn on two
                    // pages rather than a cosmetic shift. Asked after the CssSpacingBox and boxesThatEndOnRow
                    // arms rather than beside the FinishedCells guard above, because a spacer that stopped
                    // still has to align the cell it stands in for, and a spanning cell ending this row must
                    // reach CloseSpanningCell even where it also - falsely, for this row - answers resumed.
                    continue;
                }
                else if (GetRowSpan(cell) == 1)
                {
                    // rowMaxBottom is the row's own settled row-axis extent (physical X for a vertical
                    // table - see its own tracking above), so growing the cell to match is ActualRight
                    // there, not ActualBottom.
                    if (_isVertical) cell.ActualRight = rowMaxBottom;
                    else cell.ActualBottom = rowMaxBottom;
                    CssLayoutEngine.ApplyCellVerticalAlignment(g, cell, _isVertical);
                }
            }

            cursor.MaxRight = rowMaxRight;
            cursor.MaxBottom = rowMaxBottom;

            // css-tables-3 §6.1: the row's box continues into this fragmentainer and every cell's box
            // continues with it, so a cell that finished earlier is its borders and background running the
            // full depth of the row's fragment here, with no content in it. Stated only once the row's own
            // bottom is settled, which is the line above - a finished cell adds no height of its own, so
            // the depth is the one the cells that do continue decided.
            //
            // The column comes from the cursor rather than from the cell's stale ActualRight: on the page
            // grid the two agree, and inside a multi-column column they do not - the cell's Location.X
            // names the column its first fragment was in, while currentX names the one this pass is
            // filling, which is where a continuation with a band of its own belongs.
            //
            // cursor.SlotIndex rather than the row loop's own derived band, and they are the same thing:
            // TableRowCursor.BandReached writes the field before the loop asks anything of it. Nothing
            // here has to be undone when a placement is retracted, either - a shell is stated only for a
            // cell an *earlier pass* finished, those cells are named by the carried record, and a record
            // names only the row a continuation re-enters, which is the one row never retracted.
            if (finishedCells.Count > 0 && rowMaxBottom > currentY
                && _tableBox.HtmlContainer is { } htmlContainer)
            {
                foreach (var (cell, left, cellWidth) in finishedCells)
                {
                    htmlContainer.RecordContinuationShell(
                        cell, cursor.SlotIndex, new RRect(left, currentY, cellWidth, rowMaxBottom - currentY));
                }
            }
        }

        /// <summary>
        /// Closes a cell whose <c>rowspan</c> ends on the row now being placed — a cell belonging to an
        /// <b>earlier</b> row — over the depth that row reached.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Where the span stays inside one band this gives the cell the row's bottom, which is what a
        /// <c>rowspan</c> means. Where it does not, the cell's box is closed at the foot of the band it was
        /// placed in and <b>fragmented</b> instead, its remaining depth stated as continuation geometry in
        /// each later band (<see cref="StateSpanningCellContinuation"/>). Stretching one box across the
        /// boundary drew its borders and background straight through the page edge, which is what
        /// <see href="https://github.com/jhaygood86/PeachPDF/issues/511">issue #511</see> is about — and it
        /// reached that state by three different routes (the straddle correction declining, the row loop's
        /// prediction breaking before a row in the middle of a span, and a forced break declared on one),
        /// which is why the question is asked of the <i>bands</i> here rather than of whichever arm broke.
        /// </para>
        /// <para>
        /// <see href="https://www.w3.org/TR/css-tables-3/#breaking-rules">css-tables-3 §6.1</see> names
        /// both halves of this. A row is to be preserved unfragmented only "if the cells spanning the row
        /// do not span any subsequent row" — so the row that <i>ends</i> a span is moved whole, which is
        /// what the straddle correction now does, while a row in the middle of one is
        /// <i>freely fragmentable</i> and the remaining height goes to its cells. Either way the spanning
        /// cell itself continues rather than travelling, and both other engines agree: Gecko re-reflows it
        /// against the remaining block-size in <c>nsTableRowGroupFrame::SplitSpanningCells</c>, and Blink
        /// treats each cell as its own §2.1 parallel flow. Neither keeps the spanned rows together, which
        /// is why the run is not relocated with the row that opened it.
        /// </para>
        /// <para>
        /// The cell's <i>content</i> needs no separate continuation of its own here: a spanning cell whose
        /// content did not fit the band it was placed in stopped and resumed there like any other box,
        /// through the table's ordinary per-cell continuation
        /// (<see cref="TableRowCursor.UnfinishedCells"/>/<see cref="TableRowCursor.Continuation"/>), so its
        /// real fragments already exist in every band it actually occupies by the time its ending row is
        /// placed. What this method closes is only the cosmetic question of where the cell's <i>box</i> —
        /// its background and border — is treated as ending, which used to be declined wherever the cell's
        /// own content also reached past the band it opened in, out of a since-resolved concern that
        /// stating continuation geometry there would displace that real content
        /// (<see href="https://github.com/jhaygood86/PeachPDF/issues/521">issue #521</see>): a stated
        /// shell is consulted only for a band where nothing real was found at all
        /// (<c>FragmentEmitter.ShellIn</c>), so it can never stand in for a band the cell's content already
        /// occupies. The anchor band for both the close and the table's own slice-bottom record is
        /// therefore wherever the content itself ends (<see cref="SpanningCellBandGeometry"/>'s
        /// <c>ContentSlot</c>), not always <c>cellSlot</c> — the two coincide exactly when the content
        /// fits the band it opened in, which is #511's original, narrower case.
        /// </para>
        /// <para>
        /// Where the content's own band reaches the row ending the span (or, in principle, past it),
        /// there is no later, empty band left to state a shell over — and no <c>PageBreakBottoms</c> entry
        /// may be created for that band either, unlike every other slice-bottom write here: <c>slot</c> is
        /// the band the row loop is still <i>filling</i>, and content the loop places there after this row
        /// would then read as below a slice bottom recorded before the band actually closed. This
        /// asymmetry cost a real regression to find: an earlier draft of this fix wrote one anyway, and a
        /// table with further rows in the same band had its bottom border clipped through the middle of
        /// itself, with those later rows rendered outside it entirely.
        /// </para>
        /// <para>
        /// Every write here is recorded on the cursor
        /// (<see cref="TableRowCursor.RecordForeignWrite"/>), because this is geometry the row does not
        /// own: <see cref="TableRowCursor.Retract"/> takes back what the row added to the cursor and
        /// <c>PassRewind.RollBackTo</c> resets the row's own boxes, and the spanning cell is neither.
        /// Without it the straddle correction could not move this row at all.
        /// </para>
        /// </remarks>
        /// <param name="g">the graphics context layout is running against</param>
        /// <param name="cell">the spanning cell, a child of the row that opened the span</param>
        /// <param name="cursor">
        /// this pass's row cursor, which names the band the row is in and records the write
        /// </param>
        /// <param name="rowMaxBottom">
        /// the bottom the row being placed reached — already raised, before this is called, to the
        /// content-based bottom <see cref="LayoutBodyRow"/>'s own pre-pass computed for this same cell, if
        /// its content reaches into this same band
        /// </param>
        /// <param name="skipFinishedGuard">
        /// true when the caller already asked (and answered) the same <c>FinishedOnAnEarlierPass</c>
        /// question for this exact call - the <c>boxesThatEndOnRow</c> dispatch arm, whose own outer guard
        /// exempts a cell ending its span on this row from that stale check (issue #593). The
        /// <c>CssSpacingBox</c> arm never sets this: its outer guard tests the spacer box, not the cell it
        /// stands in for, so this internal check is the only thing protecting that route from a genuinely
        /// finished cell's geometry.
        /// </param>
        private void CloseSpanningCell(RGraphics g, CssBox cell, TableRowCursor cursor, double rowMaxBottom,
            bool skipFinishedGuard = false)
        {
            // The same cell arrives twice on a row that reaches it both as a CssSpacingBox's ExtendedBox
            // and through RowSpannedBoxes, and the alignment below composes rather than settling, so it may
            // only run once. Asked of the cursor's own record rather than of a set kept beside it: the
            // record is written for every cell closed here and cleared per row, so it already is the
            // answer. A rowspan cell ends on exactly one row, so the row-group measurement cursors - which
            // place rows without ever calling BeginRow - cannot dedupe one row's cell against another's.
            if (cursor.AlreadyWroteTo(cell)) return;

            // Nothing of a cell an earlier pass finished belongs to this fragmentainer, so neither does
            // its geometry. The loop's own guard above tests the CssSpacingBox, not the cell it stands in
            // for, so that route reaches here with a finished cell - unless skipFinishedGuard says the
            // caller already settled this question for a cell ending its span on the current row.
            if (!skipFinishedGuard && cursor.FinishedOnAnEarlierPass(cell)) return;

            var previousBottom = _isVertical ? cell.ActualRight : cell.ActualBottom;
            var bottom = rowMaxBottom;
            var container = _tableBox.HtmlContainer;
            var slot = cursor.SlotIndex;

            // Two fragmentainers answer nothing about where this cell has to close, and the difference
            // between them matters. One with a band of its own - a multi-column column - is not described
            // by the page grid. And a *detached* one names no fragmentainer at all: that is the
            // measurement pass a flex or grid item's layout runs behind, at a provisional position it is
            // about to be translated away from, so a close decided there would state continuation
            // geometry at coordinates nothing ends up at and no later run sweeps.
            //
            // A vertical table's row loop never fragments internally - its constructor forces pageHeight
            // to double.MaxValue for exactly this (see the axis-mapping fields' own remarks) - so this
            // whole block's band/slot reasoning has nothing to apply to: rowMaxBottom here is the row
            // axis (physical X), which has no analog in the page grid's physical-Y bands this block
            // walks. Skipping it, rather than axis-converting logic that fundamentally describes a
            // physical-Y pagination question, is the correct scope boundary - real per-cell pagination of
            // a vertical table's rowspan cells is tracked with the rest of Table's remaining
            // writing-mode gaps (#762).
            if (!_isVertical
                && container is { HasRealPageGrid: true }
                && container.CurrentFragmentainer is { HasOwnBand: false })
            {
                var (cellSlot, contentBottom, contentSlot) = SpanningCellBandGeometry(cell, container);
                var band = cellSlot < slot ? container.BandOfSlot(cellSlot) : default;

                if (cellSlot < slot
                    && HtmlContainerInt.FallsPast(rowMaxBottom, band))
                {
                    if (contentSlot >= slot)
                    {
                        // The content reaches into the very band the row ending the span is in (or, in
                        // principle, past it), so there is no later, empty band left to state a shell
                        // over - this band's own content-bearing fragment already carries the box's real
                        // bounds, and the close is simply however far the row - or the content, wherever
                        // it reaches lower - actually goes (rowMaxBottom already carries the content's own
                        // bottom where LayoutBodyRow's pre-pass found it reaching further, so this Math.Max
                        // is mostly belt: it still matters when this is the only spanning cell the row
                        // loop has not yet accounted for). No PageBreakBottoms write here - see this
                        // method's own remarks for why slot may not take one.
                        bottom = Math.Max(rowMaxBottom, contentBottom);
                    }
                    else
                    {
                        // Where the table's own slice on the band the content itself ends in stops, which
                        // is what the cell has to close level with: FragmentPainter clips the table's
                        // bottom border to the same record, so a cell closing below it is a tint drawn
                        // past the table's own edge. Bounded by the foot of the band less the room a
                        // repeated <tfoot> holds there, since that record includes the footer, and never
                        // taken above the cell's own content.
                        bottom = container.PageBottomOf(contentSlot) - RepeatedFooterHeight;

                        if (_tableBox.PageBreakBottoms?.TryGetValue(contentSlot, out var sliceBottom) is true)
                            bottom = Math.Min(bottom, sliceBottom);

                        bottom = Math.Max(bottom, contentBottom);

                        // And the slice follows the cell where the cell's own content is what reaches
                        // lowest in that band. MaxBottom never counts a spanning cell before the row that
                        // ends it, so a tall cell opened by a short row closes below the record its own
                        // table wrote - and the bottom border clipped to that record would then be drawn
                        // across the cell. contentSlot, not cellSlot: the content may now reach several
                        // bands past the one the span opened in, and cellSlot's own record has nothing to
                        // do with a close that belongs there instead.
                        if (_tableBox.PageBreakBottoms is { } bottoms
                            && bottoms.TryGetValue(contentSlot, out var recorded) && bottom > recorded)
                        {
                            bottoms[contentSlot] = bottom;
                        }

                        StateSpanningCellContinuation(container, cell, contentSlot, slot, rowMaxBottom);
                    }
                }
            }

            // bottom is the row-axis extent this cell's span closes at (physical X for a vertical
            // table, matching every other row-axis-tracking site in this file) - ActualRight, not
            // ActualBottom, is that extent there.
            if (_isVertical) cell.ActualRight = bottom;
            else cell.ActualBottom = bottom;

            var applied = CssLayoutEngine.ApplyCellVerticalAlignment(g, cell, _isVertical);

            cursor.RecordForeignWrite(cell, previousBottom, applied);

            // A header-opened cell crossing into the body (issue #788) closes here, for the first time,
            // strictly after any header proxy already created for this table captured its own snapshot at
            // the cell's natural, not-yet-closed height (LayoutBodyRows creates the header proxy before
            // its own row loop runs) - resync every such proxy now, or its painted output shows the cell
            // cut short at the header's own bottom despite the live box (this method's own work above)
            // already being correct. A proxy created after this point (a later page's repeat) needs no
            // resync of its own: it captures the already-closed live geometry directly.
            if (_headerRowSpansCrossingIntoBody.ContainsKey(cell))
            {
                ResyncHeaderProxiesFor(cell);
            }
        }

        /// <summary>
        /// A spanning cell's own band geometry: the band its box opened in, how far its own content
        /// actually reaches (<see cref="CssBox.GetMaximumBottom"/>), and which band that content itself
        /// ends in.
        /// </summary>
        /// <remarks>
        /// Shared between <see cref="LayoutBodyRow"/>'s own pre-pass - which asks whether a spanning
        /// cell's content reaches into the row's own band <i>before</i> any of that row's other cells are
        /// given their height, so the row is raised for everyone rather than only for the spanning cell
        /// itself - and <see cref="CloseSpanningCell"/>, which asks the same two questions again to decide
        /// how its own box closes and where the table's own slice-bottom record belongs. One shared
        /// computation, so the two can never disagree about what a cell's content reaches.
        /// </remarks>
        private static (int CellSlot, double ContentBottom, int ContentSlot) SpanningCellBandGeometry(
            CssBox cell, HtmlContainerInt container)
        {
            var cellSlot = container.SlotStartingAt(cell.Location.Y);
            var contentBottom = CssBox.GetMaximumBottom(cell, 0d);
            var contentSlot = container.SlotEndingAt(contentBottom);

            return (cellSlot, contentBottom, contentSlot);
        }

        /// <summary>
        /// States the geometry a fragmented <c>rowspan</c> cell occupies in every band after the one its
        /// box closes in, down to the bottom the row ending the span reached.
        /// </summary>
        /// <remarks>
        /// One rectangle per band, because a span can cross more than one: a whole band for each it passes
        /// through, and the row's own bottom for the one it ends in. Each starts below the room that band
        /// leaves a repeated <c>&lt;thead&gt;</c>, and a whole band's worth is
        /// <see cref="RoomForARowIn"/> — the same quantity the straddle correction fits a row against, so
        /// the two cannot disagree about what a repeated group costs.
        /// </remarks>
        /// <param name="container">the container holding this layout's emitter</param>
        /// <param name="cell">the spanning cell whose box was closed in <paramref name="fromSlot"/></param>
        /// <param name="fromSlot">the band the cell's own box ends in</param>
        /// <param name="toSlot">the band the row ending the span is in</param>
        /// <param name="rowMaxBottom">the bottom that row reached</param>
        private void StateSpanningCellContinuation(
            HtmlContainerInt container, CssBox cell, int fromSlot, int toSlot, double rowMaxBottom)
        {
            var left = cell.Location.X;
            var width = cell.ActualRight - left;

            if (width <= 0) return;

            for (var s = fromSlot + 1; s <= toSlot; s++)
            {
                var top = container.PageTopOf(s) + RepeatedHeaderRoom;
                var height = s == toSlot ? rowMaxBottom - top : RoomForARowIn(container, s);

                if (height > 0)
                {
                    container.RecordContinuationShell(cell, s, new RRect(left, top, width, height));
                }
            }
        }

        /// <summary>
        /// Gets the spanned width of a cell (With of all columns it spans minus one).
        /// </summary>
        private double GetSpannedMinWidth(CssBox row, int realColumnIndex, int colspan)
        {
            double w = 0f;
            for (var i = realColumnIndex; i < row.Boxes.Count || i < realColumnIndex + colspan - 1; i++)
            {
                if (i < GetColumnMinWidths().Length)
                    w += GetColumnMinWidths()[i];
            }
            return w;
        }

        /// <summary>
        /// The real column <paramref name="cell"/> occupies - <see cref="_columnPlacements"/>'s
        /// rowspan-occupancy-aware placement (see <see cref="ComputeColumnPlacements"/>'s own remarks for
        /// why summing a row's own preceding <c>Boxes</c> isn't enough for a detached header/footer row -
        /// issue #740). A <see cref="CssSpacingBox"/> has no placement of its own -
        /// <see cref="InsertEmptyBoxes"/> only ever creates one after <see cref="_columnPlacements"/> is
        /// built - so it stands in for <see cref="CssSpacingBox.ExtendedBox"/>, whose column it shares.
        /// </summary>
        private int GetCellRealColumnIndex(CssBox cell)
        {
            var target = cell is CssSpacingBox spacer ? spacer.ExtendedBox : cell;
            return _columnPlacements![target].Column;
        }

        /// <summary>
        /// Gets the cells width, taking colspan and being in the specified column
        /// </summary>
        /// <remarks>
        /// Already axis-correct for a vertical table with no fix needed: <c>_columnWidths</c> is indexed
        /// by the column axis regardless of orientation (see <see cref="CellInlineSize"/>'s own
        /// height-not-width convention), colspan is inherently a column/inline-axis concept in
        /// css-tables-3, and <see cref="ReflectRowAxisForVerticalRl"/> never touches the column axis - so
        /// this sum has no row-axis interaction to straddle in the first place. Reviewed as part of #762.
        /// </remarks>
        /// <param name="column"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        private double GetCellWidth(int column, CssBox b)
        {
            var colspanInt = GetColSpan(b);
            double colspan = Convert.ToSingle(colspanInt);
            double sum = 0f;

            for (int i = column; i < column + colspan; i++)
            {
                if (column >= _columnWidths!.Length)
                    break;
                if (_columnWidths.Length <= i)
                    break;
                sum += _columnWidths[i];
            }

            // Border-spacing strictly between the spanned columns - not the blanket (colspan - 1) *
            // spacing, since a boundary immediately after a collapsed column never exists (issue #667:
            // a colspan cell straddling a collapsed and a visible column). See GetInteriorSpacing.
            sum += GetInteriorSpacing(column, colspanInt);

            return sum;
        }

        /// <summary>
        /// Gets the colspan of the specified box
        /// </summary>
        /// <param name="b"></param>
        internal static int GetColSpan(CssBox b)
        {
            var att = b.GetAttribute("colspan", "1");

            return !int.TryParse(att, out var colspan) ? 1 : colspan;
        }

        /// <summary>
        /// Gets the rowspan of the specified box
        /// </summary>
        /// <param name="b"></param>
        internal static int GetRowSpan(CssBox b)
        {
            var att = b.GetAttribute("rowspan", "1");

            return !int.TryParse(att, out var rowSpan) ? 1 : rowSpan;
        }

        /// <summary>
        /// Recursively measures words inside the box
        /// </summary>
        /// <param name="box">the box to measure</param>
        /// <param name="g">Device to use</param>
        private static async ValueTask MeasureWords(CssBox box, RGraphics g)
        {
            foreach (var childBox in box.Boxes)
            {
                if (childBox.DerivedStyle.ActualDisplay == Keywords.None) continue;

                await childBox.MeasureWordsSize(g);
                await MeasureWords(childBox, g);
            }
        }

        /// <summary>
        /// Tells if the columns widths can be reduced,
        /// by checking the minimum widths of all cells
        /// </summary>
        /// <returns></returns>
        private bool CanReduceWidth()
        {
            for (var i = 0; i < _columnWidths!.Length; i++)
            {
                if (CanReduceWidth(i))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Tells if the specified column can be reduced,
        /// by checking its minimum width
        /// </summary>
        /// <param name="columnIndex"></param>
        /// <returns></returns>
        private bool CanReduceWidth(int columnIndex)
        {
            if (_columnWidths!.Length >= columnIndex || GetColumnMinWidths().Length >= columnIndex)
                return false;
            return _columnWidths[columnIndex] > GetColumnMinWidths()[columnIndex];
        }

        /// <summary>
        /// Gets the available width for the whole table.
        /// It also sets the value of WidthSpecified
        /// </summary>
        /// <returns></returns>
        /// <remarks>
        /// The table's width can be larger than the result of this method, because of the minimum 
        /// size that individual boxes.
        /// </remarks>
        private double GetAvailableTableWidth()
        {
            // "Available table width" here means the table's own extent along its column axis - the
            // inline axis, physical Y under a vertical writing mode - which reads the table's Height
            // property (still physically Y, per CSS: writing-mode never changes what Width/Height mean)
            // rather than Width. See ComputeAxisMapping's own remarks for why this is the same pattern
            // Flexbox's _mainAxisIsPhysicalX-gated Width/Height reads use, just for a table's column
            // (always-inline) axis rather than a flex container's main axis (which flex-direction can
            // point at either logical axis).
            var inlineSizeCss = _isVertical ? _tableBox.Height : _tableBox.Width;
            var containingBlockInlineSize = _isVertical ? _tableBox.ContainingBlock.Size.Height : _tableBox.ContainingBlock.Size.Width;

            CssLength tableBoxLength = new(inlineSizeCss);

            if (!(tableBoxLength.Number > 0)) return containingBlockInlineSize;

            _widthSpecified = true;
            return CssValueParser.ParseLength(inlineSizeCss, containingBlockInlineSize, _tableBox);

        }

        /// <summary>
        /// Gets the available width for the whole table.
        /// It also sets the value of WidthSpecified
        /// </summary>
        /// <returns></returns>
        /// <remarks>
        /// The table's width can be larger than the result of this method, because of the minimum
        /// size that individual boxes.
        /// </remarks>
        private double GetMaxTableWidth()
        {
            // See GetAvailableTableWidth's own remarks - MaxWidth/MaxHeight are still physical
            // properties; only which one plays the "column axis" role changes with writing-mode.
            var maxInlineSizeCss = _isVertical ? _tableBox.MaxHeight : _tableBox.MaxWidth;
            var parentAvailableInlineSize = _isVertical
                ? _tableBox.ParentBox!.ActualBoxSizingHeight - _tableBox.ParentBox.ActualBorderTopWidth
                    - _tableBox.ParentBox.ActualPaddingTop - _tableBox.ParentBox.ActualPaddingBottom - _tableBox.ParentBox.ActualBorderBottomWidth
                : _tableBox.ParentBox!.AvailableWidth;

            var tblen = new CssLength(maxInlineSizeCss);
            if (tblen.Number > 0)
            {
                _widthSpecified = true;
                return CssValueParser.ParseLength(maxInlineSizeCss, parentAvailableInlineSize, _tableBox);
            }
            else
            {
                return 9999f;
            }
        }

        /// <summary>
        /// Calculate the min and max width for each column of the table by the content in all rows.<br/>
        /// the min width possible without clipping content<br/>
        /// the max width the cell content can take without wrapping<br/>
        /// </summary>
        /// <param name="onlyNans">if to measure only columns that have no calculated width</param>
        /// <param name="minFullWidths">return the min width for each column - the min width possible without clipping content</param>
        /// <param name="maxFullWidths">return the max width for each column - the max width the cell content can take without wrapping</param>
        private void GetColumnsMinMaxWidthByContent(bool onlyNans, out double[] minFullWidths, out double[] maxFullWidths)
        {
            maxFullWidths = new double[_columnWidths!.Length];
            minFullWidths = new double[_columnWidths.Length];

            // CssBox.GetMinMaxWidth/GetMinimumWidth measure a box's own content-intrinsic size by
            // walking its (horizontal) word wrapping - there is no writing-mode-aware equivalent that
            // measures a vertical-writing-mode cell's own inline-axis (physical Y) content extent the
            // way CreateVerticalLineBoxes lays it out. Building one is real, separate feature work
            // (tracked as a follow-up), not a mechanical remap, so a vertical table's auto (unspecified-
            // width) columns fall back to splitting the available inline space evenly instead: min=0,
            // max=+Infinity for every applicable column skips this method's whole content-measurement
            // loop and lets DetermineMissingColumnWidths' own "spread extra width between all columns"
            // step (which needs no content-based bound to work) do the actual distribution.
            if (_isVertical)
            {
                for (var i = 0; i < maxFullWidths.Length; i++) maxFullWidths[i] = double.PositiveInfinity;
                return;
            }

            var availCellWidth = GetAvailableCellWidth();

            foreach (var row in _allRows)
            {
                for (var i = 0; i < row.Boxes.Count; i++)
                {
                    var cell = row.Boxes[i];
                    var realCol = GetCellRealColumnIndex(cell);
                    var colSpan = GetColSpan(cell);

                    // A cell that lands entirely inside collapsed column(s) is itself invisible (CSS 2.1
                    // §17.6.1), so its content must not push those columns' width up before
                    // CollapseColumnWidths zeroes them - left in, EnforceMinimumSize's colspan-neighbor
                    // adjustment would narrow the *next* column to compensate for a width the collapsed
                    // column never actually needs (issue #665). A cell straddling a collapsed and a
                    // visible column still contributes normally.
                    if (CellOccupiesOnlyCollapsedColumns(realCol, colSpan)) continue;

                    var col = _columnWidths.Length > realCol ? realCol : _columnWidths.Length - 1;

                    if ((onlyNans && !double.IsNaN(_columnWidths[col])) || i >= row.Boxes.Count) continue;
                    cell.GetMinMaxWidth(out var minWidth, out var maxWidth);

                    // Clamp by the cell's own CSS min-width/max-width, if explicitly set, so a cell
                    // can cap or raise the column's content-driven bounds independent of its content.
                    if (CssValueParser.IsValidLength(cell.MaxWidth))
                    {
                        maxWidth = Math.Min(maxWidth, CssValueParser.ParseLength(cell.MaxWidth, availCellWidth, cell));
                    }
                    if (cell.MinWidth != "0" && CssValueParser.IsValidLength(cell.MinWidth))
                    {
                        minWidth = Math.Max(minWidth, CssValueParser.ParseLength(cell.MinWidth, availCellWidth, cell));
                    }
                    maxWidth = Math.Max(maxWidth, minWidth);

                    // Divide across the span's *visible* columns only (issue #667) - a straddling cell
                    // is guaranteed at least one by the CellOccupiesOnlyCollapsedColumns skip above, and
                    // dividing by the raw colSpan would understate the one visible column's fair share
                    // of content the collapsed column(s) it also spans will never carry.
                    var visibleSpanColumns = 0;
                    for (var j = 0; j < colSpan; j++)
                    {
                        if (!IsColumnCollapsed(col + j)) visibleSpanColumns++;
                    }

                    minWidth /= visibleSpanColumns;
                    maxWidth /= visibleSpanColumns;

                    for (var j = 0; j < colSpan; j++)
                    {
                        if (IsColumnCollapsed(col + j)) continue;
                        minFullWidths[col + j] = Math.Max(minFullWidths[col + j], minWidth);
                        maxFullWidths[col + j] = Math.Max(maxFullWidths[col + j], maxWidth);
                    }
                }
            }
        }

        /// <summary>
        /// Gets each column's explicit CSS max-width, if any cell in that column has one set.
        /// Columns with no explicit max-width are uncapped (<see cref="double.PositiveInfinity"/>),
        /// distinct from <see cref="GetColumnsMinMaxWidthByContent"/>'s intrinsic content-based max,
        /// so that columns without an explicit max-width still fill available table width normally.
        /// </summary>
        private double[] GetColumnExplicitMaxWidths()
        {
            var explicitMaxWidths = new double[_columnWidths!.Length];
            for (var i = 0; i < explicitMaxWidths.Length; i++)
                explicitMaxWidths[i] = double.PositiveInfinity;

            var availCellWidth = GetAvailableCellWidth();

            foreach (var row in _allRows)
            {
                foreach (var cell in row.Boxes)
                {
                    var cellInlineMaxSize = CellInlineMaxSize(cell);
                    if (!CssValueParser.IsValidLength(cellInlineMaxSize)) continue;

                    var col = GetCellRealColumnIndex(cell);
                    col = explicitMaxWidths.Length > col ? col : explicitMaxWidths.Length - 1;
                    var colSpan = GetColSpan(cell);
                    var cellMaxWidth = CssValueParser.ParseLength(cellInlineMaxSize, availCellWidth, cell) / colSpan;

                    for (var j = 0; j < colSpan && col + j < explicitMaxWidths.Length; j++)
                        explicitMaxWidths[col + j] = Math.Min(explicitMaxWidths[col + j], cellMaxWidth);
                }
            }

            return explicitMaxWidths;
        }

        /// <summary>
        /// Gets the width available for cells
        /// </summary>
        /// <returns></returns>
        /// <remarks>
        /// It takes away the cell-spacing from <see cref="GetAvailableTableWidth"/>
        /// </remarks>
        private double GetAvailableCellWidth()
        {
            return GetAvailableTableWidth() - SumHorizontalSpacing() - TableInlineBorderStart - TableInlineBorderEnd;
        }

        /// <summary>
        /// Gets the current sum of column widths
        /// </summary>
        /// <returns></returns>
        private double GetWidthSum()
        {
            double f = 0f;

            foreach (var t in _columnWidths!)
            {
                if (double.IsNaN(t))
                    throw new Exception("CssTable Algorithm error: There's a NaN in column widths");
                else
                    f += t;
            }

            // Take cell-spacing - one border-spacing slot per column boundary (columnCount + 1 of
            // them), minus one slot for every collapsed column: a collapsed column contributes
            // neither its own width (already zeroed by CollapseColumnWidths) nor a border-spacing
            // slot of its own, matching LayoutBodyRow's cursor advance not spacing past it either.
            // Without this a table with a collapsed column measured one border-spacing unit wider
            // than a table genuinely built with one fewer column.
            f += SumHorizontalSpacingExcludingCollapsedColumns();

            //Take table borders
            f += TableInlineBorderStart + TableInlineBorderEnd;

            return f;
        }

        /// <summary>
        /// Gets the span attribute of the tag of the specified box
        /// </summary>
        /// <param name="b"></param>
        private static int GetSpan(CssBox b)
        {
            var f = CssValueParser.ParseNumber(b.GetAttribute("span"), 1);

            return Math.Max(1, Convert.ToInt32(f));
        }

        /// <summary>
        /// Gets the minimum width of each column
        /// </summary>
        private double[] GetColumnMinWidths()
        {
            if (_columnMinWidths != null) return _columnMinWidths;
            _columnMinWidths = new double[_columnWidths!.Length];

            var availCellWidth = GetAvailableCellWidth();

            foreach (var row in _allRows)
            {
                foreach (var cell in row.Boxes)
                {
                    var colspan = GetColSpan(cell);
                    var col = GetCellRealColumnIndex(cell);

                    // See the matching skip in GetColumnsMinMaxWidthByContent (issue #665): a cell
                    // confined to collapsed column(s) must not size those columns up from its own
                    // (invisible) content.
                    if (CellOccupiesOnlyCollapsedColumns(col, colspan)) continue;

                    // A straddling cell's own leftover min-width belongs to the last *visible* column
                    // it spans (issue #667) - walking back off a trailing collapsed column, since that
                    // column is about to be zeroed by CollapseColumnWidths regardless of what's assigned
                    // to it here. CellOccupiesOnlyCollapsedColumns above guarantees at least one visible
                    // column exists in the span, so this always finds one at or before col.
                    var affectColumn = Math.Min(col + colspan, _columnMinWidths.Length) - 1;
                    while (affectColumn > col && IsColumnCollapsed(affectColumn)) affectColumn--;

                    var spannedWidth = GetSpannedMinWidth(row, col, colspan) + GetInteriorSpacing(col, colspan);

                    // CssBox.GetMinimumWidth has no vertical-writing-mode-aware equivalent - see
                    // GetColumnsMinMaxWidthByContent's own remarks - so a vertical table's content-
                    // driven minimum falls back to 0 (only an explicit min-width/min-height still
                    // constrains the column) rather than measuring horizontal word-wrap content that
                    // does not describe this cell's own (vertical) flow.
                    var cellMinWidth = _isVertical ? 0 : cell.GetMinimumWidth();
                    var cellInlineMinSize = CellInlineMinSize(cell);
                    if (cellInlineMinSize != "0" && CssValueParser.IsValidLength(cellInlineMinSize))
                    {
                        cellMinWidth = Math.Max(cellMinWidth, CssValueParser.ParseLength(cellInlineMinSize, availCellWidth, cell));
                    }

                    _columnMinWidths[affectColumn] = Math.Max(_columnMinWidths[affectColumn], cellMinWidth - spannedWidth);
                }
            }

            return _columnMinWidths;
        }

        /// <summary>
        /// The gap between <c>ClientLeft</c> and the first column's own start (<c>startX</c>) - zero
        /// under <c>collapse</c>, unlike every other use of <see cref="HorizontalSpacingAt"/> at line 0:
        /// <c>ClientLeft</c> already reserves the table's own half of the outer edge via
        /// <see cref="DerivedStyle.SetCollapsedUsedBorderWidths"/>, so adding
        /// <c>HorizontalSpacingAt(0)</c> (also half that same width, negative) here would double-count
        /// it. Every other line-0/line-ColumnCount use pairs the spacing term with a matching border-width
        /// term that cancels it the same way (<c>_tableBox.ActualRight</c>'s
        /// <c>tableRight + HorizontalSpacingAt(columnCount) + ActualBorderRightWidth</c>, and
        /// <c>contentBottom</c>/<c>gridBorderBoxBottom</c>'s two-statement equivalent on the row axis) -
        /// <c>startX</c>/<c>startY</c> are the one case with no such partner, because <c>ClientLeft</c>/
        /// <c>ClientTop</c> already folded the border term in directly.
        /// </summary>
        /// <remarks>
        /// Under <c>collapse</c> this is <c>-ActualBorderLeftWidth</c>, not 0: <c>ClientLeft</c> already
        /// added that same <c>VW[0]/2</c> once (it is <c>Location.X + ActualBorderLeftWidth</c>, and
        /// padding is always zero on a table), so this cancels it back to bare <c>Location.X</c>. That is
        /// deliberate, not a second double-count - per <see cref="CollapsedBorderModel"/>'s own geometric
        /// model, the table's border box and the first cell's own border box are <i>the same edge</i>
        /// (<c>X_0 − VW[0]/2</c> both), not two edges VW[0]/2 apart the way a normal (non-collapsed) box's
        /// border-then-content layering would suggest. Verified by hand against <c>GetWidthSum</c>'s own
        /// (independently-derived, and already-correct) total: a first cell placed at
        /// <c>ClientLeft + HorizontalSpacingAt(0)</c> instead - reusing the interior formula's shape -
        /// measured 200.375pt against 200.000pt of column width for a 3-column, 1px-bordered fixture, an
        /// exact one-outer-edge (VW[0]/2 = 0.375pt) residual.
        /// </remarks>
        private double StartXSpacing() =>
            _tableBox.BorderCollapse == Keywords.Collapse ? -TableInlineBorderStart : ColumnAxisBorderSpacing;

        /// <summary>The row-axis twin of <see cref="StartXSpacing"/> - see its own remarks.</summary>
        private double StartYSpacing() =>
            _tableBox.BorderCollapse == Keywords.Collapse ? -TableRowAxisBorderStart : RowAxisBorderSpacing;

        /// <summary>
        /// CSS <c>border-spacing</c>'s two values are physical (horizontal = X gaps, vertical = Y gaps),
        /// unaffected by writing-mode - so which one is the *column*-axis spacing (the role
        /// <see cref="HorizontalSpacingAt"/>/<see cref="StartXSpacing"/> need) swaps with
        /// <see cref="_isVertical"/> the same way <see cref="TableInlineBorderStart"/> does for border
        /// width.
        /// </summary>
        private double ColumnAxisBorderSpacing => _isVertical ? _tableBox.ActualBorderSpacingVertical : _tableBox.ActualBorderSpacingHorizontal;

        private double RowAxisBorderSpacing => _isVertical ? _tableBox.ActualBorderSpacingHorizontal : _tableBox.ActualBorderSpacingVertical;

        /// <summary>The table's own border width consumed at the row axis's start edge - see <see cref="TableInlineBorderStart"/>.</summary>
        private double TableRowAxisBorderStart => _isVertical ? _tableBox.ActualBorderLeftWidth : _tableBox.ActualBorderTopWidth;

        private double TableRowAxisBorderEnd => _isVertical ? _tableBox.ActualBorderRightWidth : _tableBox.ActualBorderBottomWidth;

        /// <summary>
        /// The gap a row/column cursor advances by when it crosses vertical grid line
        /// <paramref name="line"/> (0..ColumnCount) - negative under <c>collapse</c>, since adjacent
        /// cells' border boxes overlap there by the resolved border width instead of being held apart by
        /// <c>border-spacing</c>. Borders are centered on their grid line (see
        /// <see cref="CollapsedBorderModel"/>'s own remarks), so an <b>interior</b> line's cells overlap
        /// by the whole resolved width, while the table's own two <b>outer</b> edges (line 0 and
        /// <see cref="_columnCount"/>) only give up half of it - the other half is the table's own used
        /// border width, applied separately via the table's own used-border-width override.
        /// </summary>
        private double HorizontalSpacingAt(int line)
        {
            if (_tableBox.BorderCollapse != Keywords.Collapse) return ColumnAxisBorderSpacing;
            if (_collapsedBorders is not { } model || model.VerticalLineWidth.Length == 0) return 0;

            var width = model.VerticalLineWidth[Math.Clamp(line, 0, model.VerticalLineWidth.Length - 1)];
            return line <= 0 || line >= _columnCount ? -width / 2 : -width;
        }

        /// <summary>The gap a row cursor advances by when it crosses horizontal grid line <paramref name="line"/> (0..RowCount) - see <see cref="HorizontalSpacingAt"/>'s own remarks, which apply identically on this axis.</summary>
        private double VerticalSpacingAt(int line)
        {
            if (_tableBox.BorderCollapse != Keywords.Collapse) return RowAxisBorderSpacing;
            if (_collapsedBorders is not { } model || model.HorizontalLineWidth.Length == 0) return 0;

            var rowCount = _grid?.RowCount ?? 0;
            var width = model.HorizontalLineWidth[Math.Clamp(line, 0, model.HorizontalLineWidth.Length - 1)];
            return line <= 0 || line >= rowCount ? -width / 2 : -width;
        }

        /// <summary>
        /// Sums <see cref="HorizontalSpacingAt"/> over every vertical grid line - what
        /// <see cref="GetAvailableCellWidth"/>'s own pre-existing flat-spacing formula summed without
        /// ever excluding a collapsed column's own boundary (only <see cref="GetWidthSum"/> did that);
        /// preserved here rather than reconciling the two, since fixing that asymmetry is unrelated to
        /// replacing the flat spacing constant with resolved widths.
        /// </summary>
        private double SumHorizontalSpacing()
        {
            double total = 0;
            for (var line = 0; line <= _columnCount; line++) total += HorizontalSpacingAt(line);
            return total;
        }

        /// <summary>
        /// <see cref="SumHorizontalSpacing"/>, minus one boundary per collapsed column - the per-line
        /// translation of <see cref="GetWidthSum"/>'s own pre-existing
        /// <c>(columnCount + 1 - CollapsedColumnCount())</c> removal: a collapsed column contributes
        /// neither its own width (already zeroed by <see cref="CollapseColumnWidths"/>) nor a
        /// border-spacing slot of its own, matching <see cref="LayoutBodyRow"/>'s cursor advance not
        /// spacing past it either. The boundary immediately after each collapsed column is the one
        /// skipped, mirroring <see cref="GetInteriorSpacing"/>'s and <c>LayoutBodyRow</c>'s own choice of
        /// which side of a collapsed column carries no spacing.
        /// </summary>
        private double SumHorizontalSpacingExcludingCollapsedColumns()
        {
            double total = 0;
            for (var line = 0; line <= _columnCount; line++)
            {
                if (line > 0 && IsColumnCollapsed(line - 1)) continue;
                total += HorizontalSpacingAt(line);
            }
            return total;
        }

        /// <summary>
        /// Determines if a row would cross a page boundary.
        /// </summary>
        /// <param name="container">the container whose page grid <paramref name="estimatedBottom"/> is checked against, or null</param>
        /// <param name="estimatedBottom">the row's estimated far edge along this table's row axis</param>
        /// <param name="availableHeight">the current band's usable height, net of any repeated footer</param>
        /// <param name="currentPageNumber">the band <paramref name="estimatedBottom"/> is measured from</param>
        /// <param name="pageHeight">
        /// This table's own effective page height - <see cref="double.MaxValue"/> when this table's row
        /// loop doesn't paginate (a measurement pass, or a vertical table's forced-unpaged row loop - see
        /// its own call site's remarks). Checked instead of <c>container.PageSize.Height</c> directly:
        /// the container's real page size stays finite for a vertical table in an ordinary multi-page
        /// document even though this table's own <paramref name="estimatedBottom"/> is a row-axis
        /// (physical-X) quantity that has no relationship to the container's physical-Y page bands - so
        /// checking the container's page size here compared a row-axis coordinate against a column-axis
        /// boundary and could fire a spurious mid-table break, contradicting the whole-table monolithic
        /// treatment <see cref="MonolithicContent.IsUnresumableVerticalTable"/> gives a vertical table.
        /// </param>
        private static bool WillCrossPageBoundary(HtmlContainerInt? container, double estimatedBottom, double availableHeight, int currentPageNumber, double pageHeight)
        {
            if (container is null || pageHeight >= double.MaxValue - 1)
                return false;

            var currentPageBottom = container.PageTopOf(currentPageNumber) + availableHeight;

            return estimatedBottom > currentPageBottom;
        }

        /// <summary>
        /// Phase 3: Calculates the Y position for footer at the bottom of current page
        /// </summary>
        private double CalculateFooterPositionAtPageBottom(HtmlContainerInt container, double currentY, int currentPageNumber)
        {
            if (container.PageSize.Height >= double.MaxValue - 1)
                return currentY;

            // PageBottomOf is already the margin-free content band's bottom (the band height
            // itself excludes both margins - see the availableHeight fix above) - subtracting
            // marginBottom again pulled the footer up an extra marginBottom short of the real
            // page bottom.
            return container.PageBottomOf(currentPageNumber) - _footerHeight;
        }

        /// <summary>
        /// Phase 3: Estimates the height a row will need (for page break detection)
        /// </summary>
        /// <remarks>
        /// A heuristic, not exact geometry - already known to undershoot a row holding block content by
        /// roughly 2x (see the pre-check callers' own remarks), with real layout correcting the answer
        /// afterward. Passed as a method group in one caller (<c>_bodyRows.Sum(EstimateRowHeight)</c>), so
        /// this deliberately does not take a grid-row index the way the real (non-estimated) row-advance
        /// call sites do; <see cref="VerticalSpacingAt"/> at an arbitrary interior line (1) stands in for
        /// "whatever this row's own boundary resolves to", which is exact for the overwhelmingly common
        /// case of uniform border widths across a table and only approximate otherwise - acceptable for an
        /// estimate real layout supersedes.
        /// </remarks>
        private double EstimateRowHeight(CssBox row)
        {
            double maxHeight = 0;

            foreach (var cell in row.Boxes)
            {
                // Include padding and border widths — these are computable from CSS properties
                // before the cell is laid out, making the estimate more accurate and preventing
                // page break detection from firing too late.
                var estimatedHeight = (cell.ActualFont?.Height ?? 12)
                    + cell.ActualPaddingTop + cell.ActualPaddingBottom
                    + cell.ActualBorderTopWidth + cell.ActualBorderBottomWidth;
                maxHeight = Math.Max(maxHeight, estimatedHeight);
            }

            return maxHeight + VerticalSpacingAt(1);
        }

        /// <summary>
        /// Phase 3: Calculates offset needed to move to the next page
        /// </summary>
        private static double CalculatePageBreakOffset(HtmlContainerInt container, double currentY, int currentPageNumber)
        {
            if (container.PageSize.Height >= double.MaxValue - 1)
                return 0;

            return container.PageTopOf(currentPageNumber + 1) - currentY;
        }

        /// <summary>
        /// css-break §3.1 keep-with-next: break-after/break-before: avoid on the sibling(s) immediately
        /// preceding the table (e.g. the UA default <c>h1-h6 { break-after: avoid }</c> under
        /// @media print) forbids the break a whole-table relocation would otherwise introduce between
        /// them and the table. Extends <paramref name="pageBreakOffset"/> so the run lands at the next
        /// page's content top with the table positioned right after it, when the run starts on the same
        /// page as <paramref name="currentY"/> and, together with <paramref name="trailingHeight"/> (the
        /// content the table itself still needs room for - the whole body, or just the header plus its
        /// first row, depending on the caller), it fits within <paramref name="availableHeight"/>. An
        /// unsatisfiable avoid is relaxed per spec and the returned offset is unchanged, moving the table
        /// alone exactly as before. Shared by both whole-table pre-checks in <see cref="LayoutCells"/>.
        /// </summary>
        private double PullKeepWithNextRun(HtmlContainerInt container, double currentY, double pageBreakOffset,
            int currentPageNumber, double availableHeight, double trailingHeight)
        {
            // The ladder itself belongs to EarlyBreak, which owns it for every other mover; the pre-check
            // supplies what only it knows - an *estimate* of the room the table still needs, since its rows
            // are not placed yet - and carries the move out itself, because there is no laid-out box here
            // for a stated decision to re-place.
            var (run, extraAbove, _) = EarlyBreak.TravellingRun(
                _tableBox, currentY, container.PageTopOf(currentPageNumber), trailingHeight, availableHeight);

            if (run.Count == 0) return pageBreakOffset;

            // One common offset lands the retained run's top at the next page's content top and keeps the
            // run→table spacing intact.
            var groupOffset = pageBreakOffset + extraAbove;

            foreach (var member in run)
            {
                member.OffsetTop(groupOffset);
            }

            return groupOffset;
        }

        #endregion
    }
}