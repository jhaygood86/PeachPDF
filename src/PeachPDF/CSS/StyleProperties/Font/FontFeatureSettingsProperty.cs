namespace PeachPDF.CSS
{
    using static Converters;

    /// <summary>
    /// <c>normal | &lt;feature-tag-value&gt;#</c>, where <c>&lt;feature-tag-value&gt; = &lt;string&gt;
    /// [ &lt;integer&gt; | on | off ]?</c> - the standard CSS Fonts Level 3 escape hatch for activating
    /// an arbitrary OpenType feature by tag. Real GSUB activation (not just parsing/cascading) happens
    /// via <c>DerivedStyle.ActualFontFeatureSettings</c> -&gt;
    /// <see cref="PeachPDF.Text.TextShapingFeatures.ExplicitFeatures"/>.
    /// </summary>
    internal sealed class FontFeatureSettingsProperty : Property
    {
        private static readonly IValueConverter FeatureTagValueConverter =
            WithOrder(StringConverter.Required(), FontFeatureTagListGrammar.FeatureValueConverter.Option());

        private static readonly IValueConverter StyleConverter =
            FeatureTagValueConverter.FromList().Or(Keywords.Normal).OrDefault();

        internal FontFeatureSettingsProperty()
            : base(PropertyNames.FontFeatureSettings, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
