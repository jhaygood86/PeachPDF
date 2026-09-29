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

using System;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf.Internal;

namespace PeachPDF.PdfSharpCore.Pdf.Advanced
{
    /// <summary>
    /// Represents a tiling pattern dictionary.
    /// </summary>
    internal sealed class PdfTilingPattern : PdfDictionaryWithContentStream
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PdfShadingPattern"/> class.
        /// </summary>
        public PdfTilingPattern(PdfDocument document)
            : base(document)
        {
            Elements.SetName(Keys.Type, "/Pattern");
            Elements[Keys.PatternType] = new PdfInteger(1);
        }

        /// <summary>
        /// Sets this pattern up to repeat <paramref name="brush"/>'s form. <paramref name="matrix"/> maps pattern space (the brush's own
        /// top-left-origin, y-down space, in points) onto the page's default coordinate system.
        /// </summary>
        internal void SetupFromBrush(XTilingBrush brush, XMatrix matrix)
        {
            if (brush == null)
                throw new ArgumentNullException("brush");

            Elements[Keys.PaintType] = new PdfInteger(1);
            Elements[Keys.TilingType] = new PdfInteger(1);
            Elements.SetRectangle(Keys.BBox, new PdfRectangle(0, 0, brush.CellWidth, brush.CellHeight));
            Elements[Keys.XStep] = new PdfReal(brush.CellWidth);
            Elements[Keys.YStep] = new PdfReal(brush.CellHeight);
            Elements.SetMatrix(Keys.Matrix, matrix);

            string name;
            double cx, cy;
            if (brush.Tile is XForm form)
            {
                form.Finish();
                name = Resources.AddForm(_document.FormTable.GetForm(form));

                // A form is a picture the right way up in PDF's y-up space, scaled from the form's own size to the cell.
                cx = brush.CellWidth / form.PointWidth;
                cy = brush.CellHeight / form.PointHeight;
            }
            else
            {
                // An image is the unit square in PDF space, so it is sized to the cell directly.
                var image = brush.Tile;
                var was = image.Interpolate;
                if (brush.Interpolate is { } interpolate)
                    image.Interpolate = interpolate;

                try
                {
                    name = Resources.AddImage(_document.ImageTable.GetImage(image, brush.CellWidth, brush.CellHeight));
                }
                finally
                {
                    image.Interpolate = was;
                }

                cx = brush.CellWidth;
                cy = brush.CellHeight;
            }

            // The pattern space is y down and both kinds of tile are the right way up in y-up space, so the tile is placed flipped.
            var content = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "q {0:0.####} 0 0 {1:0.####} 0 {2:0.####} cm {3} Do Q\n", cx, -cy, brush.CellHeight, name);
            Stream = new PdfStream(PdfEncoders.RawEncoding.GetBytes(content), this);
            Elements.SetInteger("/Length", Stream.Length);
        }

        ///// <summary>
        ///// Setups the shading pattern from the specified brush.
        ///// </summary>
        //public void SetupFromBrush(XLinearGradientBrush brush, XMatrix matrix)
        //{
        //  if (brush == null)
        //    throw new ArgumentNullException("brush");

        //  PdfShading shading = new PdfShading(document);
        //  shading.SetupFromBrush(brush);
        //  Elements[Keys.Shading] = shading;
        //  Elements[Keys.Matrix] = new PdfLiteral("[" + PdfEncoders.ToString(matrix) + "]");
        //}

        /// <summary>
        /// Common keys for all streams.
        /// </summary>
        internal sealed new class Keys : PdfDictionaryWithContentStream.Keys
        {
            /// <summary>
            /// (Optional) The type of PDF object that this dictionary describes; if present,
            /// must be Pattern for a pattern dictionary.
            /// </summary>
            [KeyInfo(KeyType.Name | KeyType.Required)]
            public const string Type = "/Type";

            /// <summary>
            /// (Required) A code identifying the type of pattern that this dictionary describes;
            /// must be 1 for a tiling pattern.
            /// </summary>
            [KeyInfo(KeyType.Integer | KeyType.Required)]
            public const string PatternType = "/PatternType";

            /// <summary>
            /// (Required) A code that determines how the color of the pattern cell is to be specified:
            /// 1: Colored tiling pattern. The pattern�s content stream specifies the colors used to 
            /// paint the pattern cell. When the content stream begins execution, the current color
            /// is the one that was initially in effect in the pattern�s parent content stream.
            /// 2: Uncolored tiling pattern. The pattern�s content stream does not specify any color
            /// information. Instead, the entire pattern cell is painted with a separately specified color
            /// each time the pattern is used. Essentially, the content stream describes a stencil
            /// through which the current color is to be poured. The content stream must not invoke
            /// operators that specify colors or other color-related parameters in the graphics state;
            /// otherwise, an error occurs. The content stream may paint an image mask, however,
            /// since it does not specify any color information.
            /// </summary>
            [KeyInfo(KeyType.Integer | KeyType.Required)]
            public const string PaintType = "/PaintType";

            /// <summary>
            /// (Required) A code that controls adjustments to the spacing of tiles relative to the device
            /// pixel grid:
            /// 1: Constant spacing. Pattern cells are spaced consistently�that is, by a multiple of a
            /// device pixel. To achieve this, the application may need to distort the pattern cell slightly
            /// by making small adjustments to XStep, YStep, and the transformation matrix. The amount
            /// of distortion does not exceed 1 device pixel.
            /// 2: No distortion. The pattern cell is not distorted, but the spacing between pattern cells
            /// may vary by as much as 1 device pixel, both horizontally and vertically, when the pattern
            /// is painted. This achieves the spacing requested by XStep and YStep on average but not
            /// necessarily for each individual pattern cell.
            /// 3: Constant spacing and faster tiling. Pattern cells are spaced consistently as in tiling
            /// type 1 but with additional distortion permitted to enable a more efficient implementation. 
            /// </summary>
            [KeyInfo(KeyType.Integer | KeyType.Required)]
            public const string TilingType = "/TilingType";

            /// <summary>
            /// (Required) An array of four numbers in the pattern coordinate system giving the
            /// coordinates of the left, bottom, right, and top edges, respectively, of the pattern
            /// cell�s bounding box. These boundaries are used to clip the pattern cell.
            /// </summary>
            [KeyInfo(KeyType.Rectangle | KeyType.Optional)]
            public const string BBox = "/BBox";

            /// <summary>
            /// (Required) The desired horizontal spacing between pattern cells, measured in the
            /// pattern coordinate system.
            /// </summary>
            [KeyInfo(KeyType.Real | KeyType.Required)]
            public const string XStep = "/XStep";

            /// <summary>
            /// (Required) The desired vertical spacing between pattern cells, measured in the pattern
            /// coordinate system. Note that XStep and YStep may differ from the dimensions of the
            /// pattern cell implied by the BBox entry. This allows tiling with irregularly shaped figures.
            /// XStep and YStep may be either positive or negative but not zero.
            /// </summary>
            [KeyInfo(KeyType.Real | KeyType.Required)]
            public const string YStep = "/YStep";

            /// <summary>
            /// (Required) A resource dictionary containing all of the named resources required by
            /// the pattern�s content stream (see Section 3.7.2, �Resource Dictionaries�).
            /// </summary>
            [KeyInfo(KeyType.Dictionary | KeyType.Required)]
            public new const string Resources = "/Resources";

            /// <summary>
            /// (Optional) An array of six numbers specifying the pattern matrix.
            /// Default value: the identity matrix [1 0 0 1 0 0].
            /// </summary>
            [KeyInfo(KeyType.Array | KeyType.Optional)]
            public const string Matrix = "/Matrix";

            /// <summary>
            /// Gets the KeysMeta for these keys.
            /// </summary>
            internal static DictionaryMeta Meta
            {
                get { return _meta ?? (_meta = CreateMeta(typeof(Keys))); }
            }
            static DictionaryMeta _meta = null!;
        }

        /// <summary>
        /// Gets the KeysMeta of this dictionary type.
        /// </summary>
        internal override DictionaryMeta Meta
        {
            get { return Keys.Meta; }
        }
    }
}