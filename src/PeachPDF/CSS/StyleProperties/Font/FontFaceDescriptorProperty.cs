namespace PeachPDF.CSS
{
    /// <summary>
    /// The <c>font-weight</c>, <c>font-style</c> and <c>font-stretch</c> descriptors of <c>@font-face</c> (CSS Fonts 4 section 4.4).
    /// Unlike the properties of the same names they take ranges (<c>font-weight: 100 900</c>, <c>font-stretch: 75% 125%</c>,
    /// <c>font-style: oblique 0deg 14deg</c>) and <c>auto</c>, so they are never cascaded and do not share the property grammars;
    /// the text is kept as written and read by <c>FontFaceDescriptorResolver</c>, which treats what it cannot read as <c>auto</c>.
    /// </summary>
    internal sealed class FontFaceDescriptorProperty : Property
    {
        internal FontFaceDescriptorProperty(string name)
            : base(name)
        {
        }

        internal override IValueConverter Converter => Converters.Any;
    }
}
