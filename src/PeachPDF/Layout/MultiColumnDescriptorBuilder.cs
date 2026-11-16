using PeachBrackets.Building;

namespace PeachPDF.Layout
{
    /// <summary>Builds a multi-column flow: <paramref name="box"/> holds the column styles and the content that flows through them.</summary>
    internal sealed class MultiColumnDescriptorBuilder(DocumentNode box) : IMultiColumnDescriptor
    {
        private readonly ContainerBuilder _content = new(box) { FlowsInColumns = true };

        public IMultiColumnDescriptor Columns(int count)
        {
            _content.Columns(count);
            return this;
        }

        public IMultiColumnDescriptor Spacing(PdfLength value)
        {
            _content.ColumnGap(value);
            return this;
        }

        public IMultiColumnDescriptor BalanceHeight(bool balance = true)
        {
            _content.ColumnFill(balance);
            return this;
        }

        public IContainer Content() => _content;
    }
}
