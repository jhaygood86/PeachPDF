namespace PeachBrackets.Css
{
    /// <summary>
    /// The <c>overflow-clip-margin-inline</c> shorthand (css-overflow-4 §3.2): <c>&lt;visual-box&gt; || &lt;length&gt;</c>, which every longhand it stands for takes.
    /// </summary>
    internal sealed class OverflowClipMarginInlineProperty : ShorthandProperty
    {
        private static readonly IValueConverter StyleConverter = new OverflowClipMarginShorthandConverter(PropertyNames.OverflowClipMarginInlineStart, PropertyNames.OverflowClipMarginInlineEnd).OrDefault();

        internal OverflowClipMarginInlineProperty()
            : base(PropertyNames.OverflowClipMarginInline)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
