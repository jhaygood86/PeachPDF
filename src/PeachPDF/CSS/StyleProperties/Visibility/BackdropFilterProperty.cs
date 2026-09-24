namespace PeachPDF.CSS
{
    /// <summary><c>backdrop-filter</c> (Filter Effects Level 2 §3.1) and its <c>-webkit-</c> spelling: <c>filter</c>'s own grammar.</summary>
    internal sealed class BackdropFilterProperty : Property
    {
        private static readonly IValueConverter StyleConverter = new FilterValueConverter().OrDefault();

        internal BackdropFilterProperty(string name)
            : base(name, PropertyFlags.Animatable)
        {
        }

        internal override IValueConverter Converter => StyleConverter;
    }
}
