#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Css
{
    /// <summary>
    /// Validates a <c>shape-outside</c> value (css-shapes-1 §2.1): <c>none</c>, or a <c>circle()</c>, <c>ellipse()</c>,
    /// <c>inset()</c> or <c>polygon()</c> with an optional <c>&lt;shape-box&gt;</c>, or a bare <c>&lt;shape-box&gt;</c>, all through the
    /// shared <see cref="BasicShapeGrammar"/> that <c>clip-path</c> uses. <c>path()</c>, <c>url()</c> and image values are not
    /// shape-outside values this engine supports, so they are dropped at parse time. The authored text is preserved verbatim.
    /// </summary>
    internal sealed class ShapeOutsideValueConverter : IValueConverter
    {
        /// <summary>The grammar check, reused by the property registry's validator.</summary>
        public static object Validate(IReadOnlyList<Token> tokens) => IsValid(tokens) ? new object() : null;

        private static bool IsValid(IReadOnlyList<Token> value)
        {
            var tokens = value.Where(t => t.Type != TokenType.Whitespace).ToArray();
            if (tokens is [{ Type: TokenType.Ident } ident] && ident.Data.Isi(Keywords.None)) return true;

            var shape = BasicShapeGrammar.TryParse(tokens);
            return shape is not null
                && shape.Kind is BasicShapeGrammar.BasicShapeKind.None or BasicShapeGrammar.BasicShapeKind.Polygon
                    or BasicShapeGrammar.BasicShapeKind.Inset or BasicShapeGrammar.BasicShapeKind.Circle
                    or BasicShapeGrammar.BasicShapeKind.Ellipse;
        }

        public IPropertyValue Convert(IReadOnlyList<Token> value) => IsValid(value) ? new ShapeOutsideValue(value) : null;

        public IPropertyValue Construct(Property[] properties) => properties.Guard<ShapeOutsideValue>();

        private sealed class ShapeOutsideValue : IPropertyValue
        {
            public ShapeOutsideValue(IEnumerable<Token> tokens)
            {
                Original = new TokenValue(tokens);
            }

            public string CssText => Original.Text;

            public TokenValue Original { get; }

            public TokenValue ExtractFor(string name) => Original;
        }
    }
}
