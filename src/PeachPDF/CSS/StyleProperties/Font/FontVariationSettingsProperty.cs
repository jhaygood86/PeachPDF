namespace PeachPDF.CSS
{
    using static Converters;

    /// <summary>
    /// <c>normal | [ &lt;string&gt; &lt;number&gt; ]#</c> - a value for each named axis of a variable font (CSS Fonts 4 section 6.10).
    /// Applied where the font is created, see <c>FontVariationSettingsResolver</c>.
    /// </summary>
    internal sealed class FontVariationSettingsProperty : Property
    {
        private static readonly IValueConverter AxisValueConverter =
            WithOrder(StringConverter.Required(), NumberConverter.Required());

        // Exposed for css-properties.json's "cssom-grammar" validator, like FontFeatureSettingsProperty.ValueGrammar.
        internal static readonly IValueConverter ValueGrammar =
            AxisValueConverter.FromList().Or(Keywords.Normal);

        private static readonly IValueConverter StyleConverter = ValueGrammar.OrDefault();

        internal FontVariationSettingsProperty()
            : base(PropertyNames.FontVariationSettings, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
