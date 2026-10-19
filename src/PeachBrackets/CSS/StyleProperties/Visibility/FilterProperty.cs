namespace PeachPDF.CSS
{
    internal sealed class FilterProperty : Property
    {
        // The filter grammar (none | <filter-function>+) is validated once by the shared FilterGrammar via
        // FilterValueConverter; the authored text is preserved so the paint-time resolver
        // (PeachPDF.Html.Core.Paint.FilterEffectResolver) sees exactly what was written.
        private static readonly IValueConverter StyleConverter = new FilterValueConverter().OrDefault();

        internal FilterProperty()
            : base(PropertyNames.Filter, PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
