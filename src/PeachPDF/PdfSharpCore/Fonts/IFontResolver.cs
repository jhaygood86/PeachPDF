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

namespace PeachPDF.PdfSharpCore.Fonts
{
    /// <summary>
    /// Provides functionality that convertes a requested typeface into a physical font.
    /// </summary>
    internal interface IFontResolver
    {
        /// <summary>
        /// Converts specified information about a required typeface into a specific font.
        /// </summary>
        /// <param name="familyName">Name of the font family.</param>
        /// <param name="isBold">Set to <c>true</c> when a bold fontface is required.</param>
        /// <param name="isItalic">Set to <c>true</c> when an italic fontface is required.</param>
        /// <returns>Information about the physical font, or null if the request cannot be satisfied.</returns>
        FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic);

        /// <summary>
        /// Converts specified information about a required typeface into a specific font, using a real
        /// CSS Fonts Level 4 numeric weight (1-1000) instead of a coarse bold/not-bold flag - lets the
        /// resolver perform real nearest-weight matching (§5.2) among every face registered for the
        /// family, not just an exact Regular/Bold pick. <see cref="ResolveTypeface(string, bool, bool)"/>
        /// remains for callers that only have a bold/not-bold flag (equivalent to weight 700/400).
        /// </summary>
        /// <param name="familyName">Name of the font family.</param>
        /// <param name="weight">The requested CSS Fonts numeric weight (1-1000).</param>
        /// <param name="isItalic">Set to <c>true</c> when an italic fontface is required.</param>
        /// <returns>Information about the physical font, or null if the request cannot be satisfied.</returns>
        FontResolverInfo ResolveTypeface(string familyName, int weight, bool isItalic);

        /// <summary>
        /// Converts specified information about a required typeface into a specific font, additionally
        /// matching on a real CSS Fonts Level 3 <c>font-stretch</c> value (1-9, matching the OpenType
        /// OS/2 table's <c>usWidthClass</c> scale directly) alongside weight/style. Callers that don't
        /// care about stretch can use <see cref="ResolveTypeface(string, int, bool)"/>, which matches
        /// only among normal-stretch (5) faces.
        /// </summary>
        /// <param name="familyName">Name of the font family.</param>
        /// <param name="weight">The requested CSS Fonts numeric weight (1-1000).</param>
        /// <param name="isItalic">Set to <c>true</c> when an italic fontface is required.</param>
        /// <param name="stretch">The requested CSS Fonts numeric stretch (1-9, 5 = normal).</param>
        /// <returns>Information about the physical font, or null if the request cannot be satisfied.</returns>
        FontResolverInfo ResolveTypeface(string familyName, int weight, bool isItalic, int stretch);

        //FontResolverInfo ResolveTypeface(Typeface); TODO in PDFsharp 2.0

        /// <summary>
        /// Gets the bytes of a physical font with specified face name.
        /// </summary>
        /// <param name="fontFaceName">A face name previously retrieved by ResolveTypeface.</param>
        byte[] GetFont(string fontFaceName);
    }
}