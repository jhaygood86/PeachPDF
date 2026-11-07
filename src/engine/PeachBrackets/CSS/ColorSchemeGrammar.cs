#nullable disable

using System;
using System.Collections.Generic;

namespace PeachBrackets.Css
{
    /// <summary>
    /// The computed <c>color-scheme</c> (CSS Color Adjust 1 §2): <c>normal | [ light | dark | &lt;custom-ident&gt; ]+ &amp;&amp; only?</c>.
    /// One grammar serves the CSS-OM converter and the render layer, which reads the stored declaration text with
    /// <see cref="TryParse(string)"/> (the <see cref="ImageOrientation"/> precedent).
    /// </summary>
    /// <param name="Light">The list names <c>light</c>.</param>
    /// <param name="Dark">The list names <c>dark</c>.</param>
    /// <param name="Only">The <c>only</c> keyword was written: the UA must not force a scheme the list does not name.</param>
    internal readonly record struct ColorScheme(bool Light, bool Dark, bool Only)
    {
        /// <summary><c>normal</c>: no scheme is named, so the page is light.</summary>
        public static ColorScheme Normal => default;

        /// <summary>
        /// The scheme used for an element (CSS Color Adjust 1 §2.1): when both <c>light</c> and <c>dark</c> are named the user's
        /// preference decides; when one is named it is that one; <c>normal</c> and a list of unknown identifiers are light.
        /// </summary>
        /// <param name="preferDark">Whether the user's preference (<c>prefers-color-scheme</c>) is dark.</param>
        public bool UsesDark(bool preferDark) => Light && Dark ? preferDark : Dark;

        /// <summary>Parses declaration text; <see cref="Normal"/> for empty or invalid text.</summary>
        public static ColorScheme Parse(string text) => TryParse(text) ?? Normal;

        /// <summary>Parses declaration text; null when it is not valid <c>color-scheme</c> syntax.</summary>
        public static ColorScheme? TryParse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            using var pooled = Html.Parse.CssValueParser.GetCssTokensPooled(text.Trim());
            List<Token> tokens = pooled;
            tokens.RemoveAll(static t => t.Type == TokenType.Whitespace);
            return TryParse(tokens);
        }

        /// <summary>The grammar over whitespace-free tokens; null when invalid.</summary>
        public static ColorScheme? TryParse(IReadOnlyList<Token> tokens)
        {
            if (tokens is null || tokens.Count == 0) return null;

            if (tokens.Count == 1 && tokens[0].Type == TokenType.Ident && tokens[0].Data.Isi(Keywords.Normal))
                return Normal;

            bool light = false, dark = false, only = false;
            var schemes = 0;
            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token.Type != TokenType.Ident) return null;

                var name = token.Data;
                if (name.Isi("only"))
                {
                    // `only` is written once, and at either end of the list.
                    if (only || (i != 0 && i != tokens.Count - 1)) return null;
                    only = true;
                    continue;
                }

                // CSS-wide keywords and `normal` are not custom identifiers (css-values-4 §4.2).
                if (name.Isi(Keywords.Normal) || name.Isi("initial") || name.Isi("inherit") || name.Isi("unset")
                    || name.Isi("revert") || name.Isi("revert-layer") || name.Isi("default"))
                    return null;

                if (name.Isi("light")) light = true;
                else if (name.Isi("dark")) dark = true;

                schemes++;
            }

            return schemes == 0 ? null : new ColorScheme(light, dark, only);
        }
    }
}
