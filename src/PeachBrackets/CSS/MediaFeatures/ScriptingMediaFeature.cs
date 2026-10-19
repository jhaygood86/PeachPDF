namespace PeachPDF.CSS
{
    internal sealed class ScriptingMediaFeature : MediaFeature
    {
        private static readonly IValueConverter TheConverter = Map.ScriptingStates.ToConverter();

        public ScriptingMediaFeature() : base(FeatureNames.Scripting)
        {
        }

        internal override IValueConverter Converter => TheConverter;
    }
}