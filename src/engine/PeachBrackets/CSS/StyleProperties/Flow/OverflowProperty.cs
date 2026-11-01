namespace PeachBrackets.Css
{
    /// <summary>
    /// The <c>overflow</c> shorthand (css-overflow-3 §3.1): one value sets both axes, two set
    /// <c>overflow-x</c> then <c>overflow-y</c>.
    /// </summary>
    internal sealed class OverflowProperty : ShorthandProperty
    {
        private static readonly IValueConverter StyleConverter = Converters.OverflowModeConverter
                                                                           .OrGlobalValue()
                                                                           .Periodic(PropertyNames.OverflowX, PropertyNames.OverflowY);

        internal OverflowProperty()
            : base(PropertyNames.Overflow)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
