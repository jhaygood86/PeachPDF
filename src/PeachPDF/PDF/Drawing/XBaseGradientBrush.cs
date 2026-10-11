#region PDFsharp - A .NET library for processing PDF
//
// Authors:
//   Ben Askren
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

// ReSharper disable RedundantNameQualifier because it is required for hybrid build
using System.Numerics;

namespace PeachPDF.PDF.Drawing
{
    internal class XBaseGradientBrush : XBrush
    {
        protected XBaseGradientBrush(XColor color1, XColor color2)
        {
            _color1 = color1;
            _color2 = color2;

        }

        /// <summary>
        /// Gets or sets an Matrix3x2 that defines a local geometric transform for this LinearGradientBrush.
        /// </summary>
        public Matrix3x2 Transform
        {
            get { return _matrix; }
            set { _matrix = value; }
        }

        //public void SetBlendTriangularShape(double focus);
        //public void SetBlendTriangularShape(double focus, double scale);
        //public void SetSigmaBellShape(double focus);
        //public void SetSigmaBellShape(double focus, double scale);



        //public Blend Blend { get; set; }
        //public bool GammaCorrection { get; set; }
        //public ColorBlend InterpolationColors { get; set; }
        //public XColor[] LinearColors { get; set; }
        //public RectangleF Rectangle { get; }
        //public WrapMode WrapMode { get; set; }
        //private bool interpolationColorsWasSet;

        internal XColor _color1, _color2;
        internal Matrix3x2 _matrix = Matrix3x2.Identity;

    }
}
