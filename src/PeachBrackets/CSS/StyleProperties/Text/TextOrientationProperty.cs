namespace PeachPDF.CSS
{
    /// <summary>The <c>text-orientation</c> property (CSS Writing Modes Level 4 §5.2).</summary>
    internal sealed class TextOrientationProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.TextOrientationConverter.OrDefault(TextOrientation.Mixed);

        internal TextOrientationProperty()
            : base(PropertyNames.TextOrientation, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
