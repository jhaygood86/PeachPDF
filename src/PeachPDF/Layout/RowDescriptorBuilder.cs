using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Layout
{
    /// <summary>Builds a horizontal flex row's items, each a new child of <paramref name="box"/> (a <c>display:flex; flex-direction:row</c> box).</summary>
    internal sealed class RowDescriptorBuilder(CssBox box, CssPropertyFactory properties) : IRowDescriptor
    {
        public IRowDescriptor Spacing(PdfLength value)
        {
            properties.Set(box, "column-gap", value);
            return this;
        }

        public IContainer Item()
        {
            var item = CssPropertyFactory.CreateAnonymousBox(box);
            return new ContainerBuilder(item, properties);
        }
    }
}
