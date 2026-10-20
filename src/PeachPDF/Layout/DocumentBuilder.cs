using PeachBrackets.Building;
using System;
using System.Collections.Generic;

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
        public List<Action<IPageDescriptor>> PageHandlers { get; } = [];

        /// <summary>The last-set document-level stylesheet, or null - see <see cref="Stylesheet"/>.</summary>
        public PeachPdfCssContent? DocumentStylesheet { get; private set; }

        public void Page(Action<IPageDescriptor> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            PageHandlers.Add(handler);
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
