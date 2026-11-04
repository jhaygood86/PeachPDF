using PeachBrackets.Annotations;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.PdfFeatures
{
    /// <summary>
    /// Turns the annotations a laid-out document collected (<see cref="PageAnnotation"/>, from the <c>-peachpdf-pdf-annotation-*</c>
    /// properties) into the PDF writer's annotations. It only translates: the writer's <see cref="PdfAnnotationBuilder"/> makes the objects
    /// and <see cref="PdfAnnotationAppearance"/> draws the appearance stream every annotation carries (PDF/A requires one).
    /// </summary>
    internal sealed class AnnotationBuilder(PdfAnnotationBuilder builder, PdfRenderContext renderContext, IReadOnlyCollection<string> embeddedFileNames)
    {
        private static readonly PaintColor Yellow = PaintColor.FromArgb(255, 255, 214, 64);
        private static readonly PaintColor HighlightYellow = PaintColor.FromArgb(255, 255, 235, 0);
        private static readonly PaintColor Green = PaintColor.FromArgb(255, 0, 150, 0);
        private static readonly PaintColor Red = PaintColor.FromArgb(255, 200, 0, 0);

        /// <summary>Places one collected annotation on its page. An attached-file annotation naming a file the document does not embed is skipped.</summary>
        public void Add(PdfComposerPage page, PageAnnotation annotation)
        {
            var bounds = annotation.Bounds;
            var size = new Size(Math.Max(0, bounds.Width), Math.Max(0, bounds.Height));

            // /T is the name of whoever made the annotation; the author's -title is its subject.
            void Put(
                string subtype, Action<Canvas>? paint, Rect? rect = null, string? iconName = null, PaintColor? color = null, string? embeddedFileName = null,
                IReadOnlyList<PaintPoint>? quads = null, IReadOnlyList<PaintPoint>? lineEnds = null, IReadOnlyList<IReadOnlyList<PaintPoint>>? strokes = null)
            {
                builder.AddAnnotation(page, new PdfAnnotationDefinition
                {
                    Subtype = subtype,
                    Bounds = rect ?? bounds,
                    ContentSize = rect is { } r ? new Size(r.Width, r.Height) : size,
                    PaintAppearance = paint,
                    Contents = annotation.Contents,
                    Title = annotation.Author,
                    Subject = annotation.Title,
                    Color = color ?? annotation.Color,
                    CreatedAt = annotation.CreatedAt,
                    ModifiedAt = annotation.ModifiedAt,
                    PopupBounds = annotation.PopupBounds,
                    IconName = iconName,
                    EmbeddedFileName = embeddedFileName,
                    QuadPoints = quads ?? [],
                    LineEnds = lineEnds ?? [],
                    InkStrokes = strokes ?? [],
                });
            }

            switch (annotation.Kind)
            {
                case PageAnnotationKind.Note:
                {
                    var icon = ResolveIcon(annotation.Icon, fileAttachment: false);
                    Put("Text", PdfAnnotationAppearance.Icon(icon, size, annotation.Color), null, PdfAnnotationAppearance.IconName(icon), annotation.Color ?? Yellow);
                    break;
                }

                case PageAnnotationKind.FileAttachment:
                {
                    if (annotation.AttachmentName is not { } name || !embeddedFileNames.Contains(name))
                        return;

                    var icon = ResolveIcon(annotation.Icon, fileAttachment: true);
                    Put("FileAttachment", PdfAnnotationAppearance.Icon(icon, size, annotation.Color), null, PdfAnnotationAppearance.IconName(icon), annotation.Color ?? Yellow, name);
                    break;
                }

                case PageAnnotationKind.Highlight:
                case PageAnnotationKind.Underline:
                case PageAnnotationKind.Squiggly:
                case PageAnnotationKind.StrikeOut:
                {
                    var (subtype, fallback) = annotation.Kind switch
                    {
                        PageAnnotationKind.Highlight => ("Highlight", HighlightYellow),
                        PageAnnotationKind.Underline => ("Underline", Green),
                        PageAnnotationKind.Squiggly => ("Squiggly", Green),
                        _ => ("StrikeOut", Red),
                    };
                    var color = annotation.Color ?? fallback;
                    Put(subtype, PdfAnnotationAppearance.Markup(subtype, annotation.Quads, bounds, color), null, null, color, null, annotation.Quads);
                    break;
                }

                case PageAnnotationKind.Square:
                case PageAnnotationKind.Circle:
                {
                    var color = annotation.Color ?? Red;
                    var subtype = annotation.Kind == PageAnnotationKind.Square ? "Square" : "Circle";
                    Put(subtype, PdfAnnotationAppearance.Outline(annotation.Kind == PageAnnotationKind.Circle, size, color), null, null, color);
                    break;
                }

                case PageAnnotationKind.Line:
                {
                    var color = annotation.Color ?? Red;
                    var from = new PaintPoint(0, 0);
                    var to = new PaintPoint(size.Width, size.Height);
                    var ends = new[] { new PaintPoint(bounds.X, bounds.Y), new PaintPoint(bounds.Right, bounds.Bottom) };
                    Put("Line", PdfAnnotationAppearance.Line(from, to, color), null, null, color, null, null, ends);
                    break;
                }

                case PageAnnotationKind.Ink:
                {
                    var color = annotation.Color ?? Red;
                    var points = annotation.Strokes.SelectMany(stroke => stroke).ToList();
                    if (points.Count == 0)
                        return;

                    const double pad = 2;
                    var left = points.Min(p => p.X) - pad;
                    var top = points.Min(p => p.Y) - pad;
                    var rect = new Rect(left, top, points.Max(p => p.X) + pad - left, points.Max(p => p.Y) + pad - top);
                    var local = annotation.Strokes.Select(stroke => (IReadOnlyList<PaintPoint>)stroke.Select(p => new PaintPoint(p.X - rect.X, p.Y - rect.Y)).ToArray()).ToArray();
                    Put("Ink", PdfAnnotationAppearance.Ink(local, color), rect, null, color, null, null, null, annotation.Strokes);
                    break;
                }

                case PageAnnotationKind.FreeText:
                case PageAnnotationKind.Stamp:
                {
                    var text = annotation.Contents ?? annotation.Title ?? (annotation.Kind == PageAnnotationKind.Stamp ? "Draft" : string.Empty);
                    var color = annotation.Color ?? (annotation.Kind == PageAnnotationKind.Stamp ? Red : PaintColor.Black);
                    var font = renderContext.GetFont("sans-serif", annotation.Kind == PageAnnotationKind.Stamp ? 12 : 9, PaintFontStyle.Regular);
                    Action<Canvas> paint = font is null
                        ? PdfAnnotationAppearance.Outline(false, size, color)
                        : annotation.Kind == PageAnnotationKind.Stamp
                            ? PdfAnnotationAppearance.Stamp(text, font, color, size)
                            : PdfAnnotationAppearance.FreeText(text, font, color, size);
                    Put(annotation.Kind == PageAnnotationKind.Stamp ? "Stamp" : "FreeText", paint, null, null, color);
                    break;
                }
            }
        }

        private static PdfAnnotationIcon ResolveIcon(string? keyword, bool fileAttachment)
        {
            var icon = keyword switch
            {
                "note" => PdfAnnotationIcon.Note,
                "comment" => PdfAnnotationIcon.Comment,
                "key" => PdfAnnotationIcon.Key,
                "help" => PdfAnnotationIcon.Help,
                "new-paragraph" => PdfAnnotationIcon.NewParagraph,
                "paragraph" => PdfAnnotationIcon.Paragraph,
                "insert" => PdfAnnotationIcon.Insert,
                "paperclip" => PdfAnnotationIcon.Paperclip,
                "push-pin" => PdfAnnotationIcon.PushPin,
                "graph" => PdfAnnotationIcon.Graph,
                "tag" => PdfAnnotationIcon.Tag,
                _ => fileAttachment ? PdfAnnotationIcon.Paperclip : PdfAnnotationIcon.Note,
            };

            // A note's icons and an attached file's are two separate sets in the PDF; one from the other set falls back to the default.
            return PdfAnnotationAppearance.IsFileAttachmentIcon(icon) == fileAttachment ? icon : fileAttachment ? PdfAnnotationIcon.Paperclip : PdfAnnotationIcon.Note;
        }
    }
}
