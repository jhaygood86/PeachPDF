namespace PeachPDF.CSS
{
    /// <summary>The <c>hyphenate-limit-zone</c> property (CSS Text 4 §6.3.3): <c>&lt;length-percentage&gt;</c>.</summary>
    internal sealed class HyphenateLimitZoneProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.LengthOrPercentConverter.OrDefault(Length.Zero);

        internal HyphenateLimitZoneProperty()
            : base(PropertyNames.HyphenateLimitZone, PropertyFlags.Inherited | PropertyFlags.Unitless)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
