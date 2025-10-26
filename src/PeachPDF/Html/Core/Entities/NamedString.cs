namespace PeachPDF.Html.Core.Entities
{
    /// <summary>
    /// Represents a named string defined by the string-set CSS property.
    /// Named strings can be used in running headers and footers.
    /// </summary>
    internal record NamedString(
        string Name,
        string Value
    );
}
