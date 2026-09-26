#region PDFsharp - A .NET library for processing PDF
//
// Authors:
//   Stefan Lange
//
// Copyright (c) 2005-2016 empira Software GmbH, Cologne Area (Germany)
//
// http://www.PdfSharp.com
// http://sourceforge.net/projects/pdfsharp
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included
// in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
// THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.
#endregion

#nullable disable warnings

using PeachDrawing.Text.Shaping;
using PeachDrawing.Text;
using System;
using System.Diagnostics;
using System.Text;

namespace PeachPDF.PdfSharpCore.Drawing
{
    /// <summary>
    /// Bunch of functions that do not have a better place.
    /// </summary>
    static class FontHelper
    {
        /// <summary>
        /// Measure string directly from font data.
        /// </summary>
        public static XSize MeasureString(string text, XFont font, XStringFormat stringFormat, ShapeSettings features)
        {
            XSize size = new XSize();

            TypefaceMetrics metrics = font.Typeface.Metrics;
            // Height is the sum of ascender and descender.
            var singleLineHeight = (metrics.CellAscent + metrics.CellDescent) * font.Size / font.UnitsPerEm;
            var lineGapHeight = (metrics.LineSpacing - metrics.CellAscent - metrics.CellDescent) * font.Size / font.UnitsPerEm;

            Debug.Assert(metrics.CellAscent > 0);

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
                width = 0;
                // GPOS's XAdvanceDelta (kerning) must be folded into the measured width here, not
                // just applied at paint time - line-breaking/text-align/justification all key off
                // this value, and must agree with what XGraphicsPdfRenderer.DrawString actually
                // paints (see GposPositioner).
                foreach (PlacedGlyph glyph in Shaper.Shape(font.Typeface, lineText.ToString(), features).Glyphs)
                    width += (int)Math.Round(font.Typeface.GetAdvance((ushort)glyph.GlyphIndex) + glyph.XAdvanceDelta);
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

                lineText.Append(rune.ToString());
            }
            FlushLine();
            maxWidth = Math.Max(maxWidth, width);

            // What? size.Width = maxWidth * font.Size * (font.Italic ? 1 : 1) / descriptor.UnitsPerEm;
            size.Width = maxWidth * font.Size / metrics.UnitsPerEm;
            size.Height = height;

            // Adjust bold simulation.
            if ((font.Synthesis & SyntheticStyle.Bold) == SyntheticStyle.Bold)
            {
                // Add 2% of the em-size for each character.
                // Unsure how to deal with white space. Currently count as regular character.
                size.Width += adjustedLength * font.Size * Const.BoldEmphasis;
            }

            return size;
        }

        public static XFontStyle CreateStyle(bool isBold, bool isItalic)
        {
            return (isBold ? XFontStyle.Bold : 0) | (isItalic ? XFontStyle.Italic : 0);
        }
    }
}
