namespace PeachPDF.CSS
{
    /// <summary>
    /// Hidden, undocumented compat alias for Prince's own standalone "hyphenate-before" property
    /// (<c>&lt;integer&gt;</c>, initial 2) - Prince has no combined hyphenate-limit-chars of its own, so
    /// this composes into the "before" component of the real hyphenate-limit-chars property at cascade
    /// time instead of aliasing a longhand 1:1 - see that property's css-properties.json entry and
    /// HyphenateLimitCharsGrammar.WithBefore.
    /// </summary>
    internal sealed class HyphenateBeforeProperty : Property
    {
        internal HyphenateBeforeProperty() : base(PropertyNames.HyphenateBefore, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => Converters.HyphenateBeforeAfterConverter.OrDefault();
    }
}
