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
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;
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
        /// collection of all columns boxes
        /// </summary>
        private readonly List<CssBox> _columns = [];

        /// <summary>
        /// 
        /// </summary>
        private readonly List<CssBox> _allRows = [];

        private int _columnCount;

        private bool _widthSpecified;

        private double[]? _columnWidths;

        private double[]? _columnMinWidths;

        // Header/Footer repetition fields
        private double _headerHeight;
        private double _footerHeight;
        private bool _shouldRepeatHeaders => _headerBox != null && _headerBox.Display == CssConstants.TableHeaderGroup;
        private bool _shouldRepeatFooters => _footerBox != null && _footerBox.Display == CssConstants.TableFooterGroup;

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
                switch (box.Display)
                {
                    case CssConstants.TableColumn:
                        columns += GetSpan(box);
                        break;
                    case CssConstants.TableRowGroup:
                        {
                            foreach (var cr in tableBox.Boxes)
                            {
                                count++;
                                if (cr.Display == CssConstants.TableRow)
                                    columns = Math.Max(columns, cr.Boxes.Count);
                            }

                            break;
                        }
                    case CssConstants.TableRow:
                        count++;
                        columns = Math.Max(columns, box.Boxes.Count);
                        break;
                }

                // limit the amount of rows to process for performance
                if (count > 30)
                    break;
            }

            // +1 columns because padding is between the cell and table borders
            return (columns + 1) * GetHorizontalSpacing(tableBox);
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

            // Insert EmptyBoxes for vertical cell spanning. 
            InsertEmptyBoxes();

            // Determine Row and Column Count, and ColumnWidths
            var availCellSpace = CalculateCountAndWidth();

            DetermineMissingColumnWidths(availCellSpace);

            // Check for minimum sizes (increment widths if necessary)
            EnforceMaximumSize();

            // While table width is larger than it should, and width is reducible
            EnforceMinimumSize();

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
            if (!_continuesAPreviousPass && _tableBox.MarginLeft is CssConstants.Auto)
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

            // Published only now that the run has finished - see the constructor for what a half-settled
            // setup would cost a later continuation. A continuation re-publishes the instance it inherited.
            _tableBox.TableSetup = _setup;
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
                if (_setup.Header is { } header) (_headerBox, _headerIndex, _headerHeight) = header;
                if (_setup.Footer is { } footer) (_footerBox, _footerIndex, _footerHeight) = footer;
            }

            foreach (var box in _tableBox.Boxes)
            {
                // A proxy standing in for a detached group is this engine's own output rather than
                // markup, and it inherits the source's Display - so on a continuation, where the restore
                // that drops proxies is deliberately skipped, the first one would be taken for the header
                // itself and the rest classified as body rows. A proxy has no cells, so positioning one
                // as a row throws on an empty sequence (issue #353, from the other direction).
                if (box is CssProxyBox) continue;

                switch (box.Display)
                {
                    case CssConstants.TableCaption:
                        break;
                    case CssConstants.TableRow:
                        _bodyRows.Add(box);
                        break;
                    case CssConstants.TableRowGroup:
                        foreach (CssBox childBox in box.Boxes)
                            if (childBox.Display == CssConstants.TableRow)
                                _bodyRows.Add(childBox);
                        break;
                    case CssConstants.TableHeaderGroup:
                        if (_headerBox != null)
                            _bodyRows.Add(box);
                        else
                            _headerBox = box;
                        break;
                    case CssConstants.TableFooterGroup:
                        if (_footerBox != null)
                            _bodyRows.Add(box);
                        else
                            _footerBox = box;
                        break;
                    case CssConstants.TableColumn:
                        for (int i = 0; i < GetSpan(box); i++)
                            _columns.Add(box);
                        break;
                    case CssConstants.TableColumnGroup:
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
                _allRows.AddRange(_headerBox.Boxes);

            _allRows.AddRange(_bodyRows);

            if (_footerBox != null)
                _allRows.AddRange(_footerBox.Boxes);
        }

        /// <summary>
        /// Insert EmptyBoxes for vertical cell spanning.
        /// </summary>
        private void InsertEmptyBoxes()
        {
            if (_tableBox._tableFixed) return;

            var currentRow = 0;

            foreach (var row in _bodyRows)
            {
                for (var k = 0; k < row.Boxes.Count; k++)
                {
                    var cell = row.Boxes[k];
                    var rowSpan = GetRowSpan(cell);
                    var realColumnIndex = GetCellRealColumnIndex(row, cell); //Real column of the cell

                    for (var i = currentRow + 1; i < currentRow + rowSpan; i++)
                    {
                        if (_bodyRows.Count <= i) continue;

                        var columnCount = 0;
                        for (var j = 0; j < _bodyRows[i].Boxes.Count; j++)
                        {
                            if (columnCount == realColumnIndex)
                            {
                                _bodyRows[i].Boxes.Insert(columnCount, new CssSpacingBox(_tableBox, ref cell, currentRow));
                                break;
                            }
                            columnCount++;
                            realColumnIndex -= GetColSpan(_bodyRows[i].Boxes[j]) - 1;
                        }
                    }
                }

                currentRow++;
            }

            _tableBox._tableFixed = true;
        }

        /// <summary>
        /// Determine Row and Column Count, and ColumnWidths
        /// </summary>
        /// <returns></returns>
        private double CalculateCountAndWidth()
        {
            //Columns
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
                    CssLength len = new(_columns[i].Width); //Get specified width

                    if (!(len.Number > 0)) continue; //If some width specified

                    if (len.IsPercentage) //Get width as a percentage
                    {
                        _columnWidths[i] = CssValueParser.ParseNumber(_columns[i].Width, availCellSpace);
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

                        if (i >= row.Boxes.Count || row.Boxes[i].Display != CssConstants.TableCell) continue;

                        var len = CssValueParser.ParseLength(row.Boxes[i].Width, availCellSpace, row.Boxes[i]);

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

                // spread extra width between all columns
                for (var i = 0; i < _columnWidths.Length; i++)
                {
                    if (!(maxFullWidths[i] > _columnWidths[i])) continue;

                    var temp = _columnWidths[i];
                    _columnWidths[i] = Math.Min(_columnWidths[i] + (availCellSpace - occupiedSpace) / Convert.ToSingle(_columnWidths.Length - i), maxFullWidths[i]);
                    occupiedSpace = occupiedSpace + _columnWidths[i] - temp;
                }
            }
        }

        /// <summary>
        /// While table width is larger than it should, and width is reducible.<br/>
        /// If table max width is limited by we need to lower the columns width even if it will result in clipping<br/>
        /// </summary>
        private void EnforceMaximumSize()
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

            // if table max width is limited by we need to lower the columns width even if it will result in clipping
            var maxWidth = GetMaxTableWidth();
            if (!(maxWidth < 90999)) return;

            widthSum = GetWidthSum();
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
            {
                // lower the width of columns starting from the largest one until the max width is satisfied
                for (var a = 0; a < 15 && maxWidth < widthSum - 0.1; a++) // limit iteration so bug won't create infinite loop
                {
                    var nonMaxedColumns = 0;
                    double largeWidth = 0f, secLargeWidth = 0f;
                    foreach (var columnWidth in _columnWidths)
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
            else
            {
                // spread extra width to columns that didn't reached max width where trying to spread it between all columns
                for (var a = 0; a < 15 && maxWidth > widthSum + 0.1; a++) // limit iteration so bug won't create infinite loop
                {
                    var nonMaxedColumns = 0;
                    for (var i = 0; i < _columnWidths.Length; i++)
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
                    var col = GetCellRealColumnIndex(row, cell);
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
        /// Remove header and footer from document tree for proxy-based repetition
        /// </summary>
        private void RemoveHeaderFooterFromTree()
        {
            if (_headerBox != null)
            {
                _headerIndex = _tableBox.Boxes.IndexOf(_headerBox);
                _tableBox.Boxes.Remove(_headerBox);
                _headerBox.ParentBox = null;
            }

            if (_footerBox != null)
            {
                _footerIndex = _tableBox.Boxes.IndexOf(_footerBox);
                _tableBox.Boxes.Remove(_footerBox);
                _footerBox.ParentBox = null;
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

                if (proxy.SourceIndex >= 0 && proxy.SourceIndex < _tableBox.Boxes.Count - 1)
                {
                    _tableBox.Boxes.Remove(source);
                    _tableBox.Boxes.Insert(proxy.SourceIndex, source);
                }
            }
        }

        /// <summary>
        /// Create a proxy box for the header at the specified Y position
        /// </summary>
        private CssProxyBox? CreateHeaderProxy(double yPosition)
        {
            if (_headerBox == null)
                return null;

            var proxy = new CssProxyBox(_tableBox, _headerBox, _headerIndex);
            var startX = Math.Max(_tableBox.ClientLeft + GetHorizontalSpacing(), 0);
            proxy.Location = new RPoint(startX, yPosition);
            return proxy;
        }

        /// <summary>
        /// Create a proxy box for the footer at the specified Y position
        /// </summary>
        private CssProxyBox? CreateFooterProxy(double yPosition)
        {
            if (_footerBox == null)
                return null;

            var proxy = new CssProxyBox(_tableBox, _footerBox, _footerIndex);
            var startX = Math.Max(_tableBox.ClientLeft + GetHorizontalSpacing(), 0);
            proxy.Location = new RPoint(startX, yPosition);
            return proxy;
        }

        /// <summary>
        /// Layout the cells by the calculated table layout
        /// </summary>
        /// <param name="g"></param>
        private async ValueTask LayoutCells(RGraphics g)
        {
            var startX = Math.Max(_tableBox.ClientLeft + GetHorizontalSpacing(), 0);
            var startY = Math.Max(_tableBox.ClientTop + GetVerticalSpacing(), 0);

            var container = _tableBox.HtmlContainer;
            var pageHeight = container?.PageSize.Height ?? double.MaxValue;

            // Which fragmentainer the table begins in is a question about the table's own top edge, not
            // about startY: startY is a row cursor, and GetVerticalSpacing() is -1 for a collapsed-border
            // table, so it sits one point *above* the box for exactly the tables whose top lands flush on
            // a page boundary. Reading it there names the page the table just left, and the pre-check
            // below then nudges the table one point onto the next one - which is what a table relocated
            // by CssBox's own §4.3 mover, and so placed exactly at a page top, does every time.
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
                ? TableRowCursor.Continuing(carried, ResumedRowTop(container, carried, startY, pageHeight))
                : new TableRowCursor(
                    startY, startX,
                    pageHeight < double.MaxValue - 1 ? container!.SlotStartingAt(_tableBox.ClientTop) : 0);

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
        /// Lays a row out with its fragmentainer told that a flow resuming inside it begins
        /// <paramref name="headerRoom"/> further below the band's own content edge than it otherwise
        /// would, because the repeated header this pass drew is there.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see href="https://www.w3.org/TR/css-tables-3/#repeated-headers">css-tables-3 §6.2</see> is
        /// explicit that repeating a header means <i>leaving room</i> for it, and the row cursor cannot
        /// state that on its own. It reserves the header's height by advancing <c>CurrentY</c>, which
        /// positions the rows this pass places and the cells it enters fresh — but a cell continuing an
        /// earlier fragmentainer deliberately keeps the one <c>Location</c> its first fragment was built
        /// from, and its content goes wherever the flow decides, which is
        /// <see cref="FragmentainerContext.ResumeContentTop"/>. Those were the same value, so a repeating
        /// <c>&lt;thead&gt;</c> was drawn <i>over</i> the first lines of the continuation it exists to sit
        /// above (<see href="https://github.com/jhaygood86/PeachPDF/issues/439">#439</see>): six words
        /// hidden under the header on each of two pages of
        /// <c>paged_media_table_row_continuation</c>, with every word still claimed exactly once, so no
        /// count-based check could see it.
        /// </para>
        /// <para>
        /// The inset <b>composes</b> — it is added to whatever is already reserved and restored in a
        /// <c>finally</c> — so a sibling flow resuming elsewhere in the same fragmentainer owes nothing.
        /// That is not the same as nested repeating headers working: an inner table's own proxy is placed
        /// at the band top the outer header occupies, which this does not address.
        /// </para>
        /// </remarks>
        /// <param name="g">the graphics context layout is running against</param>
        /// <param name="row">the body row to lay out, which is the one a continuation re-entered</param>
        /// <param name="startX">the table's own content left, as for <see cref="LayoutBodyRow"/></param>
        /// <param name="cursor">this pass's row cursor</param>
        /// <param name="headerRoom">the height the repeated header took, plus the vertical spacing after it</param>
        private async ValueTask LayoutBodyRowBelowTheRepeatedHeader(
            RGraphics g, CssBox row, double startX, TableRowCursor cursor, double headerRoom)
        {
            // Null on an unpaginated or measurement pass, where nothing resumes and CreateLineBoxes reads
            // the block's own ClientTop rather than any fragmentainer's content edge.
            var fragmentainer = _tableBox.HtmlContainer?.CurrentFragmentainer;

            if (fragmentainer is null)
            {
                await LayoutBodyRow(g, row, startX, cursor);
                return;
            }

            var previousReservation = fragmentainer.ReserveResumeContent(headerRoom);

            try
            {
                await LayoutBodyRow(g, row, startX, cursor);
            }
            finally
            {
                fragmentainer.RestoreResumeContent(previousReservation);
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
            if (_shouldRepeatHeaders && _headerBox != null)
            {
                // Layout header rows directly using table layout logic
                var headerRowsLayoutY = cursor.CurrentY;
                var headerCursor = cursor.ForRowGroupMeasurement(headerRowsLayoutY);

                foreach (var row in _headerBox.Boxes)
                {
                    if (row.Display != CssConstants.TableRow)
                        continue;

                    headerCursor.CurrentY = headerRowsLayoutY;
                    headerCursor.MaxBottom = headerRowsLayoutY;

                    await LayoutBodyRow(g, row, startX, headerCursor);
                    headerRowsLayoutY = headerCursor.MaxBottom + GetVerticalSpacing();

                    // Unlike the regular body-row loop below, this never set the row's own
                    // Location/ActualRight/ActualBottom (only each cell's) - left it at a
                    // degenerate (0,0,0,0) Bounds, which the paint-time visibility-culling
                    // optimization (see SetRowGroupBoxDimensions's call-site comment for the same
                    // bug at the row-group level) then silently drops from painting entirely.
                    row.Location = new RPoint(row.Boxes.Min(x => x.Location.X), row.Boxes.Min(x => x.Location.Y));
                    row.ActualRight = row.Boxes.Max(x => x.ActualRight);
                    row.ActualBottom = headerCursor.MaxBottom;
                }

                cursor.MaxRight = headerCursor.MaxRight;

                // Set header box dimensions
                _headerBox.Location = new RPoint(startX, cursor.CurrentY);
                _headerBox.ActualRight = cursor.MaxRight;
                _headerBox.ActualBottom = headerRowsLayoutY - GetVerticalSpacing();
                _headerHeight = _headerBox.ActualBottom - _headerBox.Location.Y;
            }

            // Step 3: Layout footer rows once to get dimensions (if needed)
            if (_shouldRepeatFooters && _footerBox != null)
            {
                // Layout footer rows directly
                var footerRowsLayoutY = 0d;
                var footerCursor = cursor.ForRowGroupMeasurement(footerRowsLayoutY);

                foreach (var row in _footerBox.Boxes)
                {
                    if (row.Display != CssConstants.TableRow)
                        continue;

                    footerCursor.CurrentY = footerRowsLayoutY;
                    footerCursor.MaxBottom = footerRowsLayoutY;

                    await LayoutBodyRow(g, row, startX, footerCursor);
                    footerRowsLayoutY = footerCursor.MaxBottom + GetVerticalSpacing();

                    // See the identical fix in the header-rows loop above for why this is needed.
                    row.Location = new RPoint(row.Boxes.Min(x => x.Location.X), row.Boxes.Min(x => x.Location.Y));
                    row.ActualRight = row.Boxes.Max(x => x.ActualRight);
                    row.ActualBottom = footerCursor.MaxBottom;
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
                _footerBox.Location = new RPoint(startX, 0);
                _footerBox.ActualRight = cursor.MaxRight;
                _footerBox.ActualBottom = footerRowsLayoutY - GetVerticalSpacing();
                _footerHeight = _footerBox.ActualBottom - _footerBox.Location.Y;
            }

            _setup.Header = _headerBox is null ? null : new DetachedRowGroup(_headerBox, _headerIndex, _headerHeight);
            _setup.Footer = _footerBox is null ? null : new DetachedRowGroup(_footerBox, _footerIndex, _footerHeight);
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
                && !_shouldRepeatHeaders
                && !_shouldRepeatFooters)
            {
                var slot = cursor.SlotIndex;
                var firstRowHeight = EstimateRowHeight(_bodyRows[0]);
                // The band height already excludes both margins (PdfGenerator.SetContent) -
                // subtracting them again here double-counted a marginTop+marginBottom-sized band
                // out of every page's real capacity.
                var availableHeight = container!.PageBandHeightOf(slot) - _footerHeight;
                var estimatedBodyHeight = _bodyRows.Sum(EstimateRowHeight);

                if (WillCrossPageBoundary(container, cursor.CurrentY + firstRowHeight, availableHeight, slot)
                    && estimatedBodyHeight <= availableHeight)
                {
                    var pageBreakOffset = CalculatePageBreakOffset(container, cursor.CurrentY, slot);
                    pageBreakOffset = PullKeepWithNextRun(container, cursor.CurrentY, pageBreakOffset,
                        slot, availableHeight, estimatedBodyHeight);

                    _tableBox.Location = _tableBox.Location with { Y = _tableBox.Location.Y + pageBreakOffset };
                    startY = Math.Max(_tableBox.ClientTop + GetVerticalSpacing(), 0);
                    cursor.RestartAt(startY, container.PageIndexOf(startY));
                }
            }

            // Pre-check: a repeating <thead> is laid out above (Step 2) before any body row is
            // attempted - if the header itself fits on the current page but no body row does, the
            // per-row break check below can't catch it (it requires `i > 0`, since it exists to
            // detect breaks *between* rows) and the whole header (plus any keep-with-next heading)
            // would strand alone on the current page while every body row starts on the next.
            // Mirrors the headerless pre-check above - "first body row after the header" instead
            // of "first body row after the table's own top" - but deliberately NOT gated on the
            // whole body fitting one page: a long repeating-header table should still start fresh
            // on the next page rather than orphan its header, and fragments normally via the
            // per-row check from there.
            if (_bodyRows.Count > 0
                && !_continuesAPreviousPass
                && pageHeight < double.MaxValue - 1
                && container != null
                && _shouldRepeatHeaders && _headerBox != null)
            {
                var slot = cursor.SlotIndex;
                var firstRowHeight = EstimateRowHeight(_bodyRows[0]);
                var availableHeight = container.PageBandHeightOf(slot) - _footerHeight;
                var afterHeaderY = startY + _headerHeight + GetVerticalSpacing();

                if (WillCrossPageBoundary(container, afterHeaderY + firstRowHeight, availableHeight, slot))
                {
                    var pageBreakOffset = CalculatePageBreakOffset(container, startY, slot);
                    pageBreakOffset = PullKeepWithNextRun(container, startY, pageBreakOffset,
                        slot, availableHeight, _headerHeight + firstRowHeight);

                    _tableBox.Location = _tableBox.Location with { Y = _tableBox.Location.Y + pageBreakOffset };
                    _headerBox.OffsetTop(pageBreakOffset);

                    startY = Math.Max(_tableBox.ClientTop + GetVerticalSpacing(), 0);
                    cursor.RestartAt(startY, container.PageIndexOf(startY));
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
            var resumedHeaderRoom = 0d;

            if (_shouldRepeatHeaders && _headerBox != null)
            {
                var headerProxy = CreateHeaderProxy(cursor.CurrentY);
                if (headerProxy != null)
                {
                    await headerProxy.PerformLayout(g);

                    var headerRoom = _headerHeight + GetVerticalSpacing();
                    if (_continuesAPreviousPass) resumedHeaderRoom = headerRoom;

                    cursor.CurrentY += headerRoom;
                    cursor.MaxBottom = cursor.CurrentY;
                }
            }

            // The body rows this table has reached, this pass or an earlier one - which is every body
            // row unless the loop below stops. Steps 5 and 6 close the table over them rather than over
            // its markup, because a row no pass has placed has no geometry to close over.
            var placedRows = _bodyRows.Count;

            // This run re-decides where every finished cell's box sits, so what an earlier one stated over
            // the same slots goes first. Swept here, once, rather than per cell in the loop below: a cell
            // the loop never reaches - because it stops at a cell that did not finish, or runs out of
            // columns - would otherwise keep a statement no pass still stands behind. A run continuing an
            // earlier pass sweeps only from the slot it resumes in, because the slots before it were
            // settled by the passes that filled them and are still true, which is what lets a row spanning
            // three fragmentainers hold a fragment in each.
            DiscardContinuationShells(container, _continuesAPreviousPass ? cursor.SlotIndex : null);

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
                var availableHeight = (container?.PageBandHeightOf(slot) ?? pageHeight) - _footerHeight;

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
                if (i > ResumeRowIndex && container != null
                    && (ForcedBreakFallsBeforeRow(i)
                        || WillCrossPageBoundary(container, cursor.CurrentY + estimatedRowHeight, availableHeight, slot)))
                {
                    slot = await TakeBreakBeforeRow(g, container, cursor, slot);
                }

                // Layout body row. Only the row a continuation re-enters can hold a cell whose own flow
                // resumes, so only that row owes the repeated header the room it took.
                var placement = cursor.BeginRow();
                var rowTop = cursor.CurrentY;

                if (i == ResumeRowIndex && resumedHeaderRoom > 0)
                {
                    await LayoutBodyRowBelowTheRepeatedHeader(g, row, startX, cursor, resumedHeaderRoom);
                }
                else
                {
                    await LayoutBodyRow(g, row, startX, cursor);
                }

                // The question the estimate could only guess at, now that the row has been placed and its
                // real bottom is readable: did it cross out of the band it began in? Where it did, the
                // break belongs before it (§4.3), so the placement is taken back and the row is placed
                // again on the other side of it. At most one correction per row, so this cannot loop.
                if (StraddleCorrectionAppliesTo(i, container, cursor, rowTop, slot))
                {
                    cursor.Retract(placement);
                    PassRewind.RollBackTo(null, row.Boxes);

                    slot = await TakeBreakBeforeRow(g, container!, cursor, slot);

                    // Re-clears FinishedCells, which the retracted placement filled in.
                    cursor.RowIndex = i;
                    await LayoutBodyRow(g, row, startX, cursor);
                }

                cursor.CurrentY = cursor.MaxBottom + GetVerticalSpacing();

                row.Location = new RPoint(row.Boxes.Min(x => x.Location.X), row.Boxes.Min(x => x.Location.Y));
                row.ActualRight = row.Boxes.Max(x => x.ActualRight);
                row.ActualBottom = cursor.MaxBottom;

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
                    break;
                }
            }

            // Step 5: Create final footer proxy. Not on a pass that stopped: the closing footer sits
            // under the table's last row, and a pass that has not reached the last row would put it in
            // the middle of the table on the page it is leaving. The footer for that page is the
            // per-row break block's above, and the pass that finishes the table writes this one.
            if (!cursor.Stopped && _shouldRepeatFooters && _footerHeight > 0)
            {
                var finalFooterProxy = CreateFooterProxy(cursor.CurrentY);
                if (finalFooterProxy != null)
                {
                    await finalFooterProxy.PerformLayout(g);
                    cursor.CurrentY += _footerHeight + GetVerticalSpacing();
                    cursor.MaxBottom = Math.Max(cursor.MaxBottom, finalFooterProxy.ActualBottom);
                    cursor.MaxRight = Math.Max(cursor.MaxRight, finalFooterProxy.ActualRight);
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

            // Step 7: Set final table dimensions
            var tableRight = Math.Max(cursor.MaxRight, _tableBox.Location.X + _tableBox.ActualWidth);
            _tableBox.ActualRight = tableRight + GetHorizontalSpacing() + _tableBox.ActualBorderRightWidth;
            _tableBox.ActualBottom = Math.Max(cursor.MaxBottom, startY) + GetVerticalSpacing() + _tableBox.ActualBorderBottomWidth;

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
            _tableBox.TableContinuation = cursor.Continuation(_tableBox);

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
                if (box.Display != CssConstants.TableRowGroup)
                    continue;

                var rows = box.Boxes.Where(b => b.Display == CssConstants.TableRow && placed.Contains(b))
                    .ToList();
                if (rows.Count == 0)
                    continue;

                box.Location = new RPoint(rows.Min(r => r.Location.X), rows.Min(r => r.Location.Y));
                box.ActualRight = rows.Max(r => r.ActualRight);
                box.ActualBottom = rows.Max(r => r.ActualBottom);
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
            if (_shouldRepeatFooters && _footerHeight > 0)
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
            cursor.MoveToSlot(target, container.PageTopOf(target));

            // Create new header proxy for new page. It takes no ResumeContentInset, and does not need one:
            // only a cell whose flow continues from an earlier fragmentainer owes the header its room, only
            // the row a continuation re-enters can hold one, and this arm never runs at that row.
            if (_shouldRepeatHeaders && _headerHeight > 0)
            {
                var headerProxy = CreateHeaderProxy(cursor.CurrentY);
                if (headerProxy != null)
                {
                    await headerProxy.PerformLayout(g);
                    cursor.CurrentY += _headerHeight + GetVerticalSpacing();
                    cursor.MaxRight = Math.Max(cursor.MaxRight, headerProxy.ActualRight);
                }
            }

            cursor.MaxBottom = cursor.CurrentY;

            return target;
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
        /// <see href="https://www.w3.org/TR/css-break-3/#break-between">§4.4</see> forbids, and the next
        /// band would only cut it in the same place. A row taller than a whole band lands here, which is
        /// exactly the case <see href="https://github.com/jhaygood86/PeachPDF/issues/432">#432</see> is
        /// about: it stays, overflows, and the rows after it are placed below it rather than inside it.
        /// </description></item>
        /// <item><description>
        /// <b>The next band could not hold it either.</b> §4.3's relaxation ladder ends in leaving content
        /// where it is; moving a row that will straddle again walks it down the document one band per row.
        /// </description></item>
        /// <item><description>
        /// <b>The row ends a <c>rowspan</c>.</b> Its alignment has already deep-offset a cell that belongs
        /// to an earlier row, and that is not this row's geometry to take back.
        /// </description></item>
        /// <item><description>
        /// <b>The fragmentainer has a band of its own.</b> Inside a multi-column column the page grid does
        /// not describe the fragmentainer being filled, so its bands answer nothing here.
        /// </description></item>
        /// </list>
        /// </para>
        /// </remarks>
        /// <param name="rowIndex">the body row just placed</param>
        /// <param name="container">the container whose page grid the bands come from, or null</param>
        /// <param name="cursor">this pass's row cursor, holding the row's real bottom</param>
        /// <param name="rowTop">where the row was placed</param>
        /// <param name="slot">the band it was placed in</param>
        private bool StraddleCorrectionAppliesTo(
            int rowIndex, HtmlContainerInt? container, TableRowCursor cursor, double rowTop, int slot)
        {
            if (rowIndex <= ResumeRowIndex || cursor.Stopped) return false;
            if (container is null || container.PageSize.Height >= double.MaxValue - 1) return false;
            if (container.CurrentFragmentainer is { HasOwnBand: true }) return false;

            if (!HtmlContainerInt.FallsPast(cursor.MaxBottom, container.BandOfSlot(slot))) return false;

            if (rowTop - HtmlContainerInt.PageBoundaryEpsilon <= container.PageTopOf(slot)) return false;

            if (cursor.MaxBottom - rowTop > RoomForARowIn(container, slot + 1)) return false;

            return !cursor.RowSpannedBoxes.TryGetValue(rowIndex, out var endingHere) || endingHere.Count == 0;
        }

        /// <summary>
        /// What a band leaves a body row once the groups this table repeats on every page have taken
        /// theirs.
        /// </summary>
        /// <param name="container">the container whose page grid the band comes from</param>
        /// <param name="slot">the band to measure</param>
        private double RoomForARowIn(HtmlContainerInt container, int slot)
        {
            var room = container.PageBandHeightOf(slot) - _footerHeight;

            if (_shouldRepeatHeaders && _headerHeight > 0) room -= _headerHeight + GetVerticalSpacing();

            return room;
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
            var currentColumn = 0;
            var rowMaxBottom = cursor.MaxBottom;
            var rowMaxRight = cursor.MaxRight;

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
                if (currentColumn >= _columnWidths!.Length)
                    break;

                var rowSpan = GetRowSpan(cell);
                var columnIndex = GetCellRealColumnIndex(row, cell);
                var width = GetCellWidth(columnIndex, cell);

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
                    currentColumn++;
                    currentX += width + GetHorizontalSpacing();
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
                    cell.Location = new RPoint(currentX, currentY);
                }

                cell.ActualRight = cell.Location.X + width;

                // A cell an earlier pass stopped part-way through continues from its own record rather
                // than from the start - the cells of one row are §2.1 parallel flows, so each has its own
                // stopping point and the ones that finished are simply not in the carried list.
                if (cursor.CarriedTokenFor(cell) is { } carried) cell.ResumeAt(carried, null);

                await cell.PerformLayout(g);

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
                        rowMaxBottom = Math.Max(rowMaxBottom, sb.ExtendedBox.ActualBottom);
                    }
                }
                else
                {
                    switch (rowSpan)
                    {
                        case 1:
                            rowMaxBottom = Math.Max(rowMaxBottom, cell.ActualBottom);
                            break;
                        case > 1:
                            {
                                var endRow = rowIndex + rowSpan - 1;
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

                rowMaxRight = Math.Max(rowMaxRight, cell.ActualRight);
                currentColumn++;
                currentX = cell.ActualRight + GetHorizontalSpacing();
            }

            // Vertical alignment
            IEnumerable<CssBox> boxesToVerticallyAlign = row.Boxes;
            if (rowSpannedBoxes.TryGetValue(rowIndex, out var boxesThatEndOnRow))
            {
                boxesToVerticallyAlign = boxesToVerticallyAlign.Union(boxesThatEndOnRow);
            }

            foreach (var cell in boxesToVerticallyAlign)
            {
                // Nothing of this cell belongs to this fragmentainer, so neither does its geometry: giving
                // it this fragment's bottom and re-aligning against that would drag the content an earlier
                // pass emitted down onto this page.
                if (cursor.FinishedOnAnEarlierPass(cell)) continue;

                if (cell is CssSpacingBox spacer)
                {
                    if (spacer.EndRow == rowIndex)
                    {
                        spacer.ExtendedBox.ActualBottom = rowMaxBottom;
                        CssLayoutEngine.ApplyCellVerticalAlignment(g, spacer.ExtendedBox);
                    }
                }
                else if (stoppedCells.Contains(cell) || cursor.ResumedFromAnEarlierPass(cell))
                {
                    // Only a cell whose fragment both opens and closes in this fragmentainer has leftover
                    // room of its own to distribute. One that continues elsewhere overfills its fragment by
                    // definition, and one that *continues an earlier* fragment had where its content sits
                    // settled by the pass that opened it - re-aligning that drags content an earlier
                    // fragmentainer has already emitted onto this page, which is a whole line drawn on two
                    // pages rather than a cosmetic shift. Asked after the CssSpacingBox arm rather than
                    // beside the FinishedCells guard above, because a spacer that stopped still has to
                    // align the cell it stands in for.
                    continue;
                }
                else if (GetRowSpan(cell) == 1 || (boxesThatEndOnRow?.Contains(cell) ?? false))
                {
                    cell.ActualBottom = rowMaxBottom;
                    CssLayoutEngine.ApplyCellVerticalAlignment(g, cell);
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
        /// Gets the cell column index checking its position and other cells colspans
        /// </summary>
        /// <param name="row"></param>
        /// <param name="cell"></param>
        /// <returns></returns>
        private static int GetCellRealColumnIndex(CssBox row, CssBox cell)
        {
            int i = 0;

            foreach (CssBox b in row.Boxes)
            {
                if (b.Equals(cell))
                    break;
                i += GetColSpan(b);
            }

            return i;
        }

        /// <summary>
        /// Gets the cells width, taking colspan and being in the specified column
        /// </summary>
        /// <param name="column"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        private double GetCellWidth(int column, CssBox b)
        {
            double colspan = Convert.ToSingle(GetColSpan(b));
            double sum = 0f;

            for (int i = column; i < column + colspan; i++)
            {
                if (column >= _columnWidths!.Length)
                    break;
                if (_columnWidths.Length <= i)
                    break;
                sum += _columnWidths[i];
            }

            sum += (colspan - 1) * GetHorizontalSpacing();

            return sum;
        }

        /// <summary>
        /// Gets the colspan of the specified box
        /// </summary>
        /// <param name="b"></param>
        private static int GetColSpan(CssBox b)
        {
            var att = b.GetAttribute("colspan", "1");

            return !int.TryParse(att, out var colspan) ? 1 : colspan;
        }

        /// <summary>
        /// Gets the rowspan of the specified box
        /// </summary>
        /// <param name="b"></param>
        private static int GetRowSpan(CssBox b)
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
                if (childBox.Display == CssConstants.None) continue;

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
            CssLength tableBoxLength = new(_tableBox.Width);

            if (!(tableBoxLength.Number > 0)) return _tableBox.ContainingBlock.Size.Width;

            _widthSpecified = true;
            return CssValueParser.ParseLength(_tableBox.Width, _tableBox.ContainingBlock.Size.Width, _tableBox);

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
            var tblen = new CssLength(_tableBox.MaxWidth);
            if (tblen.Number > 0)
            {
                _widthSpecified = true;
                return CssValueParser.ParseLength(_tableBox.MaxWidth, _tableBox.ParentBox!.AvailableWidth, _tableBox);
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

            var availCellWidth = GetAvailableCellWidth();

            foreach (var row in _allRows)
            {
                for (var i = 0; i < row.Boxes.Count; i++)
                {
                    var cell = row.Boxes[i];
                    var col = GetCellRealColumnIndex(row, cell);
                    col = _columnWidths.Length > col ? col : _columnWidths.Length - 1;

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

                    var colSpan = GetColSpan(cell);
                    minWidth /= colSpan;
                    maxWidth /= colSpan;

                    for (var j = 0; j < colSpan; j++)
                    {
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
                    if (!CssValueParser.IsValidLength(cell.MaxWidth)) continue;

                    var col = GetCellRealColumnIndex(row, cell);
                    col = explicitMaxWidths.Length > col ? col : explicitMaxWidths.Length - 1;
                    var colSpan = GetColSpan(cell);
                    var cellMaxWidth = CssValueParser.ParseLength(cell.MaxWidth, availCellWidth, cell) / colSpan;

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
            return GetAvailableTableWidth() - GetHorizontalSpacing() * (_columnCount + 1) - _tableBox.ActualBorderLeftWidth - _tableBox.ActualBorderRightWidth;
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

            //Take cell-spacing
            f += GetHorizontalSpacing() * (_columnWidths.Length + 1);

            //Take table borders
            f += _tableBox.ActualBorderLeftWidth + _tableBox.ActualBorderRightWidth;

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
                    var col = GetCellRealColumnIndex(row, cell);
                    var affectColumn = Math.Min(col + colspan, _columnMinWidths.Length) - 1;
                    var spannedWidth = GetSpannedMinWidth(row, col, colspan) + (colspan - 1) * GetHorizontalSpacing();

                    var cellMinWidth = cell.GetMinimumWidth();
                    if (cell.MinWidth != "0" && CssValueParser.IsValidLength(cell.MinWidth))
                    {
                        cellMinWidth = Math.Max(cellMinWidth, CssValueParser.ParseLength(cell.MinWidth, availCellWidth, cell));
                    }

                    _columnMinWidths[affectColumn] = Math.Max(_columnMinWidths[affectColumn], cellMinWidth - spannedWidth);
                }
            }

            return _columnMinWidths;
        }

        /// <summary>
        /// Gets the actual horizontal spacing of the table
        /// </summary>
        private double GetHorizontalSpacing()
        {
            return _tableBox.BorderCollapse == CssConstants.Collapse ? -1f : _tableBox.ActualBorderSpacingHorizontal;
        }

        /// <summary>
        /// Gets the actual horizontal spacing of the table
        /// </summary>
        private static double GetHorizontalSpacing(CssBox box)
        {
            return box.BorderCollapse == CssConstants.Collapse ? -1f : box.ActualBorderSpacingHorizontal;
        }

        /// <summary>
        /// Gets the actual vertical spacing of the table
        /// </summary>
        private double GetVerticalSpacing()
        {
            return _tableBox.BorderCollapse == CssConstants.Collapse ? -1f : _tableBox.ActualBorderSpacingVertical;
        }

        /// <summary>
        /// Determines if a row would cross a page boundary
        /// </summary>
        private static bool WillCrossPageBoundary(HtmlContainerInt? container, double estimatedBottom, double availableHeight, int currentPageNumber)
        {
            if (container is null || container.PageSize.Height >= double.MaxValue - 1)
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

            return maxHeight + GetVerticalSpacing();
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
        /// preceding the table (e.g. the UA default <c>h1-h6 { page-break-after: avoid }</c> under
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