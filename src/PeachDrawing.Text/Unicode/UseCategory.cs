namespace PeachDrawing.Text.Unicode
{
    /// <summary>
    /// The category a character has in the Universal Shaping Engine (USE), the model that shapes Indic scripts: the alphabet its
    /// syllable grammar is written over and its reordering acts on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// USE categories are not a Unicode property. They are worked out from the Indic syllabic and positional categories and the
    /// general category of each character, by <see cref="UniversalShaping.Classify"/>.
    /// </para>
    /// <para>
    /// Only the categories that Devanagari, Bengali, Gujarati and Tamil can produce are here. A script that needs more (medial
    /// consonants, or a static reordering killer) needs new members.
    /// </para>
    /// </remarks>
    public enum UseCategory : byte
    {
        /// <summary>A character that takes no part in syllable structure: punctuation, another script's letters, stray marks. It forms a syllable on its own and is never reordered.</summary>
        O,

        /// <summary>A syllable's base: an ordinary consonant, an independent vowel, a digit or an avagraha.</summary>
        B,

        /// <summary>A combining grapheme joiner or zero-width joiner, which is dropped from the syllable grammar altogether.</summary>
        CGJ,

        /// <summary>A halant (virama), which suppresses the inherent vowel of the consonant before it.</summary>
        H,

        /// <summary>A zero-width non-joiner, which stops conjuncts and ligatures forming across it.</summary>
        ZWNJ,

        /// <summary>A repha, the raised form of a leading consonant. No character has this category statically: it is given to a leading glyph that the font's <c>rphf</c> feature actually substituted.</summary>
        R,

        /// <summary>A consonant modifier that sits above the base. None occur in Devanagari itself.</summary>
        CMAbv,

        /// <summary>A consonant modifier that sits below the base, such as the nukta.</summary>
        CMBlw,

        /// <summary>A dependent vowel sign (matra) that is written before the base: the one category that reordering moves, to just after the nearest halant or the start of the syllable.</summary>
        VPre,

        /// <summary>A dependent vowel sign above the base.</summary>
        VAbv,

        /// <summary>A dependent vowel sign below the base.</summary>
        VBlw,

        /// <summary>A dependent vowel sign written after the base.</summary>
        VPst,

        /// <summary>A vowel modifier (bindu, visarga, tone mark) written before the base.</summary>
        VMPre,

        /// <summary>A vowel modifier above the base, such as the anusvara and candrabindu.</summary>
        VMAbv,

        /// <summary>A vowel modifier below the base, such as the anudatta.</summary>
        VMBlw,

        /// <summary>A vowel modifier written after the base, such as the visarga.</summary>
        VMPst,

        /// <summary>A consonant placeholder that stands where a base consonant would, which is Bengali anji. It leads a syllable as a base does and is reordered as one.</summary>
        GB,

        /// <summary>A final syllable modifier above the base, which is the Bengali sandhi mark.</summary>
        FMAbv,
    }
}
