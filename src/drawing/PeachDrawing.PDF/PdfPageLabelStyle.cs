namespace PeachDrawing.PDF;

/// <summary>How the number of a page label is written (ISO 32000-1 12.4.2), set on a <see cref="PdfPageLabelRange"/>.</summary>
public enum PdfPageLabelStyle
{
    /// <summary>No number: the label is its prefix alone.</summary>
    None,

    /// <summary>Decimal Arabic numerals: 1, 2, 3.</summary>
    Decimal,

    /// <summary>Uppercase roman numerals: I, II, III.</summary>
    RomanUpper,

    /// <summary>Lowercase roman numerals: i, ii, iii.</summary>
    RomanLower,

    /// <summary>Uppercase letters: A to Z, then AA to ZZ, then AAA.</summary>
    LettersUpper,

    /// <summary>Lowercase letters: a to z, then aa to zz, then aaa.</summary>
    LettersLower,
}
