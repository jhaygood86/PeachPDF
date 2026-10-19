namespace PeachPDF.CSS
{
    internal sealed class ColumnGapProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.LengthOrPercentOrNormalConverter
                      .OrGlobalValue()
                      .OrDefault(new Length(1f, Length.Unit.Em));

        internal ColumnGapProperty()
            : base(PropertyNames.ColumnGap, PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}