using PeachBrackets.Tagging;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachBrackets.Html.Handlers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.PdfFeatures
{
    /// <summary>
    /// Feeds the structure the fragment paint walk reports (<see cref="IStructureTagSink"/>) to the PDF writer's tag recorder, which builds the
    /// tagged-PDF structure tree. It only translates: an element is identified to the recorder by its <see cref="StructureNode"/>, and
    /// everything about how the structure is stored and written is the recorder's.
    /// Only ever constructed when <see cref="PdfGenerateConfig.EnableTaggedPdf"/> is set (or an accessible PDF/A level asks for it).
    /// </summary>
    internal sealed class StructureTagBuilder(PdfTagRecorder recorder) : IStructureTagSink
    {
        /// <summary>The recorder this builder writes to.</summary>
        public PdfTagRecorder Recorder => recorder;

        /// <summary>Called once per page, right after the page is created, before it is painted.</summary>
        public void BeginPage(PdfComposerPage page) => recorder.BeginPage(page);

        public IDisposable OpenGroupingElement(StructureNode node, string structureType) => recorder.OpenGroupingElement(node, structureType);

        public IDisposable OpenPhantomAncestors(IReadOnlyList<PhantomAncestor> ancestors) =>
            recorder.OpenPhantomAncestors(ancestors.Select(a => ((object)a.Node, a.StructureType)).ToList());

        public IDisposable OpenListItemBodyElement(StructureNode node) => recorder.OpenListItemBodyElement(node);

        public IDisposable OpenContentElement(Canvas g, StructureNode node, string structureType, string? altText = null) =>
            recorder.OpenContentElement(g, node, structureType, altText);

        public void AttachMathMlSource(StructureNode node, string mathMlSource) => recorder.AttachMathMlSource(node, mathMlSource);

        public bool IsInMarkedContent => recorder.IsInMarkedContent;

        public IDisposable OpenArtifact(Canvas g) => recorder.OpenArtifact(g);

        /// <summary>
        /// Ties a link to the structure element of the content it covers - called once per link, after painting and <see cref="Finish"/>.
        /// </summary>
        public void LinkAnnotationToStructureElement(StructureNode node, PdfLink link) => recorder.LinkAnnotation(node, link);

        /// <summary>Completes the structure tree after the whole page-render loop.</summary>
        public void Finish() => recorder.Finish();
    }
}
