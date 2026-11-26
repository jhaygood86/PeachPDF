namespace PeachBrackets.Css
{
    internal sealed class ScriptingMediaFeature : MediaFeature
    {
        private static readonly IValueConverter TheConverter = Map.ScriptingStates.ToConverter().Option();

        public ScriptingMediaFeature() : base(FeatureNames.Scripting)
        {
        }

        internal override IValueConverter Converter => TheConverter;
    }
}