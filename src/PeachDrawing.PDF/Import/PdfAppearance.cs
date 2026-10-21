using System;
using System.Collections.Generic;
using System.Numerics;
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine;

namespace PeachDrawing.PDF.Import;

/// <summary>Appearance streams (the <c>/AP</c> of an annotation or form field widget) fitted to a rectangle and drawn onto a canvas.</summary>
internal static class PdfAppearance
{
    /// <summary>
    /// The normal appearance of an annotation: the stream in <c>/AP /N</c>, or, when that is a dictionary of states, the one for
    /// <paramref name="state"/> (default the annotation's <c>/AS</c>). Null when there is none.
    /// </summary>
    public static PdfDictionary? Normal(PdfSourceDocument doc, PdfDictionary annotation, string? state = null)
    {
        if (doc.DictOf(annotation, "AP") is not { } ap || doc.Resolve(ap.GetRaw("N")) is not PdfDictionary n) return null;
        if (n.Stream is not null) return n;
        state ??= doc.NameOf(annotation.GetRaw("AS"))?.TrimStart('/');
        return state is not null && doc.Resolve(n.GetRaw(state)) is PdfDictionary { Stream: not null } chosen ? chosen : null;
    }

    /// <summary>
    /// The transform that carries the appearance's box (its <c>/BBox</c> taken through its <c>/Matrix</c>) onto the rectangle, as ISO 32000-1
    /// 12.5.5 describes. It is applied outside the form's own matrix. Null for an empty box or rectangle.
    /// </summary>
    public static Matrix3x2? Fit(PdfSourceDocument doc, PdfDictionary form, double x0, double y0, double x1, double y1)
    {
        if (doc.ArrayOf(form, "BBox") is not { Elements.Count: >= 4 } b) return null;
        var matrix = Matrix3x2.Identity;
        if (doc.ArrayOf(form, "Matrix") is { Elements.Count: >= 6 } m)
            matrix = new Matrix3x2((float)doc.NumberOf(m.Elements[0]), (float)doc.NumberOf(m.Elements[1]), (float)doc.NumberOf(m.Elements[2]),
                (float)doc.NumberOf(m.Elements[3]), (float)doc.NumberOf(m.Elements[4]), (float)doc.NumberOf(m.Elements[5]));

        double bx0 = doc.NumberOf(b.Elements[0]), by0 = doc.NumberOf(b.Elements[1]), bx1 = doc.NumberOf(b.Elements[2]), by1 = doc.NumberOf(b.Elements[3]);
        var corners = new[]
        {
            Vector2.Transform(new Vector2((float)bx0, (float)by0), matrix), Vector2.Transform(new Vector2((float)bx1, (float)by0), matrix),
            Vector2.Transform(new Vector2((float)bx1, (float)by1), matrix), Vector2.Transform(new Vector2((float)bx0, (float)by1), matrix),
        };
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var c in corners)
        {
            minX = Math.Min(minX, c.X); minY = Math.Min(minY, c.Y); maxX = Math.Max(maxX, c.X); maxY = Math.Max(maxY, c.Y);
        }

        double width = maxX - minX, height = maxY - minY;
        double rw = Math.Abs(x1 - x0), rh = Math.Abs(y1 - y0);
        if (!(width > 0) || !(height > 0) || !(rw > 0) || !(rh > 0)) return null;
        return Matrix3x2.CreateTranslation(-minX, -minY) * Matrix3x2.CreateScale((float)(rw / width), (float)(rh / height))
            * Matrix3x2.CreateTranslation((float)Math.Min(x0, x1), (float)Math.Min(y0, y1));
    }

    /// <summary>
    /// Draws <paramref name="form"/> onto <paramref name="canvas"/> under <paramref name="toCanvas"/> (which carries the appearance's
    /// target space onto the canvas), fitted to the rectangle given in that space.
    /// </summary>
    public static IReadOnlyCollection<string> Draw(PdfSourceDocument doc, PdfDictionary form, Canvas canvas, Matrix3x2 toCanvas, Rect region,
        double x0, double y0, double x1, double y1, PdfDictionary? fallbackResources, bool variableText, PdfTagBinding? tags = null)
    {
        if (Fit(doc, form, x0, y0, x1, y1) is not { } fit) return [];
        var interpreter = new PdfContentInterpreter(doc, canvas, region) { MarkVariableText = variableText, Tags = tags };
        canvas.PushTransform(fit * toCanvas);
        try { interpreter.PaintAppearance(form, fallbackResources); }
        finally { interpreter.Finish(); canvas.PopTransform(); }
        return interpreter.Unsupported;
    }

    /// <summary>
    /// Draws the visible annotations of a page (those with a normal appearance), after its content. Links and popups have nothing to draw;
    /// form field widgets are drawn only when <paramref name="includeWidgets"/> is set.
    /// </summary>
    public static IReadOnlyCollection<string> DrawAnnotations(PdfSourceDocument doc, PdfDictionary page, Canvas canvas, Matrix3x2 pageToCanvas, Rect region, bool includeWidgets, PdfTagBinding? tags = null)
    {
        var unsupported = new HashSet<string>();
        if (doc.ArrayOf(page, "Annots") is not { } annots) return unsupported;
        var resources = doc.InheritedAttribute(page, "Resources") as PdfDictionary;

        foreach (var item in annots.Elements)
        {
            if (doc.Resolve(item) is not PdfDictionary annot) continue;
            var subtype = doc.NameOf(annot.GetRaw("Subtype"));
            if (subtype is null or "/Link" or "/Popup") continue;
            // A widget that is carried as a form field is not drawn here; one that cannot be (a push button, a signature) always is.
            if (subtype == "/Widget" && !includeWidgets && PdfSourceForms.KindOf(doc, annot) is not null) continue;
            int flags = doc.IntOf(annot.GetRaw("F"));
            if ((flags & (2 | 32)) != 0) continue;                         // hidden, or not for viewing
            if (doc.ArrayOf(annot, "Rect") is not { Elements.Count: >= 4 } r || Normal(doc, annot) is not { } form) continue;

            foreach (var why in Draw(doc, form, canvas, pageToCanvas, region, doc.NumberOf(r.Elements[0]), doc.NumberOf(r.Elements[1]),
                         doc.NumberOf(r.Elements[2]), doc.NumberOf(r.Elements[3]), resources, variableText: false, tags))
                unsupported.Add(why);
        }
        return unsupported;
    }
}
