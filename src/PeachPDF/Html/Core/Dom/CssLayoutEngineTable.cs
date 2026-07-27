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
        /// Init.
        /// </summary>
        /// <param name="tableBox"></param>
        private CssLayoutEngineTable(CssBox tableBox)
        {
            _tableBox = tableBox;
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
        public static async ValueTask PerformLayout(RGraphics g, CssBox tableBox)
        {
            ArgumentNullException.ThrowIfNull(g);
            ArgumentNullException.ThrowIfNull(tableBox);

            try
            {
                var table = new CssLayoutEngineTable(tableBox);
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

            // This engine may be run again over the same table - a resumed fragmentainer pass, the
            // per-page-width reflow loop, ShrinkToFit - and it does not start from the markup unless the
            // last run's output is undone first.
            RestoreStructureFromAnyPreviousRun();

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
            if (_tableBox.MarginLeft is CssConstants.Auto)
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
        }

        /// <summary>
        /// Get the table boxes into the proper fields.
        /// </summary>
        private void AssignBoxKinds()
        {
            foreach (var box in _tableBox.Boxes)
            {
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
            var cursor = new TableRowCursor(
                startY, startX,
                pageHeight < double.MaxValue - 1 ? container!.SlotStartingAt(_tableBox.ClientTop) : 0);

            // Reset page-break tracking so re-layout doesn't accumulate stale entries
            _tableBox.PageBreakBottoms = null;

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

            // Step 4: Layout body rows with page break detection.
            //
            // Pre-check: move the entire table to the next page when the first body row
            // would cross a page boundary AND the full table body fits on one page.
            // The per-row page-break check uses `i > 0` so it never fires for single-row
            // tables; this pre-check handles that case by adjusting the table's location.
            // Restricted to tables without repeating headers/footers to avoid repositioning
            // proxy boxes that were already placed above.
            if (_bodyRows.Count > 0
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
            if (_shouldRepeatHeaders && _headerBox != null)
            {
                var headerProxy = CreateHeaderProxy(cursor.CurrentY);
                if (headerProxy != null)
                {
                    await headerProxy.PerformLayout(g);

                    cursor.CurrentY += _headerHeight + GetVerticalSpacing();
                    cursor.MaxBottom = cursor.CurrentY;
                }
            }

            for (var i = 0; i < _bodyRows.Count; i++)
            {
                var row = _bodyRows[i];
                cursor.RowIndex = i;

                // The band the loop last opened, which is not in general the band the cursor has reached -
                // see TableRowCursor for why it is nevertheless what every question here is asked of.
                var slot = cursor.SlotIndex;
                var estimatedRowHeight = EstimateRowHeight(row);
                // See the identical fix/comment on the pre-check's availableHeight above.
                var availableHeight = (container?.PageBandHeightOf(slot) ?? pageHeight) - _footerHeight;

                // Check for page break: either the row does not fit, or a break value says the break
                // falls here regardless (css-break-3 §3.1's class-A break point between two rows).
                if (i > 0 && container != null
                    && (ForcedBreakFallsBeforeRow(i)
                        || WillCrossPageBoundary(container, cursor.CurrentY + estimatedRowHeight, availableHeight, slot)))
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

                    // Record after footer so the border clip includes the footer area.
                    _tableBox.PageBreakBottoms ??= new Dictionary<int, double>();
                    _tableBox.PageBreakBottoms[slot] = pageBreakBottomY;

                    // Move to next page
                    cursor.OpenNextSlot(
                        cursor.CurrentY + CalculatePageBreakOffset(container, cursor.CurrentY, slot));

                    // Create new header proxy for new page
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
                }

                // Layout body row
                await LayoutBodyRow(g, row, startX, cursor);

                cursor.CurrentY = cursor.MaxBottom + GetVerticalSpacing();

                row.Location = new RPoint(row.Boxes.Min(x => x.Location.X), row.Boxes.Min(x => x.Location.Y));
                row.ActualRight = row.Boxes.Max(x => x.ActualRight);
                row.ActualBottom = cursor.MaxBottom;
            }

            // Step 5: Create final footer proxy
            if (_shouldRepeatFooters && _footerHeight > 0)
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
            SetRowGroupBoxDimensions();

            // Step 7: Set final table dimensions
            var tableRight = Math.Max(cursor.MaxRight, _tableBox.Location.X + _tableBox.ActualWidth);
            _tableBox.ActualRight = tableRight + GetHorizontalSpacing() + _tableBox.ActualBorderRightWidth;
            _tableBox.ActualBottom = Math.Max(cursor.MaxBottom, startY) + GetVerticalSpacing() + _tableBox.ActualBorderBottomWidth;

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
        private void SetRowGroupBoxDimensions()
        {
            foreach (var box in _tableBox.Boxes)
            {
                if (box.Display != CssConstants.TableRowGroup)
                    continue;

                var rows = box.Boxes.Where(b => b.Display == CssConstants.TableRow).ToList();
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

            foreach (var cell in row.Boxes)
            {
                if (currentColumn >= _columnWidths!.Length)
                    break;

                var rowSpan = GetRowSpan(cell);
                var columnIndex = GetCellRealColumnIndex(row, cell);
                var width = GetCellWidth(columnIndex, cell);

                cell.Location = new RPoint(currentX, currentY);
                cell.ActualRight = cell.Location.X + width;

                await cell.PerformLayout(g);

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
                if (cell is CssSpacingBox spacer)
                {
                    if (spacer.EndRow == rowIndex)
                    {
                        spacer.ExtendedBox.ActualBottom = rowMaxBottom;
                        CssLayoutEngine.ApplyCellVerticalAlignment(g, spacer.ExtendedBox);
                    }
                }
                else if (GetRowSpan(cell) == 1 || (boxesThatEndOnRow?.Contains(cell) ?? false))
                {
                    cell.ActualBottom = rowMaxBottom;
                    CssLayoutEngine.ApplyCellVerticalAlignment(g, cell);
                }
            }

            cursor.MaxRight = rowMaxRight;
            cursor.MaxBottom = rowMaxBottom;
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