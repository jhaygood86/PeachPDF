using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using System.Collections.Generic;
using System.Globalization;

namespace PeachPDF.Layout
{
    /// <summary>Builds one table row's cells, each a new <c>display: table-cell</c> child of <paramref name="rowBox"/>.</summary>
    internal sealed class TableRowDescriptorBuilder(CssBox rowBox, CssPropertyFactory properties) : ITableRowDescriptor
    {
        public IContainer Cell(int columnSpan = 1, int rowSpan = 1)
        {
            // CssLayoutEngineTable.GetColSpan/GetRowSpan read these as raw HtmlTag attributes (the same
            // ones a real <td colspan="N"> would carry), so a cell needs a real HtmlTag rather than the
            // generic anonymous-box tag every other declarative builder uses.
            Dictionary<string, string>? attributes = null;
            if (columnSpan != 1 || rowSpan != 1)
            {
                attributes = new Dictionary<string, string>();
                if (columnSpan != 1) attributes["colspan"] = columnSpan.ToString(CultureInfo.InvariantCulture);
                if (rowSpan != 1) attributes["rowspan"] = rowSpan.ToString(CultureInfo.InvariantCulture);
            }

            var cell = CssBox.CreateBox(rowBox, new HtmlTag("td", false, attributes));
            properties.Set(cell, "display", "table-cell");
            return new ContainerBuilder(cell, properties);
        }
    }
}
