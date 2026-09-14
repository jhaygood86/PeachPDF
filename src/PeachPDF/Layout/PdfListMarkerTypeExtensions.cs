using System;

namespace PeachPDF.Layout
{
    /// <summary>Maps <see cref="PdfListMarkerType"/> to the exact <c>list-style-type</c> keyword text PeachPDF's CSS parser accepts.</summary>
    internal static class PdfListMarkerTypeExtensions
    {
        public static string ToKeyword(this PdfListMarkerType markerType) => markerType switch
        {
            PdfListMarkerType.Disc => "disc",
            PdfListMarkerType.Circle => "circle",
            PdfListMarkerType.Square => "square",
            PdfListMarkerType.None => "none",
            PdfListMarkerType.Decimal => "decimal",
            PdfListMarkerType.DecimalLeadingZero => "decimal-leading-zero",
            PdfListMarkerType.LowerRoman => "lower-roman",
            PdfListMarkerType.UpperRoman => "upper-roman",
            PdfListMarkerType.LowerAlpha => "lower-alpha",
            PdfListMarkerType.UpperAlpha => "upper-alpha",
            PdfListMarkerType.LowerGreek => "lower-greek",
            PdfListMarkerType.Armenian => "armenian",
            PdfListMarkerType.LowerArmenian => "lower-armenian",
            PdfListMarkerType.Georgian => "georgian",
            PdfListMarkerType.Hebrew => "hebrew",
            PdfListMarkerType.Hiragana => "hiragana",
            PdfListMarkerType.HiraganaIroha => "hiragana-iroha",
            PdfListMarkerType.Katakana => "katakana",
            PdfListMarkerType.KatakanaIroha => "katakana-iroha",
            PdfListMarkerType.ArabicIndic => "arabic-indic",
            PdfListMarkerType.Bengali => "bengali",
            PdfListMarkerType.Cambodian => "cambodian",
            PdfListMarkerType.CjkDecimal => "cjk-decimal",
            PdfListMarkerType.CjkEarthlyBranch => "cjk-earthly-branch",
            PdfListMarkerType.CjkHeavenlyStem => "cjk-heavenly-stem",
            PdfListMarkerType.Devanagari => "devanagari",
            PdfListMarkerType.DisclosureOpen => "disclosure-open",
            PdfListMarkerType.DisclosureClosed => "disclosure-closed",
            PdfListMarkerType.EthiopicNumeric => "ethiopic-numeric",
            PdfListMarkerType.Gujarati => "gujarati",
            PdfListMarkerType.Gurmukhi => "gurmukhi",
            PdfListMarkerType.Kannada => "kannada",
            PdfListMarkerType.Lao => "lao",
            PdfListMarkerType.Malayalam => "malayalam",
            PdfListMarkerType.Mongolian => "mongolian",
            PdfListMarkerType.Myanmar => "myanmar",
            PdfListMarkerType.Oriya => "oriya",
            PdfListMarkerType.Persian => "persian",
            PdfListMarkerType.Tamil => "tamil",
            PdfListMarkerType.Telugu => "telugu",
            PdfListMarkerType.Thai => "thai",
            PdfListMarkerType.Tibetan => "tibetan",
            _ => throw new ArgumentOutOfRangeException(nameof(markerType), markerType, null)
        };
    }
}
