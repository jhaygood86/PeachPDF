namespace PeachPDF.CSS
{
    /// <summary>
    /// Hidden, undocumented compat alias for Prince's own bare bookmark-target spelling (Prince's own
    /// convenience alias for its prince-bookmark-target extension) - see -peachpdf-bookmark-target's
    /// css-properties.json entry. Not css-content-3's bookmark-target, since that property doesn't
    /// exist in the real spec.
    /// </summary>
    internal sealed class BookmarkTargetAliasProperty : Property
    {
        internal BookmarkTargetAliasProperty() : base(PropertyNames.BookmarkTarget)
        {
        }

        internal override IValueConverter Converter => Converters.BookmarkTargetConverter.OrDefault();
    }
}
