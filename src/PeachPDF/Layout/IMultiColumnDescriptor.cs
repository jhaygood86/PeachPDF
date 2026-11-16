namespace PeachPDF.Layout
{
    /// <summary>Describes content that flows through columns (<see cref="IContainer.MultiColumn"/>).</summary>
    public interface IMultiColumnDescriptor
    {
        /// <summary>Sets the number of columns (<c>column-count</c>).</summary>
        IMultiColumnDescriptor Columns(int count);

        /// <summary>Sets the gap between the columns (<c>column-gap</c>).</summary>
        IMultiColumnDescriptor Spacing(PdfLength value);

        /// <summary>Balances the columns' heights when true, the default (<c>column-fill</c>), or fills each column in turn when false.</summary>
        IMultiColumnDescriptor BalanceHeight(bool balance = true);

        /// <summary>The container whose content flows through the columns.</summary>
        IContainer Content();
    }
}
