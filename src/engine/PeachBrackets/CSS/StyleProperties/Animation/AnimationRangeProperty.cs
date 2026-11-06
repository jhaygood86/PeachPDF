namespace PeachBrackets.Css
{
    using static Converters;

    /// <summary><c>animation-range: &lt;start&gt; &lt;end&gt;?</c> (Scroll-driven Animations 1 §4.2).</summary>
    internal sealed class AnimationRangeProperty : ShorthandProperty
    {
        private static readonly IValueConverter RangeConverter = WithOrder(
            RangeEdgeConverter.Option().For(PropertyNames.AnimationRangeStart),
            RangeEdgeConverter.Option().For(PropertyNames.AnimationRangeEnd)).OrDefault();

        internal AnimationRangeProperty() : base(PropertyNames.AnimationRange)
        {
        }

        internal override IValueConverter Converter => RangeConverter;
    }
}
