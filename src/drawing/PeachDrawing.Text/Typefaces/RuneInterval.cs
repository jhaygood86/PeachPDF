using System.Text;

namespace PeachDrawing.Text
{
    /// <summary>
    /// An inclusive interval of Unicode scalar values: the code points a face is to be used for (CSS
    /// <c>unicode-range</c>), or the ones its <c>cmap</c> covers.
    /// </summary>
    /// <remarks>
    /// Both ends are <see cref="Rune"/>s, so a surrogate or an out-of-range value cannot be expressed.
    /// </remarks>
    /// <param name="Start">The first code point of the interval.</param>
    /// <param name="End">The last code point of the interval, which is inside it.</param>
    public readonly record struct RuneInterval(Rune Start, Rune End)
    {
        /// <summary>Whether a character lies inside this interval, both ends included.</summary>
        /// <param name="rune">The character to test.</param>
        public bool Contains(Rune rune) => rune.Value >= Start.Value && rune.Value <= End.Value;
    }
}
