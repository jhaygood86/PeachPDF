namespace PeachBrackets.Css
{
    /// <summary>
    /// The <c>forced-colors</c> media feature (Media Queries 5). Matched against the renderer's fixed <c>none</c> stance in
    /// <c>MediaQueryMatcher</c>: a PDF is never drawn under a user agent's forced palette.
    /// </summary>
    internal sealed class ForcedColorsMediaFeature : MediaFeature
    {
        public ForcedColorsMediaFeature() : base(FeatureNames.ForcedColors)
        {
        }

        internal override IValueConverter Converter => Converters.Any;
    }
}
