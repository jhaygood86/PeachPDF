namespace PeachPDF.CSS
{
    /// <summary>
    /// The <c>hyphenate-limit-chars</c> property (CSS Text 4 §6.3.4): <c>[ auto | &lt;integer&gt; ]{1,3}</c>.
    /// Validated by the shared <see cref="HyphenateLimitCharsGrammar"/> and the authored text preserved for
    /// the layout engine, which re-parses it when deciding whether a candidate break point satisfies the
    /// word/before/after minimums.
    /// </summary>
    internal sealed class HyphenateLimitCharsProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.HyphenateLimitCharsConverter.OrDefault();

        internal HyphenateLimitCharsProperty()
            : base(PropertyNames.HyphenateLimitChars, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
