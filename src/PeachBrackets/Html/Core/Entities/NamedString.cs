namespace PeachPDF.Html.Core.Entities
{
    /// <summary>
    /// Represents a named string defined by the string-set CSS property.
    /// Named strings can be used in running headers and footers.
    /// Y is the vertical position (in internal layout units) of the element that produced this string.
    /// </summary>
    internal record NamedString(
        string Name,
        string Value,
        double Y = 0
    )
    {
        public double Y { get; set; } = Y;
    }
}
