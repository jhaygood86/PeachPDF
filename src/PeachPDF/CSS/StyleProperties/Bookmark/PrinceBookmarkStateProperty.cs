namespace PeachPDF.CSS
{
    /// <summary>Hidden, undocumented compat alias for Prince's own -prince-bookmark-state spelling - see bookmark-state's css-properties.json entry.</summary>
    internal sealed class PrinceBookmarkStateProperty : Property
    {
        internal PrinceBookmarkStateProperty() : base(PropertyNames.PrinceBookmarkState)
        {
        }

        internal override IValueConverter Converter => Converters.BookmarkStateConverter.OrDefault();
    }
}
