namespace PeachPDF.CSS
{
    /// <summary>The <c>hyphenate-character</c> property (CSS Text 4 §6.3.1): <c>auto | &lt;string&gt;</c>.</summary>
    internal sealed class HyphenateCharacterProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.HyphenateCharacterConverter.OrDefault();

        internal HyphenateCharacterProperty()
            : base(PropertyNames.HyphenateCharacter, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
