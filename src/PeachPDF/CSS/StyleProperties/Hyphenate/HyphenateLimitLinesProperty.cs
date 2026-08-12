namespace PeachPDF.CSS
{
    /// <summary>The <c>hyphenate-limit-lines</c> property (CSS Text 4 §6.3.5): <c>no-limit | &lt;integer [0,∞]&gt;</c>.</summary>
    internal sealed class HyphenateLimitLinesProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.HyphenateLimitLinesConverter.OrDefault();

        internal HyphenateLimitLinesProperty()
            : base(PropertyNames.HyphenateLimitLines, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
