namespace PeachPDF.Text
{
    /// <summary>
    /// The four values of Unicode's <c>Vertical_Orientation</c> property
    /// (<see href="https://www.unicode.org/reports/tr50/">UAX #50</see>), which classifies how a
    /// codepoint's glyph is oriented when set in vertical text. Every assigned codepoint resolves to
    /// exactly one of these - see <see cref="VerticalOrientationTable"/>.
    /// </summary>
    internal enum VerticalOrientationClass : byte
    {
        /// <summary>Upright, the same orientation as in the code charts.</summary>
        U,

        /// <summary>Rotated 90 degrees clockwise compared to the code charts.</summary>
        R,

        /// <summary>
        /// Transformed typographically (a dedicated vertical glyph form), falling back to <see cref="U"/>
        /// when no such form is available - which is always, for a renderer with no vertical-form GSUB
        /// substitution (<c>vert</c>/<c>vrt2</c>). Consumers with no vertical-glyph-substitution pipeline
        /// should treat this identically to <see cref="U"/>.
        /// </summary>
        Tu,

        /// <summary>
        /// Transformed typographically, falling back to <see cref="R"/> when no dedicated vertical glyph
        /// form is available - which is always, for a renderer with no vertical-form GSUB substitution.
        /// Consumers with no vertical-glyph-substitution pipeline should treat this identically to
        /// <see cref="R"/>.
        /// </summary>
        Tr
    }
}
