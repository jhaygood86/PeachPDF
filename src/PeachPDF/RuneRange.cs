using System.Text;

namespace PeachPDF
{
    /// <summary>
    /// An inclusive range of Unicode scalar values (codepoints), e.g. the <c>U+0000-00FF</c> of a CSS
    /// <c>@font-face</c> <c>unicode-range</c> descriptor, or the coverage a font declares for a face
    /// registered via <see cref="PdfGenerator"/>'s <c>AddFontFromStream</c>.
    /// Both ends are inclusive - unlike <see cref="System.Range"/>, which is half-open and int/index
    /// based - and the endpoints are real <see cref="Rune"/>s, so surrogate/out-of-range values can't be
    /// expressed by construction.
    /// </summary>
    public readonly record struct RuneRange(Rune Start, Rune End)
    {
        /// <summary>Whether <paramref name="rune"/> lies within this range (both ends inclusive).</summary>
        public bool Contains(Rune rune) => rune.Value >= Start.Value && rune.Value <= End.Value;
    }
}
