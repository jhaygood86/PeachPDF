using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Layout
{
    /// <summary>
    /// Builds one <see cref="ITableDescriptor.Columns"/> call's <c>&lt;col&gt;</c> children of
    /// <paramref name="colGroupBox"/> (a <c>display: table-column-group</c> box). A relative column's
    /// actual width isn't known until every column has been declared, so each one is recorded here and
    /// only resolved into a real <c>width</c> by <see cref="ResolveRelativeColumnWidths"/>, which
    /// <see cref="TableDescriptorBuilder.Columns"/> calls once the whole handler has returned.
    /// </summary>
    internal sealed class TableColumnsDescriptorBuilder(CssBox colGroupBox, CssPropertyFactory properties) : ITableColumnsDescriptor
    {
        private readonly List<(CssBox Box, double Weight)> _relativeColumns = [];
        private readonly List<PdfLength> _fixedColumnWidths = [];

        public ITableColumnsDescriptor RelativeColumn(double weight = 1)
        {
            var column = CssPropertyFactory.CreateAnonymousBox(colGroupBox);
            properties.Set(column, "display", "table-column");
            _relativeColumns.Add((column, weight));
            return this;
        }

        public ITableColumnsDescriptor FixedColumn(PdfLength width)
        {
            var column = CssPropertyFactory.CreateAnonymousBox(colGroupBox);
            properties.Set(column, "display", "table-column");

            // CssLayoutEngineTable.TryGetColumnElementWidth only reads a <col>'s width when it's a
            // percentage or a pixel/unitless length - a pre-existing, deliberate limitation of that one
            // reader (an absolute unit like pt/in/cm is silently ignored there, by its own doc comment).
            // Converting to the equivalent pixel length here means a FixedColumn declared in any
            // PdfLength unit still actually constrains the column, without touching that shared reader
            // (which auto-layout tables also depend on).
            properties.Set(column, "width", PdfLength.Pixels(width.ToPoints() / 0.75));
            _fixedColumnWidths.Add(width);
            return this;
        }

        internal void ResolveRelativeColumnWidths()
        {
            if (_relativeColumns.Count == 0) return;

            var totalWeight = _relativeColumns.Sum(c => c.Weight);

            if (_fixedColumnWidths.Count == 0)
            {
                foreach (var (column, weight) in _relativeColumns)
                {
                    properties.Set(column, "width", PdfLength.Percent(weight / totalWeight * 100));
                }
                return;
            }

            // A fixed column claims an absolute share of the table's own width; a relative column is left
            // at its own initial `width: auto` rather than given a computed percentage here - CSS 2.1
            // §17.5.2.1's fixed layout algorithm already defines "any remaining columns divide the
            // remaining horizontal space evenly" for auto columns, so equally-weighted relative columns
            // alongside a fixed one already come out correct with no extra work. An unequal weight given
            // alongside a fixed column is a known, narrower limitation (there is no way to express "a
            // fraction of however much space table-layout:fixed leaves over" as a single static CSS
            // length up front) - it degrades to an even split rather than silently producing a wrong
            // proportion.
        }
    }
}
