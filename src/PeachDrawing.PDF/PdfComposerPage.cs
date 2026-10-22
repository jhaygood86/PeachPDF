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
