namespace PeachPDF.CSS
{
    internal sealed class UnknownProperty : Property
    {
        internal UnknownProperty(string name)
            : base(name)
        {
        }

        internal override IValueConverter Converter => Converters.Any;
    }
}