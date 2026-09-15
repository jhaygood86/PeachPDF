using PeachPDF.Html.Adapters;
using PeachPDF.Svg;
using System;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace PeachPDF
{
    /// <summary>
    /// A reusable, resolve-once image source for the declarative document-building API
    /// (<see cref="PdfGenerator.CreateDocument"/>) - pass the same instance to more than one
    /// <c>IContainer.Image(PdfImage)</c> call to place the same image (raster or SVG - the format is
    /// detected automatically, same as the other <c>Image</c>/<c>Svg</c> overloads) in multiple places
    /// without re-reading/redecoding its source for each placement. Mirrors QuestPDF's own shared-image
    /// API (<see href="https://www.questpdf.com/api-reference/image/shared.html"/>): decode/parse once,
    /// reuse the resolved result everywhere the same instance is placed.
    /// </summary>
    public sealed class PdfImage
    {
        private readonly byte[]? _bytes;
        private readonly string? _filePath;

        // Cached by the RAdapter that produced it (tied to a specific document generation) - a second
        // Resolve call with the SAME adapter (placements within one document build, the common, well-
        // scoped case this class exists for) reuses it for free; a different adapter (this instance reused
        // across a separate CreateDocument/AddPages call) re-resolves rather than risking a PDF resource
        // that assumes single-document ownership - see this repo's own PdfSharpCore fork, whose form/image
        // XObjects are written against one specific PdfDocument.
        private RAdapter? _resolvedByAdapter;
        private RImage? _resolvedImage;
        private SvgDocument? _resolvedSvgDocument;

        private PdfImage(byte[]? bytes, string? filePath)
        {
            _bytes = bytes;
            _filePath = filePath;
        }

        /// <summary>Loads an image from a local file path.</summary>
        public static PdfImage FromFile(string path)
        {
            ArgumentNullException.ThrowIfNull(path);
            return new PdfImage(null, path);
        }

        /// <summary>Loads an image from raw bytes (raster or SVG - the format is detected automatically).</summary>
        public static PdfImage FromBytes(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            return new PdfImage(data, null);
        }

        /// <summary>Loads an image read fully from a stream (raster or SVG - the format is detected automatically).</summary>
        public static PdfImage FromStream(Stream stream)
        {
            ArgumentNullException.ThrowIfNull(stream);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return FromBytes(ms.ToArray());
        }

        /// <summary>
        /// Decodes this image's source into an <see cref="RImage"/> (raster) or <see cref="SvgDocument"/>
        /// (SVG), caching the result against <paramref name="adapter"/> so a second placement of this same
        /// <see cref="PdfImage"/> with the same adapter decodes nothing. A file path is read (and, unlike
        /// bytes already in memory, format-sniffed by its own <c>.svg</c> extension rather than content)
        /// lazily, here, on first resolution - not at <see cref="FromFile"/> call time, since no adapter
        /// exists yet to decode with then.
        /// </summary>
        internal (RImage? Image, SvgDocument? SvgDocument) Resolve(RAdapter adapter)
        {
            if (_resolvedByAdapter == adapter)
            {
                return (_resolvedImage, _resolvedSvgDocument);
            }

            var bytes = _bytes ?? File.ReadAllBytes(_filePath!);
            var isSvg = _filePath is { } path
                ? path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                : LooksLikeSvgMarkup(bytes);

            if (isSvg)
            {
                // StreamReader (not Encoding.UTF8.GetString directly) so a leading UTF-8 BOM - common in
                // SVG files saved by many editors - is detected and stripped rather than surviving into
                // the decoded string as a literal U+FEFF character, which XElement.Parse rejects outright
                // ("Data at the root level is invalid").
                using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var markup = reader.ReadToEnd();
                _resolvedSvgDocument = SvgTreeBuilder.Build(new XElementSvgSourceNode(XElement.Parse(markup)), adapter);
                _resolvedImage = null;
            }
            else
            {
                _resolvedImage = adapter.ImageFromStream(new MemoryStream(bytes));
                _resolvedSvgDocument = null;
            }

            _resolvedByAdapter = adapter;
            return (_resolvedImage, _resolvedSvgDocument);
        }

        /// <summary>
        /// Sniffs raw bytes as SVG by looking for an XML/<c>&lt;svg&gt;</c> prefix (after an optional
        /// UTF-8 BOM and leading whitespace, both stripped by <see cref="StreamReader"/> the same way
        /// <see cref="Resolve"/>'s own real parse does) - the only option for in-memory bytes with no
        /// filename/URL extension to check instead (unlike <see cref="FromFile"/>'s path, or the HTML
        /// <c>&lt;img src&gt;</c> path's own extension/<c>Content-Type</c> sniffing).
        /// </summary>
        private static bool LooksLikeSvgMarkup(byte[] bytes)
        {
            using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var preview = new char[256];
            var read = reader.Read(preview, 0, preview.Length);
            var text = new string(preview, 0, read).TrimStart();

            return text.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("<svg", StringComparison.OrdinalIgnoreCase);
        }
    }
}
