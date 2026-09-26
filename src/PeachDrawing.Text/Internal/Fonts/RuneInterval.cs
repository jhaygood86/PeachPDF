using System.Text;

namespace PeachDrawing.Text.Internal.Fonts
{
    /// <summary>
    /// An inclusive interval of Unicode scalar values: a font face's <c>unicode-range</c> restriction, or the
    /// coverage its cmap declares. Both ends are inclusive and are real <see cref="Rune"/>s, so a surrogate or
    /// out-of-range value cannot be expressed. (The public, PdfGenerator-facing form is <c>RuneRange</c>.)
    /// </summary>
    internal readonly record struct RuneInterval(Rune Start, Rune End)
    {
        /// <summary>Whether <paramref name="rune"/> lies within this interval (both ends inclusive).</summary>
        public bool Contains(Rune rune) => rune.Value >= Start.Value && rune.Value <= End.Value;
    }
}
