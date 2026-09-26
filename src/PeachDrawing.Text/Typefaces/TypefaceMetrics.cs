using PeachDrawing.Text.Internal.Fonts.OpenType;

namespace PeachDrawing.Text
{
    /// <summary>
    /// The vertical dimensions of a <see cref="Typeface"/>, in design units: whole numbers on the grid the font was drawn
    /// on, <see cref="UnitsPerEm"/> of them to the em.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nothing here depends on a text size. To get a length at a size, multiply by the size and divide by
    /// <see cref="UnitsPerEm"/>. Distances above the baseline are positive, and a descent is a positive length below it in
    /// every font that follows the specification; a font that records a descent with the wrong sign is reported as it is.
    /// </para>
    /// <para>
    /// Two sets of line dimensions are given because platforms disagree about which one text is laid out with.
    /// <see cref="CellAscent"/>, <see cref="CellDescent"/> and <see cref="LineSpacing"/> are the rectangle Windows
    /// draws a line of text in, derived from the <c>OS/2</c> Windows metrics unless the font asks for its typographic
    /// metrics; in that case the typographic line gap is folded into the cell ascent, so the cell can be taller than
    /// <see cref="NormalLineAscent"/>. <see cref="NormalLineAscent"/>, <see cref="NormalLineDescent"/> and
    /// <see cref="NormalLineGap"/> are what browsers use for CSS <c>line-height: normal</c>: the <c>hhea</c> triple, or
    /// the <c>OS/2</c> typographic triple when the font sets its USE_TYPO_METRICS bit and records typographic metrics
    /// at all.
    /// </para>
    /// </remarks>
    public sealed class TypefaceMetrics
    {
        internal TypefaceMetrics(FontDescriptor source)
        {
            UnitsPerEm = source.UnitsPerEm;
            CellAscent = source.Ascender;
            CellDescent = source.Descender;
            LineSpacing = source.LineSpacing;
            NormalLineAscent = source.NormalLineHeightAscent;
            NormalLineDescent = source.NormalLineHeightDescent;
            NormalLineGap = source.NormalLineHeightGap;
            UnderlinePosition = source.UnderlinePosition;
            UnderlineThickness = source.UnderlineThickness;
            StrikeoutPosition = source.StrikeoutPosition;
            StrikeoutThickness = source.StrikeoutSize;
            CapHeight = source.CapHeight;
            XHeight = source.XHeight;
            HasMeasuredXHeight = source.HasAuthenticXHeight;
            ItalicAngle = source.ItalicAngle;
            XMin = source.XMin;
            YMin = source.YMin;
            XMax = source.XMax;
            YMax = source.YMax;
        }

        /// <summary>The number of design units to the em square (the <c>head</c> table's <c>unitsPerEm</c>).</summary>
        public int UnitsPerEm { get; }

        /// <summary>The height of the line cell above the baseline.</summary>
        public int CellAscent { get; }

        /// <summary>The depth of the line cell below the baseline.</summary>
        public int CellDescent { get; }

        /// <summary>The distance from one baseline to the next when lines are set solid: the cell plus any external leading, which is none when the font asks for its typographic metrics.</summary>
        public int LineSpacing { get; }

        /// <summary>The height above the baseline that CSS <c>line-height: normal</c> uses.</summary>
        public int NormalLineAscent { get; }

        /// <summary>The depth below the baseline that CSS <c>line-height: normal</c> uses.</summary>
        public int NormalLineDescent { get; }

        /// <summary>The gap between lines that CSS <c>line-height: normal</c> adds to the ascent and descent, never negative.</summary>
        public int NormalLineGap { get; }

        /// <summary>The position of the top of the underline stroke relative to the baseline, negative below it (the <c>post</c> table).</summary>
        public int UnderlinePosition { get; }

        /// <summary>The thickness of the underline stroke, which a poorly authored font may give as 0.</summary>
        public int UnderlineThickness { get; }

        /// <summary>The position of the strikeout stroke relative to the baseline (the <c>OS/2</c> table).</summary>
        public int StrikeoutPosition { get; }

        /// <summary>The thickness of the strikeout stroke.</summary>
        public int StrikeoutThickness { get; }

        /// <summary>
        /// The height of a flat capital letter above the baseline. A font that does not record it (an <c>OS/2</c> table
        /// older than version 2) reports its <see cref="CellAscent"/>, which is the fallback CSS Values 4 prescribes for the
        /// <c>cap</c> unit.
        /// </summary>
        public int CapHeight { get; }

        /// <summary>
        /// The height of a lower-case letter without an ascender above the baseline. A font that does not record it reports
        /// 66 percent of its <see cref="CellAscent"/>, and <see cref="HasMeasuredXHeight"/> says so.
        /// </summary>
        public int XHeight { get; }

        /// <summary>Whether <see cref="XHeight"/> is the font's own figure and not an estimate.</summary>
        public bool HasMeasuredXHeight { get; }

        /// <summary>The angle of italic strokes in degrees counter-clockwise from vertical, so negative for a slope to the right (the <c>post</c> table).</summary>
        public float ItalicAngle { get; }

        /// <summary>The left edge of the box that holds every glyph (the <c>head</c> table's <c>xMin</c>).</summary>
        public int XMin { get; }

        /// <summary>The bottom edge of the box that holds every glyph.</summary>
        public int YMin { get; }

        /// <summary>The right edge of the box that holds every glyph.</summary>
        public int XMax { get; }

        /// <summary>The top edge of the box that holds every glyph.</summary>
        public int YMax { get; }
    }
}
