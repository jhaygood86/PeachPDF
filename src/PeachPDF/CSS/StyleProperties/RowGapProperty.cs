namespace PeachPDF.CSS
{
    internal sealed class RowGapProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.LengthOrPercentOrNormalConverter
                                                                           .OrGlobalValue()
                                                                           .OrDefault(0);

        internal RowGapProperty()
            : base(PropertyNames.RowGap, PropertyFlags.Animatable)
        { }

        internal override IValueConverter Converter => StyleConverter;
    }
}
