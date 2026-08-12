namespace PeachPDF.CSS
{
    /// <summary>Hidden, undocumented compat alias for Prince's own -prince-hyphenate-limit-lines spelling - see hyphenate-limit-lines's css-properties.json entry.</summary>
    internal sealed class PrinceHyphenateLimitLinesProperty : Property
    {
        internal PrinceHyphenateLimitLinesProperty() : base(PropertyNames.PrinceHyphenateLimitLines, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => Converters.HyphenateLimitLinesConverter.OrDefault();
    }
}
