namespace PeachBrackets.Css
{
    internal static class WritingModeExtensions
    {
        /// <summary>
        /// Whether lines stack along the horizontal axis - every value but <c>horizontal-tb</c>. <c>sideways-rl</c> and
        /// <c>sideways-lr</c> are vertical writing modes too (CSS Writing Modes 4 section 4.1): they lay out like
        /// <c>vertical-rl</c>/<c>vertical-lr</c> and differ only in glyph orientation (always sideways), with <c>sideways-lr</c> also
        /// reading bottom to top.
        /// </summary>
        internal static bool IsVertical(this WritingMode mode) => mode != WritingMode.HorizontalTb;
    }
}
