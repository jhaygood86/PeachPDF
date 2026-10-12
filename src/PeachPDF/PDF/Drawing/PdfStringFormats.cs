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

namespace PeachPDF.PDF.Drawing
{
    /// <summary>
    /// Represents predefined text layouts.
    /// </summary>
    internal static class PdfStringFormats
    {
        /// <summary>
        /// Gets a new PdfStringFormat object that aligns the text left on the base line.
        /// This is the same as BaseLineLeft.
        /// </summary>
        public static PdfStringFormat Default
        {
            get { return BaseLineLeft; }
        }

        /// <summary>
        /// Gets a new PdfStringFormat object that aligns the text left on the base line.
        /// This is the same as Default.
        /// </summary>
        public static PdfStringFormat BaseLineLeft
        {
            get
            {
                // Create new format to allow changes.
                PdfStringFormat format = new PdfStringFormat();
                format.Alignment = PdfStringAlignment.Near;
                format.LineAlignment = PdfLineAlignment.BaseLine;
                return format;
            }
        }

        /// <summary>
        /// Gets a new PdfStringFormat object that aligns the text top left of the layout rectangle.
        /// </summary>
        public static PdfStringFormat TopLeft
        {
            get
            {
                // Create new format to allow changes.
                PdfStringFormat format = new PdfStringFormat();
                format.Alignment = PdfStringAlignment.Near;
                format.LineAlignment = PdfLineAlignment.Near;
                return format;
            }
        }

        /// <summary>
        /// Gets a new PdfStringFormat object that aligns the text center left of the layout rectangle.
        /// </summary>
        public static PdfStringFormat CenterLeft
        {
            get
            {
                // Create new format to allow changes.
                PdfStringFormat format = new PdfStringFormat();
                format.Alignment = PdfStringAlignment.Near;
                format.LineAlignment = PdfLineAlignment.Center;
                return format;
            }
        }

        /// <summary>
        /// Gets a new PdfStringFormat object that aligns the text bottom left of the layout rectangle.
        /// </summary>
        public static PdfStringFormat BottomLeft
        {
            get
            {
                // Create new format to allow changes.
                PdfStringFormat format = new PdfStringFormat();
                format.Alignment = PdfStringAlignment.Near;
                format.LineAlignment = PdfLineAlignment.Far;
                return format;
            }
        }

        /// <summary>
        /// Gets a new PdfStringFormat object that centers the text in the middle of the base line.
        /// </summary>
        public static PdfStringFormat BaseLineCenter
        {
            get
            {
                // Create new format to allow changes.
                PdfStringFormat format = new PdfStringFormat();
                format.Alignment = PdfStringAlignment.Center;
                format.LineAlignment = PdfLineAlignment.BaseLine;
                return format;
            }
        }

        /// <summary>
        /// Gets a new PdfStringFormat object that centers the text at the top of the layout rectangle.
        /// </summary>
        public static PdfStringFormat TopCenter
        {
            get
            {
                // Create new format to allow changes.
                PdfStringFormat format = new PdfStringFormat();
                format.Alignment = PdfStringAlignment.Center;
                format.LineAlignment = PdfLineAlignment.Near;
                return format;
            }
        }

        /// <summary>
        /// Gets a new PdfStringFormat object that centers the text in the middle of the layout rectangle.
        /// </summary>
        public static PdfStringFormat Center
        {
            get
            {
                // Create new format to allow changes.
                PdfStringFormat format = new PdfStringFormat();
                format.Alignment = PdfStringAlignment.Center;
                format.LineAlignment = PdfLineAlignment.Center;
                return format;
            }
        }

        /// <summary>
        /// Gets a new PdfStringFormat object that centers the text at the bottom of the layout rectangle.
        /// </summary>
        public static PdfStringFormat BottomCenter
        {
            get
            {
                // Create new format to allow changes.
                PdfStringFormat format = new PdfStringFormat();
                format.Alignment = PdfStringAlignment.Center;
                format.LineAlignment = PdfLineAlignment.Far;
                return format;
            }
        }

        /// <summary>
        /// Gets a new PdfStringFormat object that aligns the text in right on the base line.
        /// </summary>
        public static PdfStringFormat BaseLineRight
        {
            get
            {
                // Create new format to allow changes.
                PdfStringFormat format = new PdfStringFormat();
                format.Alignment = PdfStringAlignment.Far;
                format.LineAlignment = PdfLineAlignment.BaseLine;
                return format;
            }
        }

        /// <summary>
        /// Gets a new PdfStringFormat object that aligns the text top right of the layout rectangle.
        /// </summary>
        public static PdfStringFormat TopRight
        {
            get
            {
                // Create new format to allow changes.
                PdfStringFormat format = new PdfStringFormat();
                format.Alignment = PdfStringAlignment.Far;
                format.LineAlignment = PdfLineAlignment.Near;
                return format;
            }
        }

        /// <summary>
        /// Gets a new PdfStringFormat object that aligns the text center right of the layout rectangle.
        /// </summary>
        public static PdfStringFormat CenterRight
        {
            get
            {
                // Create new format to allow changes.
                PdfStringFormat format = new PdfStringFormat();
                format.Alignment = PdfStringAlignment.Far;
                format.LineAlignment = PdfLineAlignment.Center;
                return format;
            }
        }

        /// <summary>
        /// Gets a new PdfStringFormat object that aligns the text at the bottom right of the layout rectangle.
        /// </summary>
        public static PdfStringFormat BottomRight
        {
            get
            {
                // Create new format to allow changes.
                PdfStringFormat format = new PdfStringFormat();
                format.Alignment = PdfStringAlignment.Far;
                format.LineAlignment = PdfLineAlignment.Far;
                return format;
            }
        }
    }
}