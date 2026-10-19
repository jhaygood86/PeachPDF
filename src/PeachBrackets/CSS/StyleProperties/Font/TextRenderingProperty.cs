namespace PeachPDF.CSS
{
    /// <summary><c>text-rendering</c>: <c>auto | optimizeSpeed | optimizeLegibility | geometricPrecision</c>. Inherited.</summary>
    internal sealed class TextRenderingProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.TextRenderingModeConverter.OrDefault(TextRenderingMode.Auto);

        internal TextRenderingProperty()
            : base(PropertyNames.TextRendering, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
