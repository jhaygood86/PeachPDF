namespace PeachPDF.CSS
{
    using static Converters;

    internal sealed class TransformOriginProperty : Property
    {
        // CSS Transforms 1 (https://www.w3.org/TR/css-transforms-1/#transform-origin-property):
        //   [ left | center | right | top | bottom | <length-percentage> ]
        // | [ left | center | right | <length-percentage> ] [ top | center | bottom | <length-percentage> ] <length>?
        // | [ [ center | left | right ] && [ center | top | bottom ] ] <length>?
        //
        // The two-value <length-percentage> form is ORDERED (horizontal then vertical). Only the keyword-only
        // form may be reordered ("center left" == "left center"). So "2px left" and "top 2px" are invalid: a
        // length in one axis forces the ordered form, and the remaining token is a keyword for the wrong axis.
        private static readonly IValueConverter Horizontal =
            LengthOrPercentConverter
                .Or(Keywords.Left, Length.Zero)
                .Or(Keywords.Right, Length.Full)
                .Or(Keywords.Center, Length.Half);

        private static readonly IValueConverter Vertical =
            LengthOrPercentConverter
                .Or(Keywords.Top, Length.Zero)
                .Or(Keywords.Bottom, Length.Full)
                .Or(Keywords.Center, Length.Half);

        private static readonly IValueConverter HorizontalKeyword =
            Assign(Keywords.Left, Length.Zero)
                .Or(Keywords.Right, Length.Full)
                .Or(Keywords.Center, Length.Half);

        private static readonly IValueConverter VerticalKeyword =
            Assign(Keywords.Top, Length.Zero)
                .Or(Keywords.Bottom, Length.Full)
                .Or(Keywords.Center, Length.Half);

        private static readonly IValueConverter StyleConverter = WithOrder(
            LengthOrPercentConverter.Or(Keywords.Center, Point.Center)
                // Ordered horizontal-then-vertical (rejects a keyword in the wrong axis position).
                .Or(WithOrder(Horizontal.Option(Length.Half), Vertical.Option(Length.Half)))
                // Keyword-only form, any order ("center left"): a token accepted by both axes must not be
                // claimed positionally, so this needs order-independent matching.
                .Or(WithAnyOrderIndependent(HorizontalKeyword.Option(Length.Half), VerticalKeyword.Option(Length.Half)))
                .Required(),
            LengthConverter.Option(Length.Zero)).OrDefault(Point.Center);

        internal TransformOriginProperty()
            : base(PropertyNames.TransformOrigin, PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
