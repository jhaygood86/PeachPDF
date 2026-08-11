namespace PeachPDF.CSS
{
    /// <summary>Hidden, undocumented compat alias for Prince's own -prince-bookmark-label spelling - see bookmark-label's css-properties.json entry.</summary>
    internal sealed class PrinceBookmarkLabelProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.BookmarkLabelConverter.OrDefault();

        internal PrinceBookmarkLabelProperty() : base(PropertyNames.PrinceBookmarkLabel)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
