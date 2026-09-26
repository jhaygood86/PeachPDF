namespace PeachDrawing.Text
{
    /// <summary>Which raised or lowered position of text a <see cref="ScriptPosition"/> is asked for.</summary>
    public enum ScriptPlacement
    {
        /// <summary>Text set lower than the baseline (CSS <c>vertical-align: sub</c>, <c>font-variant-position: sub</c>).</summary>
        Subscript,

        /// <summary>Text set higher than the baseline (CSS <c>vertical-align: super</c>, <c>font-variant-position: super</c>).</summary>
        Superscript
    }

    /// <summary>
    /// How a font's designer wants subscripts or superscripts drawn: the size and offset of the <c>OS/2</c> table's
    /// recommended values.
    /// </summary>
    /// <remarks>
    /// Only the vertical size is used, so a run scaled by it keeps its proportions. Both values are fractions of the em.
    /// </remarks>
    /// <param name="SizeScale">The size of the smaller text as a fraction of the em, to multiply the text size by.</param>
    /// <param name="BaselineShift">How far the text moves from the baseline as a fraction of the em, up for a superscript and down for a subscript.</param>
    public readonly record struct ScriptPosition(double SizeScale, double BaselineShift);
}
