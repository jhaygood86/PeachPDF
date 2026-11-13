// Derived from PDFsharp (MIT) - see LICENSE.md

using System.Linq;
using System.Numerics;
using PeachDrawing.PDF.Engine.Drawing;
using PeachDrawing.Text.Outlines;
using PeachDrawing.Text.Shaping;
using PeachDrawing.Text;
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine.Internal;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Advanced;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

// ReSharper disable RedundantNameQualifier
// ReSharper disable CompareOfFloatsByEqualityOperator

namespace PeachDrawing.PDF.Engine.Rendering;

internal sealed partial class PdfGraphicsRenderer
{
    // ----- DrawImage ----------------------------------------------------------------------------

    public void DrawImage(PdfRasterImage image, double x, double y, double width, double height)
    {
        const string format = PdfNumberFormats.FourDecimals;

        string name = Realize(image, width, height);
        if (!(image is PdfForm))
        {
            AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm {4} Do Q\n",
                x, y + height, width, height, name);
            
        }
        else
        {
            BeginPage();

            PdfForm form = (PdfForm)image;
            form.Finish();

            Owner.FormTable.GetForm(form);

            double cx = width / image.PointWidth;
            double cy = height / image.PointHeight;

            if (cx != 0 && cy != 0)
            {
                AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm 100 Tz {4} Do Q\n",
                    x, y + height, cx, cy, name);
                
            }
        }
    }

    /// <summary>
    /// Draws all of <paramref name="image"/> into <paramref name="destRect"/>. <paramref name="srcRect"/>
    /// is NOT honored and cannot be at this level: PDF has no "draw this sub-rectangle of an XObject"
    /// operator, so cropping is a clip plus an off-destination placement of the whole image, which
    /// <c>PdfCanvas.DrawImage</c> composes above. Callers wanting a portion of an image must go
    /// through that; this overload exists so a whole-image draw keeps emitting the operators it always
    /// has. (Silently dropping srcRect here is what once made every <c>border-image</c> slice paint the
    /// entire source squashed into its own ninth of the frame.)
    /// </summary>
    public void DrawImage(PdfRasterImage image, Rect destRect, Rect srcRect)
    {
        const string format = PdfNumberFormats.FourDecimals;

        double x = destRect.X;
        double y = destRect.Y;
        double width = destRect.Width;
        double height = destRect.Height;

        string name = Realize(image, width, height);
        if (!(image is PdfForm))
        {
            AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm {4} Do\nQ\n",
                x, y + height, width, height, name);
            
        }
        else
        {
            BeginPage();

            PdfForm form = (PdfForm)image;
            form.Finish();

            Owner.FormTable.GetForm(form);

            double cx = width / image.PointWidth;
            double cy = height / image.PointHeight;

            if (cx != 0 && cy != 0)
            {
                AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm {4} Do Q\n",
                    x, y + height, cx, cy, name);
                
            }
        }
    }

    internal void AppendFormatImage(string format, double x, double y, double width, double height, string name)
    {
        PaintPoint result = WorldToView(new PaintPoint(x, y));
        _content.AppendFormat(CultureInfo.InvariantCulture, format, result.X, result.Y, width, height, name);
    }

    /// <summary>
    /// Gets the resource name of the specified image within this page or form.
    /// </summary>
    internal string GetImageName(PdfRasterImage image, double width, double height)
    {
        if (_page is not null)
            return _page.GetImageName(image, width, height);
        return _form.GetImageName(image, width, height);
    }

    /// <summary>
    /// Gets the resource name of the specified form within this page or form.
    /// </summary>
    internal string GetFormName(PdfForm form)
    {
        if (_page is not null)
            return _page.GetFormName(form);
        return _form.GetFormName(form);
    }
}
