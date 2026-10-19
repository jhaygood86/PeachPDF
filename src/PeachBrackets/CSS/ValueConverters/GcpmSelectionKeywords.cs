namespace PeachPDF.CSS
{
    /// <summary>
    /// The <c>first</c>/<c>start</c>/<c>last</c>/<c>first-except</c> keyword set css-gcpm-3 defines
    /// identically as the optional second argument of both <c>string()</c> and <c>element()</c> - shared
    /// so <see cref="StringFunctionConverter"/> and <see cref="GcpmElementFunctionConverter"/> validate
    /// against one grammar rather than two independently-maintained copies.
    /// </summary>
    internal static class GcpmSelectionKeywords
    {
        internal static readonly string[] Values = ["first", "last", "start", "first-except"];
    }
}
