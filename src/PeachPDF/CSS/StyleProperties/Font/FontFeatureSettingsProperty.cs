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

        // Exposed for css-properties.json's "cssom-grammar" validator (ValidatorExpressionBuilder),
        // which calls this same real grammar directly instead of the full cssom PropertyFactory/
        // StylesheetParser round trip - see CLAUDE.md's "one parser" rule.
        internal static readonly IValueConverter ValueGrammar =
            FeatureTagValueConverter.FromList().Or(Keywords.Normal);

        private static readonly IValueConverter StyleConverter = ValueGrammar.OrDefault();

        internal FontFeatureSettingsProperty()
            : base(PropertyNames.FontFeatureSettings, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
