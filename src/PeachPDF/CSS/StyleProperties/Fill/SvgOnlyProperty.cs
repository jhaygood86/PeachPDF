namespace PeachPDF.CSS
{
    /// <summary>
    /// A property only an SVG element reads (SVG 2 / Filter Effects / CSS Masking): flood and lighting colours, <c>paint-order</c>, the marker and
    /// mask references, the text baseline properties and the like. It exists so a stylesheet rule or a <c>@keyframes</c> block keeps the declaration
    /// instead of dropping it for want of a property; the SVG builder applies the real grammar when it reads the value, so the converters here only
    /// refuse what is plainly not the property's value (a colour property given a number) and take the rest as written.
    /// </summary>
    internal sealed class SvgOnlyProperty : Property
    {
        /// <summary><c>none</c> or a <c>url()</c> reference: the grammar of <c>marker</c>, <c>marker-start</c>, <c>marker-mid</c> and <c>marker-end</c>.</summary>
        internal static readonly IValueConverter MarkerReference = Converters.Assign(Keywords.None, Keywords.None).Or(Converters.UrlConverter).OrGlobalValue();

        /// <summary>A <c>&lt;length-percentage&gt;</c> or <c>auto</c>: the grammar of the SVG geometry properties <c>r</c>, <c>cx</c>, <c>cy</c>, <c>rx</c>, <c>ry</c>, <c>x</c> and <c>y</c>. A bare number (<c>r: 10</c>) is the attribute's user-unit form, not a length, but a stylesheet takes it too.</summary>
        internal static readonly IValueConverter GeometryLength = Converters.AutoLengthOrPercentConverter.Or(Converters.NumberConverter).OrGlobalValue();

        private readonly IValueConverter _converter;

        internal SvgOnlyProperty(string name, IValueConverter converter)
            : base(name, PropertyFlags.Animatable)
        {
            _converter = converter;
        }

        internal override IValueConverter Converter => _converter;
    }
}
