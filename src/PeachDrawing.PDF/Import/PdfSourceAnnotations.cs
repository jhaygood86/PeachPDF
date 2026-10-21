using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine;

namespace PeachDrawing.PDF.Import;

/// <summary>
/// Reads the annotations of a page that are neither links nor form field widgets (highlights, notes, stamps, drawings) as
/// <see cref="PdfAnnotationDefinition"/>s the writer can add to a page, each drawn by the appearance stream it had in the file.
/// </summary>
internal sealed class PdfSourceAnnotations(PdfSourceDocument doc)
{
    /// <summary>The annotations on <paramref name="page"/> that have an appearance, in order.</summary>
    public IReadOnlyList<PdfAnnotationDefinition> Annotations(PdfDictionary page, double pixelsPerPoint)
    {
        var result = new List<PdfAnnotationDefinition>();
        if (doc.ArrayOf(page, "Annots") is not { } annots) return result;
        var toCanvas = PdfPagePainter.PageToCanvas(doc, page);
        var resources = doc.InheritedAttribute(page, "Resources") as PdfDictionary;

        foreach (var item in annots.Elements)
        {
            if (doc.Resolve(item) is not PdfDictionary annot) continue;
            var subtype = doc.NameOf(annot.GetRaw("Subtype"))?.TrimStart('/');
            if (subtype is null or "Link" or "Popup" or "Widget") continue;
            int flags = doc.IntOf(annot.GetRaw("F"));
            if ((flags & (2 | 32)) != 0) continue;                                      // hidden, or not for viewing
            if (doc.ArrayOf(annot, "Rect") is not { Elements.Count: >= 4 } rect || PdfAppearance.Normal(doc, annot) is not { } form) continue;

            double x0 = doc.NumberOf(rect.Elements[0]), y0 = doc.NumberOf(rect.Elements[1]), x1 = doc.NumberOf(rect.Elements[2]), y1 = doc.NumberOf(rect.Elements[3]);
            var a = Vector2.Transform(new Vector2((float)x0, (float)y0), toCanvas);
            var b = Vector2.Transform(new Vector2((float)x1, (float)y1), toCanvas);
            var bounds = new Rect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
            var content = new Size(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
            if (content.Width <= 0 || content.Height <= 0) continue;

            Matrix3x2? transform = new Matrix3x2(toCanvas.M11, toCanvas.M12, -toCanvas.M21, -toCanvas.M22, 0, 0);
            if (Math.Abs(transform.Value.M11 - 1) < 1e-6 && Math.Abs(transform.Value.M22 - 1) < 1e-6 && Math.Abs(transform.Value.M12) < 1e-6 && Math.Abs(transform.Value.M21) < 1e-6)
                transform = null;

            double? borderWidth = doc.DictOf(annot, "BS") is { } bs && bs.ContainsKeyRaw("W") ? doc.NumberOf(bs.GetRaw("W"))
                : doc.ArrayOf(annot, "Border") is { Elements.Count: >= 3 } border ? doc.NumberOf(border.Elements[2]) : null;

            result.Add(new PdfAnnotationDefinition
            {
                Subtype = subtype,
                Bounds = bounds,
                ContentSize = content,
                AppearanceTransform = transform,
                PixelsPerPoint = pixelsPerPoint,
                PaintAppearance = canvas =>
                {
                    var toForm = new Matrix3x2((float)pixelsPerPoint, 0, 0, (float)-pixelsPerPoint, 0, (float)(content.Height * pixelsPerPoint));
                    var region = new Rect(0, 0, Math.Max(1, Math.Ceiling(content.Width * pixelsPerPoint)), Math.Max(1, Math.Ceiling(content.Height * pixelsPerPoint)));
                    PdfAppearance.Draw(doc, form, canvas, toForm, region, 0, 0, content.Width, content.Height, resources, variableText: false);
                },
                Contents = Text(annot, "Contents"),
                Title = Text(annot, "T"),
                Subject = Text(annot, "Subj"),
                Color = Colour(annot, "C"),
                InteriorColor = Colour(annot, "IC"),
                Opacity = annot.ContainsKeyRaw("CA") ? doc.NumberOf(annot.GetRaw("CA"), 1) : null,
                BorderWidth = borderWidth,
                IconName = doc.NameOf(annot.GetRaw("Name"))?.TrimStart('/'),
                QuadPoints = Points(annot, "QuadPoints", toCanvas),
                Vertices = Points(annot, "Vertices", toCanvas),
                LineEnds = Points(annot, "L", toCanvas),
                InkStrokes = Strokes(annot, toCanvas),
                ReadOnly = (flags & 64) != 0,
                Locked = (flags & 128) != 0,
                NoZoom = (flags & 8) != 0,
                NoRotate = (flags & 16) != 0,
                NotPrinted = (flags & 4) == 0,
            });
        }
        return result;
    }

    string? Text(PdfDictionary annot, string key) => doc.Resolve(annot.GetRaw(key)) is PdfString s ? PdfSourceNavigation.TextOf(s.Value) : null;

    /// <summary>A colour array of one (gray), three (RGB) or four (CMYK) numbers; null for none or transparent.</summary>
    PaintColor? Colour(PdfDictionary annot, string key)
    {
        if (doc.ArrayOf(annot, key) is not { } array) return null;
        var c = array.Elements.Select(e => doc.NumberOf(e)).ToArray();
        return c.Length switch
        {
            1 => PdfColorSpace.Gray.ToColor(c, 1),
            3 => PdfColorSpace.Rgb.ToColor(c, 1),
            4 => PdfColorSpace.Cmyk.ToColor(c, 1),
            _ => null,
        };
    }

    IReadOnlyList<PaintPoint> Points(PdfDictionary annot, string key, Matrix3x2 toCanvas)
    {
        if (doc.ArrayOf(annot, key) is not { } array) return [];
        return PointsOf(array, toCanvas);
    }

    IReadOnlyList<PaintPoint> PointsOf(PdfArray array, Matrix3x2 toCanvas)
    {
        var points = new List<PaintPoint>();
        for (int i = 0; i + 1 < array.Elements.Count; i += 2)
        {
            var p = Vector2.Transform(new Vector2((float)doc.NumberOf(array.Elements[i]), (float)doc.NumberOf(array.Elements[i + 1])), toCanvas);
            points.Add(new PaintPoint(p.X, p.Y));
        }
        return points;
    }

    IReadOnlyList<IReadOnlyList<PaintPoint>> Strokes(PdfDictionary annot, Matrix3x2 toCanvas)
    {
        if (doc.ArrayOf(annot, "InkList") is not { } list) return [];
        var strokes = new List<IReadOnlyList<PaintPoint>>();
        foreach (var item in list.Elements)
            if (doc.Resolve(item) is PdfArray stroke) strokes.Add(PointsOf(stroke, toCanvas));
        return strokes;
    }
}
