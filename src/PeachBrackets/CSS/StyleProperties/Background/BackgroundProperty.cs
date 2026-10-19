namespace PeachBrackets.Css
{
    using System.Collections.Generic;
    using static Converters;

    internal sealed class BackgroundProperty : ShorthandProperty
    {
        internal BackgroundProperty()
            : base(PropertyNames.Background, PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;

        private static readonly IValueConverter NormalLayerConverter = WithAny(
            OptionalImageSourceConverter.Option().For(PropertyNames.BackgroundImage),
            WithOrder(
                PointConverter.Option().For(PropertyNames.BackgroundPosition),
                BackgroundSizeConverter.StartsWithDelimiter().Option().For(PropertyNames.BackgroundSize)),
            BackgroundRepeatsConverter.Option().For(PropertyNames.BackgroundRepeat),
            BackgroundAttachmentConverter.Option().For(PropertyNames.BackgroundAttachment),
            BoxModelConverter.Option().For(PropertyNames.BackgroundOrigin),
            BackgroundClipConverter.Option().For(PropertyNames.BackgroundClip));

        private static readonly IValueConverter FinalLayerConverter = WithAny(
            OptionalImageSourceConverter.Option().For(PropertyNames.BackgroundImage),
            WithOrder(
                PointConverter.Option().For(PropertyNames.BackgroundPosition),
                BackgroundSizeConverter.StartsWithDelimiter().Option().For(PropertyNames.BackgroundSize)),
            BackgroundRepeatsConverter.Option().For(PropertyNames.BackgroundRepeat),
            BackgroundAttachmentConverter.Option().For(PropertyNames.BackgroundAttachment),
            BoxModelConverter.Option().For(PropertyNames.BackgroundOrigin),
            BackgroundClipConverter.Option().For(PropertyNames.BackgroundClip),
            CurrentColorConverter.Option().For(PropertyNames.BackgroundColor));

        // What a layer takes for a longhand its own text leaves out (the initial values, CSS Backgrounds 3 section 2.10). The colour is not here:
        // it is set by the last layer only.
        private static readonly Dictionary<string, string> LayerDefaults = new()
        {
            [PropertyNames.BackgroundImage] = "none",
            [PropertyNames.BackgroundPosition] = "0% 0%",
            [PropertyNames.BackgroundSize] = "auto",
            [PropertyNames.BackgroundRepeat] = "repeat",
            [PropertyNames.BackgroundAttachment] = "scroll",
            [PropertyNames.BackgroundOrigin] = "padding-box",
            [PropertyNames.BackgroundClip] = "border-box",
        };

        private static readonly IValueConverter StyleConverter =
            NormalLayerConverter.RequiresEnd(FinalLayerConverter, LayerDefaults).OrDefault();
    }
}