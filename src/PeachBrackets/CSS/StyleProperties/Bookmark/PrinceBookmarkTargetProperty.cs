namespace PeachPDF.CSS
{
    /// <summary>Hidden, undocumented compat alias for Prince's own -prince-bookmark-target spelling - see -peachpdf-bookmark-target's css-properties.json entry.</summary>
    internal sealed class PrinceBookmarkTargetProperty : Property
    {
        internal PrinceBookmarkTargetProperty() : base(PropertyNames.PrinceBookmarkTarget)
        {
        }

        internal override IValueConverter Converter => Converters.BookmarkTargetConverter.OrDefault();
    }
}
