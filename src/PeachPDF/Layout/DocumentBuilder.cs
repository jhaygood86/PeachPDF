using PeachPDF.Html.Core.Utils;
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

        public void Page(Action<IPageDescriptor> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            PageHandlers.Add(handler);
        }

        /// <summary>
        /// Builds and returns one page descriptor's own root <see cref="Html.Core.Dom.CssBox"/> tree by
        /// running <paramref name="handler"/> against a fresh <see cref="PageDescriptorBuilder"/>.
        /// </summary>
        internal static PageDescriptorBuilder BuildPage(Action<IPageDescriptor> handler, CssPropertyFactory properties)
        {
            var descriptor = new PageDescriptorBuilder(properties);
            handler(descriptor);
            return descriptor;
        }
    }
}
