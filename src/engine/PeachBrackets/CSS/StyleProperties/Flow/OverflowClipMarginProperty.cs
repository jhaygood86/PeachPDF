namespace PeachBrackets.Css
{
    /// <summary>
    /// The <c>overflow-clip-margin</c> shorthand (css-overflow-4 §3.2): <c>&lt;visual-box&gt; || &lt;length&gt;</c>, which every longhand it stands for takes.
    /// </summary>
    internal sealed class OverflowClipMarginProperty : ShorthandProperty
    {
        private static readonly IValueConverter StyleConverter = new OverflowClipMarginShorthandConverter(PropertyNames.OverflowClipMarginTop, PropertyNames.OverflowClipMarginRight, PropertyNames.OverflowClipMarginBottom, PropertyNames.OverflowClipMarginLeft).OrDefault();

        internal OverflowClipMarginProperty()
            : base(PropertyNames.OverflowClipMargin)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
