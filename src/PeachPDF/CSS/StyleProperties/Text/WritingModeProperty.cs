namespace PeachPDF.CSS
{
    /// <summary>The <c>writing-mode</c> property (CSS Writing Modes Level 4 §4.1).</summary>
    internal sealed class WritingModeProperty : Property
    {
        private static readonly IValueConverter StyleConverter =
            Converters.WritingModeConverter.OrDefault(WritingMode.HorizontalTb);

        internal WritingModeProperty()
            : base(PropertyNames.WritingMode, PropertyFlags.Inherited)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
