namespace PeachPDF.CSS
{
    using static Converters;

    /// <summary>
    /// <c>normal | [ stylistic(&lt;ident&gt;) || historical-forms || styleset(&lt;ident&gt;#) ||
    /// character-variant(&lt;ident&gt;#) || swash(&lt;ident&gt;) || ornaments(&lt;ident&gt;) ||
    /// annotation(&lt;ident&gt;) ]</c> (CSS Fonts Module Level 4 §6.8). Real GSUB activation (not just
    /// parsing/cascading) happens via <c>DerivedStyle.ActualFontVariantAlternates</c> -&gt;
    /// <see cref="PeachPDF.Text.TextShapingFeatures.ExplicitFeatures"/>, resolved against the
    /// document's <c>@font-feature-values</c> registry
    /// (<see cref="PeachPDF.Html.Core.FontVariantAlternatesResolver"/>).
    /// </summary>
    internal sealed class FontVariantAlternatesProperty : Property
    {
        // Exposed for css-properties.json's "cssom-grammar" validator (ValidatorExpressionBuilder),
        // which calls this same real grammar directly instead of the full cssom PropertyFactory/
        // StylesheetParser round trip - see CLAUDE.md's "one parser" rule.
        internal static readonly IValueConverter ValueGrammar = new FontVariantAlternatesGrammar();

        private static readonly IValueConverter StyleConverter = ValueGrammar.OrDefault();

        internal FontVariantAlternatesProperty()
            : base(PropertyNames.FontVariantAlternates, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
