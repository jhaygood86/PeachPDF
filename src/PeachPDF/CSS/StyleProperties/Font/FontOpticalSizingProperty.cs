namespace PeachPDF.CSS
{
    /// <summary>
    /// <c>auto | none</c> - whether a variable font's <c>opsz</c> axis follows the font size (<c>auto</c>, the initial value) or stays at
    /// the font's default. Applied where the font is created, see <c>FontVariationSettingsResolver</c>.
    /// </summary>
    internal sealed class FontOpticalSizingProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.FontOpticalSizingModeConverter.OrDefault(FontOpticalSizingMode.Auto);

        internal FontOpticalSizingProperty()
            : base(PropertyNames.FontOpticalSizing, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
