using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using PeachDrawing.PDF;

namespace PeachPDF.Layout
{
    /// <summary>The file layers of a document being built, with the document's page count that their target ranges are read against.</summary>
    internal sealed class FileLayerPlan
    {
        private readonly List<(FileLayer Layer, IReadOnlyList<int> Targets)> _layers = [];

        // Where each page of each layer's file was drawn: the layer, the zero-based page of its file, the zero-based page of the document.
        // The first target of a page that is drawn on several wins, so a link to it has one place to lead to.
        private readonly List<(int Layer, int SourcePage, int TargetPage)> _stamps = [];

        public FileLayerPlan(IEnumerable<FileLayer> layers, int totalPages)
        {
            foreach (var layer in layers)
                _layers.Add((layer, layer.ResolveTargets(totalPages)));
        }

        /// <summary>Draws every layer of the requested kind that targets <paramref name="pageNumber"/> (one-based) onto the page.</summary>
        public void Paint(PdfComposerPage page, int pageNumber, bool underlay, PdfTagRecorder? tags = null)
        {
            foreach (var (layer, targets) in _layers)
            {
                if (layer.IsUnderlay != underlay)
                    continue;

                var ordinal = IndexOf(targets, pageNumber);
                if (ordinal < 0 || layer.SourcePageFor(ordinal) is not { } sourcePage)
                    continue;

                // The canvas works in layout units; the imported page is in points.
                var canvas = page.Canvas;
                var scaled = canvas.PixelsPerPoint != 1.0;
                if (scaled)
                    canvas.PushTransform(Matrix3x2.CreateScale((float)canvas.PixelsPerPoint));

                _stamps.Add((_layers.FindIndex(l => ReferenceEquals(l.Layer, layer)), sourcePage, page.Index));

                try
                {
                    layer.Source.Paint(sourcePage, canvas, PdfAnnotationPaint.ExceptFormFields, tags);
                }
                finally
                {
                    if (scaled)
                        canvas.PopTransform();
                }
            }
        }

        /// <summary>
        /// Fills the document information a render left empty from the files that were drawn, and attaches their files. A field the
        /// document already has is kept: a stamp or letterhead does not retitle the document it is laid on.
        /// </summary>
        public void ApplyDocumentInformation(PdfComposerOptions options, bool canAttachFiles)
        {
            foreach (var layer in _layers.Select(l => l.Layer).Distinct())
            {
                var info = layer.Source.GetMetadata();
                if (string.IsNullOrEmpty(options.Title)) options.Title = info.Title;
                if (string.IsNullOrEmpty(options.Author)) options.Author = info.Author;
                if (string.IsNullOrEmpty(options.Subject)) options.Subject = info.Subject;
                if (string.IsNullOrEmpty(options.Keywords)) options.Keywords = info.Keywords;
                if (string.IsNullOrEmpty(options.Language)) options.Language = info.Language;

                // A document building to PDF/X must name its output condition and supply the profile; when it has not, the file's output intent is
                // used. (PDF/A brings its own sRGB intent, and a document with no level writes none.) The file's own PDF/A or PDF/X claim is never
                // adopted: the document's content is not the file's, and the document says what it conforms to itself.
                if (options.XConformance != PeachDrawing.PDF.PdfXConformance.None && options.ColorOptions?.OutputIntentProfile is not { Length: > 0 }
                    && layer.Source.GetOutputIntent() is { } intent)
                {
                    options.ColorOptions ??= new PeachDrawing.PDF.ColorOptions();
                    options.ColorOptions.OutputIntentProfile = intent.Profile;
                    options.ColorOptions.OutputIntentIdentifier = intent.Identifier ?? intent.Info ?? "Imported";
                }

                if (!canAttachFiles)
                    continue;

                foreach (var file in layer.Source.GetAttachments())
                {
                    if (options.EmbeddedFiles.All(f => !string.Equals(f.FileName, file.FileName, StringComparison.Ordinal)))
                        options.EmbeddedFiles.Add(file);
                }
            }
        }

        /// <summary>
        /// Carries the navigation of the drawn pages across, once every page of the document exists: the links, which now lead to the
        /// pages their targets were drawn on, the named destinations, and the outline. Whatever leads to a page that was not drawn is left out.
        /// </summary>
        public void Finish(PdfComposer composer, PdfRenderContext renderContext)
        {
            var destinations = new Dictionary<(int Layer, int SourcePage), int>();
            foreach (var (layer, sourcePage, targetPage) in _stamps)
                destinations.TryAdd((layer, sourcePage), targetPage);

            // Each drawn page carries its links once, however many times it was drawn.
            var linked = new HashSet<(int, int, int)>();
            foreach (var (layerIndex, sourcePage, targetPage) in _stamps)
            {
                if (!linked.Add((layerIndex, sourcePage, targetPage)) || targetPage >= composer.Pages.Count)
                    continue;

                var target = composer.Pages[targetPage];
                foreach (var link in _layers[layerIndex].Layer.Source.GetLinks(sourcePage))
                {
                    if (link.Uri is not null)
                    {
                        target.AddLink(link.Bounds, link.Uri);
                    }
                    else if (link.Destination is { } to && destinations.TryGetValue((layerIndex, to.PageIndex), out var toPage))
                    {
                        var name = DestinationName(layerIndex, to.PageIndex, to.TopPoints);
                        composer.AddNamedDestination(name, toPage, to.TopPoints);
                        target.AddDestinationLink(link.Bounds, name);
                    }
                }
            }

            // The form fields of each drawn page. A page drawn more than once gets its fields again under names of their own (the first keeps
            // the file's), while the buttons of one radio group, which share a name, stay together.
            var forms = composer.CreateFormBuilder(renderContext, "PeachPDFFileField");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (layerIndex, sourcePage, targetPage) in _stamps.Distinct())
            {
                if (targetPage >= composer.Pages.Count)
                    continue;

                var target = composer.Pages[targetPage];
                var renamed = new Dictionary<string, string>(StringComparer.Ordinal);
                string Unique(string name)
                {
                    if (renamed.TryGetValue(name, out var existing))
                        return existing;

                    var unique = name;
                    for (var n = 2; !names.Add(unique); n++)
                        unique = name + "_" + n;
                    return renamed[name] = unique;
                }

                foreach (var field in _layers[layerIndex].Layer.Source.GetFormFields(sourcePage, target.Canvas.PixelsPerPoint, Unique))
                    forms.AddField(target, field);
            }

            foreach (var layerIndex in destinations.Keys.Select(k => k.Layer).Distinct())
            {
                var source = _layers[layerIndex].Layer.Source;
                foreach (var (name, to) in source.GetNamedDestinations())
                {
                    if (destinations.TryGetValue((layerIndex, to.PageIndex), out var toPage))
                        composer.AddNamedDestination(name, toPage, to.TopPoints);
                }

                var outline = CarryOutline(source.GetOutline(), page => destinations.TryGetValue((layerIndex, page), out var toPage) ? toPage : -1);
                if (outline.Count > 0)
                    composer.AddBookmarks(outline);
            }
        }

        private static string DestinationName(int layer, int sourcePage, double top) =>
            string.Create(System.Globalization.CultureInfo.InvariantCulture, $"PeachPDFFile{layer}-{sourcePage}-{top:0.###}");

        /// <summary>
        /// The outline entries whose destination was drawn (or that open an address), re-pointed at the document's pages, with the entries
        /// above them kept as plain headings so the nesting of the rest holds.
        /// </summary>
        private static List<PdfBookmark> CarryOutline(IReadOnlyList<PdfBookmark> entries, Func<int, int> targetPageOf)
        {
            var keep = new bool[entries.Count];
            for (var i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                if (entry.Uri is not null || (entry.PageIndex is { } page && targetPageOf(page) >= 0))
                {
                    keep[i] = true;

                    // Every entry above this one that it nests under is kept too.
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

        private static int IndexOf(IReadOnlyList<int> targets, int pageNumber)
        {
            for (var i = 0; i < targets.Count; i++)
            {
                if (targets[i] == pageNumber)
                    return i;
            }

            return -1;
        }
    }
}
