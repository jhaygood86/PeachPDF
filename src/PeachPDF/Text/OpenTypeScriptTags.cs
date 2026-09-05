using System;
using System.Collections.Generic;

namespace PeachPDF.Text
{
    /// <summary>
    /// Resolves a Unicode <c>Script</c> property value (<see cref="ScriptTable"/>, e.g.
    /// <c>"Arabic"</c>, <c>"Latin"</c>) to an OpenType 4-character *old-style* script tag (e.g.
    /// <c>"arab"</c>, <c>"latn"</c>) for <see cref="GsubShaper"/>'s script-table selection
    /// (<c>GsubTable</c>'s <c>ScriptList</c> lookup - see <c>GsubShaper.ScriptPreference</c>, which this
    /// replaces real script detection for). Old-style tags only for v1: OpenType's 2016-era *new-style*
    /// tags (<c>dev2</c>/<c>bng2</c>/etc. - a handful of Indic scripts whose shaping model changed
    /// enough to need a second, incompatible `Script` table) are a real-font-testing-driven follow-up
    /// (Phase 4/5b), not a mechanical extension of this table - see
    /// <c>.claude/accepted-gaps/no-text-shaping.md</c>.
    /// </summary>
    /// <remarks>
    /// A hand-curated subset (the ISO 15924 → OpenType-tag mapping is not a mechanical lowercase of the
    /// Unicode script name — a handful of scripts keep a legacy 3-letter-plus-space tag instead, e.g.
    /// <c>Lao</c> → <c>"lao "</c>, <c>Nko</c> → <c>"nko "</c>, <c>Vai</c> → <c>"vai "</c>, <c>Yi</c> →
    /// <c>"yi  "</c>, and <c>Hiragana</c>/<c>Katakana</c> both collapse to the single combined <c>"kana"</c>
    /// script — mirroring the exception list HarfBuzz's own <c>hb_ot_old_tag_from_script</c> encodes),
    /// not the full ~170-script Unicode <c>Script</c> property vocabulary. A script absent from this
    /// table simply isn't recognized (the caller falls back to <c>GsubShaper.ScriptPreference</c>'s
    /// existing <c>"latn"</c>/<c>"DFLT"</c> behavior, never worse than before this table existed).
    /// <c>Common</c>/<c>Inherited</c> (the two non-script <see cref="ScriptTable"/> values covering
    /// punctuation/whitespace/digits/combining-marks) are deliberately absent — resolving one of those to
    /// a real script is a text-run concern (scan the surrounding text for the nearest actual script, UAX
    /// #24 §5.1), not something a single-codepoint tag lookup can answer on its own.
    /// </remarks>
    internal static class OpenTypeScriptTags
    {
        private static readonly IReadOnlyDictionary<string, string> Tags = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // The 13 scripts ArabicShaping.txt/DerivedJoiningType.txt cover (Phase 3's direct
            // consumers) - see that file's own header for the section/table cross-references.
            ["Arabic"] = "arab",
            ["Syriac"] = "syrc",
            ["Mandaic"] = "mand",
            ["Manichaean"] = "mani",
            ["Psalter_Pahlavi"] = "phlp",
            ["Chorasmian"] = "chrs",
            ["Mongolian"] = "mong",
            ["Phags_Pa"] = "phag",
            ["Sogdian"] = "sogd",
            ["Old_Uyghur"] = "ougr",
            ["Hanifi_Rohingya"] = "rohg",
            ["Nko"] = "nko ",
            ["Adlam"] = "adlm",

            // Common non-joining scripts, so script-run detection resolves a sensible tag for ordinary
            // text too, not just the joining-script subset above.
            ["Latin"] = "latn",
            ["Greek"] = "grek",
            ["Cyrillic"] = "cyrl",
            ["Armenian"] = "armn",
            ["Georgian"] = "geor",
            ["Han"] = "hani",
            ["Hiragana"] = "kana",
            ["Katakana"] = "kana",
            ["Hangul"] = "hang",
            ["Hebrew"] = "hebr",
            ["Thai"] = "thai",
            ["Lao"] = "lao ",
            ["Khmer"] = "khmr",
            ["Myanmar"] = "mymr",
            ["Tibetan"] = "tibt",
            ["Thaana"] = "thaa",
            ["Sinhala"] = "sinh",
            ["Vai"] = "vai ",
            ["Yi"] = "yi  ",

            // Indic scripts (old-style tags) - Devanagari is Phase 5b's own direct consumer; the rest
            // are included for the same "ordinary text gets a sensible tag" reason as the block above.
            ["Devanagari"] = "deva",
            ["Bengali"] = "beng",
            ["Gujarati"] = "gujr",
            ["Gurmukhi"] = "guru",
            ["Kannada"] = "knda",
            ["Malayalam"] = "mlym",
            ["Oriya"] = "orya",
            ["Tamil"] = "taml",
            ["Telugu"] = "telu",
        };

        /// <summary>Resolves a Unicode <c>Script</c> property value (as returned by <see cref="ScriptTable.Of(int)"/>)
        /// to an OpenType old-style script tag, or null if absent from this curated table.</summary>
        public static string? Resolve(string? unicodeScript) =>
            unicodeScript is not null && Tags.TryGetValue(unicodeScript, out var tag) ? tag : null;
    }
}
