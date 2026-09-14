namespace PeachPDF.CSS
{
    internal sealed class BlockEllipsisProperty : Property
    {
        private static readonly IValueConverter StyleConverter = Converters.BlockEllipsisConverter;

        internal BlockEllipsisProperty()
            : base(PropertyNames.BlockEllipsis)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
