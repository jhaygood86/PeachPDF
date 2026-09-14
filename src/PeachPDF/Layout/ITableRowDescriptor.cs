namespace PeachPDF.Layout
{
    /// <summary>Builds one <see cref="ITableDescriptor"/> row's cells, in left-to-right order.</summary>
    public interface ITableRowDescriptor
    {
        /// <summary>
        /// Appends one cell, optionally spanning more than one column/row (<c>colspan</c>/<c>rowspan</c>),
        /// and returns its content container.
        /// </summary>
        IContainer Cell(int columnSpan = 1, int rowSpan = 1);
    }
}
