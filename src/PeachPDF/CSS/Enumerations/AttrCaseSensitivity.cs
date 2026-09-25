namespace PeachPDF.CSS
{
    /// <summary>
    /// The case-sensitivity modifier of an attribute selector's value (Selectors 4 §6.3):
    /// <c>[attr=value i]</c> and <c>[attr=value s]</c>.
    /// </summary>
    internal enum AttrCaseSensitivity : byte
    {
        /// <summary>No modifier: the value is compared the way the document language says (for HTML, ASCII case-insensitively).</summary>
        Default,

        /// <summary>The <c>i</c> modifier: the value is compared ASCII case-insensitively.</summary>
        Insensitive,

        /// <summary>The <c>s</c> modifier: the value is compared case-sensitively.</summary>
        Sensitive
    }
}
