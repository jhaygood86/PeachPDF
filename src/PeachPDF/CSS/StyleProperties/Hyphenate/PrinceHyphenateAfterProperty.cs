namespace PeachPDF.CSS
{
    /// <summary>Hidden, undocumented compat alias for Prince's own -prince-hyphenate-after spelling - see HyphenateAfterProperty.</summary>
    internal sealed class PrinceHyphenateAfterProperty : Property
    {
        internal PrinceHyphenateAfterProperty() : base(PropertyNames.PrinceHyphenateAfter, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => Converters.HyphenateBeforeAfterConverter.OrDefault();
    }
}
