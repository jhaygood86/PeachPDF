using PeachBrackets.Building;
using PeachDrawing.PDF;
using System;
using System.Collections.Generic;
using System.IO;

namespace PeachPDF.Layout
{
    /// <summary>
    /// Collects a declarative document's page descriptors (<see cref="PdfGenerator.CreateDocument"/>/
    /// <see cref="PdfGenerator.AddPages"/>'s own <c>Action&lt;IDocumentBuilder&gt;</c> callback runs
    /// against one of these). The callback itself is synchronous, so each <see cref="Page"/> call only
    /// records its handler here - <see cref="PdfGenerator"/> builds and renders each page descriptor's
    /// own box tree afterward, since that part is genuinely asynchronous (image loading, layout).
    /// </summary>
    internal sealed class DocumentBuilder : IDocumentBuilder
    {
        /// <summary>One part of the document, in the order it is written: a page section or the pages of an existing PDF.</summary>
        internal sealed class Section
        {
            public Action<IPageDescriptor>? Page { get; init; }

            public PdfOperation? Pdf { get; init; }

            public PdfCopyMode? Mode { get; init; }
        }

        /// <summary>The parts of the document in order: the <see cref="Page"/> calls, with the PDFs added around and between them.</summary>
        public List<Section> Sections { get; } = [];

        /// <summary>Whether the generated pages count the pages of the added PDFs - see <see cref="UseContinuousPageNumbers"/>.</summary>
        public bool ContinuousPageNumbers { get; private set; }

        private int _prepended;

        /// <summary>The last-set document-level stylesheet, or null - see <see cref="Stylesheet"/>.</summary>
        public PeachPdfCssContent? DocumentStylesheet { get; private set; }

        public void Page(Action<IPageDescriptor> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            Sections.Add(new Section { Page = handler });
        }

        public void Append(byte[] pdf, PdfCopyMode mode = PdfCopyMode.Redraw, string? password = null)
        {
            ArgumentNullException.ThrowIfNull(pdf);
            Add(PdfOperation.LoadBytes(pdf, password), mode, prepend: false);
        }

        public void Append(Stream pdf, PdfCopyMode mode = PdfCopyMode.Redraw, string? password = null)
        {
            ArgumentNullException.ThrowIfNull(pdf);
            Add(PdfOperation.LoadStream(pdf, password), mode, prepend: false);
        }

        public void Append(PdfOperation operation, PdfCopyMode? mode = null)
        {
            ArgumentNullException.ThrowIfNull(operation);
            Add(operation, mode, prepend: false);
        }

        public void Prepend(byte[] pdf, PdfCopyMode mode = PdfCopyMode.Redraw, string? password = null)
        {
            ArgumentNullException.ThrowIfNull(pdf);
            Add(PdfOperation.LoadBytes(pdf, password), mode, prepend: true);
        }

        public void Prepend(Stream pdf, PdfCopyMode mode = PdfCopyMode.Redraw, string? password = null)
        {
            ArgumentNullException.ThrowIfNull(pdf);
            Add(PdfOperation.LoadStream(pdf, password), mode, prepend: true);
        }

        public void Prepend(PdfOperation operation, PdfCopyMode? mode = null)
        {
            ArgumentNullException.ThrowIfNull(operation);
            Add(operation, mode, prepend: true);
        }

        public void UseContinuousPageNumbers() => ContinuousPageNumbers = true;

        private void Add(PdfOperation operation, PdfCopyMode? mode, bool prepend)
        {
            var section = new Section { Pdf = operation, Mode = mode };
            if (prepend)
                Sections.Insert(_prepended++, section);
            else
                Sections.Add(section);
        }

        /// <summary>The PDF files to stamp over or under the document, in the order added.</summary>
        public List<FileLayerBuilder> FileLayers { get; } = [];

        public void OverlayFile(Action<IFileLayerDescriptor> handler) => AddLayer(handler, isUnderlay: false);

        public void UnderlayFile(Action<IFileLayerDescriptor> handler) => AddLayer(handler, isUnderlay: true);

        private void AddLayer(Action<IFileLayerDescriptor> handler, bool isUnderlay)
        {
            ArgumentNullException.ThrowIfNull(handler);
            var layer = new FileLayerBuilder(isUnderlay);
            handler(layer);
            FileLayers.Add(layer);
        }

        public void Stylesheet(PeachPdfCssContent content)
        {
            ArgumentNullException.ThrowIfNull(content);
            DocumentStylesheet = content;
        }

        /// <summary>
        /// Builds and returns one page descriptor's own root <c>DocumentNode</c> tree by
        /// running <paramref name="handler"/> against a fresh <see cref="PageDescriptorBuilder"/>.
        /// </summary>
        internal static PageDescriptorBuilder BuildPage(Action<IPageDescriptor> handler, DocumentBuildContext context)
        {
            var descriptor = new PageDescriptorBuilder(context);
            handler(descriptor);
            return descriptor;
        }
    }
}
