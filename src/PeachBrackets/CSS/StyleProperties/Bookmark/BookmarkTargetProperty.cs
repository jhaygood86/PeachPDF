namespace PeachPDF.CSS
{
    internal sealed class BookmarkTargetProperty : Property
    {
        internal BookmarkTargetProperty() : base(PropertyNames.PeachPdfBookmarkTarget)
        {
        }

        internal override IValueConverter Converter => Converters.BookmarkTargetConverter.OrDefault();
    }
}
