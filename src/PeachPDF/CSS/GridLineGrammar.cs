#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// A parsed grid <c>&lt;grid-line&gt;</c> (one edge of <c>grid-column</c>/<c>grid-row</c>/<c>grid-area</c>):
    /// <c>auto</c>, an integer line number (may be negative — counting from the end edge), or <c>span N</c>.
    /// Named lines (<c>[name]</c>) are out of v1 scope.
    /// </summary>
    internal sealed class GridLine
    {
        public bool IsAuto { get; private init; }
        public bool IsSpan { get; private init; }

        /// <summary>The line number (for a line reference) or the span count (when <see cref="IsSpan"/>).</summary>
        public int Value { get; private init; }

        public static readonly GridLine Auto = new() { IsAuto = true };
        public static GridLine Span(int n) => new() { IsSpan = true, Value = n };
        public static GridLine Line(int n) => new() { Value = n };
    }

    /// <summary>
    /// Shared grammar for a single grid <c>&lt;grid-line&gt;</c> value. Used by Layer A (to validate the
    /// <c>grid-column-start</c>/<c>-end</c>/<c>grid-row-start</c>/<c>-end</c> longhands) and by Layer B (the
    /// grid placement engine).
    /// </summary>
    internal static class GridLineGrammar
    {
        internal static GridLine TryParse(IReadOnlyList<Token> tokens)
        {
            var toks = tokens.Where(t => t.Type != TokenType.Whitespace).ToArray();
            if (toks.Length == 0) return null;

            // auto
            if (toks.Length == 1 && toks[0].Type == TokenType.Ident && toks[0].Data.Isi(Keywords.Auto))
                return GridLine.Auto;

            // <integer>
            if (toks.Length == 1 && toks[0] is NumberToken { IsInteger: true } number && number.IntegerValue != 0)
                return GridLine.Line(number.IntegerValue);

            // span <integer [1,∞]>
            if (toks.Length == 2 && toks[0].Type == TokenType.Ident && toks[0].Data.Isi(Keywords.Span)
                && toks[1] is NumberToken { IsInteger: true } spanCount && spanCount.IntegerValue >= 1)
                return GridLine.Span(spanCount.IntegerValue);

            return null;
        }
    }
}
