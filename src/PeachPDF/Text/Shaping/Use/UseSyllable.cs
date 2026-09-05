namespace PeachPDF.Text.Shaping.Use
{
    /// <summary>One syllable <see cref="UseSyllableScanner.Scan"/> found: a contiguous
    /// <see cref="Start"/>/<see cref="Length"/> span (indices into the same category array the
    /// scanner was given) and its <see cref="Type"/>.</summary>
    internal readonly record struct UseSyllable(int Start, int Length, UseSyllableType Type);
}
