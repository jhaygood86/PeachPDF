// Derived from PDFsharp (MIT) - see LICENSE.md

namespace PeachPDF.PDF
{
    /// <summary>Custom .NET number format strings for the numbers written to a PDF (invariant culture, no trailing zeros).</summary>
    internal static class PdfNumberFormats
    {
        /// <summary>Up to two fraction digits.</summary>
        public const string TwoDecimals = "0.##";

        /// <summary>Up to three fraction digits.</summary>
        public const string ThreeDecimals = "0.###";

        /// <summary>Up to four fraction digits.</summary>
        public const string FourDecimals = "0.####";

        /// <summary>Up to seven fraction digits.</summary>
        public const string SevenDecimals = "0.#######";

        /// <summary>Up to ten fraction digits.</summary>
        public const string TenDecimals = "0.##########";
    }
}
