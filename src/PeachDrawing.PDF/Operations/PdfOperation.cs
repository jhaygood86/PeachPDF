using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PeachDrawing.PDF
{
    /// <summary>
    /// Works on PDF files that already exist: take some of their pages, merge several into one, lay one over another, attach files,
    /// extend the metadata and protect or unprotect the result. Steps are chained and nothing is written until <see cref="Save(string)"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Pages are carried across by drawing them again into the new file (see <see cref="PdfPageSource"/>), together with their links, form
    /// fields, annotations, named destinations and outline. Whatever the importer cannot draw is left out of the page and
    /// reported by <see cref="Warnings"/> after saving.
    /// </para>
    /// <para>
    /// Steps run in the order they are chained: a layer lands on the pages the output has when it is added, and a later
    /// <see cref="TakePages"/> carries those pages' layers with them. A mistake in a step (a wrong password, a selector that names a page
    /// the file lacks) is reported by the call that made it.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// PdfOperation.LoadFile("a.pdf").TakePages("1-5").MergeFile("b.pdf", "1,3,5").Save("merged.pdf");
    /// </code>
    /// </example>
    public sealed class PdfOperation
    {
        private readonly List<OutputPage> _pages = [];
        private readonly List<PdfFileAttachment> _attachments = [];
        private readonly List<XElement> _metadata = [];
        private readonly List<string> _warnings = [];
        private PdfEncryptionOptions? _encryption;
        private PdfCopyMode _copyMode;

        private PdfOperation()
        {
        }

        /// <summary>Opens a PDF that needs no password.</summary>
        /// <param name="filePath">the file to open</param>
        /// <returns>an operation whose pages are those of the file</returns>
        /// <exception cref="PdfPasswordException">the file needs a password</exception>
        public static PdfOperation LoadFile(string filePath) => LoadFile(filePath, null);

        /// <summary>Opens a PDF protected with a password.</summary>
        /// <param name="filePath">the file to open</param>
        /// <param name="password">the user password (for an AES-256 file, the owner password also works)</param>
        /// <returns>an operation whose pages are those of the file</returns>
        /// <exception cref="PdfPasswordException">the password is missing or wrong</exception>
        public static PdfOperation LoadFile(string filePath, string? password)
        {
            ArgumentException.ThrowIfNullOrEmpty(filePath);
            return LoadBytes(File.ReadAllBytes(filePath), password);
        }

        /// <summary>Opens a PDF held in memory.</summary>
        /// <param name="pdf">the bytes of the file</param>
        /// <param name="password">the password, or <see langword="null"/> if the file needs none</param>
        /// <returns>an operation whose pages are those of the file</returns>
        /// <exception cref="PdfPasswordException">the password is missing or wrong</exception>
        public static PdfOperation LoadBytes(byte[] pdf, string? password = null)
        {
            var operation = new PdfOperation();
            operation.Append(PdfPageSource.Open(pdf, password), null);
            return operation;
        }

        /// <summary>Opens a PDF read from a stream, which is read to the end and left open.</summary>
        /// <param name="pdf">a stream positioned at the start of the file</param>
        /// <param name="password">the password, or <see langword="null"/> if the file needs none</param>
        /// <returns>an operation whose pages are those of the file</returns>
        /// <exception cref="PdfPasswordException">the password is missing or wrong</exception>
        public static PdfOperation LoadStream(Stream pdf, string? password = null)
        {
            var operation = new PdfOperation();
            operation.Append(PdfPageSource.Open(pdf, password), null);
            return operation;
        }

        /// <summary>The number of pages the output has so far.</summary>
        public int PageCount => _pages.Count;

        /// <summary>The features the importer could not draw, named once each, found by the last save.</summary>
        public IReadOnlyList<string> Warnings => _warnings;

        /// <summary>Keeps only the pages a selector names, in the order it names them.</summary>
        /// <param name="pageSelector">a <see cref="PdfPageSelector"/> selector, such as <c>"1,3,5-10"</c> or <c>"z-1"</c></param>
        /// <returns>this operation</returns>
        /// <exception cref="FormatException">the selector is not valid for the pages there are</exception>
        public PdfOperation TakePages(string pageSelector)
        {
            var taken = PdfPageSelector.Select(pageSelector, _pages.Count).Select(p => _pages[p - 1].Copy()).ToList();
            _pages.Clear();
            _pages.AddRange(taken);
            return this;
        }

        /// <summary>Adds the pages of another PDF to the end.</summary>
        /// <param name="filePath">the file to add</param>
        /// <param name="pageSelector">the pages of it to add, or <see langword="null"/> for all</param>
        /// <param name="password">its password, if it has one</param>
        /// <returns>this operation</returns>
        public PdfOperation MergeFile(string filePath, string? pageSelector = null, string? password = null)
        {
            ArgumentException.ThrowIfNullOrEmpty(filePath);
            return MergeBytes(File.ReadAllBytes(filePath), pageSelector, password);
        }

        /// <summary>Adds the pages of a PDF held in memory to the end.</summary>
        /// <param name="pdf">the bytes of the file</param>
        /// <param name="pageSelector">the pages of it to add, or <see langword="null"/> for all</param>
        /// <param name="password">its password, if it has one</param>
        /// <returns>this operation</returns>
        public PdfOperation MergeBytes(byte[] pdf, string? pageSelector = null, string? password = null)
        {
            Append(PdfPageSource.Open(pdf, password), pageSelector);
            return this;
        }

        /// <summary>Adds the pages of a PDF read from a stream to the end.</summary>
        /// <param name="pdf">a stream positioned at the start of the file; read to the end and left open</param>
        /// <param name="pageSelector">the pages of it to add, or <see langword="null"/> for all</param>
        /// <param name="password">its password, if it has one</param>
        /// <returns>this operation</returns>
        public PdfOperation MergeStream(Stream pdf, string? pageSelector = null, string? password = null)
        {
            Append(PdfPageSource.Open(pdf, password), pageSelector);
            return this;
        }

        /// <summary>Draws pages of another PDF over pages of the output.</summary>
        /// <param name="layer">which pages go over which</param>
        /// <returns>this operation</returns>
        public PdfOperation OverlayFile(PdfLayerConfiguration layer) => Layer(layer, underlay: false);

        /// <summary>Draws pages of another PDF under pages of the output, so the output's own content hides them where it is opaque.</summary>
        /// <param name="layer">which pages go under which</param>
        /// <returns>this operation</returns>
        public PdfOperation UnderlayFile(PdfLayerConfiguration layer) => Layer(layer, underlay: true);

        /// <summary>Attaches a file to the output.</summary>
        /// <param name="attachment">the file and how to describe it</param>
        /// <returns>this operation</returns>
        public PdfOperation AddAttachment(PdfFileAttachment attachment)
        {
            ArgumentNullException.ThrowIfNull(attachment);
            if ((attachment.FilePath is null) == (attachment.Data is null))
                throw new ArgumentException("Give the attachment either a FilePath or its Data.", nameof(attachment));
            if (attachment.FilePath is not null && !File.Exists(attachment.FilePath))
                throw new FileNotFoundException("The file to attach does not exist.", attachment.FilePath);
            if (attachment.Data is not null && string.IsNullOrEmpty(attachment.Key) && string.IsNullOrEmpty(attachment.AttachmentName))
                throw new ArgumentException("An attachment given as Data needs a Key or an AttachmentName.", nameof(attachment));

            _attachments.Add(attachment);
            return this;
        }

        /// <summary>Adds elements to the XMP metadata of the output, inside its <c>rdf:Description</c>.</summary>
        /// <param name="xml">one or more XML elements, each in the namespace of its schema</param>
        /// <returns>this operation</returns>
        /// <exception cref="System.Xml.XmlException">the text is not well-formed XML</exception>
        public PdfOperation ExtendMetadata(string xml)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(xml);

            // Several sibling elements are not one document, so they are read inside a wrapper that only carries them.
            var wrapper = XElement.Parse("<w>" + xml + "</w>");
            _metadata.AddRange(wrapper.Elements());
            return this;
        }

        /// <summary>Protects the output with a password and permissions.</summary>
        /// <param name="options">the passwords, the permissions and whether the metadata is encrypted</param>
        /// <returns>this operation</returns>
        public PdfOperation Encrypt(PdfEncryptionOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            _encryption = options.Clone();
            return this;
        }

        /// <summary>
        /// Writes the output with no protection, even when the pages came from an encrypted file or an earlier <see cref="Encrypt"/> step
        /// asked for some. A file opened here is already read with its password, so this only has to stop it being protected again.
        /// </summary>
        /// <returns>this operation</returns>
        public PdfOperation Decrypt()
        {
            _encryption = null;
            return this;
        }

        /// <summary>
        /// Writes the output with no permission restrictions. Pages are drawn again rather than copied, so a restriction or a signature
        /// of the original is never carried across; this is the same as <see cref="Decrypt"/> and exists for symmetry with that step.
        /// </summary>
        /// <returns>this operation</returns>
        public PdfOperation RemoveRestrictions() => Decrypt();

        /// <summary>Chooses how the pages of the loaded files are carried into the output.</summary>
        /// <param name="mode">the mode; <see cref="PdfCopyMode.Redraw"/> until this is called</param>
        /// <returns>this operation</returns>
        public PdfOperation WithCopyMode(PdfCopyMode mode)
        {
            if (!Enum.IsDefined(mode))
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);

            _copyMode = mode;
            return this;
        }

        /// <summary>Writes the output to a file, replacing it.</summary>
        /// <param name="filePath">where to write</param>
        public void Save(string filePath)
        {
            ArgumentException.ThrowIfNullOrEmpty(filePath);
            using var stream = File.Create(filePath);
            Save(stream);
        }

        /// <summary>Writes the output to a stream, which is left open.</summary>
        /// <param name="stream">where to write</param>
        public void Save(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            if (_pages.Count == 0)
                throw new InvalidOperationException("There are no pages to write.");

            _warnings.Clear();
            PdfOperationWriter.Write(this, stream);
        }

        /// <summary>Writes the output to memory.</summary>
        /// <returns>the bytes of the PDF file</returns>
        public byte[] ToArray()
        {
            using var stream = new MemoryStream();
            Save(stream);
            return stream.ToArray();
        }

        // ---------------------------------------------------------------------------------------------------------------

        private void Append(PdfPageSource source, string? selector)
        {
            var chosen = selector is null
                ? Enumerable.Range(1, source.PageCount)
                : PdfPageSelector.Select(selector, source.PageCount);
            foreach (var page in chosen)
                _pages.Add(new OutputPage(source, page - 1));
        }

        private PdfOperation Layer(PdfLayerConfiguration layer, bool underlay)
        {
            ArgumentNullException.ThrowIfNull(layer);
            var given = (layer.FilePath is not null ? 1 : 0) + (layer.Data is not null ? 1 : 0) + (layer.Stream is not null ? 1 : 0);
            if (given != 1)
                throw new ArgumentException("Give the layer exactly one of FilePath, Data and Stream.", nameof(layer));

            var source = layer.FilePath is not null
                ? PdfPageSource.Open(File.ReadAllBytes(layer.FilePath), layer.Password)
                : layer.Data is not null
                    ? PdfPageSource.Open(layer.Data, layer.Password)
                    : PdfPageSource.Open(layer.Stream!, layer.Password);

            var targets = layer.TargetPages is null
                ? Enumerable.Range(1, _pages.Count).ToList()
                : PdfPageSelector.Select(layer.TargetPages, _pages.Count).ToList();
            var from = layer.SourcePages is null
                ? Enumerable.Range(1, source.PageCount).ToList()
                : PdfPageSelector.Select(layer.SourcePages, source.PageCount).ToList();
            var repeat = layer.RepeatSourcePages is null ? [] : PdfPageSelector.Select(layer.RepeatSourcePages, source.PageCount).ToList();

            for (var ordinal = 0; ordinal < targets.Count; ordinal++)
            {
                var sourcePage = ordinal < from.Count
                    ? from[ordinal] - 1
                    : repeat.Count == 0 ? -1 : repeat[(ordinal - from.Count) % repeat.Count] - 1;
                if (sourcePage >= 0)
                    _pages[targets[ordinal] - 1].Layers.Add(new PageLayer(source, sourcePage, underlay));
            }

            return this;
        }

        internal sealed record PageLayer(PdfPageSource Source, int SourcePage, bool Underlay);

        /// <summary>One page of the output: the page of a file it is, and the layers drawn on it.</summary>
        internal sealed class OutputPage(PdfPageSource source, int sourcePage)
        {
            public PdfPageSource Source { get; } = source;

            public int SourcePage { get; } = sourcePage;

            public List<PageLayer> Layers { get; } = [];

            public OutputPage Copy()
            {
                var copy = new OutputPage(Source, SourcePage);
                copy.Layers.AddRange(Layers);
                return copy;
            }
        }

        internal IReadOnlyList<OutputPage> OutputPages => _pages;

        internal IReadOnlyList<PdfFileAttachment> Attachments => _attachments;

        internal IReadOnlyList<XElement> MetadataElements => _metadata;

        internal PdfEncryptionOptions? EncryptionOptions => _encryption;

        internal PdfCopyMode CopyMode => _copyMode;

        internal List<string> WarningsList => _warnings;
    }
}
