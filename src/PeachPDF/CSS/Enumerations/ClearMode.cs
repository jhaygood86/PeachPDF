namespace PeachPDF.CSS
{
    internal enum ClearMode : byte
    {
        None,
        Left,
        Right,
        Both,

        /// <summary>
        /// CSS Logical Properties §2.2: clears floats on the inline-start side of the containing block
        /// (see <see cref="Floating.InlineStart"/>); resolved to <see cref="Left"/>/<see cref="Right"/> by
        /// <c>CssBox.EffectiveClear</c>.
        /// </summary>
        InlineStart,

        /// <summary>The mirror of <see cref="InlineStart"/>: clears floats on the inline-end side.</summary>
        InlineEnd
    }
}