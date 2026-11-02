namespace PeachBrackets.Css
{
    /// <summary>
    /// The <c>inverted-colors</c> media feature (Media Queries 5). Matched against the reader's setting in <c>MediaQueryMatcher</c>
    /// (<c>none</c> unless the host reports that the system inverts colours).
    /// </summary>
    internal sealed class InvertedColorsMediaFeature : MediaFeature
    {
        public InvertedColorsMediaFeature() : base(FeatureNames.InvertedColors)
        {
        }

        internal override IValueConverter Converter => Converters.Any;
    }
}
