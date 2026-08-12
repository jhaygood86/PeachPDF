namespace PeachPDF.CSS
{
    /// <summary>
    /// Hidden, undocumented compat alias for Prince's own standalone "hyphenate-after" property
    /// (<c>&lt;integer&gt;</c>, initial 2) - composes into the "after" component of the real
    /// hyphenate-limit-chars property at cascade time - see HyphenateBeforeProperty's doc comment.
    /// </summary>
    internal sealed class HyphenateAfterProperty : Property
    {
        internal HyphenateAfterProperty() : base(PropertyNames.HyphenateAfter, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => Converters.HyphenateBeforeAfterConverter.OrDefault();
    }
}
