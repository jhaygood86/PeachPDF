namespace PeachBrackets.Css
{
    /// <summary><c>scroll-margin</c>: one to four lengths, top first (CSS Scroll Snap 1 §3.1).</summary>
    internal sealed class ScrollMarginProperty : ShorthandProperty
    {
        private static readonly IValueConverter StyleConverter = Converters.LengthConverter.Periodic(
                PropertyNames.ScrollMarginTop, PropertyNames.ScrollMarginRight, PropertyNames.ScrollMarginBottom, PropertyNames.ScrollMarginLeft)
            .OrDefault(Length.Zero);

        internal ScrollMarginProperty() : base(PropertyNames.ScrollMargin)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }

    /// <summary><c>scroll-padding</c>: one to four lengths, top first (CSS Scroll Snap 1 §3.2).</summary>
    internal sealed class ScrollPaddingProperty : ShorthandProperty
    {
        private static readonly IValueConverter StyleConverter = Converters.LengthConverter.Periodic(
                PropertyNames.ScrollPaddingTop, PropertyNames.ScrollPaddingRight, PropertyNames.ScrollPaddingBottom, PropertyNames.ScrollPaddingLeft)
            .OrDefault(Length.Zero);

        internal ScrollPaddingProperty() : base(PropertyNames.ScrollPadding)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }

    /// <summary><c>overscroll-behavior</c>: one value for both axes, or <c>x</c> then <c>y</c> (CSS Overscroll Behavior 1 §3).</summary>
    internal sealed class OverscrollBehaviorProperty : ShorthandProperty
    {
        private static readonly IValueConverter StyleConverter = Converters.OverscrollBehaviorConverter
            .OrGlobalValue()
            .Periodic(PropertyNames.OverscrollBehaviorX, PropertyNames.OverscrollBehaviorY);

        internal OverscrollBehaviorProperty() : base(PropertyNames.OverscrollBehavior)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
