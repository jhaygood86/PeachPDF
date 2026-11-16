using PeachBrackets.Building;
using System.Collections.Generic;

namespace PeachPDF.Layout
{
    /// <summary>
    /// Builds a vertical column's items, each a new child of <paramref name="box"/>: a <c>display:flex; flex-direction:column</c> box, or
    /// with <paramref name="blockFlow"/> a plain block whose items stack as blocks and are separated by a top margin, because the content
    /// of a multi-column container flows from one column into the next and a flex container's items do not.
    /// </summary>
    internal sealed class ColumnDescriptorBuilder(DocumentNode box, bool blockFlow = false) : IColumnDescriptor
    {
        private readonly List<DocumentNode> _items = [];
        private PdfLength? _spacing;

        public IColumnDescriptor Spacing(PdfLength value)
        {
            if (!blockFlow)
            {
                box.SetStyle("row-gap", value);
                return this;
            }

            _spacing = value;
            for (var i = 1; i < _items.Count; i++) _items[i].SetStyle("margin-top", value);
            return this;
        }

        public IContainer Item()
        {
            var item = box.AppendBlock();
            if (blockFlow)
            {
                item.SetStyle("display", "block");
                if (_items.Count > 0 && _spacing is { } spacing) item.SetStyle("margin-top", spacing);
                _items.Add(item);
            }

            return new ContainerBuilder(item);
        }
    }
}
