using PeachBrackets.Building;

namespace PeachPDF.Layout
{
    /// <summary>Builds a vertical flex column's items, each a new child of <paramref name="box"/> (a <c>display:flex; flex-direction:column</c> box).</summary>
    internal sealed class ColumnDescriptorBuilder(DocumentNode box) : IColumnDescriptor
    {
        public IColumnDescriptor Spacing(PdfLength value)
        {
            box.SetStyle("row-gap", value);
            return this;
        }

        public IContainer Item()
        {
            var item = box.AppendBlock();
            return new ContainerBuilder(item);
        }
    }
}
