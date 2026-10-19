using PeachBrackets.Building;

namespace PeachPDF.Layout
{
    /// <summary>The declarative builders' lengths and colours, written to a node as the canonical CSS token the HTML core understands.</summary>
    internal static class DocumentNodeStyleExtensions
    {
        /// <summary>Sets <paramref name="node"/>'s <paramref name="propertyName"/> to <paramref name="value"/>'s canonical CSS length token.</summary>
        public static void SetStyle(this DocumentNode node, string propertyName, PdfLength value) =>
            node.SetStyle(propertyName, value.ToCssText());

        /// <summary>Sets <paramref name="node"/>'s <paramref name="propertyName"/> to <paramref name="value"/>'s canonical CSS color token.</summary>
        public static void SetStyle(this DocumentNode node, string propertyName, PdfColor value) =>
            node.SetStyle(propertyName, value.ToCssText());
    }
}
