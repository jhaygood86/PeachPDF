#nullable disable

namespace PeachPDF.CSS
{
    /// <summary>
    /// Shared <c>&lt;target&gt;</c> argument-shape check for <c>target-counter()</c>/<c>target-text()</c>
    /// (css-content-3 §5) - kept in one place so both converters validate the same grammar rather than
    /// each re-deriving it, per this repo's "don't write two independent parsers for the same CSS value
    /// grammar" convention. Mirrors <c>-peachpdf-bookmark-target</c>'s own <c>&lt;string&gt; | url(#id) |
    /// attr(name)</c> house convention (see <c>BookmarkOutlineBuilder.ResolveTargetValue</c>) rather than
    /// the stricter formal grammar - every real UA also accepts <c>attr(href)</c> in practice.
    /// </summary>
    internal static class TargetGrammar
    {
        public static bool IsValidTarget(Token token) =>
            token is StringToken or UrlToken or FunctionToken { Data: FunctionNames.Attr };
    }
}
