namespace PeachPDF.CSS
{
    /// <summary>
    /// The inert <c>@font-face {{ font-variant: ... }}</c> descriptor (<see cref="IFontFaceRule.Variant"/>) -
    /// a flat, never-cascaded declaration, so it needs its own restricted-grammar class rather than
    /// reusing <see cref="FontVariantProperty"/> (now a <see cref="ShorthandProperty"/>, which doesn't
    /// fit a flat descriptor). Never consumed anywhere in the render pipeline, same as before this
    /// property was split out of the old combined class.
    /// </summary>
    internal sealed class FontFaceVariantProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.FontVariantCss2Converter.OrDefault(FontVariantCaps.Normal);

        internal FontFaceVariantProperty()
            : base(PropertyNames.FontVariant)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
