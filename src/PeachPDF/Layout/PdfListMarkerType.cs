namespace PeachPDF.Layout
{
    /// <summary>
    /// A list's marker numbering/bullet style, mapping directly onto <c>list-style-type</c>'s full
    /// supported keyword set (CSS Lists Level 3 / CSS Counter Styles Level 3) - every value here already
    /// renders correctly, not a curated subset.
    /// </summary>
    public enum PdfListMarkerType
    {
        /// <summary>A solid disc bullet (●).</summary>
        Disc,
        /// <summary>A hollow circle bullet (○).</summary>
        Circle,
        /// <summary>A solid square bullet (■).</summary>
        Square,
        /// <summary>No marker at all.</summary>
        None,
        /// <summary>Arabic numerals (1, 2, 3, ...).</summary>
        Decimal,
        /// <summary>Arabic numerals, zero-padded to at least two digits (01, 02, ..., 10, ...).</summary>
        DecimalLeadingZero,
        /// <summary>Lowercase Roman numerals (i, ii, iii, ...).</summary>
        LowerRoman,
        /// <summary>Uppercase Roman numerals (I, II, III, ...).</summary>
        UpperRoman,
        /// <summary>Lowercase Latin letters (a, b, c, ...).</summary>
        LowerAlpha,
        /// <summary>Uppercase Latin letters (A, B, C, ...).</summary>
        UpperAlpha,
        /// <summary>Lowercase classical Greek letters (α, β, γ, ...).</summary>
        LowerGreek,
        /// <summary>Armenian numbering.</summary>
        Armenian,
        /// <summary>Lowercase Armenian numbering.</summary>
        LowerArmenian,
        /// <summary>Georgian numbering.</summary>
        Georgian,
        /// <summary>Hebrew numbering.</summary>
        Hebrew,
        /// <summary>Hiragana lettering.</summary>
        Hiragana,
        /// <summary>Hiragana lettering in the traditional iroha ordering.</summary>
        HiraganaIroha,
        /// <summary>Katakana lettering.</summary>
        Katakana,
        /// <summary>Katakana lettering in the traditional iroha ordering.</summary>
        KatakanaIroha,
        /// <summary>Arabic-Indic numerals.</summary>
        ArabicIndic,
        /// <summary>Bengali numerals.</summary>
        Bengali,
        /// <summary>Khmer/Cambodian numerals.</summary>
        Cambodian,
        /// <summary>CJK decimal numerals.</summary>
        CjkDecimal,
        /// <summary>CJK earthly branch numbering.</summary>
        CjkEarthlyBranch,
        /// <summary>CJK heavenly stem numbering.</summary>
        CjkHeavenlyStem,
        /// <summary>Devanagari numerals.</summary>
        Devanagari,
        /// <summary>An open triangular disclosure marker (▽), typically for an expanded state.</summary>
        DisclosureOpen,
        /// <summary>A closed triangular disclosure marker (▷), typically for a collapsed state.</summary>
        DisclosureClosed,
        /// <summary>Ethiopic numeric numbering.</summary>
        EthiopicNumeric,
        /// <summary>Gujarati numerals.</summary>
        Gujarati,
        /// <summary>Gurmukhi numerals.</summary>
        Gurmukhi,
        /// <summary>Kannada numerals.</summary>
        Kannada,
        /// <summary>Lao numerals.</summary>
        Lao,
        /// <summary>Malayalam numerals.</summary>
        Malayalam,
        /// <summary>Mongolian numerals.</summary>
        Mongolian,
        /// <summary>Myanmar numerals.</summary>
        Myanmar,
        /// <summary>Oriya numerals.</summary>
        Oriya,
        /// <summary>Persian numerals.</summary>
        Persian,
        /// <summary>Tamil numerals.</summary>
        Tamil,
        /// <summary>Telugu numerals.</summary>
        Telugu,
        /// <summary>Thai numerals.</summary>
        Thai,
        /// <summary>Tibetan numerals.</summary>
        Tibetan
    }
}
