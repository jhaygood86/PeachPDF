using System;
using System.Collections.Generic;
using System.Numerics;
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine;

namespace PeachDrawing.PDF.Import;

/// <summary>Paints one page of a <see cref="PdfSourceDocument"/> onto a <see cref="Canvas"/>.</summary>
internal static class PdfPagePainter
{
    /// <summary>The size of a page after its <c>/Rotate</c>, in points, using the crop box (or media box).</summary>
    public static (double Width, double Height) GetPageSize(PdfSourceDocument doc, PdfDictionary page)
    {
        var (_, _, w, h, rot) = Geometry(doc, page);
        return rot % 180 == 0 ? (w, h) : (h, w);
    }

    static (double X0, double Y0, double W, double H, int Rotate) Geometry(PdfSourceDocument doc, PdfDictionary page)
    {
        var box = doc.InheritedAttribute(page, "CropBox") as PdfArray ?? doc.InheritedAttribute(page, "MediaBox") as PdfArray;
        double x0 = 0, y0 = 0, x1 = 612, y1 = 792;
        if (box is { Elements.Count: >= 4 })
        {
            double a = doc.NumberOf(box.Elements[0]), b = doc.NumberOf(box.Elements[1]), c = doc.NumberOf(box.Elements[2]), d = doc.NumberOf(box.Elements[3]);
            x0 = Math.Min(a, c); x1 = Math.Max(a, c); y0 = Math.Min(b, d); y1 = Math.Max(b, d);
        }
        int rot = ((doc.IntOf(doc.InheritedAttribute(page, "Rotate")) % 360) + 360) % 360;
        return (x0, y0, x1 - x0, y1 - y0, rot / 90 * 90);
    }

    /// <summary>Maps PDF user space (origin bottom-left, y up) of <paramref name="page"/> to the canvas (origin top-left, y down), honouring <c>/Rotate</c> (clockwise).</summary>
    public static Matrix3x2 PageToCanvas(PdfSourceDocument doc, PdfDictionary page)
    {
        var (x0, y0, w, h, rot) = Geometry(doc, page);
        return rot switch
        {
            90 => new Matrix3x2(0, 1, 1, 0, (float)-y0, (float)-x0),
            180 => new Matrix3x2(-1, 0, 0, 1, (float)(x0 + w), (float)-y0),
            270 => new Matrix3x2(0, -1, -1, 0, (float)(y0 + h), (float)(x0 + w)),
            _ => new Matrix3x2(1, 0, 0, -1, (float)-x0, (float)(y0 + h)),
        };
    }

    /// <summary>
    /// Draws <paramref name="page"/> onto <paramref name="canvas"/>, whose origin is the top-left of the area the page
    /// occupies and whose y axis points down (the <see cref="Canvas"/> convention). Returns the features that could not
    /// be drawn yet.
    /// </summary>
    public static IReadOnlyCollection<string> Paint(PdfSourceDocument doc, PdfDictionary page, Canvas canvas, PdfAnnotationPaint annotations = PdfAnnotationPaint.None)
    {
        var (x0, y0, w, h, rot) = Geometry(doc, page);
        var toCanvas = PageToCanvas(doc, page);
        // A soft-masked group is drawn into a tile covering the page, in the canvas' own units.
        var (pageWidth, pageHeight) = rot % 180 == 0 ? (w, h) : (h, w);
        var origin = canvas.CurrentTransform;
        var far = System.Numerics.Vector2.Transform(new System.Numerics.Vector2((float)pageWidth, (float)pageHeight), origin);
        var region = new Rect(0, 0, Math.Max(1, Math.Ceiling(Math.Max(far.X, 0))), Math.Max(1, Math.Ceiling(Math.Max(far.Y, 0))));
        var interpreter = new PdfContentInterpreter(doc, canvas, region);
        canvas.PushTransform(toCanvas);
        try
        {
            var clip = canvas.GetGraphicsPath();
            clip.Start(x0, y0); clip.LineTo(x0 + w, y0); clip.LineTo(x0 + w, y0 + h); clip.LineTo(x0, y0 + h); clip.CloseFigure();
            canvas.PushClip(clip);
            try { interpreter.Run(doc.GetPageContent(page), doc.InheritedAttribute(page, "Resources") as PdfDictionary); }
            finally { interpreter.Finish(); canvas.PopClip(); }
        }
        finally { canvas.PopTransform(); }

        if (annotations == PdfAnnotationPaint.None) return interpreter.Unsupported;

        // The annotations are drawn over the content, each clipped to the page like the content is.
        var unsupported = new HashSet<string>(interpreter.Unsupported);
        canvas.PushTransform(toCanvas);
        try
        {
            var clip = canvas.GetGraphicsPath();
            clip.Start(x0, y0); clip.LineTo(x0 + w, y0); clip.LineTo(x0 + w, y0 + h); clip.LineTo(x0, y0 + h); clip.CloseFigure();
            canvas.PushClip(clip);
            try
            {
                foreach (var why in PdfAppearance.DrawAnnotations(doc, page, canvas, Matrix3x2.Identity, region, includeWidgets: annotations == PdfAnnotationPaint.All))
                    unsupported.Add(why);
            }
            finally { canvas.PopClip(); }
        }
        finally { canvas.PopTransform(); }
        return unsupported;
    }
}
