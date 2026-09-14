using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Layout
{
    /// <summary>Builds a vertical flex column's items, each a new child of <paramref name="box"/> (a <c>display:flex; flex-direction:column</c> box).</summary>
    internal sealed class ColumnDescriptorBuilder(CssBox box, CssPropertyFactory properties) : IColumnDescriptor
    {
        public IColumnDescriptor Spacing(PdfLength value)
        {
            properties.Set(box, "row-gap", value);
            return this;
        }

        public IContainer Item()
        {
            var item = CssPropertyFactory.CreateAnonymousBox(box);
            return new ContainerBuilder(item, properties);
        }
    }
}
