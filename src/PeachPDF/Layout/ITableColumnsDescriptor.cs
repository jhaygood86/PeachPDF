namespace PeachPDF.Layout
{
    /// <summary>Defines one <see cref="ITableDescriptor"/>'s columns, in left-to-right order.</summary>
    public interface ITableColumnsDescriptor
    {
        /// <summary>
        /// Adds a column that shares the table's width with every other relative column, proportionally
        /// to <paramref name="weight"/> (e.g. two columns weighted 1 and 2 split the width 1:2). Maps to
        /// a <c>&lt;col&gt;</c> with a computed percentage <c>width</c>.
        /// </summary>
        ITableColumnsDescriptor RelativeColumn(double weight = 1);

        /// <summary>Adds a column of a fixed width, independent of the table's own width.</summary>
        ITableColumnsDescriptor FixedColumn(PdfLength width);
    }
}
