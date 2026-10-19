namespace PeachPDF.CSS
{
    /// <summary>Hidden, undocumented compat alias for Prince's own "hyphenate-lines" spelling of CSS Text 4's hyphenate-limit-lines - see that property's css-properties.json entry.</summary>
    internal sealed class HyphenateLinesProperty : Property
    {
        internal HyphenateLinesProperty() : base(PropertyNames.HyphenateLines, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => Converters.HyphenateLimitLinesConverter.OrDefault();
    }
}
