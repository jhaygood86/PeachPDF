#region PDFsharp - A .NET library for processing PDF
//
// Authors:
//   Klaus Potzesny
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

namespace PeachPDF.PdfSharpCore.Drawing.BarCodes
{
    /// <summary>
    /// Specifies whether and how the text is displayed at the code.
    /// </summary>
    public enum TextLocation
    {
        /// <summary>
        /// No text is drawn.
        /// </summary>
        None,

        /// <summary>
        /// The text is located above the code.
        /// </summary>
        Above,

        /// <summary>
        /// The text is located below the code.
        /// </summary>
        Below,


        /// <summary>
        /// The text is located above within the code.
        /// </summary>
        AboveEmbedded,


        /// <summary>
        /// The text is located below within the code.
        /// </summary>
        BelowEmbedded,
    }
}
