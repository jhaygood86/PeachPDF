namespace PeachDrawing.Text.Unicode
{
    /// <summary>
    /// The positional form a character of a joining script takes: the one that decides which glyph a font substitutes for it.
    /// </summary>
    /// <remarks>
    /// Each form except <see cref="None"/> is named after the OpenType feature tag that asks a font for the glyph in that
    /// form. There are seven and not the four a reading of Arabic alone would suggest, because Syriac has alternate final and
    /// medial forms (<see cref="Fin2"/>, <see cref="Fin3"/>, <see cref="Med2"/>) that fonts define separately.
    /// <see cref="ArabicJoining.Resolve"/> works the forms out for a text.
    /// </remarks>
    public enum ArabicJoiningForm : byte
    {
        /// <summary>
        /// No form is asked for: the character does not join, or it is transparent to joining (a combining mark). The glyph
        /// the font maps it to is used as it is.
        /// </summary>
        None,

        /// <summary>The isolated form (<c>isol</c>): the character joins nothing on either side.</summary>
        Isol,

        /// <summary>The final form (<c>fina</c>): the character joins the one before it.</summary>
        Fina,

        /// <summary>The second final form (<c>fin2</c>), which only Syriac Alaph produces.</summary>
        Fin2,

        /// <summary>The third final form (<c>fin3</c>), which only Syriac Alaph produces.</summary>
        Fin3,

        /// <summary>The medial form (<c>medi</c>): the character joins the ones on both sides.</summary>
        Medi,

        /// <summary>The second medial form (<c>med2</c>), which only Syriac Alaph and Dalath Rish produce.</summary>
        Med2,

        /// <summary>The initial form (<c>init</c>): the character joins the one after it.</summary>
        Init,
    }
}
