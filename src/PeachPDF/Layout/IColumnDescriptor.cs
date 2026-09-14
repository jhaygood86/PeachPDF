namespace PeachPDF.Layout
{
    /// <summary>Describes a vertical stack of items (<see cref="IContainer.Column"/>), each its own <see cref="IContainer"/>.</summary>
    public interface IColumnDescriptor
    {
        /// <summary>Sets the gap between items.</summary>
        IColumnDescriptor Spacing(PdfLength value);

        /// <summary>Appends one more item and returns its container.</summary>
        IContainer Item();
    }
}
