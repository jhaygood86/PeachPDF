namespace PeachDrawing.Text.Unicode
{
    /// <summary>A maximal run of consecutive characters that share one resolved bidi embedding level.</summary>
    /// <param name="Start">Index of the run's first element in the sequence the levels describe.</param>
    /// <param name="Length">Number of elements in the run.</param>
    /// <param name="Level">The embedding level shared by every element of the run; odd levels are right-to-left.</param>
    public readonly record struct BidiRun(int Start, int Length, byte Level)
    {
        /// <summary>Whether the run reads right to left, which is to say its level is odd.</summary>
        public bool IsRtl => (Level & 1) == 1;
    }
}
