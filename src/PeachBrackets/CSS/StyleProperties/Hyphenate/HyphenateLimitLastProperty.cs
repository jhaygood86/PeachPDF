namespace PeachPDF.CSS
{
    /// <summary>The <c>hyphenate-limit-last</c> property (CSS Text 4 §6.3.5): <c>none | always | column | page | spread</c>.</summary>
    internal sealed class HyphenateLimitLastProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.HyphenateLimitLastConverter.OrDefault();

        internal HyphenateLimitLastProperty()
            : base(PropertyNames.HyphenateLimitLast, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
