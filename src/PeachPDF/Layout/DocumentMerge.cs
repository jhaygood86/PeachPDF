using System;
using System.Collections.Generic;

namespace PeachPDF.Layout
{
    /// <summary>How the page numbers of a <see cref="DocumentMerge"/> run (<c>CurrentPageNumber()</c> and <c>TotalPages()</c>).</summary>
    public enum MergePageNumbering
    {
        /// <summary>Each merged document numbers its own pages from 1 and reports its own total: two documents of 2 and 3 pages read 1, 2, 1, 2, 3.</summary>
        Original,

        /// <summary>The documents read as one: the same two documents read 1, 2, 3, 4, 5, and every footer reports a total of 5.</summary>
        Continuous,
    }

    /// <summary>
    /// Several declarative documents written into one PDF, for <see cref="PdfGenerator.CreateMergedDocument"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each part is the callback that builds one document, as <see cref="PdfGenerator.CreateDocument"/>
    /// takes. Numbering the pages of the parts as one run needs every part's page count before any page is drawn, so the parts are laid out
    /// once to count and again to draw, and each callback is called twice; build the document from the callback's own state, not from
    /// anything that must happen once.
    /// </para>
    /// <para>
    /// To merge PDF files that already exist, see <c>PeachDrawing.PDF.PdfOperation</c>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var merge = DocumentMerge.Of(BuildReport("Q1"), BuildReport("Q2")).UseContinuousPageNumbers();
    /// var document = await generator.CreateMergedDocument(merge);
    /// </code>
    /// </example>
    public sealed class DocumentMerge
    {
        private readonly List<Action<IDocumentBuilder>> _parts = [];

        private DocumentMerge()
        {
        }

        /// <summary>Starts a merge of the given documents, in order.</summary>
        /// <param name="documents">the callbacks that build each document</param>
        /// <returns>the merge; its numbering is <see cref="MergePageNumbering.Original"/> until changed</returns>
        public static DocumentMerge Of(params Action<IDocumentBuilder>[] documents)
        {
            ArgumentNullException.ThrowIfNull(documents);
            var merge = new DocumentMerge();
            foreach (var document in documents)
                merge.Add(document);
            return merge;
        }

        /// <summary>Starts a merge of the given documents, in order.</summary>
        /// <param name="documents">the callbacks that build each document</param>
        /// <returns>the merge; its numbering is <see cref="MergePageNumbering.Original"/> until changed</returns>
        public static DocumentMerge Of(IEnumerable<Action<IDocumentBuilder>> documents)
        {
            ArgumentNullException.ThrowIfNull(documents);
            var merge = new DocumentMerge();
            foreach (var document in documents)
                merge.Add(document);
            return merge;
        }

        /// <summary>The way the pages of the parts are numbered.</summary>
        public MergePageNumbering Numbering { get; private set; }

        /// <summary>The number of documents merged.</summary>
        public int Count => _parts.Count;

        /// <summary>Adds a document to the end of the merge.</summary>
        /// <param name="document">the callback that builds it</param>
        /// <returns>this merge</returns>
        public DocumentMerge Add(Action<IDocumentBuilder> document)
        {
            ArgumentNullException.ThrowIfNull(document);
            _parts.Add(document);
            return this;
        }

        /// <summary>Numbers each document's pages from 1 and reports its own total (the default).</summary>
        /// <returns>this merge</returns>
        public DocumentMerge UseOriginalPageNumbers()
        {
            Numbering = MergePageNumbering.Original;
            return this;
        }

        /// <summary>Numbers the pages of all the documents as one run, with the total of all of them.</summary>
        /// <returns>this merge</returns>
        public DocumentMerge UseContinuousPageNumbers()
        {
            Numbering = MergePageNumbering.Continuous;
            return this;
        }

        internal IReadOnlyList<Action<IDocumentBuilder>> Parts => _parts;
    }
}
