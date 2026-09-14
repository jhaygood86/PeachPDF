namespace PeachPDF.Layout
{
    /// <summary>Describes a horizontal row of items (<see cref="IContainer.Row"/>), each its own <see cref="IContainer"/>.</summary>
    public interface IRowDescriptor
    {
        /// <summary>Sets the gap between items.</summary>
        IRowDescriptor Spacing(PdfLength value);

        /// <summary>Appends one more item and returns its container.</summary>
        IContainer Item();
    }
}
