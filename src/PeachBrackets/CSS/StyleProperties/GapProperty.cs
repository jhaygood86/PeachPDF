namespace PeachPDF.CSS
{
    internal class GapProperty : ShorthandProperty
    {
        private static readonly IValueConverter StyleConverter = Converters.LengthOrPercentOrNormalConverter
                                                                           .OrGlobalValue()
                                                                           .Periodic(PropertyNames.RowGap, PropertyNames.ColumnGap);

        internal GapProperty()
            : base(PropertyNames.Gap, PropertyFlags.Animatable)
        { }

        internal override IValueConverter Converter => StyleConverter;
    }
}
