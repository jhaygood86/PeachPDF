using PeachDrawing.Text;
using PeachDrawing.Text.Shaping;
using System;
using System.Diagnostics;
using System.Text;

namespace PeachDrawing.Core
{
    /// <summary>
    /// Measures a run of text against a <see cref="Typeface"/> at a size - the one measurement algorithm
    /// every backend's own <see cref="Canvas.MeasureString(string, Font, ShapeSettings?)"/> needs, so it
    /// lives here once rather than once per backend. Line breaks split the run; each line is shaped as one
    /// run (so GSUB ligatures merge the
    /// way painting's own shaping does) and its glyph advances (GPOS kerning included) summed.
    /// </summary>
    public static class TextMeasurement
    {
        /// <summary>
        /// The sine of the angle a regular face is sheared by to simulate italic (20 degrees), when a font
        /// needs faux-italic synthesis and declares no explicit oblique angle of its own.
        /// </summary>
        public const double ItalicSkewAngleSinus = 0.34202014332566873304409961468226; // = sin(20 degrees)

        /// <summary>
        /// The fraction of the em size a line widens by, per character, to simulate bold on a face with no
        /// real bold of its own (the 2% figure XPS 1.0's own documentation gives for the same synthesis).
        /// </summary>
        public const double BoldEmphasis = 0.02;

        /// <summary>Measures <paramref name="text"/> set in <paramref name="typeface"/> at <paramref name="size"/>.</summary>
        public static Size Measure(string text, Typeface typeface, double size, SyntheticStyle synthesis, ShapeSettings features)
        {
            var result = new Size();

            TypefaceMetrics metrics = typeface.Metrics;
            var unitsPerEm = metrics.UnitsPerEm;
            // Height is the sum of ascender and descender.
            var singleLineHeight = (metrics.CellAscent + metrics.CellDescent) * size / unitsPerEm;
            var lineGapHeight = (metrics.LineSpacing - metrics.CellAscent - metrics.CellDescent) * size / unitsPerEm;

            Debug.Assert(metrics.CellAscent > 0);

            int adjustedLength;
            var height = singleLineHeight;
            int maxWidth;

            if (IsPlainRun(text))
            {
                // The whole of a word, which is what nearly every call is: nothing to drop, remap or split, so the text
                // shaped is the text given. None of the builder, per-character strings and closure the general path needs.
                maxWidth = ShapedWidth(typeface, text, features);
                adjustedLength = text.Length;
            }
            else
            {
                (maxWidth, adjustedLength, height) = MeasureLines(text, typeface, features, singleLineHeight, lineGapHeight);
            }

            result.Width = maxWidth * size / metrics.UnitsPerEm;
            result.Height = height;

            // Adjust bold simulation.
            if ((synthesis & SyntheticStyle.Bold) == SyntheticStyle.Bold)
            {
                // Add 2% of the em-size for each character.
                // Unsure how to deal with white space. Currently count as regular character.
                result.Width += adjustedLength * size * BoldEmphasis;
            }

            return result;
        }

        /// <summary>
        /// Whether <paramref name="text"/> is a run <see cref="Measure"/> shapes exactly as given: no line feed, tab or other
        /// control character, and no surrogate (a lone one is replaced when the text is read as runes).
        /// </summary>
        private static bool IsPlainRun(string text)
        {
            foreach (var c in text)
            {
                if (c < 32 || char.IsSurrogate(c)) return false;
            }

            return true;
        }

        /// <summary>
        /// The width of <paramref name="text"/> shaped as one run, in font design units. GPOS's <c>XAdvanceDelta</c> (kerning)
        /// is folded in here, not just applied at paint time - line-breaking, text-align and justification all key off this
        /// value, and it must agree with what a backend's own glyph painter actually paints.
        /// </summary>
        private static int ShapedWidth(Typeface typeface, string text, ShapeSettings features)
        {
            var glyphs = Shaper.Shape(typeface, text, features).Glyphs;
            var width = 0;

            for (var i = 0; i < glyphs.Count; i++)
            {
                var glyph = glyphs[i];
                width += (int)Math.Round(typeface.GetAdvance((ushort)glyph.GlyphIndex) + glyph.XAdvanceDelta);
            }

            return width;
        }

        /// <summary>Measures text that holds line feeds, tabs or other characters needing handling, one line at a time.</summary>
        private static (int MaxWidth, int AdjustedLength, double Height) MeasureLines(
            string text, Typeface typeface, ShapeSettings features, double singleLineHeight, double lineGapHeight)
        {
            int adjustedLength = 0;
            var height = singleLineHeight;
            int maxWidth = 0;
            int width = 0;
            // Accumulates one measured line's printable text (tabs remapped to spaces, other
            // control chars dropped) so it can be shaped - and any GSUB ligatures merged - as
            // one run, rather than measuring glyph-by-glyph the way a single Shape call for the
            // whole line already replaces.
            var lineText = new StringBuilder();
            // A '\n' starts a new line only when another rune follows it (a trailing newline adds no
            // line); iterating runes (not UTF-16 units) loses the index used for that "is-last" test,
            // so defer the line break until the next rune actually arrives.
            bool pendingNewlineBreak = false;

            void FlushLine()
            {
                // GPOS's XAdvanceDelta (kerning) must be folded into the measured width here, not
                // just applied at paint time - line-breaking/text-align/justification all key off
                // this value, and must agree with what a backend's own glyph painter actually paints.
                width = ShapedWidth(typeface, lineText.ToString(), features);
                lineText.Clear();
            }

            foreach (Rune rune in text.EnumerateRunes())
            {
                if (pendingNewlineBreak)
                {
                    FlushLine();
                    maxWidth = Math.Max(maxWidth, width);
                    height += lineGapHeight + singleLineHeight;
                    pendingNewlineBreak = false;
                }

                int value = rune.Value;
                adjustedLength++;

                // Handle line feed ( \n)
                if (value == 10)
                {
                    adjustedLength--;
                    pendingNewlineBreak = true;

                    continue;
                }

                // HACK: Handle tabulator sign as space (\t)
                if (value == 9)
                {
                    lineText.Append(' ');
                    continue;
                }

                // HACK: Unclear what to do here.
                if (value < 32)
                {
                    adjustedLength--;

                    continue;
                }

                lineText.Append(rune);
            }
            FlushLine();
            maxWidth = Math.Max(maxWidth, width);

            return (maxWidth, adjustedLength, height);
        }
    }
}
