namespace PeachPDF.CSS
{
    /// <summary><c>backdrop-filter</c> (Filter Effects Level 2 §3.1): <c>filter</c>'s own grammar. (<c>-webkit-backdrop-filter</c> resolves to this property via <see cref="VendorPropertyAliases"/>.)</summary>
    internal sealed class BackdropFilterProperty : Property
    {
        private static readonly IValueConverter StyleConverter = new FilterValueConverter().OrDefault();

        internal BackdropFilterProperty()
            : base(PropertyNames.BackdropFilter, PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
