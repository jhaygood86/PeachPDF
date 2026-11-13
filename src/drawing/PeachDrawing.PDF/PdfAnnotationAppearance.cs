using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachDrawing.PDF;
/// <summary>
/// Draws the appearance of the standard annotations - icons, text markup, outlined shapes, free text and stamps - so a caller needs no
/// drawing code of its own, and so the annotation carries the appearance stream a PDF/A document requires of every annotation.
/// Each method returns a callback to give as <see cref="PdfAnnotationDefinition.PaintAppearance"/>; the callback draws in points,
/// from the top-left corner of the annotation's <see cref="PdfAnnotationDefinition.ContentSize"/>, so leave
/// <see cref="PdfAnnotationDefinition.PixelsPerPoint"/> at 1.
/// </summary>
public static class PdfAnnotationAppearance
{
    /// <summary>The name the PDF gives <paramref name="icon"/> (the <c>/Name</c> of the annotation).</summary>
    /// <param name="icon">the icon</param>
    public static string IconName(PdfAnnotationIcon icon) => icon switch
    {
        PdfAnnotationIcon.Note => "Note",
        PdfAnnotationIcon.Comment => "Comment",
        PdfAnnotationIcon.Key => "Key",
        PdfAnnotationIcon.Help => "Help",
        PdfAnnotationIcon.NewParagraph => "NewParagraph",
        PdfAnnotationIcon.Paragraph => "Paragraph",
        PdfAnnotationIcon.Insert => "Insert",
        PdfAnnotationIcon.Paperclip => "Paperclip",
        PdfAnnotationIcon.PushPin => "PushPin",
        PdfAnnotationIcon.Graph => "Graph",
        PdfAnnotationIcon.Tag => "Tag",
        _ => throw new ArgumentOutOfRangeException(nameof(icon), icon, null),
    };

    /// <summary>Whether <paramref name="icon"/> is one of the icons a file attachment annotation may use (the others belong to notes).</summary>
    /// <param name="icon">the icon</param>
    public static bool IsFileAttachmentIcon(PdfAnnotationIcon icon) =>
        icon is PdfAnnotationIcon.Paperclip or PdfAnnotationIcon.PushPin or PdfAnnotationIcon.Graph or PdfAnnotationIcon.Tag;

    /// <summary>The color a note or an attached-file icon is filled with when none is given.</summary>
    public static PaintColor DefaultIconColor { get; } = PaintColor.FromArgb(255, 255, 214, 64);

    /// <summary>Draws <paramref name="icon"/> filling a box of <paramref name="size"/> points.</summary>
    /// <param name="icon">the icon</param>
    /// <param name="size">the size of the annotation, in points</param>
    /// <param name="color">the icon's fill, or <see langword="null"/> for <see cref="DefaultIconColor"/></param>
    public static Action<Canvas> Icon(PdfAnnotationIcon icon, Size size, PaintColor? color = null)
    {
        var fill = color ?? DefaultIconColor;
        return canvas => PaintIcon(canvas, icon, size, fill);
    }

    /// <summary>
    /// Draws a text markup annotation over the regions <paramref name="quadPoints"/> names: a translucent fill for <c>Highlight</c>, a
    /// line under the text for <c>Underline</c>, a wavy one for <c>Squiggly</c>, a line through it for <c>StrikeOut</c>.
    /// </summary>
    /// <param name="subtype">the annotation type: <c>Highlight</c>, <c>Underline</c>, <c>Squiggly</c> or <c>StrikeOut</c></param>
    /// <param name="quadPoints">four points per region, in points from the page's top-left corner with y down</param>
    /// <param name="bounds">the annotation's box on the page, in the same space</param>
    /// <param name="color">the color of the mark</param>
    public static Action<Canvas> Markup(string subtype, IReadOnlyList<PaintPoint> quadPoints, Rect bounds, PaintColor color)
    {
        ArgumentNullException.ThrowIfNull(subtype);
        ArgumentNullException.ThrowIfNull(quadPoints);

        if (subtype is not ("Highlight" or "Underline" or "Squiggly" or "StrikeOut"))
            throw new ArgumentException($"'{subtype}' is not a text markup annotation type.", nameof(subtype));

        var regions = new List<Rect>();
        for (var i = 0; i + 3 < quadPoints.Count; i += 4)
        {
            var xs = new[] { quadPoints[i].X, quadPoints[i + 1].X, quadPoints[i + 2].X, quadPoints[i + 3].X };
            var ys = new[] { quadPoints[i].Y, quadPoints[i + 1].Y, quadPoints[i + 2].Y, quadPoints[i + 3].Y };
            regions.Add(new Rect(xs.Min() - bounds.X, ys.Min() - bounds.Y, xs.Max() - xs.Min(), ys.Max() - ys.Min()));
        }

        return canvas =>
        {
            foreach (var region in regions)
            {
                switch (subtype)
                {
                    case "Highlight":
                        canvas.DrawRectangle(canvas.GetSolidBrush(PaintColor.FromArgb(110, color.R, color.G, color.B)), region.X, region.Y, region.Width, region.Height);
                        break;
                    case "Underline":
                        DrawHorizontal(canvas, color, region.X, region.Right, region.Bottom - 0.6);
                        break;
                    case "StrikeOut":
                        DrawHorizontal(canvas, color, region.X, region.Right, region.Y + region.Height * 0.55);
                        break;
                    default:
                        DrawSquiggle(canvas, color, region);
                        break;
                }
            }
        };
    }

    /// <summary>Draws the outline of a rectangle (<c>Square</c>) or its inscribed ellipse (<c>Circle</c>) filling <paramref name="size"/>.</summary>
    /// <param name="ellipse"><see langword="true"/> for the ellipse</param>
    /// <param name="size">the size of the annotation, in points</param>
    /// <param name="color">the outline color</param>
    /// <param name="lineWidth">the outline width, in points</param>
    public static Action<Canvas> Outline(bool ellipse, Size size, PaintColor color, double lineWidth = 1)
    {
        return canvas =>
        {
            var pen = canvas.GetPen(color);
            pen.Width = lineWidth;
            var inset = lineWidth / 2;
            if (!ellipse)
            {
                canvas.DrawRectangle(pen, inset, inset, Math.Max(0, size.Width - lineWidth), Math.Max(0, size.Height - lineWidth));
                return;
            }

            using var path = canvas.GetGraphicsPath();
            AddEllipse(path, size.Width / 2, size.Height / 2, Math.Max(0.1, size.Width / 2 - inset), Math.Max(0.1, size.Height / 2 - inset));
            canvas.DrawPath(pen, path);
        };
    }

    /// <summary>Draws a straight line between two corners of the annotation box (<c>Line</c>).</summary>
    /// <param name="from">the start, in points from the annotation's top-left corner</param>
    /// <param name="to">the end, in the same space</param>
    /// <param name="color">the line color</param>
    /// <param name="lineWidth">the line width, in points</param>
    public static Action<Canvas> Line(PaintPoint from, PaintPoint to, PaintColor color, double lineWidth = 1)
    {
        return canvas =>
        {
            var pen = canvas.GetPen(color);
            pen.Width = lineWidth;
            pen.LineCap = LineCap.Round;
            canvas.DrawLine(pen, from.X, from.Y, to.X, to.Y);
        };
    }

    /// <summary>Draws freehand strokes (<c>Ink</c>).</summary>
    /// <param name="strokes">each stroke a run of points, in points from the annotation's top-left corner</param>
    /// <param name="color">the stroke color</param>
    /// <param name="lineWidth">the stroke width, in points</param>
    public static Action<Canvas> Ink(IReadOnlyList<IReadOnlyList<PaintPoint>> strokes, PaintColor color, double lineWidth = 1)
    {
        ArgumentNullException.ThrowIfNull(strokes);
        return canvas =>
        {
            var pen = canvas.GetPen(color);
            pen.Width = lineWidth;
            pen.LineCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;
            foreach (var stroke in strokes.Where(s => s.Count > 1))
            {
                using var path = canvas.GetGraphicsPath();
                path.Start(stroke[0].X, stroke[0].Y);
                for (var i = 1; i < stroke.Count; i++)
                    path.LineTo(stroke[i].X, stroke[i].Y);
                canvas.DrawPath(pen, path);
            }
        };
    }

    /// <summary>Draws a box of text (<c>FreeText</c>): <paramref name="text"/> on a pale background with a thin border.</summary>
    /// <param name="text">the text</param>
    /// <param name="font">the font to draw it with</param>
    /// <param name="textColor">the color of the text and the border</param>
    /// <param name="size">the size of the annotation, in points</param>
    public static Action<Canvas> FreeText(string text, Font font, PaintColor textColor, Size size)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(font);
        return canvas =>
        {
            canvas.DrawRectangle(canvas.GetSolidBrush(PaintColor.FromArgb(255, 255, 255, 224)), 0, 0, size.Width, size.Height);
            var pen = canvas.GetPen(textColor);
            pen.Width = 0.75;
            canvas.DrawRectangle(pen, 0.375, 0.375, Math.Max(0, size.Width - 0.75), Math.Max(0, size.Height - 0.75));
            var measured = canvas.MeasureString(text, font);
            canvas.DrawString(text, font, textColor, new PaintPoint(3, Math.Max(0, (size.Height - measured.Height) / 2)), measured);
        };
    }

    /// <summary>Draws a rubber stamp: <paramref name="label"/> in capitals inside a double border.</summary>
    /// <param name="label">the text of the stamp</param>
    /// <param name="font">the font to draw it with</param>
    /// <param name="color">the color of the text and the border</param>
    /// <param name="size">the size of the annotation, in points</param>
    public static Action<Canvas> Stamp(string label, Font font, PaintColor color, Size size)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(font);
        return canvas =>
        {
            var pen = canvas.GetPen(color);
            pen.Width = 1.5;
            canvas.DrawRectangle(pen, 0.75, 0.75, Math.Max(0, size.Width - 1.5), Math.Max(0, size.Height - 1.5));
            pen = canvas.GetPen(color);
            pen.Width = 0.5;
            canvas.DrawRectangle(pen, 3, 3, Math.Max(0, size.Width - 6), Math.Max(0, size.Height - 6));
            var text = label.ToUpperInvariant();
            var measured = canvas.MeasureString(text, font);
            canvas.DrawString(text, font, color, new PaintPoint(Math.Max(0, (size.Width - measured.Width) / 2), Math.Max(0, (size.Height - measured.Height) / 2)), measured);
        };
    }

    private static void DrawHorizontal(Canvas canvas, PaintColor color, double x1, double x2, double y)
    {
        var pen = canvas.GetPen(color);
        pen.Width = 0.9;
        canvas.DrawLine(pen, x1, y, x2, y);
    }

    private static void DrawSquiggle(Canvas canvas, PaintColor color, Rect region)
    {
        var pen = canvas.GetPen(color);
        pen.Width = 0.7;
        using var path = canvas.GetGraphicsPath();
        var baseline = region.Bottom - 1.2;
        path.Start(region.X, baseline);
        var up = true;
        for (var x = region.X + 1.5; x < region.Right + 1.5; x += 1.5)
        {
            path.LineTo(Math.Min(x, region.Right), baseline + (up ? -1 : 1));
            up = !up;
        }
        canvas.DrawPath(pen, path);
    }

    private static void AddEllipse(GraphicsPath path, double cx, double cy, double rx, double ry)
    {
        path.Start(cx + rx, cy);
        path.AddArc(cx - rx, cy, rx, ry, 0, false, true);
        path.AddArc(cx + rx, cy, rx, ry, 0, false, true);
        path.CloseFigure();
    }

    // The icons are designed on a 16 x 16 grid and scaled to the box; strokes are in grid units.
    private static void PaintIcon(Canvas canvas, PdfAnnotationIcon icon, Size size, PaintColor fill)
    {
        var scale = Math.Min(size.Width, size.Height) / 16;
        if (scale <= 0)
            return;

        var ox = (size.Width - 16 * scale) / 2;
        var oy = (size.Height - 16 * scale) / 2;
        double X(double v) => ox + v * scale;
        double Y(double v) => oy + v * scale;

        var ink = PaintColor.FromArgb(255, 40, 40, 40);
        var brush = canvas.GetSolidBrush(fill);
        var inkBrush = canvas.GetSolidBrush(ink);
        var pen = canvas.GetPen(ink);
        pen.Width = Math.Max(0.4, 1 * scale);
        pen.LineCap = LineCap.Round;
        pen.LineJoin = LineJoin.Round;

        void Polygon(Brush? f, Pen? s, params double[] pts)
        {
            using var path = canvas.GetGraphicsPath();
            path.Start(X(pts[0]), Y(pts[1]));
            for (var i = 2; i + 1 < pts.Length; i += 2)
                path.LineTo(X(pts[i]), Y(pts[i + 1]));
            path.CloseFigure();
            if (f != null) canvas.DrawPath(f, path);
            if (s != null) canvas.DrawPath(s, path);
        }

        void Polyline(Pen s, params double[] pts)
        {
            using var path = canvas.GetGraphicsPath();
            path.Start(X(pts[0]), Y(pts[1]));
            for (var i = 2; i + 1 < pts.Length; i += 2)
                path.LineTo(X(pts[i]), Y(pts[i + 1]));
            canvas.DrawPath(s, path);
        }

        void Circle(Brush? f, Pen? s, double cx, double cy, double r)
        {
            using var path = canvas.GetGraphicsPath();
            AddEllipse(path, X(cx), Y(cy), r * scale, r * scale);
            if (f != null) canvas.DrawPath(f, path);
            if (s != null) canvas.DrawPath(s, path);
        }

        switch (icon)
        {
            case PdfAnnotationIcon.Note:
                Polygon(brush, pen, 3, 1.5, 10.5, 1.5, 13, 4, 13, 14.5, 3, 14.5);
                Polyline(pen, 10.5, 1.5, 10.5, 4, 13, 4);
                Polyline(pen, 5, 7, 11, 7);
                Polyline(pen, 5, 9.5, 11, 9.5);
                Polyline(pen, 5, 12, 9, 12);
                break;
            case PdfAnnotationIcon.Comment:
                Polygon(brush, pen, 1.5, 2.5, 14.5, 2.5, 14.5, 11, 8, 11, 4.5, 14.5, 4.5, 11, 1.5, 11);
                Polyline(pen, 4, 5.5, 12, 5.5);
                Polyline(pen, 4, 8, 10, 8);
                break;
            case PdfAnnotationIcon.Key:
                Circle(brush, pen, 5, 5, 3.5);
                Polyline(pen, 7.5, 7.5, 14, 14);
                Polyline(pen, 11.5, 11.5, 13.5, 9.5);
                Polyline(pen, 13, 13, 15, 11);
                break;
            case PdfAnnotationIcon.Help:
                Circle(brush, pen, 8, 8, 6.5);
                using (var hook = canvas.GetGraphicsPath())
                {
                    hook.Start(X(5.8), Y(6.2));
                    hook.AddBezierTo(X(5.8), Y(3.2), X(10.4), Y(3.2), X(10.4), Y(6));
                    hook.AddBezierTo(X(10.4), Y(7.6), X(8), Y(7.6), X(8), Y(9.4));
                    canvas.DrawPath(pen, hook);
                }
                Circle(inkBrush, null, 8, 11.7, 0.8);
                break;
            case PdfAnnotationIcon.NewParagraph:
                Polygon(inkBrush, null, 3, 6, 6, 1.5, 9, 6);
                Polyline(pen, 6, 6, 6, 9);
                Polyline(pen, 10, 10, 15, 10);
                Polyline(pen, 12.5, 7.5, 12.5, 12.5);
                Polyline(pen, 2, 14.5, 9, 14.5);
                break;
            case PdfAnnotationIcon.Paragraph:
                Circle(brush, pen, 8, 5, 3.5);
                Polyline(pen, 8.5, 1.5, 8.5, 14.5);
                Polyline(pen, 11.5, 1.5, 11.5, 14.5);
                Polyline(pen, 8.5, 1.5, 14, 1.5);
                break;
            case PdfAnnotationIcon.Insert:
                Polygon(brush, pen, 8, 2, 14, 13.5, 2, 13.5);
                break;
            case PdfAnnotationIcon.Paperclip:
                using (var clip = canvas.GetGraphicsPath())
                {
                    var clipPen = canvas.GetPen(PaintColor.FromArgb(255, 90, 90, 100));
                    clipPen.Width = Math.Max(0.5, 1.4 * scale);
                    clipPen.LineCap = LineCap.Round;
                    clipPen.LineJoin = LineJoin.Round;
                    clip.Start(X(11), Y(5));
                    clip.LineTo(X(11), Y(11.5));
                    clip.AddBezierTo(X(11), Y(15), X(5), Y(15), X(5), Y(11.5));
                    clip.LineTo(X(5), Y(4));
                    clip.AddBezierTo(X(5), Y(0.8), X(9), Y(0.8), X(9), Y(4));
                    clip.LineTo(X(9), Y(10.5));
                    canvas.DrawPath(clipPen, clip);
                }
                break;
            case PdfAnnotationIcon.PushPin:
                Polygon(brush, pen, 5, 1.5, 11, 1.5, 11, 3, 9.5, 3, 9.5, 7, 12, 9, 12, 10, 8.8, 10, 8, 15, 7.2, 10, 4, 10, 4, 9, 6.5, 7, 6.5, 3, 5, 3);
                break;
            case PdfAnnotationIcon.Graph:
                Polygon(brush, pen, 2, 14, 2, 8, 5, 8, 5, 14);
                Polygon(brush, pen, 6.5, 14, 6.5, 3, 9.5, 3, 9.5, 14);
                Polygon(brush, pen, 11, 14, 11, 6, 14, 6, 14, 14);
                break;
            case PdfAnnotationIcon.Tag:
                Polygon(brush, pen, 1.5, 2, 8.5, 2, 14.5, 8, 8, 14.5, 1.5, 8);
                Circle(inkBrush, null, 5, 5.5, 1.1);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(icon), icon, null);
        }
    }
}
