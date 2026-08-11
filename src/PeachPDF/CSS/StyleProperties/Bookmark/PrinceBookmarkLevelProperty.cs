namespace PeachPDF.CSS
{
    /// <summary>Hidden, undocumented compat alias for Prince's own -prince-bookmark-level spelling - see bookmark-level's css-properties.json entry.</summary>
    internal sealed class PrinceBookmarkLevelProperty : Property
    {
        internal PrinceBookmarkLevelProperty() : base(PropertyNames.PrinceBookmarkLevel)
        {
        }

        internal override IValueConverter Converter => Converters.BookmarkLevelConverter.OrDefault();
    }
}
