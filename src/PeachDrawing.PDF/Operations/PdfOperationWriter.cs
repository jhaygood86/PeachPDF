using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Advanced;
using PeachDrawing.PDF.Import;

namespace PeachDrawing.PDF
{
    /// <summary>Draws the pages of a <see cref="PdfOperation"/> into a new file, with the navigation of the files they came from.</summary>
    internal static class PdfOperationWriter
    {
        public static void Write(PdfOperation operation, Stream stream)
        {
            var pages = operation.OutputPages;
            var sources = pages.Select(p => p.Source).Distinct().ToList();

            var options = new PdfComposerOptions { Encryption = operation.EncryptionOptions };
            ApplyDocumentInformation(options, sources);
            AddAttachments(options, sources, operation.Attachments);
            if (operation.MetadataElements.Count > 0)
            {
                options.CreationDate ??= DateTimeOffset.UtcNow;
                foreach (var element in operation.MetadataElements)
                    options.CustomXmpProperties.Add(new System.Xml.Linq.XElement(element));
            }

            var context = new PdfRenderContext();
            using var composer = new PdfComposer(context, options);
            var warnings = new SortedSet<string>(StringComparer.Ordinal);

            // Where each page of each file was put; the first place wins, so a link to it has one place to lead to.
            var drawnAt = new Dictionary<(PdfPageSource, int), int>();
            for (var i = 0; i < pages.Count; i++)
                drawnAt.TryAdd((pages[i].Source, pages[i].SourcePage), i);

            var copy = operation.CopyMode == PdfCopyMode.ObjectCopy;
            if (copy)
            {
                CopyPages(composer, context, pages, sources, warnings);
            }
            else
            {
                foreach (var entry in pages)
                {
                    var (width, height) = entry.Source.GetPageSize(entry.SourcePage);
                    var page = composer.AddPage(width, height);

                    foreach (var layer in entry.Layers.Where(l => l.Underlay))
                        Draw(layer.Source, layer.SourcePage, page, warnings);
                    Draw(entry.Source, entry.SourcePage, page, warnings);
                    foreach (var layer in entry.Layers.Where(l => !l.Underlay))
                        Draw(layer.Source, layer.SourcePage, page, warnings);
                }
            }

            // Copied pages carry their own links, form fields and annotations; drawn ones have them added here.
            CarryNavigation(composer, context, pages, sources, drawnAt, carryPageFeatures: !copy);

            foreach (var warning in warnings)
                operation.WarningsList.Add(warning);

            composer.Save(stream);
        }

        private static void Draw(PdfPageSource source, int sourcePage, PdfComposerPage page, SortedSet<string> warnings)
        {
            foreach (var warning in source.Paint(sourcePage, page.Canvas, PdfAnnotationPaint.None))
                warnings.Add(warning);
        }

        /// <summary>The title, author and the rest of the document information, each from the first file that gives it.</summary>
        private static void ApplyDocumentInformation(PdfComposerOptions options, List<PdfPageSource> sources)
        {
            foreach (var info in sources.Select(s => s.GetMetadata()))
            {
                if (string.IsNullOrEmpty(options.Title)) options.Title = info.Title;
                if (string.IsNullOrEmpty(options.Author)) options.Author = info.Author;
                if (string.IsNullOrEmpty(options.Subject)) options.Subject = info.Subject;
                if (string.IsNullOrEmpty(options.Keywords)) options.Keywords = info.Keywords;
                if (string.IsNullOrEmpty(options.Creator)) options.Creator = info.Creator;
                if (string.IsNullOrEmpty(options.Language)) options.Language = info.Language;
            }
        }

        private static void AddAttachments(PdfComposerOptions options, List<PdfPageSource> sources, IReadOnlyList<PdfFileAttachment> added)
        {
            foreach (var file in sources.SelectMany(s => s.GetAttachments()))
            {
                if (options.EmbeddedFiles.All(f => !string.Equals(f.FileName, file.FileName, StringComparison.Ordinal)))
                    options.EmbeddedFiles.Add(file);
            }

            foreach (var attachment in added)
            {
                var data = attachment.Data ?? File.ReadAllBytes(attachment.FilePath!);
                var key = !string.IsNullOrEmpty(attachment.Key) ? attachment.Key : Path.GetFileName(attachment.FilePath!);
                var existing = options.EmbeddedFiles.FirstOrDefault(f => string.Equals(f.FileName, key, StringComparison.Ordinal));
                if (existing is not null)
                {
                    if (!attachment.Replace)
                        throw new InvalidOperationException($"The PDF already has an attachment '{key}'. Set Replace to true to replace it.");
                    options.EmbeddedFiles.Remove(existing);
                }

                options.EmbeddedFiles.Add(new PdfEmbeddedFile
                {
                    FileName = attachment.AttachmentName is { Length: > 0 } name ? name : key,
                    Data = data,
                    MimeType = attachment.MimeType ?? "application/octet-stream",
                    Description = attachment.Description,
                    Relationship = attachment.Relationship,
                    ModificationDate = attachment.ModificationDate
                        ?? (attachment.FilePath is null ? null : new DateTimeOffset(File.GetLastWriteTimeUtc(attachment.FilePath), TimeSpan.Zero)),
                });
            }
        }

        /// <summary>
        /// Puts the objects of each page into the new file as they are (see <see cref="PdfObjectCopier"/>), then draws the layers a page has over
        /// and under what was copied.
        /// </summary>
        private static void CopyPages(
            PdfComposer composer,
            PdfRenderContext context,
            IReadOnlyList<PdfOperation.OutputPage> pages,
            List<PdfPageSource> sources,
            SortedSet<string> warnings)
        {
            var fieldNames = new HashSet<string>(StringComparer.Ordinal);
            var copiers = sources.ToDictionary(s => s, s => new PdfObjectCopier(s.Document, composer.Document, fieldNames));

            // Every page is made before any is filled, so a link on an early page can lead to a later one.
            var made = new List<PdfComposerPage>(pages.Count);
            foreach (var entry in pages)
            {
                var (width, height) = entry.Source.GetPageSize(entry.SourcePage);
                var page = composer.AddPage(width, height);
                made.Add(page);
                copiers[entry.Source].RegisterPage(entry.Source.Document.Pages[entry.SourcePage], page.Page);
            }

            for (var i = 0; i < pages.Count; i++)
            {
                var entry = pages[i];
                var page = made[i];
                var sourcePage = entry.Source.Document.Pages[entry.SourcePage];
                var layered = entry.Layers.Count > 0;
                if (layered)
                    EnsureLayerCanBeDrawn(entry.Source.Document, sourcePage);

                // The page's content goes in first, so what the layers add to the resources takes names the copied content does not use. The page's
                // own canvas is the first content stream: underlays drawn on it go beneath what is copied in after it, and the overlays are drawn last,
                // once that canvas is closed, into a content stream added after everything.
                var streams = copiers[entry.Source].FillPage(page.Page, sourcePage);
                var contents = page.Page.Contents.Elements;
                var at = 1;
                if (layered)
                    contents.Insert(at++, ContentStream(composer, "q\n"));
                foreach (var stream in streams)
                    contents.Insert(at++, stream);
                if (layered)
                    contents.Insert(at, ContentStream(composer, "\nQ"));

                foreach (var layer in entry.Layers.Where(l => l.Underlay))
                    DrawOn(layer.Source, layer.SourcePage, page.Canvas, warnings);
                page.Finish();

                var overlays = entry.Layers.Where(l => !l.Underlay).ToList();
                if (overlays.Count > 0)
                    page.DrawAppended(context, canvas => overlays.ForEach(layer => DrawOn(layer.Source, layer.SourcePage, canvas, warnings)));
            }
            foreach (var copier in copiers.Values)
                copier.FinishForm();
        }

        private static PdfItem ContentStream(PdfComposer composer, string text)
        {
            var content = new PdfContent(composer.Document);
            composer.Document.Internals.AddObject(content);
            content.CreateStream(System.Text.Encoding.ASCII.GetBytes(text));
            return content.RequiredReference;
        }

        /// <summary>A layer is drawn in the page's own top-left-origin space, which is the space of the copied content only when nothing moves it.</summary>
        private static void EnsureLayerCanBeDrawn(PdfSourceDocument source, PdfDictionary page)
        {
            var rotated = source.InheritedAttribute(page, "/Rotate") is { } rotate && source.IntOf(rotate) % 360 != 0;
            var cropped = source.InheritedAttribute(page, "/CropBox") is not null;
            var offset = source.InheritedAttribute(page, "/MediaBox") is PdfArray { Elements.Count: >= 2 } box
                && (source.NumberOf(box.Elements[0]) != 0 || source.NumberOf(box.Elements[1]) != 0);
            if (rotated || cropped || offset)
            {
                throw new NotSupportedException(
                    "A layer cannot be drawn on a page copied as objects when the page is rotated, has a crop box, or has a media box that does not start " +
                    "at the origin. Use PdfCopyMode.Redraw for this file.");
            }
        }

        private static void DrawOn(PdfPageSource source, int sourcePage, PdfCanvas canvas, SortedSet<string> warnings)
        {
            foreach (var warning in source.Paint(sourcePage, canvas, PdfAnnotationPaint.None))
                warnings.Add(warning);
        }

        /// <summary>
        /// Carries across what the drawn pages had that is not drawing: links (now leading to the pages their targets were drawn on), form
        /// fields, annotations, named destinations and the outline. Whatever leads to a page that was not taken is left out.
        /// </summary>
        private static void CarryNavigation(
            PdfComposer composer,
            PdfRenderContext context,
            IReadOnlyList<PdfOperation.OutputPage> pages,
            List<PdfPageSource> sources,
            Dictionary<(PdfPageSource, int), int> drawnAt,
            bool carryPageFeatures)
        {
            var annotations = composer.CreateAnnotationBuilder(context);
            var forms = composer.CreateFormBuilder(context, "PeachPDFMergedField");
            var fieldNames = new HashSet<string>(StringComparer.Ordinal);
            var destinationNames = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 0; carryPageFeatures && i < pages.Count; i++)
            {
                var entry = pages[i];
                var target = composer.Pages[i];
                var sourceIndex = sources.IndexOf(entry.Source);

                // A page drawn more than once carries its links every time, but its fields again only under names of their own.
                foreach (var link in entry.Source.GetLinks(entry.SourcePage))
                {
                    if (link.Uri is not null)
                    {
                        target.AddLink(link.Bounds, link.Uri);
                    }
                    else if (link.Destination is { } to && drawnAt.TryGetValue((entry.Source, to.PageIndex), out var toPage))
                    {
                        var name = string.Create(CultureInfo.InvariantCulture, $"PeachPDFMerged{sourceIndex}-{to.PageIndex}-{to.TopPoints:0.###}");
                        if (destinationNames.Add(name))
                            composer.AddNamedDestination(name, toPage, to.TopPoints);
                        target.AddDestinationLink(link.Bounds, name);
                    }
                }

                var renamed = new Dictionary<string, string>(StringComparer.Ordinal);
                string Unique(string name)
                {
                    if (renamed.TryGetValue(name, out var existing))
                        return existing;

                    var unique = name;
                    for (var n = 2; !fieldNames.Add(unique); n++)
                        unique = name + "_" + n;
                    return renamed[name] = unique;
                }

                foreach (var field in entry.Source.GetFormFields(entry.SourcePage, target.Canvas.PixelsPerPoint, Unique))
                    forms.AddField(target, field);
                foreach (var annotation in entry.Source.GetAnnotations(entry.SourcePage, target.Canvas.PixelsPerPoint))
                    annotations.AddAnnotation(target, annotation);
            }

            for (var s = 0; s < sources.Count; s++)
            {
                var source = sources[s];
                foreach (var (name, to) in source.GetNamedDestinations())
                {
                    if (!drawnAt.TryGetValue((source, to.PageIndex), out var toPage))
                        continue;

                    var unique = name;
                    for (var n = 2; !destinationNames.Add(unique); n++)
                        unique = name + "_" + n;
                    composer.AddNamedDestination(unique, toPage, to.TopPoints);
                }

                var outline = CarryOutline(source.GetOutline(), page => drawnAt.TryGetValue((source, page), out var toPage) ? toPage : -1);
                if (outline.Count > 0)
                    composer.AddBookmarks(outline);
            }
        }

        /// <summary>
        /// The outline entries whose destination was drawn (or that open an address), re-pointed at the new pages, with the entries
        /// above them kept as plain headings so the nesting of the rest holds.
        /// </summary>
        private static List<PdfBookmark> CarryOutline(IReadOnlyList<PdfBookmark> entries, Func<int, int> targetPageOf)
        {
            var keep = new bool[entries.Count];
            for (var i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                if (entry.Uri is null && !(entry.PageIndex is { } page && targetPageOf(page) >= 0))
                    continue;

                keep[i] = true;
                var level = entry.Level;
                for (var j = i - 1; j >= 0 && level > 1; j--)
                {
                    if (entries[j].Level < level)
                    {
                        keep[j] = true;
                        level = entries[j].Level;
                    }
                }
            }

            var carried = new List<PdfBookmark>();
            for (var i = 0; i < entries.Count; i++)
            {
                if (!keep[i])
                    continue;

                var entry = entries[i];
                var copy = new PdfBookmark(entry.Level, entry.Title, entry.IsOpen) { Uri = entry.Uri };
                if (entry.PageIndex is { } page && targetPageOf(page) is var toPage and >= 0)
                {
                    copy.PageIndex = toPage;
                    copy.TopPoints = entry.TopPoints;
                }

                carried.Add(copy);
            }

            return carried;
        }
    }
}
