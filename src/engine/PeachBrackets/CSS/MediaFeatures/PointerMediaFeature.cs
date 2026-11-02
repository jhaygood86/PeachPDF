namespace PeachBrackets.Css
{
    internal sealed class PointerMediaFeature : MediaFeature
    {
        private static readonly IValueConverter TheConverter = Map.PointerAccuracies.ToConverter().Option();

        public PointerMediaFeature() : base(FeatureNames.Pointer)
        {
        }

        internal override IValueConverter Converter => TheConverter;
    }
}