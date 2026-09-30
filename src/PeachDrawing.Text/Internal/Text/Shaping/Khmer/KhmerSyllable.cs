namespace PeachDrawing.Text.Internal.Text.Shaping.Khmer
{
    /// <summary>One syllable <see cref="KhmerSyllableScanner.Scan"/> found: a contiguous
    /// <see cref="Start"/>/<see cref="Length"/> span (indices into the same category array the
    /// scanner was given) and its <see cref="Type"/>.</summary>
    internal readonly record struct KhmerSyllable(int Start, int Length, KhmerSyllableType Type);
}
