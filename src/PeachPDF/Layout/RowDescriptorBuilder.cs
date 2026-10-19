using PeachBrackets.Building;

namespace PeachPDF.Layout
{
    /// <summary>Builds a horizontal flex row's items, each a new child of <paramref name="box"/> (a <c>display:flex; flex-direction:row</c> box).</summary>
    internal sealed class RowDescriptorBuilder(DocumentNode box) : IRowDescriptor
    {
        public IRowDescriptor Spacing(PdfLength value)
        {
            box.SetStyle("column-gap", value);
            return this;
        }

        public IContainer Item()
        {
            var item = box.AppendBlock();
            return new ContainerBuilder(item);
        }
    }
}
