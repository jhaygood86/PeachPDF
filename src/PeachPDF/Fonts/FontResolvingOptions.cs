#region PDFsharp - A .NET library for processing PDF
//
// Authors:
//   Stefan Lange
//
// Copyright (c) 2005-2016 empira Software GmbH, Cologne Area (Germany)
//
// http://www.PeachPDF.PdfSharpCore.com
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

using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Fonts
{
    /// <summary>
    /// Parameters that affect font selection.
    /// </summary>
    class FontResolvingOptions
    {
        public FontResolvingOptions(XFontStyle fontStyle)
        {
            FontStyle = fontStyle;
            Weight = IsBold ? 700 : 400;
            Stretch = TtfFontDescription.DefaultStretch;
        }

        public FontResolvingOptions(XFontStyle fontStyle, XStyleSimulations styleSimulations)
        {
            FontStyle = fontStyle;
            OverrideStyleSimulations = true;
            StyleSimulations = styleSimulations;
            Weight = IsBold ? 700 : 400;
            Stretch = TtfFontDescription.DefaultStretch;
        }

        public FontResolvingOptions(XFontStyle fontStyle, int weight, int stretch = 5)
        {
            FontStyle = fontStyle;
            Weight = weight;
            Stretch = stretch;
        }

        /// <summary>
        /// The real CSS Fonts Level 4 numeric weight (1-1000) this request should be matched against -
        /// defaults to 700/400 (derived from <see cref="IsBold"/>) for callers that only ever specify a
        /// bold/not-bold <see cref="XFontStyle"/>, so <see cref="Fonts.FontFactory"/>/<see cref="IFontResolver"/>
        /// always have a real number to key/match on regardless of which constructor was used.
        /// </summary>
        public int Weight { get; }

        /// <summary>
        /// CSS Fonts Level 3 <c>font-stretch</c> value (1-9, matching OS/2 <c>usWidthClass</c> directly) this
        /// request should be matched against - defaults to normal (5) for callers that don't specify one.
        /// </summary>
        public int Stretch { get; }

        public bool IsBold
        {
            get { return (FontStyle & XFontStyle.Bold) == XFontStyle.Bold; }
        }

        public bool IsItalic
        {
            get { return (FontStyle & XFontStyle.Italic) == XFontStyle.Italic; }
        }

        public bool IsBoldItalic
        {
            get { return (FontStyle & XFontStyle.BoldItalic) == XFontStyle.BoldItalic; }
        }

        public bool MustSimulateBold
        {
            get { return (StyleSimulations & XStyleSimulations.BoldSimulation) == XStyleSimulations.BoldSimulation; }
        }

        public bool MustSimulateItalic
        {
            get { return (StyleSimulations & XStyleSimulations.ItalicSimulation) == XStyleSimulations.ItalicSimulation; }
        }

        public XFontStyle FontStyle;

        public bool OverrideStyleSimulations;

        public XStyleSimulations StyleSimulations;

        /// <summary>
        /// The specific Unicode scalar value this request is resolving a font for, when doing
        /// per-codepoint font matching (unicode-range / glyph-coverage fallback). Null for ordinary
        /// box/metrics resolution, which is not codepoint-scoped. When set, face selection is restricted
        /// to faces that cover this codepoint (see <c>FontResolver.ResolveTypeface</c>).
        /// </summary>
        public System.Text.Rune? Codepoint;
    }
}
