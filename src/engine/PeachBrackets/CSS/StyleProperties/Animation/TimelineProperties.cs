namespace PeachBrackets.Css
{
    using static Converters;

    /// <summary><c>scroll-timeline: &lt;name&gt; &lt;axis&gt;?</c> (Scroll-driven Animations 1 §3.2).</summary>
    internal sealed class ScrollTimelineProperty : ShorthandProperty
    {
        private static readonly IValueConverter TimelineConverter = WithOrder(
            TimelineNameConverter.Option().For(PropertyNames.ScrollTimelineName),
            TimelineAxisConverter.Option().For(PropertyNames.ScrollTimelineAxis)).OrDefault();

        internal ScrollTimelineProperty() : base(PropertyNames.ScrollTimeline)
        {
        }

        internal override IValueConverter Converter => TimelineConverter;
    }

    /// <summary><c>view-timeline: &lt;name&gt; [ &lt;axis&gt; || &lt;inset&gt; ]?</c> (Scroll-driven Animations 1 §4.2).</summary>
    internal sealed class ViewTimelineProperty : ShorthandProperty
    {
        private static readonly IValueConverter TimelineConverter = WithOrder(
            TimelineNameConverter.Option().For(PropertyNames.ViewTimelineName),
            TimelineAxisConverter.Option().For(PropertyNames.ViewTimelineAxis),
            TimelineInsetConverter.Option().For(PropertyNames.ViewTimelineInset)).OrDefault();

        internal ViewTimelineProperty() : base(PropertyNames.ViewTimeline)
        {
        }

        internal override IValueConverter Converter => TimelineConverter;
    }
}
