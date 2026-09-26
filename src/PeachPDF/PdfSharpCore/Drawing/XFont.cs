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

// #??? Clean up

using PeachDrawing.Text;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Utils;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace PeachPDF.PdfSharpCore.Drawing
{
    /// <summary>
    /// Defines an object used to draw text.
    /// </summary>
    [DebuggerDisplay("{DebuggerDisplay}")]
    internal sealed class XFont
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="XFont"/> class: a matched typeface at an em size.
        /// </summary>
        /// <param name="emSize">The em size.</param>
        /// <param name="style">The font style (the bold and italic bits say what the box asked for; the typeface's own bold and italic are in <paramref name="match"/>).</param>
        /// <param name="pdfOptions">Additional PDF options.</param>
        /// <param name="match">The typeface the font set matched, and the synthesis the renderer has to apply to it.</param>
        /// <param name="obliqueSkewSinus">
        /// The sine of a declared CSS Fonts Level 4 <c>oblique &lt;angle&gt;</c> (e.g. <c>oblique 10deg</c>) - a purely
        /// rendering-side hint read by <c>XGraphicsPdfRenderer</c>'s faux-italic shear when synthesis is
        /// needed, with no bearing on face selection. Null (the common case: <c>italic</c>, bare <c>oblique</c>, or no
        /// synthesis needed) falls back to the renderer's fixed default skew.
        /// </param>
        public XFont(double emSize, XFontStyle style, XPdfFontOptions pdfOptions, TypefaceMatch match, double? obliqueSkewSinus = null)
        {
            _emSize = emSize;
            _style = style;
            _pdfOptions = pdfOptions;
            ObliqueSkewSinus = obliqueSkewSinus;

            // In principle an XFont is a typeface plus an em-size.
            Typeface = match.Typeface;
            Synthesis = match.Synthesis;
            InitializeFontMetrics();
        }

        /// <summary>The typeface this font sets text in: what the font set matched, with no size.</summary>
        public Typeface Typeface { get; }

        /// <summary>What the renderer has to fake because the typeface lacks it (bold, italic).</summary>
        internal SyntheticStyle Synthesis { get; }

        void InitializeFontMetrics()
        {
            var metrics = Typeface.Metrics;
            UnitsPerEm = metrics.UnitsPerEm;
            CellAscent = metrics.CellAscent;
            CellDescent = metrics.CellDescent;
            CellSpace = metrics.LineSpacing;
        }



        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////


        /// <summary>
        /// Gets the XFontFamily object associated with this XFont object.
        /// </summary>
        public string Name
        {
            get { return Typeface.FamilyName; }
        }

        /// <summary>
        /// Gets the em-size of this font measured in the unit of this font object.
        /// </summary>
        public double Size
        {
            get { return _emSize; }
        }
        readonly double _emSize;

        /// <summary>
        /// Gets style information for this Font object.
        /// </summary>
        [Browsable(false)]
        public XFontStyle Style
        {
            get { return _style; }
        }
        readonly XFontStyle _style;

        /// <summary>
        /// The sine of a declared CSS Fonts Level 4 <c>oblique &lt;angle&gt;</c>, when the requesting box's
        /// <c>font-style</c> specified one - null for <c>italic</c>, bare <c>oblique</c>, or <c>normal</c>,
        /// in which case <c>XGraphicsPdfRenderer</c> falls back to its own fixed default skew
        /// (<c>Const.ItalicSkewAngleSinus</c>) when faux-italic synthesis is needed.
        /// </summary>
        internal double? ObliqueSkewSinus { get; }

        /// <summary>
        /// Indicates whether this XFont object is bold.
        /// </summary>
        public bool Bold
        {
            get { return (_style & XFontStyle.Bold) == XFontStyle.Bold; }
        }

        /// <summary>
        /// Indicates whether this XFont object is italic.
        /// </summary>
        public bool Italic
        {
            get { return (_style & XFontStyle.Italic) == XFontStyle.Italic; }
        }

        /// <summary>
        /// Indicates whether this XFont object is stroke out.
        /// </summary>
        public bool Strikeout
        {
            get { return (_style & XFontStyle.Strikeout) == XFontStyle.Strikeout; }
        }

        /// <summary>
        /// Indicates whether this XFont object is underlined.
        /// </summary>
        public bool Underline
        {
            get { return (_style & XFontStyle.Underline) == XFontStyle.Underline; }
        }

        /// <summary>
        /// Temporary HACK for XPS to PDF converter.
        /// </summary>
        internal bool IsVertical
        {
            get { return _isVertical; }
            set { _isVertical = value; }
        }
        bool _isVertical;


        /// <summary>
        /// Gets the PDF options of the font.
        /// </summary>
        public XPdfFontOptions PdfOptions
        {
            get { return _pdfOptions ?? (_pdfOptions = new XPdfFontOptions()); }
        }
        XPdfFontOptions _pdfOptions;

        /// <summary>
        /// Indicates whether this XFont is encoded as Unicode.
        /// </summary>
        internal bool Unicode
        {
            get { return _pdfOptions != null && _pdfOptions.FontEncoding == PdfFontEncoding.Unicode; }
        }

        /// <summary>
        /// Gets the cell space for the font. The CellSpace is the line spacing, the sum of CellAscent and CellDescent and optionally some extra space.
        /// </summary>
        public int CellSpace
        {
            get { return _cellSpace; }
            internal set { _cellSpace = value; }
        }
        int _cellSpace;

        /// <summary>
        /// Gets the cell ascent, the area above the base line that is used by the font.
        /// </summary>
        public int CellAscent
        {
            get { return _cellAscent; }
            internal set { _cellAscent = value; }
        }
        int _cellAscent;

        /// <summary>
        /// Gets the cell descent, the area below the base line that is used by the font.
        /// </summary>
        public int CellDescent
        {
            get { return _cellDescent; }
            internal set { _cellDescent = value; }
        }
        int _cellDescent;


        /// <summary>
        /// Returns the line spacing, in pixels, of this font. The line spacing is the vertical distance
        /// between the base lines of two consecutive lines of text. Thus, the line spacing includes the
        /// blank space between lines along with the height of the character itself.
        /// </summary>
        public double GetHeight()
        {
            double value = CellSpace * _emSize / UnitsPerEm;
            return value;
        }

        /// <summary>
        /// Gets the line spacing of this font.
        /// </summary>
        [Browsable(false)]
        public int Height
        {
            // Implementation from System.Drawing.Font.cs
            get { return (int)Math.Ceiling(GetHeight()); }
        }

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        internal int UnitsPerEm
        {
            get { return _unitsPerEm; }
            private set { _unitsPerEm = value; }
        }
        internal int _unitsPerEm;

        /// <summary>
        /// Cache PdfFontTable.FontSelector to speed up finding the right PdfFont
        /// if this font is used more than once.
        /// </summary>
        internal string Selector
        {
            get { return _selector; }
            set { _selector = value; }
        }
        string _selector = null!;

        /// <summary>
        /// Gets the DebuggerDisplayAttribute text.
        /// </summary>
        // ReSharper disable UnusedMember.Local
        string DebuggerDisplay
        // ReSharper restore UnusedMember.Local
        {
            get { return String.Format(CultureInfo.InvariantCulture, "font=('{0}' {1:0.##})", Name, Size); }
        }
    }
}