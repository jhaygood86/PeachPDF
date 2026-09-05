namespace PeachPDF.Text
{
    /// <summary>
    /// Unicode's <c>Indic_Positional_Category</c> property (UAX #44) - the second of HarfBuzz's
    /// Universal Shaping Engine's two raw per-codepoint inputs (see
    /// <see cref="IndicSyllabicCategory"/>'s own remarks), consumed by
    /// <see cref="PeachPDF.Text.Shaping.Use.UseCategoryClassifier"/> to pick a matra/modifier's
    /// <c>Abv</c>/<c>Blw</c>/<c>Pst</c>/<c>Pre</c> suffix (e.g. a <c>Left</c>-positioned dependent
    /// vowel sign becomes USE category <c>VPre</c>, the one category
    /// <c>PeachPDF.Text.Shaping.Use.UseReorderer</c> moves before the syllable's base consonant).
    /// Member names strip underscores from the UCD value, matching
    /// <see cref="IndicSyllabicCategory"/>'s own convention.
    /// </summary>
    internal enum IndicPositionalCategory : byte
    {
        /// <summary>The file's own <c>@missing</c> default - every codepoint the UCD data doesn't
        /// explicitly list, and every non-positional category (a plain consonant, a halant, etc.).</summary>
        NotApplicable = 0,
        Right,
        Left,
        VisualOrderLeft,
        LeftAndRight,
        Top,
        Bottom,
        TopAndBottom,
        TopAndRight,
        TopAndLeft,
        TopAndLeftAndRight,
        BottomAndRight,
        BottomAndLeft,
        TopAndBottomAndRight,
        TopAndBottomAndLeft,
        Overstruck,
    }
}
