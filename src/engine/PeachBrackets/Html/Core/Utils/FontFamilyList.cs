using System;
using System.Collections.Generic;
using System.Text;
using PeachBrackets.Css;
using PeachBrackets.Html.Parse;

namespace PeachBrackets.Html.Utils
{
    /// <summary>
    /// One family of a <c>font-family</c> list: its <see cref="Name"/> as the value names it (quotes removed, escapes decoded,
    /// whitespace of an unquoted name collapsed) and whether it was written as a string.
    /// </summary>
    /// <param name="Name">the family name</param>
    /// <param name="IsQuoted">true when the family was written as a quoted string</param>
    internal readonly record struct FontFamilyEntry(string Name, bool IsQuoted)
    {
        /// <summary>
        /// True for an unquoted CSS Fonts 4 §2.1 generic family keyword. A quoted <c>"serif"</c> is not one: it names a
        /// family that is literally called serif.
        /// </summary>
        public bool IsGeneric => !IsQuoted && FontFamilyList.IsGenericKeyword(Name);

        /// <summary>True for a quoted name that spells a generic keyword and so must match a registered family of that exact name only.</summary>
        public bool IsQuotedGenericSpelling => IsQuoted && FontFamilyList.IsGenericKeyword(Name);
    }

    /// <summary>
    /// Parses a <c>font-family</c> value (CSS Fonts 4 §2.1: <c>&lt;family-name&gt; = &lt;string&gt; | &lt;custom-ident&gt;+</c>, comma
    /// separated) into its families. Shared by every place that reads the list, so a quoted and an unquoted spelling of one name
    /// always select the same face.
    /// </summary>
    internal static class FontFamilyList
    {
        private static readonly HashSet<string> GenericKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            "serif", "sans-serif", "monospace", "cursive", "fantasy", "system-ui", "ui-serif", "ui-sans-serif",
            "ui-monospace", "ui-rounded", "math", "emoji", "fangsong",
        };

        internal static bool IsGenericKeyword(string name) => GenericKeywords.Contains(name);

        /// <summary>
        /// The families of <paramref name="value"/> in order; empty entries (a stray comma) are dropped. The CSS tokenizer does the
        /// lexing - it already decodes string escapes and keeps a comma inside a string in its token - so this only groups the
        /// tokens between commas: a single string is a quoted family, a run of identifiers is one unquoted name.
        /// </summary>
        internal static FontFamilyEntry[] Parse(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return [];

            using var pooled = CssValueParser.GetCssTokensPooled(value);
            var entries = new List<FontFamilyEntry>();
            var name = new StringBuilder();
            var quoted = false;

            void Flush()
            {
                if (name.Length > 0)
                    entries.Add(new FontFamilyEntry(name.ToString(), quoted));

                name.Clear();
                quoted = false;
            }

            foreach (var token in (List<Token>)pooled)
            {
                switch (token.Type)
                {
                    case TokenType.Comma:
                        Flush();
                        break;
                    case TokenType.Whitespace:
                        break;
                    default:
                        var text = token.Data.ToString();

                        // A string standing alone is the whole name; anything mixed into an identifier run is joined as written.
                        quoted = name.Length == 0 && token.Type == TokenType.String;

                        if (name.Length > 0 && text.Length > 0)
                            name.Append(' ');

                        name.Append(text);
                        break;
                }
            }

            Flush();
            return [.. entries];
        }
    }
}
