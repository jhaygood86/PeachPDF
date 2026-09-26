#region PDFsharp - A .NET library for processing PDF
//
// Authors:
//   Stefan Lange
//
// Copyright (c) 2005-2016 empira Software GmbH, Cologne Area (Germany)
//
// https://www.pdfsharp.com/
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


namespace PeachDrawing.Text.Internal.Fonts
{
    /// <summary>
    /// Parameters that affect font selection.
    /// </summary>
    class FontResolvingOptions
    {
        /// <summary>Prefix of every typeface cache key ("typeface key").</summary>
        internal const string TypefaceKeyPrefix = "tk:";

        public FontResolvingOptions(FaceStyle fontStyle)
        {
            FontStyle = fontStyle;
            Weight = IsBold ? 700 : 400;
            Stretch = TtfFontDescription.DefaultStretch;
            WidthPercent = WidthClasses.Normal;
        }

        public FontResolvingOptions(FaceStyle fontStyle, SyntheticStyle styleSimulations)
        {
            FontStyle = fontStyle;
            OverrideStyleSimulations = true;
            StyleSimulations = styleSimulations;
            Weight = IsBold ? 700 : 400;
            Stretch = TtfFontDescription.DefaultStretch;
            WidthPercent = WidthClasses.Normal;
        }

        public FontResolvingOptions(FaceStyle fontStyle, int weight, int stretch = 5)
        {
            FontStyle = fontStyle;
            Weight = weight;
            Stretch = stretch;
            WidthPercent = WidthClasses.ToPercent(stretch);
        }

        /// <summary>
        /// Creates options for a width that is not necessarily one of the nine classes, as a percentage of the normal width. A factory and
        /// not an overload, since an integer literal (<c>100</c>) would otherwise bind to the constructor that takes a width class.
        /// </summary>
        public static FontResolvingOptions ForWidthPercent(FaceStyle fontStyle, int weight, double widthPercent) =>
            new(fontStyle, weight, WidthClasses.FromPercent(widthPercent), widthPercent);

        private FontResolvingOptions(FaceStyle fontStyle, int weight, int stretch, double widthPercent)
        {
            FontStyle = fontStyle;
            Weight = weight;
            Stretch = stretch;
            WidthPercent = widthPercent;
        }

        /// <summary>
        /// The real CSS Fonts Level 4 numeric weight (1-1000) this request should be matched against -
        /// defaults to 700/400 (derived from <see cref="IsBold"/>) for callers that only ever specify a
        /// bold/not-bold <see cref="FaceStyle"/>, so <see cref="Fonts.FontFactory"/>/<see cref="IFontResolver"/>
        /// always have a real number to key/match on regardless of which constructor was used.
        /// </summary>
        public int Weight { get; }

        /// <summary>
        /// CSS Fonts Level 3 <c>font-stretch</c> value (1-9, matching OS/2 <c>usWidthClass</c> directly) this
        /// request should be matched against - defaults to normal (5) for callers that don't specify one.
        /// </summary>
        public int Stretch { get; }

        /// <summary>
        /// The width this request should be matched against, as a percentage of the normal width (CSS <c>font-stretch: 87.5%</c>).
        /// For a request made with a width class it is that class's percentage, and <see cref="Stretch"/> is the class nearest to it
        /// otherwise.
        /// </summary>
        public double WidthPercent { get; }

        public bool IsBold
        {
            get { return (FontStyle & FaceStyle.Bold) == FaceStyle.Bold; }
        }

        public bool IsItalic
        {
            get { return (FontStyle & FaceStyle.Italic) == FaceStyle.Italic; }
        }

        public bool IsBoldItalic
        {
            get { return (FontStyle & FaceStyle.BoldItalic) == FaceStyle.BoldItalic; }
        }

        public bool MustSimulateBold
        {
            get { return (StyleSimulations & SyntheticStyle.Bold) == SyntheticStyle.Bold; }
        }

        public bool MustSimulateItalic
        {
            get { return (StyleSimulations & SyntheticStyle.Italic) == SyntheticStyle.Italic; }
        }

        public FaceStyle FontStyle;

        public bool OverrideStyleSimulations;

        public SyntheticStyle StyleSimulations;

        /// <summary>
        /// The specific Unicode scalar value this request is resolving a font for, when doing
        /// per-codepoint font matching (unicode-range / glyph-coverage fallback). Null for ordinary
        /// box/metrics resolution, which is not codepoint-scoped. When set, face selection is restricted
        /// to faces that cover this codepoint (see <c>FontResolver.ResolveTypeface</c>).
        /// </summary>
        public System.Text.Rune? Codepoint;

        /// <summary>
        /// Computes the bijective, human readable key of the typeface this request resolves for
        /// <paramref name="familyName"/>: family, italic, numeric weight and stretch, and any forced synthesis.
        /// </summary>
        internal string ComputeTypefaceKey(string familyName)
        {
            string simulationSuffix = "";
            if (OverrideStyleSimulations)
            {
                switch (StyleSimulations)
                {
                    case SyntheticStyle.Bold: simulationSuffix = "|b+/i-"; break;
                    case SyntheticStyle.Italic: simulationSuffix = "|b-/i+"; break;
                    case SyntheticStyle.BoldItalic: simulationSuffix = "|b+/i+"; break;
                    case SyntheticStyle.None: break;
                    default: throw new System.ArgumentOutOfRangeException();
                }
            }
            return TypefaceKeyPrefix + familyName.ToLowerInvariant()
                + (IsItalic ? "/i" : "/n") // normal / oblique / italic
                + "/" + Weight
                + "/" + (WidthPercent == WidthClasses.ToPercent(Stretch)
                    ? Stretch.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : "w" + WidthPercent.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
                + simulationSuffix;
        }
    }
}
