namespace PeachBrackets.Css
{
    internal sealed class UnknownMediaFeature : MediaFeature
    {
        public UnknownMediaFeature(string name) : base(name)
        {
        }

        internal override IValueConverter Converter => Converters.Any;
    }
}