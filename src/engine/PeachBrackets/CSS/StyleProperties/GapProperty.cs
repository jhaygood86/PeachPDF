namespace PeachBrackets.Css
{
    internal class GapProperty : ShorthandProperty
    {
        private static readonly IValueConverter StyleConverter = Converters.NonNegativeLengthOrPercentOrNormalConverter
                                                                           .OrGlobalValue()
                                                                           .Periodic(PropertyNames.RowGap, PropertyNames.ColumnGap);

        internal GapProperty()
            : base(PropertyNames.Gap, PropertyFlags.Animatable)
        { }

        internal override IValueConverter Converter => StyleConverter;
    }
}
