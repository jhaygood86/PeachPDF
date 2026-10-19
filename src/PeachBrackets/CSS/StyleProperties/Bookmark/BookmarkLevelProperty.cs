namespace PeachBrackets.Css
{
    internal sealed class BookmarkLevelProperty : Property
    {
        internal BookmarkLevelProperty() : base(PropertyNames.BookmarkLevel)
        {
        }

        internal override IValueConverter Converter => Converters.BookmarkLevelConverter.OrDefault();
    }
}
