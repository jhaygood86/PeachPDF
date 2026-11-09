namespace PeachBrackets.Css
{
    /// <summary>
    /// The <c>overflow-clip-margin-block</c> shorthand (css-overflow-4 §3.2): <c>&lt;visual-box&gt; || &lt;length&gt;</c>, which every longhand it stands for takes.
    /// </summary>
    internal sealed class OverflowClipMarginBlockProperty : ShorthandProperty
    {
        private static readonly IValueConverter StyleConverter = new OverflowClipMarginShorthandConverter(PropertyNames.OverflowClipMarginBlockStart, PropertyNames.OverflowClipMarginBlockEnd).OrDefault();

        internal OverflowClipMarginBlockProperty()
            : base(PropertyNames.OverflowClipMarginBlock)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
