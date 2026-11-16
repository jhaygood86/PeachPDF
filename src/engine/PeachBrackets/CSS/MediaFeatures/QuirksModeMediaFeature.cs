namespace PeachBrackets.Css
{
    /// <summary>
    /// The <c>-peachpdf-quirks-mode</c> media feature: true for a document in quirks mode. The user-agent style sheet's block of rules for quirks mode
    /// (HTML Standard 15, "in quirks mode, the following rules are also expected to apply") is written under it, so a document's mode reaches the
    /// cascade as an <c>@media</c> condition, through <c>MediaQueryContext</c>, with no second style sheet to merge.
    /// </summary>
    internal sealed class QuirksModeMediaFeature : MediaFeature
    {
        public QuirksModeMediaFeature() : base(FeatureNames.QuirksMode)
        {
        }

        internal override IValueConverter Converter => Converters.Any;
    }
}
