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

#nullable disable warnings

using System.Collections.Generic;

namespace PeachPDF.PdfSharpCore.Pdf.Advanced
{
    /// <summary>
    /// Contains all used ExtGState objects of a document.
    /// </summary>
    internal sealed class PdfExtGStateTable : PdfResourceTable
    {
        /// <summary>
        /// Initializes a new instance of this class, which is a singleton for each document.
        /// </summary>
        public PdfExtGStateTable(PdfDocument document)
            : base(document)
        { }


        /// <summary>
        /// Gets a PdfExtGState with the key 'CA' set to the specified alpha value.
        /// </summary>
        public PdfExtGState GetExtGStateStroke(double alpha, bool overprint)
        {
            string key = PdfExtGState.MakeKey(alpha, overprint);
            PdfExtGState extGState;
            if (!_strokeAlphaValues.TryGetValue(key, out extGState))
            {
                extGState = new PdfExtGState(Owner);
                //extGState.Elements[PdfExtGState.Keys.CA] = new PdfReal(alpha);
                extGState.StrokeAlpha = alpha;
                if (overprint)
                {
                    extGState.StrokeOverprint = true;
                    extGState.Elements.SetInteger(PdfExtGState.Keys.OPM, 1);
                }
                _strokeAlphaValues[key] = extGState;
            }
            return extGState;
        }

        /// <summary>
        /// Gets a PdfExtGState with the key 'ca' set to the specified alpha value.
        /// </summary>
        public PdfExtGState GetExtGStateNonStroke(double alpha, bool overprint)
        {
            string key = PdfExtGState.MakeKey(alpha, overprint);
            PdfExtGState extGState;
            if (!_nonStrokeStates.TryGetValue(key, out extGState))
            {
                extGState = new PdfExtGState(Owner);
                //extGState.Elements[PdfExtGState.Keys.ca] = new PdfReal(alpha);
                extGState.NonStrokeAlpha = alpha;
                if (overprint)
                {
                    extGState.NonStrokeOverprint = true;
                    extGState.Elements.SetInteger(PdfExtGState.Keys.OPM, 1);
                }

                _nonStrokeStates[key] = extGState;
            }
            return extGState;
        }

        /// <summary>
        /// Gets a PdfExtGState with both 'ca' (non-stroking alpha) and 'CA' (stroking alpha) set to the
        /// same alpha value - used to composite a whole transparency-group Form XObject (e.g. for CSS/SVG
        /// group <c>opacity</c>) with a single <c>gs</c> operator, rather than needing separate stroke/non-stroke states.
        /// No overprint parameter, unlike <see cref="GetExtGStateStroke"/>/<see cref="GetExtGStateNonStroke"/> -
        /// overprint is a fill/stroke color-realization concern, not something whole-group opacity compositing needs.
        /// </summary>
        public PdfExtGState GetExtGState(double alpha)
        {
            string key = PdfExtGState.MakeKey(alpha, false);
            PdfExtGState extGState;
            if (!_combinedAlphaStates.TryGetValue(key, out extGState))
            {
                extGState = new PdfExtGState(Owner)
                {
                    StrokeAlpha = alpha,
                    NonStrokeAlpha = alpha
                };

                _combinedAlphaStates[key] = extGState;
            }
            return extGState;
        }

        readonly Dictionary<string, PdfExtGState> _strokeAlphaValues = new Dictionary<string, PdfExtGState>();
        readonly Dictionary<string, PdfExtGState> _nonStrokeStates = new Dictionary<string, PdfExtGState>();
        readonly Dictionary<string, PdfExtGState> _combinedAlphaStates = new Dictionary<string, PdfExtGState>();
    }
}