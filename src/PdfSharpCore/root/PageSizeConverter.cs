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
using System;

namespace PeachPDF.PdfSharpCore
{
    /// <summary>
    /// Converter from <see cref="PageSize"/> to <see cref="XSize"/>.
    /// </summary>
    public static class PageSizeConverter
    {
        /// <summary>
        /// Converts the specified page size enumeration to a pair of values in point.
        /// </summary>
        public static XSize ToSize(PageSize value, int dpi = 72)
        {
            // The international definitions are:
            //   1 inch == 25.4 mm
            //   1 inch == 72 point
            var size = value switch
            {
                // Source http://www.din-formate.de/reihe-a-din-groessen-mm-pixel-dpi.html
                PageSize.A0 => new XSize(2384, 3370),
                PageSize.A1 => new XSize(1684, 2384),
                PageSize.A2 => new XSize(1191, 1684),
                PageSize.A3 => new XSize(842, 1191),
                PageSize.A4 => new XSize(595, 842),
                PageSize.A5 => new XSize(420, 595),
                PageSize.A6 => new XSize(298, 420),
                PageSize.RA0 => new XSize(2438, 3458),
                PageSize.RA1 => new XSize(1729, 2438),
                PageSize.RA2 => new XSize(1219, 1729),
                PageSize.RA3 => new XSize(865, 1219),
                PageSize.RA4 => new XSize(609, 865),
                PageSize.RA5 => new XSize(433, 609),
                PageSize.B0 => new XSize(2835, 4008),
                PageSize.B1 => new XSize(2004, 2835),
                PageSize.B2 => new XSize(1417, 2004),
                PageSize.B3 => new XSize(1001, 1417),
                PageSize.B4 => new XSize(709, 1001),
                PageSize.B5 => new XSize(499, 709),
                // The non-ISO sizes ...
                PageSize.Quarto => // 8 x 10 inch²
                    new XSize(576, 720),
                PageSize.Foolscap => // 8 x 13 inch²
                    new XSize(576, 936),
                PageSize.Executive => // 7.5 x 10 inch²
                    new XSize(540, 720),
                PageSize.GovernmentLetter => // 8 x 10.5 inch²
                    new XSize(576, 756),
                PageSize.Letter => // 8.5 x 11 inch²
                    new XSize(612, 792),
                PageSize.Legal => // 8.5 x 14 inch²
                    new XSize(612, 1008),
                PageSize.Ledger => // 17 x 11 inch²
                    new XSize(1224, 792),
                PageSize.Tabloid => // 11 x 17 inch²
                    new XSize(792, 1224),
                PageSize.Post => // 15.5 x 19.25 inch²
                    new XSize(1126, 1386),
                PageSize.Crown => // 20 x 15 inch²
                    new XSize(1440, 1080),
                PageSize.LargePost => // 16.5 x 21 inch²
                    new XSize(1188, 1512),
                PageSize.Demy => // 17.5 x 22 inch²
                    new XSize(1260, 1584),
                PageSize.Medium => // 18 x 23 inch²
                    new XSize(1296, 1656),
                PageSize.Royal => // 20 x 25 inch²
                    new XSize(1440, 1800),
                PageSize.Elephant => // 23 x 28 inch²
                    new XSize(1565, 2016),
                PageSize.DoubleDemy => // 23.5 x 35 inch²
                    new XSize(1692, 2520),
                PageSize.QuadDemy => // 35 x 45 inch²
                    new XSize(2520, 3240),
                PageSize.STMT => // 5.5 x 8.5 inch²
                    new XSize(396, 612),
                PageSize.Folio => // 8.5 x 13 inch²
                    new XSize(612, 936),
                PageSize.Statement => // 5.5 x 8.5 inch²
                    new XSize(396, 612),
                PageSize.Size10x14 => // 10 x 14 inch²
                    new XSize(720, 1008),
                _ => throw new ArgumentException("Invalid PageSize.", "value")
            };

            var ratio = dpi / 72.0;

            return new XSize(size.Width * ratio, size.Height * ratio);
        }
    }
}