using System;
using System.Collections.Generic;
using PeachDrawing.Text.Internal.Text;
using System.Text;

namespace PeachDrawing.Text.Unicode
{
    /// <summary>
    /// Unicode scripts (UAX #24): the script of a character, and the script each character of a text is treated
    /// as belonging to once shared characters have been resolved against their surroundings.
    /// </summary>
    /// <remarks>
    /// Scripts are named as the Unicode <c>Script</c> property names them, for example <c>Latin</c>,
    /// <c>Arabic</c>, <c>Han</c>. Punctuation, digits and spaces used by many scripts are <see cref="Common"/>, and
    /// combining marks that take their base's script are <see cref="Inherited"/>.
    /// </remarks>
    public static class Scripts
    {
        /// <summary>The script of a code point the Unicode Character Database does not assign one.</summary>
        public const string Unknown = ScriptTable.Unknown;

        /// <summary>The script of characters used by many scripts, such as punctuation, digits and spaces.</summary>
        public const string Common = ScriptTable.Common;

        /// <summary>The script of characters that take the script of the character they combine with.</summary>
        public const string Inherited = ScriptTable.Inherited;

        /// <summary>The <c>Script</c> property of a character, with no resolution against surrounding text.</summary>
        /// <param name="rune">The character.</param>
        public static string Of(Rune rune) => ScriptTable.Of(rune);

        /// <summary>The <c>Script</c> property of a code point, with no resolution against surrounding text.</summary>
        /// <param name="codepoint">A Unicode code point.</param>
        public static string Of(int codepoint) => ScriptTable.Of(codepoint);

        /// <summary>
        /// Gives every code point of a text the script it is to be treated as (UAX #24 section 5.1): a character
        /// with a real script keeps it, and one that is <see cref="Common"/> or <see cref="Inherited"/> takes the
        /// nearest preceding real script, or the nearest following one if the text opens with such characters.
        /// A text with no real script at all has nothing to resolve against: its <see cref="Inherited"/> characters
        /// become <see cref="Common"/> and the others stay as they are.
        /// </summary>
        /// <param name="codepoints">The text as code points.</param>
        /// <returns>One script per entry of <paramref name="codepoints"/>.</returns>
        public static IReadOnlyList<string> Resolve(IReadOnlyList<int> codepoints)
        {
            ArgumentNullException.ThrowIfNull(codepoints);
            return ScriptRunResolver.Resolve(codepoints);
        }

        /// <summary>
        /// The same resolution as <see cref="Resolve"/>, for a caller that already looked up each character's script.
        /// </summary>
        /// <param name="scripts">The unresolved script of each character, as <see cref="Of(int)"/> returns it.</param>
        /// <returns>One resolved script per entry of <paramref name="scripts"/>.</returns>
        public static IReadOnlyList<string> ResolveLooked(IReadOnlyList<string> scripts)
        {
            ArgumentNullException.ThrowIfNull(scripts);
            return ScriptRunResolver.ResolveRaw(scripts);
        }
    }

    /// <summary>
    /// The four-letter tags OpenType uses in place of script and language names.
    /// </summary>
    /// <remarks>
    /// Both lookups cover the scripts and languages that fonts in practice provide layout for, not every value
    /// the registries define, and answer <see langword="null"/> for one they do not know. A caller treats that as
    /// "no particular script or language".
    /// </remarks>
    public static class OpenTypeTags
    {
        /// <summary>The OpenType script tag of a Unicode script, such as <c>arab</c> for <c>Arabic</c>.</summary>
        /// <param name="script">A name as <see cref="Scripts"/> returns it. <see cref="Scripts.Common"/> and <see cref="Scripts.Inherited"/> have no tag.</param>
        /// <returns>The tag, or <see langword="null"/> when the script is not one the table covers.</returns>
        public static string? ForScript(string? script) => OpenTypeScriptTags.Resolve(script);

        /// <summary>The OpenType language-system tag of a BCP 47 language tag, such as <c>ENG</c> for <c>en-US</c>.</summary>
        /// <param name="language">A BCP 47 tag. Progressively shorter prefixes are tried, so <c>en-US</c> falls back to <c>en</c>.</param>
        /// <returns>The tag, or <see langword="null"/> when the language is empty or not one the table covers.</returns>
        public static string? ForLanguage(string? language) => OpenTypeLanguageTags.Resolve(language);
    }
}
