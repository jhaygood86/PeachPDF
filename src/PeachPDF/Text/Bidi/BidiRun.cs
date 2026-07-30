namespace PeachPDF.Text.Bidi
{
    /// <summary>A maximal run of consecutive characters sharing one resolved bidi embedding level.</summary>
    internal readonly record struct BidiRun(int Start, int Length, byte Level)
    {
        public bool IsRtl => (Level & 1) == 1;
    }
}
