using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Layout
{
    /// <summary>The declarative builders' lengths and colours, written to a box as the canonical CSS token the HTML core understands.</summary>
    internal static class CssPropertyFactoryExtensions
    {
        /// <summary>Sets <paramref name="box"/>'s <paramref name="propertyName"/> to <paramref name="value"/>'s canonical CSS length token.</summary>
        public static void Set(this CssPropertyFactory properties, CssBox box, string propertyName, PdfLength value) =>
            properties.Set(box, propertyName, value.ToCssText());

        /// <summary>Sets <paramref name="box"/>'s <paramref name="propertyName"/> to <paramref name="value"/>'s canonical CSS color token.</summary>
        public static void Set(this CssPropertyFactory properties, CssBox box, string propertyName, PdfColor value) =>
            properties.Set(box, propertyName, value.ToCssText());
    }
}
