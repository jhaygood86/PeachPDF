namespace PeachPDF.CSS
{
    /// <summary>Hidden, undocumented compat alias for Prince's own -prince-hyphenate-character spelling - see hyphenate-character's css-properties.json entry.</summary>
    internal sealed class PrinceHyphenateCharacterProperty : Property
    {
        internal PrinceHyphenateCharacterProperty() : base(PropertyNames.PrinceHyphenateCharacter, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => Converters.HyphenateCharacterConverter.OrDefault();
    }
}
