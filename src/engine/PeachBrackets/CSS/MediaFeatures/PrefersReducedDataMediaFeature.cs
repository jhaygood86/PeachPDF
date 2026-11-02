namespace PeachBrackets.Css
{
    /// <summary>
    /// The <c>prefers-reduced-data</c> media feature (Media Queries 5). Matched against the reader's setting in <c>MediaQueryMatcher</c>
    /// (<c>no-preference</c> unless the host reports that the reader wants less data used).
    /// </summary>
    internal sealed class PrefersReducedDataMediaFeature : MediaFeature
    {
        public PrefersReducedDataMediaFeature() : base(FeatureNames.PrefersReducedData)
        {
        }

        internal override IValueConverter Converter => Converters.Any;
    }
}
