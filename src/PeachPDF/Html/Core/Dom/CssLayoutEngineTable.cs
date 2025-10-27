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
    /// Layout engine for tables executing the complex layout of tables with rows/columns/headers/etc.
    /// </summary>
    internal sealed class CssLayoutEngineTable
    {
        /// <summary>
        /// the main box of the table
        /// </summary>
        private readonly CssBox _tableBox;

        private CssBox? _headerBox;

        private CssBox? _footerBox;

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

        // Header repetition fields
        private double _headerHeight;
        private readonly List<CssBox> _headerRows = [];
        private bool _shouldRepeatHeaders => _headerBox != null && _headerBox.Display == CssConstants.TableHeaderGroup;
        private int _currentPageNumber = 0;

        // Phase 2: Footer repetition fields
        private double _footerHeight;
        private readonly List<CssBox> _footerRows = [];
        private bool _shouldRepeatFooters => _footerBox != null && _footerBox.Display == CssConstants.TableFooterGroup;

        // Phase 2: Track header cell spans for complex headers
        private readonly Dictionary<CssBox, (int rowSpan, int colSpan)> _headerCellSpans = [];

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
                tableBox.HtmlContainer?.ReportError(HtmlRenderErrorType.Layout, "Failed table layout", ex);
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

            // get the table boxes into the proper fields
            AssignBoxKinds();

            // Insert EmptyBoxes for vertical cell spanning. 
            InsertEmptyBoxes();

            // Determine Row and Column Count, and ColumnWidths
            var availCellSpace = CalculateCountAndWidth();

            DetermineMissingColumnWidths(availCellSpace);

            // Check for minimum sizes (increment widths if necessary)
            EnforceMinimumSize();

            // While table width is larger than it should, and width is reducible
            EnforceMaximumSize();

            _tableBox.Location = _tableBox.Location with
            {
                X = _tableBox.Location.X + CssLayoutEngine.GetActualMarginLeft(_tableBox, GetWidthSum())
            };

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
                        _columnWidths[i] = len.Number; //Get width as an absolute-pixel value
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
                        // spread extra width between all non width specified columns
                        var extWidth = (availCellSpace - occupiedSpace) / orgNumOfNans;
                        for (var i = 0; i < _columnWidths.Length; i++)
                            if (orgColWidths == null || double.IsNaN(orgColWidths[i]))
                                _columnWidths[i] += extWidth;
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
        /// Layout the cells by the calculated table layout
        /// </summary>
        /// <param name="g"></param>
        private async ValueTask LayoutCells(RGraphics g)
        {
            var startX = Math.Max(_tableBox.ClientLeft + GetHorizontalSpacing(), 0);
            var startY = Math.Max(_tableBox.ClientTop + GetVerticalSpacing(), 0);
            var currentY = startY;
            var maxRight = startX;
            var maxBottom = 0d;
            var currentRow = 0;

            var pageHeight = _tableBox.HtmlContainer?.PageSize.Height ?? double.MaxValue;
            var marginTop = _tableBox.HtmlContainer?.MarginTop ?? 0;
            var marginBottom = _tableBox.HtmlContainer?.MarginBottom ?? 0;

            // First layout header to calculate its height and store for repetition
            if (_shouldRepeatHeaders)
            {
                var headerMaxRight = maxRight;
                _headerHeight = await LayoutHeaderSection(g, startX, currentY);
                maxRight = Math.Max(maxRight, _tableBox.ActualRight);
                currentY += _headerHeight;
                if (_headerHeight > 0)
                {
                    currentY += GetVerticalSpacing();
                }
            }

            // Phase 3: Layout footer to calculate its height (will be positioned at end or repeated)
            if (_shouldRepeatFooters && _footerBox != null)
            {
                // Temporarily layout footer to get its height
                var tempFooterY = currentY;
                _footerHeight = await LayoutFooterSection(g, startX, tempFooterY);
            }

            Dictionary<int, List<CssBox>> rowSpannedBoxes = new();

            for (var i = 0; i < _bodyRows.Count; i++)
            {
                var row = _bodyRows[i];

                // Check if we need to break to a new page before laying out this row
                var estimatedRowHeight = EstimateRowHeight(row);

                // Phase 3: Account for footer height when checking page boundaries
                var availableHeight = pageHeight - _footerHeight - marginBottom;
                var wouldCrossPageBoundary = WillCrossPageBoundary(currentY, currentY + estimatedRowHeight, pageHeight, marginTop, availableHeight);

                if (wouldCrossPageBoundary && i > 0 && _tableBox.HtmlContainer != null)
                {
                    // Phase 3: Render footer at bottom of current page before page break
                    if (_shouldRepeatFooters && _footerHeight > 0)
                    {
                        var footerY = CalculateFooterPositionAtPageBottom(currentY, pageHeight, marginTop, marginBottom);
                        await RenderFooterOnPage(g, startX, footerY);
                    }

                    // Move to next page
                    var pageBreakOffset = CalculatePageBreakOffset(currentY, pageHeight, marginTop, marginBottom);
                    currentY += pageBreakOffset;
                    _currentPageNumber++;

                    // Repeat header on new page
                    if (_shouldRepeatHeaders && _headerHeight > 0)
                    {
                        currentY = await RenderHeaderOnNewPage(g, startX, currentY);
                        maxRight = Math.Max(maxRight, _tableBox.ActualRight);
                        currentY += GetVerticalSpacing();
                    }

                    maxBottom = currentY;
                }

                var currentX = startX;
                var currentColumn = 0;
                var breakPage = false;

                foreach (var cell in row.Boxes)
                {
                    if (currentColumn >= _columnWidths!.Length)
                        break;

                    var rowSpan = GetRowSpan(cell);
                    var columnIndex = GetCellRealColumnIndex(row, cell);
                    var width = GetCellWidth(columnIndex, cell);

                    cell.Location = new RPoint(currentX, currentY);
                    cell.ActualRight = cell.Location.X + width;

                    await cell.PerformLayout(g); //That will automatically set the bottom of the cell

                    //Alter max bottom only if row is cell's row + cell's rowspan - 1
                    if (cell is CssSpacingBox sb)
                    {
                        if (sb.EndRow == currentRow)
                        {
                            maxBottom = Math.Max(maxBottom, sb.ExtendedBox.ActualBottom);
                        }
                    }
                    else switch (rowSpan)
                        {
                            case 1:
                                maxBottom = Math.Max(maxBottom, cell.ActualBottom);
                                break;
                            case > 1:
                                {
                                    var endRow = i + rowSpan - 1;

                                    if (!rowSpannedBoxes.TryGetValue(endRow, out var rowSpannedBoxesForRow))
                                    {
                                        rowSpannedBoxesForRow = (List<CssBox>)[];
                                        rowSpannedBoxes[endRow] = rowSpannedBoxesForRow;
                                    }

                                    rowSpannedBoxesForRow.Add(cell);
                                    break;
                                }
                        }

                    maxRight = Math.Max(maxRight, cell.ActualRight);
                    currentColumn++;
                    currentX = cell.ActualRight + GetHorizontalSpacing();
                }

                IEnumerable<CssBox> boxesToVerticallyAlign = row.Boxes;

                if (rowSpannedBoxes.TryGetValue(i, out var boxesThatEndOnRow))
                {
                    boxesToVerticallyAlign = boxesToVerticallyAlign.Union(boxesThatEndOnRow);
                }

                foreach (var cell in boxesToVerticallyAlign)
                {
                    var spacer = cell as CssSpacingBox;

                    if (spacer == null && (GetRowSpan(cell) == 1 || (boxesThatEndOnRow?.Contains(cell) ?? false)))
                    {
                        cell.ActualBottom = maxBottom;
                        CssLayoutEngine.ApplyCellVerticalAlignment(g, cell);
                    }
                    else if (spacer != null && spacer.EndRow == currentRow)
                    {
                        spacer.ExtendedBox.ActualBottom = maxBottom;
                        CssLayoutEngine.ApplyCellVerticalAlignment(g, spacer.ExtendedBox);
                    }

                    // Phase 3: Check for break-inside: avoid on table or thead
                    if (ShouldAvoidBreak()) continue;

                    breakPage = cell.BreakPage();
                    if (!breakPage) continue;

                    currentY = cell.Location.Y;
                    break;
                }

                if (breakPage) // go back to move the whole row to the next page
                {
                    if (i == 1) // do not leave single row in previous page
                        i = -1; // Start layout from the first row on new page
                    else
                        i--;

                    maxBottom = 0;
                    continue;
                }

                currentY = maxBottom + GetVerticalSpacing();

                var rowX = row.Boxes.Min(x => x.Location.X);
                var rowY = row.Boxes.Min(x => x.Location.Y);
                var rowActualRight = row.Boxes.Max(x => x.ActualRight);

                row.Location = new RPoint(rowX, rowY);
                row.ActualRight = rowActualRight;
                row.ActualBottom = maxBottom;

                currentRow++;
            }

            // Phase 3: Render footer at final position (bottom of last page or after body)
            if (_shouldRepeatFooters && _footerHeight > 0)
            {
                var finalFooterY = currentY;
                await RenderFooterOnPage(g, startX, finalFooterY);
                currentY += _footerHeight + GetVerticalSpacing();
                maxBottom = Math.Max(maxBottom, currentY);
            }

            maxRight = Math.Max(maxRight, _tableBox.Location.X + _tableBox.ActualWidth);
            _tableBox.ActualRight = maxRight + GetHorizontalSpacing() + _tableBox.ActualBorderRightWidth;
            _tableBox.ActualBottom = Math.Max(maxBottom, startY) + GetVerticalSpacing() + _tableBox.ActualBorderBottomWidth;
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

            return sum; // -b.ActualBorderLeftWidth - b.ActualBorderRightWidth - b.ActualPaddingRight - b.ActualPaddingLeft;
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

            foreach (var row in _allRows)
            {
                for (var i = 0; i < row.Boxes.Count; i++)
                {
                    var col = GetCellRealColumnIndex(row, row.Boxes[i]);
                    col = _columnWidths.Length > col ? col : _columnWidths.Length - 1;

                    if ((onlyNans && !double.IsNaN(_columnWidths[col])) || i >= row.Boxes.Count) continue;
                    row.Boxes[i].GetMinMaxWidth(out var minWidth, out var maxWidth);

                    var colSpan = GetColSpan(row.Boxes[i]);
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

            foreach (var row in _allRows)
            {
                foreach (var cell in row.Boxes)
                {
                    var colspan = GetColSpan(cell);
                    var col = GetCellRealColumnIndex(row, cell);
                    var affectColumn = Math.Min(col + colspan, _columnMinWidths.Length) - 1;
                    var spannedWidth = GetSpannedMinWidth(row, col, colspan) + (colspan - 1) * GetHorizontalSpacing();

                    _columnMinWidths[affectColumn] = Math.Max(_columnMinWidths[affectColumn], cell.GetMinimumWidth() - spannedWidth);
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
        /// Layouts the header section and returns its height
        /// </summary>
        private async ValueTask<double> LayoutHeaderSection(RGraphics g, double startX, double startY)
        {
            if (_headerBox == null || _headerBox.Boxes.Count == 0)
                return 0;

            var headerStartY = startY;
            var currentY = startY;
            var maxRight = startX;

            // Store header rows for later repetition
            _headerRows.Clear();
            _headerRows.AddRange(_headerBox.Boxes);

            // Phase 2: Track cell spans for complex headers
            _headerCellSpans.Clear();

            foreach (var headerRow in _headerRows)
            {
                var currentX = startX;
                var rowMaxBottom = currentY;

                foreach (var cell in headerRow.Boxes)
                {
                    var columnIndex = GetCellRealColumnIndex(headerRow, cell);
                    var width = GetCellWidth(columnIndex, cell);

                    // Phase 2: Store cell span information
                    var rowSpan = GetRowSpan(cell);
                    var colSpan = GetColSpan(cell);
                    _headerCellSpans[cell] = (rowSpan, colSpan);

                    cell.Location = new RPoint(currentX, currentY);
                    cell.ActualRight = cell.Location.X + width;

                    await cell.PerformLayout(g);

                    rowMaxBottom = Math.Max(rowMaxBottom, cell.ActualBottom);
                    maxRight = Math.Max(maxRight, cell.ActualRight);
                    currentX = cell.ActualRight + GetHorizontalSpacing();
                }

                // Align cells vertically
                foreach (var cell in headerRow.Boxes)
                {
                    cell.ActualBottom = rowMaxBottom;
                    CssLayoutEngine.ApplyCellVerticalAlignment(g, cell);
                }

                headerRow.Location = new RPoint(startX, currentY);
                headerRow.ActualRight = headerRow.Boxes.Count > 0 ? headerRow.Boxes.Max(x => x.ActualRight) : startX;
                headerRow.ActualBottom = rowMaxBottom;
                currentY = rowMaxBottom + GetVerticalSpacing();
            }

            // Update table's actual right
            _tableBox.ActualRight = Math.Max(_tableBox.ActualRight, maxRight);

            return currentY - headerStartY;
        }

        /// <summary>
        /// Renders a copy of the header at the top of a new page
        /// </summary>
        private async ValueTask<double> RenderHeaderOnNewPage(RGraphics g, double startX, double startY)
        {
            if (_headerRows.Count == 0)
                return startY;

            var currentY = startY;
            var maxRight = startX;

            foreach (var originalHeaderRow in _headerRows)
            {
                var currentX = startX;
                var rowMaxBottom = currentY;

                // Re-layout header cells at new position
                foreach (var cell in originalHeaderRow.Boxes)
                {
                    var columnIndex = GetCellRealColumnIndex(originalHeaderRow, cell);
                    var width = GetCellWidth(columnIndex, cell);

                    // Calculate the height of the cell from original layout
                    var cellHeight = cell.ActualBottom - cell.Location.Y;

                    // Update cell position to new page location
                    var oldLocation = cell.Location;
                    cell.Location = new RPoint(currentX, currentY);
                    cell.ActualRight = cell.Location.X + width;
                    cell.ActualBottom = currentY + cellHeight;

                    // Update word positions within the cell
                    OffsetCellContentVertically(cell, oldLocation.Y, currentY);

                    rowMaxBottom = Math.Max(rowMaxBottom, cell.ActualBottom);
                    maxRight = Math.Max(maxRight, cell.ActualRight);
                    currentX = cell.ActualRight + GetHorizontalSpacing();
                }

                // Update row position
                originalHeaderRow.Location = new RPoint(startX, currentY);
                originalHeaderRow.ActualRight = originalHeaderRow.Boxes.Count > 0 ? originalHeaderRow.Boxes.Max(x => x.ActualRight) : startX;
                originalHeaderRow.ActualBottom = rowMaxBottom;
                currentY = rowMaxBottom + GetVerticalSpacing();
            }

            // Update table's actual right
            _tableBox.ActualRight = Math.Max(_tableBox.ActualRight, maxRight);

            return currentY;
        }

        /// <summary>
        /// Offsets the content within a cell vertically when header is repeated
        /// </summary>
        private void OffsetCellContentVertically(CssBox cell, double oldY, double newY)
        {
            var offset = newY - oldY;

            if (Math.Abs(offset) < 0.01)
                return;

            // Update word positions
            foreach (var word in cell.Words)
            {
                word.Top += offset;
            }

            // Recursively update child boxes
            foreach (var childBox in cell.Boxes)
            {
                childBox.Location = childBox.Location with { Y = childBox.Location.Y + offset };
                childBox.ActualBottom += offset;
                OffsetCellContentVertically(childBox, oldY, newY);
            }
        }

        /// <summary>
        /// Determines if a row would cross a page boundary
        /// </summary>
        private bool WillCrossPageBoundary(double currentY, double estimatedBottom, double pageHeight, double marginTop)
        {
            if (pageHeight >= double.MaxValue - 1)
                return false;

            // Calculate which page we're on
            var currentPageTop = (_currentPageNumber * pageHeight) + marginTop;
            var currentPageBottom = currentPageTop + pageHeight;

            // Check if the row would extend beyond the current page
            return estimatedBottom > currentPageBottom;
        }

        /// <summary>
        /// Phase 3: Determines if a row would cross a page boundary with available height consideration
        /// </summary>
        private bool WillCrossPageBoundary(double currentY, double estimatedBottom, double pageHeight, double marginTop, double availableHeight)
        {
            if (pageHeight >= double.MaxValue - 1)
                return false;

            // Calculate which page we're on
            var currentPageTop = (_currentPageNumber * pageHeight) + marginTop;
            var currentPageBottom = currentPageTop + availableHeight;

            // Check if the row would extend beyond the available space (accounting for footer)
            return estimatedBottom > currentPageBottom;
        }

        /// <summary>
        /// Phase 3: Calculates the Y position for footer at the bottom of current page
        /// </summary>
        private double CalculateFooterPositionAtPageBottom(double currentY, double pageHeight, double marginTop, double marginBottom)
        {
            if (pageHeight >= double.MaxValue - 1)
                return currentY;

            var currentPageTop = (_currentPageNumber * pageHeight) + marginTop;
            var currentPageBottom = currentPageTop + pageHeight;

            // Position footer at bottom of page, accounting for margins and footer height
            return currentPageBottom - _footerHeight - marginBottom;
        }

        /// <summary>
        /// Phase 3: Check if break should be avoided based on CSS properties
        /// </summary>
        private bool ShouldAvoidBreak()
        {
            // Check table's break-inside property
            if (_tableBox.BreakInside == CssConstants.Avoid)
                return true;

            // Phase 3: Check thead's break-inside property
            if (_headerBox?.BreakInside == CssConstants.Avoid)
                return true;

            // Phase 3: Check thead's break-after property
            if (_headerBox?.BreakAfter == CssConstants.Avoid)
                return true;

            return false;
        }

        /// <summary>
        /// Phase 3: Check if header is taller than a single page (edge case)
        /// </summary>
        private bool IsHeaderTallerThanPage(double pageHeight, double marginTop, double marginBottom)
        {
            if (pageHeight >= double.MaxValue - 1)
                return false;

            var availablePageHeight = pageHeight - marginTop - marginBottom;
            return _headerHeight > availablePageHeight;
        }

        /// <summary>
        /// Phase 3: Handle oversized headers by clipping to page height
        /// </summary>
        private double ClipHeaderHeight(double pageHeight, double marginTop, double marginBottom)
        {
            if (pageHeight >= double.MaxValue - 1)
                return _headerHeight;

            var availablePageHeight = pageHeight - marginTop - marginBottom;
            if (_headerHeight > availablePageHeight)
            {
                // Log warning that header is being clipped
                _tableBox.HtmlContainer?.ReportError(
                    HtmlRenderErrorType.Layout,
                    $"Table header height ({_headerHeight:F2}) exceeds page height ({availablePageHeight:F2}). Header will be clipped.",
                    null
                );
                return availablePageHeight;
            }
            return _headerHeight;
        }

        /// <summary>
        /// Estimates the height a row will need (for page break detection)
        /// </summary>
        private double EstimateRowHeight(CssBox row)
        {
            // Quick estimation: use max of cell minimum heights
            double maxHeight = 0;

            foreach (var cell in row.Boxes)
            {
                // Use font height as minimum estimate
                var estimatedHeight = cell.ActualFont?.Height ?? 12;
                maxHeight = Math.Max(maxHeight, estimatedHeight);
            }

            return maxHeight + GetVerticalSpacing();
        }

        /// <summary>
        /// Calculates offset needed to move to the next page
        /// </summary>
        private double CalculatePageBreakOffset(double currentY, double pageHeight, double marginTop, double marginBottom)
        {
            if (pageHeight >= double.MaxValue - 1)
                return 0;

            var currentPageNumber = (int)((currentY - marginTop) / pageHeight);
            var nextPageStart = (currentPageNumber + 1) * pageHeight + marginTop;

            return nextPageStart - currentY + marginTop;
        }

        /// <summary>
        /// Layouts the footer section and returns its height (Phase 2)
        /// </summary>
        private async ValueTask<double> LayoutFooterSection(RGraphics g, double startX, double startY)
        {
            if (_footerBox == null || _footerBox.Boxes.Count == 0)
                return 0;

            var footerStartY = startY;
            var currentY = startY;
            var maxRight = startX;

            // Store footer rows for later repetition
            _footerRows.Clear();
            _footerRows.AddRange(_footerBox.Boxes);

            foreach (var footerRow in _footerRows)
            {
                var currentX = startX;
                var rowMaxBottom = currentY;

                foreach (var cell in footerRow.Boxes)
                {
                    var columnIndex = GetCellRealColumnIndex(footerRow, cell);
                    var width = GetCellWidth(columnIndex, cell);

                    cell.Location = new RPoint(currentX, currentY);
                    cell.ActualRight = cell.Location.X + width;

                    await cell.PerformLayout(g);

                    rowMaxBottom = Math.Max(rowMaxBottom, cell.ActualBottom);
                    maxRight = Math.Max(maxRight, cell.ActualRight);
                    currentX = cell.ActualRight + GetHorizontalSpacing();
                }

                // Align cells vertically
                foreach (var cell in footerRow.Boxes)
                {
                    cell.ActualBottom = rowMaxBottom;
                    CssLayoutEngine.ApplyCellVerticalAlignment(g, cell);
                }

                footerRow.Location = new RPoint(startX, currentY);
                footerRow.ActualRight = footerRow.Boxes.Count > 0 ? footerRow.Boxes.Max(x => x.ActualRight) : startX;
                footerRow.ActualBottom = rowMaxBottom;
                currentY = rowMaxBottom + GetVerticalSpacing();
            }

            // Update table's actual right
            _tableBox.ActualRight = Math.Max(_tableBox.ActualRight, maxRight);

            return currentY - footerStartY;
        }

        /// <summary>
        /// Renders a copy of the footer at the bottom of a page (Phase 2)
        /// </summary>
        private async ValueTask<double> RenderFooterOnPage(RGraphics g, double startX, double startY)
        {
            if (_footerRows.Count == 0)
                return startY;

            var currentY = startY;
            var maxRight = startX;

            foreach (var originalFooterRow in _footerRows)
            {
                var currentX = startX;
                var rowMaxBottom = currentY;

                // Re-layout footer cells at new position
                foreach (var cell in originalFooterRow.Boxes)
                {
                    var columnIndex = GetCellRealColumnIndex(originalFooterRow, cell);
                    var width = GetCellWidth(columnIndex, cell);

                    // Calculate the height of the cell from original layout
                    var cellHeight = cell.ActualBottom - cell.Location.Y;

                    // Update cell position to new page location
                    var oldLocation = cell.Location;
                    cell.Location = new RPoint(currentX, currentY);
                    cell.ActualRight = cell.Location.X + width;
                    cell.ActualBottom = currentY + cellHeight;

                    // Update word positions within the cell
                    OffsetCellContentVertically(cell, oldLocation.Y, currentY);

                    rowMaxBottom = Math.Max(rowMaxBottom, cell.ActualBottom);
                    maxRight = Math.Max(maxRight, cell.ActualRight);
                    currentX = cell.ActualRight + GetHorizontalSpacing();
                }

                // Update row position
                originalFooterRow.Location = new RPoint(startX, currentY);
                originalFooterRow.ActualRight = originalFooterRow.Boxes.Count > 0 ? originalFooterRow.Boxes.Max(x => x.ActualRight) : startX;
                originalFooterRow.ActualBottom = rowMaxBottom;
                currentY = rowMaxBottom + GetVerticalSpacing();
            }

            // Update table's actual right
            _tableBox.ActualRight = Math.Max(_tableBox.ActualRight, maxRight);

            return currentY;
        }

        #endregion
    }
}