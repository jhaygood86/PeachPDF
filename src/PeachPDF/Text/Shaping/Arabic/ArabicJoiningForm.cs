namespace PeachPDF.Text.Shaping.Arabic
{
    /// <summary>
    /// The positional joining form <see cref="ArabicJoiningShaper"/> resolves for one character - maps
    /// 1:1 to the OpenType GSUB feature tag that requests that character's glyph in that form
    /// (<c>isol</c>/<c>fina</c>/<c>fin2</c>/<c>fin3</c>/<c>medi</c>/<c>med2</c>/<c>init</c>). Seven forms
    /// rather than the simpler four (isolated/initial/medial/final) a pure-Arabic reading might expect:
    /// <see cref="Fin2"/>/<see cref="Fin3"/>/<see cref="Med2"/> are alternate final/medial forms only the
    /// Syriac joining groups <c>ALAPH</c>/<c>DALATH_RISH</c> ever produce (see
    /// <see cref="ArabicJoiningStateTable"/>'s own remarks) - collapsing them into plain
    /// <see cref="Fina"/>/<see cref="Medi"/> would request the wrong OpenType feature for a Syriac font
    /// that defines these forms separately, which real fonts do.
    /// </summary>
    internal enum ArabicJoiningForm : byte
    {
        /// <summary>No positional form requested - either the character doesn't participate in joining
        /// at all (Joining_Type <c>U</c>), or is transparent to it (Joining_Type <c>T</c>, a combining
        /// mark). The glyph's own default (isolated) form is used, un-substituted.</summary>
        None,
        Isol,
        Fina,
        Fin2,
        Fin3,
        Medi,
        Med2,
        Init,
    }
}
