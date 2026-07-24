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

using PeachPDF.Fonts;
using PeachPDF.Fonts.OpenType;
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
        /// Initializes a new instance of the <see cref="XFont"/> class.
        /// </summary>
        /// <param name="familyName">Name of the font family.</param>
        /// <param name="emSize">The em size.</param>
        /// <param name="fontResolver">The font resolver used to look up the font's glyph data.</param>
        public XFont(string familyName, double emSize, IFontResolver fontResolver)
            : this(familyName, emSize, XFontStyle.Regular, new XPdfFontOptions(GlobalFontSettings.DefaultFontEncoding), fontResolver)
        { }

        /// <summary>
        /// Initializes a new instance of the <see cref="XFont"/> class.
        /// </summary>
        /// <param name="familyName">Name of the font family.</param>
        /// <param name="emSize">The em size.</param>
        /// <param name="style">The font style.</param>
        /// <param name="fontResolver">The font resolver used to look up the font's glyph data.</param>
        public XFont(string familyName, double emSize, XFontStyle style, IFontResolver fontResolver)
            : this(familyName, emSize, style, new XPdfFontOptions(GlobalFontSettings.DefaultFontEncoding), fontResolver)
        { }

        /// <summary>
        /// Initializes a new instance of the <see cref="XFont"/> class.
        /// </summary>
        /// <param name="familyName">Name of the font family.</param>
        /// <param name="emSize">The em size.</param>
        /// <param name="style">The font style.</param>
        /// <param name="pdfOptions">Additional PDF options.</param>
        /// <param name="fontResolver">The font resolver used to look up the font's glyph data.</param>
        public XFont(string familyName, double emSize, XFontStyle style, XPdfFontOptions pdfOptions, IFontResolver fontResolver)
        {
            _familyName = familyName;
            _emSize = emSize;
            _style = style;
            _pdfOptions = pdfOptions;
            Initialize(fontResolver);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="XFont"/> class using a real CSS Fonts Level 4
        /// numeric weight (1-1000) instead of just <paramref name="style"/>'s Bold bit, so the resolver
        /// can perform nearest-weight matching (see <see cref="Fonts.IFontResolver.ResolveTypeface(string, int, bool)"/>)
        /// rather than only ever asking for an exact Regular/Bold pick.
        /// </summary>
        /// <param name="familyName">Name of the font family.</param>
        /// <param name="emSize">The em size.</param>
        /// <param name="style">The font style (italic/underline/strikeout bits; the Bold bit is superseded by <paramref name="weight"/>).</param>
        /// <param name="pdfOptions">Additional PDF options.</param>
        /// <param name="weight">The requested CSS Fonts numeric weight (1-1000).</param>
        /// <param name="fontResolver">The font resolver used to look up the font's glyph data.</param>
        public XFont(string familyName, double emSize, XFontStyle style, XPdfFontOptions pdfOptions, int weight, IFontResolver fontResolver)
            : this(familyName, emSize, style, pdfOptions, weight, TtfFontDescription.DefaultStretch, fontResolver)
        { }

        /// <summary>
        /// Initializes a new instance of the <see cref="XFont"/> class using both a real CSS Fonts Level 4
        /// numeric weight and a real CSS Fonts Level 3 numeric stretch (1-9, matching OS/2
        /// <c>usWidthClass</c>), so the resolver can perform nearest-weight/-stretch matching.
        /// </summary>
        /// <param name="familyName">Name of the font family.</param>
        /// <param name="emSize">The em size.</param>
        /// <param name="style">The font style (italic/underline/strikeout bits; the Bold bit is superseded by <paramref name="weight"/>).</param>
        /// <param name="pdfOptions">Additional PDF options.</param>
        /// <param name="weight">The requested CSS Fonts numeric weight (1-1000).</param>
        /// <param name="stretch">The requested CSS Fonts numeric stretch (1-9, 5 = normal).</param>
        /// <param name="fontResolver">The font resolver used to look up the font's glyph data.</param>
        public XFont(string familyName, double emSize, XFontStyle style, XPdfFontOptions pdfOptions, int weight, int stretch, IFontResolver fontResolver)
            : this(familyName, emSize, style, pdfOptions, weight, stretch, null, fontResolver)
        { }

        /// <summary>
        /// Initializes a new instance of the <see cref="XFont"/> class, additionally carrying the sine of
        /// a declared CSS Fonts Level 4 <c>oblique &lt;angle&gt;</c> (e.g. <c>oblique 10deg</c>) - a purely
        /// rendering-side hint read by <c>XGraphicsPdfRenderer</c>'s faux-italic shear when synthesis is
        /// needed, with no bearing on face selection (unlike <paramref name="weight"/>/<paramref name="stretch"/>).
        /// Null (the common case: <c>italic</c>, bare <c>oblique</c>, or no synthesis needed) falls back
        /// to the renderer's fixed default skew.
        /// </summary>
        /// <param name="familyName">Name of the font family.</param>
        /// <param name="emSize">The em size.</param>
        /// <param name="style">The font style (italic/underline/strikeout bits; the Bold bit is superseded by <paramref name="weight"/>).</param>
        /// <param name="pdfOptions">Additional PDF options.</param>
        /// <param name="weight">The requested CSS Fonts numeric weight (1-1000).</param>
        /// <param name="stretch">The requested CSS Fonts numeric stretch (1-9, 5 = normal).</param>
        /// <param name="obliqueSkewSinus">The sine of a declared <c>oblique &lt;angle&gt;</c>, or null.</param>
        /// <param name="fontResolver">The font resolver used to look up the font's glyph data.</param>
        public XFont(string familyName, double emSize, XFontStyle style, XPdfFontOptions pdfOptions, int weight, int stretch, double? obliqueSkewSinus, IFontResolver fontResolver)
        {
            _familyName = familyName;
            _emSize = emSize;
            _style = style;
            _pdfOptions = pdfOptions;
            _weight = weight;
            _stretch = stretch;
            ObliqueSkewSinus = obliqueSkewSinus;
            Initialize(fontResolver);
        }

        /// <summary>
        /// Same as the numeric-weight/stretch constructor, but additionally scoped to a single Unicode
        /// scalar value (<paramref name="codepoint"/>) so the resolver picks the face that actually covers
        /// that codepoint - the basis of per-codepoint <c>unicode-range</c> selection and glyph-coverage
        /// fallback. Used only by the per-codepoint resolution path; ordinary font creation stays
        /// codepoint-less.
        /// </summary>
        internal XFont(string familyName, double emSize, XFontStyle style, XPdfFontOptions pdfOptions, int weight, int stretch, double? obliqueSkewSinus, System.Text.Rune? codepoint, IFontResolver fontResolver)
        {
            _familyName = familyName;
            _emSize = emSize;
            _style = style;
            _pdfOptions = pdfOptions;
            _weight = weight;
            _stretch = stretch;
            ObliqueSkewSinus = obliqueSkewSinus;
            _codepoint = codepoint;
            Initialize(fontResolver);
        }

        internal XFont(string familyName, double emSize, XFontStyle style, XPdfFontOptions pdfOptions, XStyleSimulations styleSimulations, IFontResolver fontResolver)
        {
            _familyName = familyName;
            _emSize = emSize;
            _style = style;
            _pdfOptions = pdfOptions;
            OverrideStyleSimulations = true;
            StyleSimulations = styleSimulations;
            Initialize(fontResolver);
        }

        /// <summary>
        /// Initializes this instance by computing the glyph typeface, font family, font source and TrueType fontface.
        /// (PDFsharp currently only deals with TrueType fonts.)
        /// </summary>
        void Initialize(IFontResolver fontResolver)
        {
#if DEBUG
            if (_familyName == "Segoe UI Semilight" && (_style & XFontStyle.BoldItalic) == XFontStyle.Italic)
                GetType();
#endif

            FontResolvingOptions fontResolvingOptions = OverrideStyleSimulations
                ? new FontResolvingOptions(_style, StyleSimulations)
                : _weight is { } weight
                    ? new FontResolvingOptions(_style, weight, _stretch ?? TtfFontDescription.DefaultStretch)
                    : new FontResolvingOptions(_style);

            fontResolvingOptions.Codepoint = _codepoint;

            // HACK: 'PlatformDefault' is used in unit test code.
            if (StringComparer.OrdinalIgnoreCase.Compare(_familyName, GlobalFontSettings.DefaultFontName) == 0)
            {
            }

            // In principle an XFont is an XGlyphTypeface plus an em-size.
            _glyphTypeface = XGlyphTypeface.GetOrCreateFrom(_familyName, fontResolvingOptions, fontResolver);
            CreateDescriptorAndInitializeFontMetrics();
        }

        /// <summary>
        /// Code separated from Metric getter to make code easier to debug.
        /// (Setup properties in their getters caused side effects during debugging because Visual Studio calls a getter
        /// to early to show its value in a debugger window.)
        /// </summary>
        void CreateDescriptorAndInitializeFontMetrics()  // TODO: refactor
        {
            Debug.Assert(_fontMetrics == null, "InitializeFontMetrics() was already called.");

            // FontDescriptorCache is ALSO a global, static cache keyed purely by the typeface key string
            // (family+style+weight) - with no notion of which FontResolver instance's font bytes actually
            // produced _glyphTypeface. Left unrouted, it would silently reintroduce the exact cross-
            // PdfGenerator-instance collision XGlyphTypeface.GetOrCreateFrom's own cache split fixes, one
            // layer further down (font metrics/embedding data, not just glyph outlines). Route custom
            // (OwningInstanceResolver != null) fonts through that same resolver's own instance cache.
            var owningResolver = _glyphTypeface.OwningInstanceResolver;
            if (owningResolver != null)
            {
                var key = FontDescriptor.ComputeKey(this);
                if (!owningResolver.InstanceFontDescriptorsByKey.TryGetValue(key, out var instanceDescriptor))
                {
                    instanceDescriptor = new OpenTypeDescriptor(key, this);
                    owningResolver.InstanceFontDescriptorsByKey[key] = instanceDescriptor;
                }

                _descriptor = (OpenTypeDescriptor)instanceDescriptor;
            }
            else
            {
                _descriptor = (OpenTypeDescriptor)FontDescriptorCache.GetOrCreateDescriptorFor(this);
            }
            _fontMetrics = new XFontMetrics(_descriptor.FontName, _descriptor.UnitsPerEm, _descriptor.Ascender, _descriptor.Descender,
                _descriptor.Leading, _descriptor.LineSpacing, _descriptor.CapHeight, _descriptor.XHeight, _descriptor.StemV, 0, 0, 0,
                _descriptor.UnderlinePosition, _descriptor.UnderlineThickness, _descriptor.StrikeoutPosition, _descriptor.StrikeoutSize);

            XFontMetrics fm = Metrics;

            // Already done in CreateDescriptorAndInitializeFontMetrics.
            //if (_descriptor == null)
            //    _descriptor = (OpenTypeDescriptor)FontDescriptorStock.Global.CreateDescriptor(this);  //(Name, (XGdiFontStyle)Font.Style);

            UnitsPerEm = _descriptor.UnitsPerEm;
            CellAscent = _descriptor.Ascender;
            CellDescent = _descriptor.Descender;
            CellSpace = _descriptor.LineSpacing;
            Debug.Assert(fm.UnitsPerEm == _descriptor.UnitsPerEm);
        }



        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////


        /// <summary>
        /// Gets the XFontFamily object associated with this XFont object.
        /// </summary>
        [Browsable(false)]
        public XFontFamily FontFamily
        {
            get { return _glyphTypeface.FontFamily; }
        }

        /// <summary>
        /// WRONG: Gets the face name of this Font object.
        /// Indeed it returns the font family name.
        /// </summary>
        // [Obsolete("This function returns the font family name, not the face name. Use xxx.FontFamily.Name or xxx.FaceName")]
        public string Name
        {
            get { return _glyphTypeface.FontFamily.Name; }
        }

        internal string FaceName
        {
            get { return _glyphTypeface.FaceName; }
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
        /// The real CSS Fonts Level 4 numeric weight (1-1000) requested via the weight-carrying
        /// constructor overload, if any - null for callers that only specify <see cref="Style"/>'s Bold
        /// bit, in which case <see cref="Initialize"/> derives 700/400 from it instead.
        /// </summary>
        readonly int? _weight;

        /// <summary>
        /// The real CSS Fonts Level 3 numeric stretch (1-9) requested via the weight+stretch-carrying
        /// constructor overload, if any - null when unspecified, in which case <see cref="Initialize"/>
        /// falls back to normal (5).
        /// </summary>
        readonly int? _stretch;

        readonly System.Text.Rune? _codepoint;

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
        /// Gets the font metrics.
        /// </summary>
        /// <value>The metrics.</value>
        public XFontMetrics Metrics
        {
            get
            {
                // Code moved to InitializeFontMetrics().
                //if (_fontMetrics == null)
                //{
                //    FontDescriptor descriptor = FontDescriptorStock.Global.CreateDescriptor(this);
                //    _fontMetrics = new XFontMetrics(descriptor.FontName, descriptor.UnitsPerEm, descriptor.Ascender, descriptor.Descender,
                //        descriptor.Leading, descriptor.LineSpacing, descriptor.CapHeight, descriptor.XHeight, descriptor.StemV, 0, 0, 0);
                //}
                Debug.Assert(_fontMetrics != null, "InitializeFontMetrics() not yet called.");
                return _fontMetrics;
            }
        }
        XFontMetrics _fontMetrics = null!;

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

        internal XGlyphTypeface GlyphTypeface
        {
            get { return _glyphTypeface; }
        }
        XGlyphTypeface _glyphTypeface = null!;


        internal OpenTypeDescriptor Descriptor
        {
            get { return _descriptor; }
            private set { _descriptor = value; }
        }
        OpenTypeDescriptor _descriptor = null!;


        internal string FamilyName
        {
            get { return _familyName; }
        }
        string _familyName;


        internal int UnitsPerEm
        {
            get { return _unitsPerEm; }
            private set { _unitsPerEm = value; }
        }
        internal int _unitsPerEm;

        /// <summary>
        /// Override style simulations by using the value of StyleSimulations.
        /// </summary>
        internal bool OverrideStyleSimulations;

        /// <summary>
        /// Used to enforce style simulations by renderer. For development purposes only.
        /// </summary>
        internal XStyleSimulations StyleSimulations;

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