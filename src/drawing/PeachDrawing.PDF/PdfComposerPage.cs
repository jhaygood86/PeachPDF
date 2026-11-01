#nullable enable

using PeachDrawing.Core;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Annotations;
using PeachDrawing.PDF.Engine.Drawing;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachDrawing.PDF
{
    /// <summary>
    /// One page of a <see cref="PdfComposer"/>: the canvas to draw it with, and the links that make parts of it clickable.
    /// </summary>
    /// <remarks>
    /// Positions given to a page are in points, measured from its top-left corner with y running down, the way the page is drawn; the
    /// page converts to PDF's own convention (y up from the bottom edge) itself.
    /// </remarks>
    public sealed class PdfComposerPage
    {
        private readonly PdfComposer _composer;
        private readonly PdfPage _page;
        private readonly PdfGraphics _graphics;
        private readonly List<PdfCanvas> _canvases = [];
        private readonly HashSet<string> _customStringDictionaries = new(StringComparer.Ordinal);
        private static readonly HashSet<string> ReservedPageKeys =
        [
            PdfPage.Keys.Type,
            PdfPage.Keys.Parent,
            PdfPage.Keys.LastModified,
            PdfPage.Keys.Resources,
            PdfPage.Keys.MediaBox,
            PdfPage.Keys.CropBox,
            PdfPage.Keys.BleedBox,
            PdfPage.Keys.TrimBox,
            PdfPage.Keys.ArtBox,
            PdfPage.Keys.BoxColorInfo,
            PdfPage.Keys.Contents,
            PdfPage.Keys.Rotate,
            PdfPage.Keys.Group,
            PdfPage.Keys.Thumb,
            PdfPage.Keys.B,
            PdfPage.Keys.Dur,
            PdfPage.Keys.Trans,
            PdfPage.Keys.Annots,
            PdfPage.Keys.AA,
            PdfPage.Keys.Metadata,
            PdfPage.Keys.PieceInfo,
            PdfPage.Keys.StructParents,
            PdfPage.Keys.ID,
            PdfPage.Keys.PZ,
            PdfPage.Keys.SeparationInfo,
            PdfPage.Keys.Tabs,
            PdfPage.Keys.TemplateInstantiated,
            PdfPage.Keys.PresSteps,
            PdfPage.Keys.UserUnit,
            PdfPage.Keys.VP
        ];
        private bool _finished;

        internal PdfComposerPage(PdfComposer composer, PdfPage page, int index, PdfRenderContext renderContext)
        {
            _composer = composer;
            _page = page;
            Index = index;
            _graphics = PdfGraphics.FromPdfPage(page);
            Canvas = CreateCanvas(renderContext);
        }

        /// <summary>The zero-based position of the page in the document.</summary>
        public int Index { get; }

        /// <summary>The page's width in points.</summary>
        public double WidthPoints => _page.Width;

        /// <summary>The page's height in points.</summary>
        public double HeightPoints => _page.Height;

        /// <summary>The canvas made with the composer's <see cref="PdfRenderContext"/> when the page was added.</summary>
        public PdfCanvas Canvas { get; }

        /// <summary>The engine's page, for the rest of the package.</summary>
        internal PdfPage Page => _page;

        /// <summary>
        /// Draws into a content stream added after the ones the page has, for a page that is finished and whose content was copied in from a file:
        /// what is drawn lands over that content, where what the page's own canvas drew lands under it. The canvas is closed when this returns.
        /// </summary>
        internal void DrawAppended(PdfRenderContext renderContext, Action<PdfCanvas> draw)
        {
            using var graphics = PdfGraphics.FromPdfPage(_page);
            using var canvas = new PdfCanvas(renderContext, graphics, renderContext.PixelsPerPoint) { Page = this };
            draw(canvas);
        }

        /// <summary>Sets a custom string entry in a dictionary associated with the page.</summary>
        /// <remarks>
        /// The dictionary is created as an indirect object on first use. Another call with the same
        /// <paramref name="dictionaryKey"/> adds an entry to that dictionary; setting the same <paramref name="key"/>
        /// again replaces its value.
        /// </remarks>
        /// <param name="dictionaryKey">the PDF name of the dictionary entry in the page, including its leading slash</param>
        /// <param name="key">the PDF name of the string entry in the dictionary, including its leading slash</param>
        /// <param name="value">the string value to store</param>
        public void SetCustomString(string dictionaryKey, string key, string value)
        {
            PdfCustomString.ValidateName(dictionaryKey, nameof(dictionaryKey));
            PdfCustomString.ValidateName(key, nameof(key));
            ArgumentNullException.ThrowIfNull(value);

            if (ReservedPageKeys.Contains(dictionaryKey))
            {
                throw new ArgumentException(
                    $"The page already contains an entry named '{dictionaryKey}'.",
                    nameof(dictionaryKey));
            }

            PdfDictionary dictionary;
            if (_customStringDictionaries.Contains(dictionaryKey))
            {
                dictionary = _page.Elements.GetDictionary(dictionaryKey)!;
            }
            else
            {
                if (_page.Elements.ContainsKey(dictionaryKey))
                {
                    throw new ArgumentException(
                        $"The page already contains an entry named '{dictionaryKey}'.",
                        nameof(dictionaryKey));
                }

                dictionary = new PdfDictionary(_page.Owner);
                _page.Owner.Internals.AddObject(dictionary);
                _page.Elements.SetReference(dictionaryKey, dictionary);
                _customStringDictionaries.Add(dictionaryKey);
            }

            dictionary.Elements.SetString(key, value, PdfStringEncoding.Unicode);
        }

        /// <summary>Names a place on this page (<paramref name="topPoints"/> below its top edge) for a destination link to lead to.</summary>
        internal void AddDestination(string name, double topPoints) => _composer.AddNamedDestination(name, Index, topPoints);

        /// <summary>
        /// Makes another canvas over this page's content, drawing with <paramref name="renderContext"/>. The canvases of one page share its
        /// content but each keeps its own transform and clip bookkeeping, so one made for a piece of work can be disposed when that is done.
        /// </summary>
        /// <param name="renderContext">the context whose fonts and images the canvas draws with</param>
        /// <returns>a canvas that is valid until the page is finished</returns>
        /// <exception cref="InvalidOperationException">the page is finished</exception>
        public PdfCanvas CreateCanvas(PdfRenderContext renderContext)
        {
            ArgumentNullException.ThrowIfNull(renderContext);
            if (_finished)
                throw new InvalidOperationException("The page is finished; its content cannot be drawn on any more.");

            var canvas = new PdfCanvas(renderContext, _graphics, renderContext.PixelsPerPoint) { Page = this };
            _canvases.Add(canvas);
            return canvas;
        }

        /// <summary>
        /// Moves everything drawn on the page from now until the returned scope is disposed by (<paramref name="xPoints"/>,
        /// <paramref name="yPoints"/>) points, whichever canvas draws it. Disposing the scope puts the page back as it was.
        /// </summary>
        /// <param name="xPoints">how far to move right</param>
        /// <param name="yPoints">how far to move down</param>
        /// <returns>a scope that undoes the move when disposed</returns>
        public IDisposable Shift(double xPoints, double yPoints)
        {
            if (_finished)
                throw new InvalidOperationException("The page is finished; its content cannot be drawn on any more.");

            var state = _graphics.Save();
            _graphics.TranslateTransform(xPoints, yPoints);
            return new ShiftScope(_graphics, state);
        }

        /// <summary>
        /// Ends drawing on the page: its content is written and its canvases can no longer be used. Links can still be added. Called for
        /// every page when the composer is saved; call it earlier to release the page's drawing state as soon as it is drawn.
        /// </summary>
        public void Finish()
        {
            if (_finished)
                return;

            _finished = true;
            foreach (var canvas in _canvases)
                canvas.Dispose();

            _canvases.Clear();
            _graphics.Dispose();
        }

        /// <summary>
        /// Makes <paramref name="bounds"/> a link to a web address (or any other URI).
        /// </summary>
        /// <param name="bounds">the clickable area</param>
        /// <param name="uri">where it leads</param>
        /// <param name="quad">
        /// the four corners of the clickable shape (top-left, top-right, bottom-left, bottom-right of the rectangle it started as) when it is
        /// not <paramref name="bounds"/> itself - a link over a rotated element - or <see langword="null"/>. PDF has no key for it before
        /// version 1.6, so it is left out of an older file.
        /// </param>
        /// <returns>the link, to attach to a structure element</returns>
        public PdfLink AddLink(Rect bounds, string uri, IReadOnlyList<PaintPoint>? quad = null)
        {
            ArgumentNullException.ThrowIfNull(uri);

            var annotation = _page.AddWebLink(new PdfRectangle(ToPdf(bounds)), uri);
            return Complete(annotation, quad);
        }

        /// <summary>
        /// Makes <paramref name="bounds"/> a link to a place in the document that was named with <see cref="PdfComposer.AddNamedDestination"/>.
        /// </summary>
        /// <param name="bounds">the clickable area</param>
        /// <param name="destinationName">the name of the destination it leads to</param>
        /// <param name="quad">see <see cref="AddLink"/></param>
        /// <returns>the link, to attach to a structure element</returns>
        public PdfLink AddDestinationLink(Rect bounds, string destinationName, IReadOnlyList<PaintPoint>? quad = null)
        {
            ArgumentException.ThrowIfNullOrEmpty(destinationName);

            var annotation = _page.AddDocumentLink(new PdfRectangle(ToPdf(bounds)), destinationName);
            return Complete(annotation, quad);
        }

        private PdfLink Complete(PdfLinkAnnotation annotation, IReadOnlyList<PaintPoint>? quad)
        {
            if (quad is { Count: > 0 } && _composer.Document.Version >= 16)
                annotation.SetQuadPoints(quad.Select(corner => new PaintPoint(corner.X, HeightPoints - corner.Y)).ToArray());

            return new PdfLink(annotation, this);
        }

        /// <summary>The page's own convention: a rectangle measured from the page's bottom edge.</summary>
        private Rect ToPdf(Rect bounds) => new(bounds.X, HeightPoints - (bounds.Y + bounds.Height), bounds.Width, bounds.Height);

        private sealed class ShiftScope(PdfGraphics graphics, PdfGraphicsStateToken state) : IDisposable
        {
            private bool _disposed;

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                graphics.Restore(state);
            }
        }
    }

    /// <summary>
    /// A link on a page, returned when it is added so it can be tied to the structure element of the content it covers.
    /// </summary>
    public sealed class PdfLink
    {
        internal PdfLink(PdfLinkAnnotation annotation, PdfComposerPage page)
        {
            Annotation = annotation;
            Page = page;
        }

        internal PdfLinkAnnotation Annotation { get; }

        internal PdfComposerPage Page { get; }
    }
}
