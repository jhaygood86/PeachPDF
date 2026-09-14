using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using System;

namespace PeachPDF.Layout
{
    /// <summary>
    /// Builds one <see cref="IContainer.Table"/> call's column/header/footer/row children of
    /// <paramref name="tableBox"/> (a <c>display: table</c> box). Every child is classified purely by its
    /// own <c>display</c> value (<see cref="CssLayoutEngineTable"/> scans <c>tableBox.Boxes</c> once,
    /// switching on each child's resolved display) - unlike HTML's own anonymous-table-object
    /// construction, there's no positional requirement (colgroup need not precede rows), so each builder
    /// method below is independent of the others' call order.
    /// </summary>
    internal sealed class TableDescriptorBuilder(CssBox tableBox, CssPropertyFactory properties) : ITableDescriptor
    {
        public void Columns(Action<ITableColumnsDescriptor> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);

            var colGroupBox = CssPropertyFactory.CreateAnonymousBox(tableBox);
            properties.Set(colGroupBox, "display", "table-column-group");

            // table-layout: fixed makes each <col>'s own width authoritative (CSS Tables §3.3) instead of
            // the auto algorithm recomputing column widths from cell content - but CssLayoutEngineTable
            // only actually switches to the fixed algorithm when the table itself resolves to a definite
            // width (its own §17.5.2.1 precondition: fixed layout needs a definite table width to
            // distribute among columns in the first place). A declarative table with explicit columns has
            // no other way to state that width, so default it to 100% of the available space - the same
            // "fill the container" default a QuestPDF-style table is expected to have.
            properties.Set(tableBox, "table-layout", "fixed");
            properties.Set(tableBox, "width", "100%");

            var columns = new TableColumnsDescriptorBuilder(colGroupBox, properties);
            handler(columns);
            columns.ResolveRelativeColumnWidths();
        }

        public void Header(Action<ITableRowDescriptor> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            BuildRowGroup("table-header-group", handler);
        }

        public void Footer(Action<ITableRowDescriptor> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            BuildRowGroup("table-footer-group", handler);
        }

        public void Row(Action<ITableRowDescriptor> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);

            var row = CssPropertyFactory.CreateAnonymousBox(tableBox);
            properties.Set(row, "display", "table-row");
            handler(new TableRowDescriptorBuilder(row, properties));
        }

        private void BuildRowGroup(string groupDisplay, Action<ITableRowDescriptor> handler)
        {
            var group = CssPropertyFactory.CreateAnonymousBox(tableBox);
            properties.Set(group, "display", groupDisplay);

            var row = CssPropertyFactory.CreateAnonymousBox(group);
            properties.Set(row, "display", "table-row");
            handler(new TableRowDescriptorBuilder(row, properties));
        }
    }
}
