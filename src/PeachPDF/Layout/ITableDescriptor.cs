using System;

namespace PeachPDF.Layout
{
    /// <summary>
    /// Builds a table's column definitions and rows (<see cref="IContainer.Table"/>) - backed by
    /// PeachPDF's existing CSS table layout engine (<c>display: table</c>/<c>table-row</c>/<c>table-cell</c>,
    /// already fully supporting colspan/rowspan and repeating header/footer row groups), not a
    /// from-scratch table model.
    /// </summary>
    public interface ITableDescriptor
    {
        /// <summary>Defines this table's columns (widths). Optional - omitting it lets every column size itself automatically from its cells' content, exactly like an ordinary CSS table.</summary>
        void Columns(Action<ITableColumnsDescriptor> handler);

        /// <summary>Sets this table's repeating header row (<c>table-header-group</c>) - repainted at the top of every page the table spans.</summary>
        void Header(Action<ITableRowDescriptor> handler);

        /// <summary>Sets this table's repeating footer row (<c>table-footer-group</c>) - repainted at the bottom of every page the table spans.</summary>
        void Footer(Action<ITableRowDescriptor> handler);

        /// <summary>Appends one body row. Callable more than once, once per row.</summary>
        void Row(Action<ITableRowDescriptor> handler);
    }
}
