namespace PeachPDF.CSS
{
    /// <summary>Hidden, undocumented compat alias for Prince's own -prince-hyphenate-before spelling - see HyphenateBeforeProperty.</summary>
    internal sealed class PrinceHyphenateBeforeProperty : Property
    {
        internal PrinceHyphenateBeforeProperty() : base(PropertyNames.PrinceHyphenateBefore, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => Converters.HyphenateBeforeAfterConverter.OrDefault();
    }
}
