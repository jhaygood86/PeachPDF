using System;
using System.Collections.Generic;
using System.IO;
using PeachDrawing.PDF;

namespace PeachPDF.Layout
{
    /// <summary>A PDF file stamped over or under pages of the document, and which pages it goes on.</summary>
    internal sealed class FileLayerBuilder(bool isUnderlay) : IFileLayerDescriptor
    {
        private byte[]? _bytes;
        private string? _path;
        private Stream? _stream;
        private string _target = "1-z";
        private string _source = "1-z";
        private string? _repeat;

        public IFileLayerDescriptor File(string path)
        {
            ArgumentException.ThrowIfNullOrEmpty(path);
            (_path, _bytes, _stream) = (path, null, null);
            return this;
        }

        public IFileLayerDescriptor File(byte[] pdf)
        {
            ArgumentNullException.ThrowIfNull(pdf);
            (_path, _bytes, _stream) = (null, pdf, null);
            return this;
        }

        public IFileLayerDescriptor File(Stream pdf)
        {
            ArgumentNullException.ThrowIfNull(pdf);
            (_path, _bytes, _stream) = (null, null, pdf);
            return this;
        }

        public IFileLayerDescriptor TargetPages(string range)
        {
            ArgumentNullException.ThrowIfNull(range);
            _target = range;
            return this;
        }

        public IFileLayerDescriptor SourcePages(string range)
        {
            ArgumentNullException.ThrowIfNull(range);
            _source = range;
            return this;
        }

        public IFileLayerDescriptor RepeatSourcePages(string range)
        {
            ArgumentNullException.ThrowIfNull(range);
            _repeat = range;
            return this;
        }

        /// <summary>Reads the file and resolves the source ranges, which depend only on the file's own page count.</summary>
        public FileLayer Build()
        {
            var bytes = _bytes
                ?? (_path is not null ? System.IO.File.ReadAllBytes(_path) : null)
                ?? (_stream is not null ? ReadAll(_stream) : null)
                ?? throw new InvalidOperationException("A file layer needs a PDF file: call File(...) on it.");

            var source = PdfPageSource.Open(bytes);
            var sourcePages = PageRange.Parse(_source, source.PageCount);
            var repeatPages = _repeat is null ? [] : PageRange.Parse(_repeat, source.PageCount);
            return new FileLayer(source, isUnderlay, _target, sourcePages, repeatPages);
        }

        private static byte[] ReadAll(Stream stream)
        {
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        }
    }

    /// <summary>A resolved file layer, ready to paint.</summary>
    internal sealed class FileLayer(PdfPageSource source, bool isUnderlay, string targetRange, IReadOnlyList<int> sourcePages, IReadOnlyList<int> repeatPages)
    {
        public bool IsUnderlay { get; } = isUnderlay;

        public PdfPageSource Source { get; } = source;

        /// <summary>The one-based pages of the document that receive this layer, once the page count is known.</summary>
        public IReadOnlyList<int> ResolveTargets(int documentPageCount) => PageRange.Parse(targetRange, documentPageCount);

        /// <summary>The zero-based page of the file for the <paramref name="ordinal"/>th target page, or null when the layer has run out.</summary>
        public int? SourcePageFor(int ordinal)
        {
            if (ordinal < sourcePages.Count)
                return sourcePages[ordinal] - 1;

            if (repeatPages.Count == 0)
                return null;

            return repeatPages[(ordinal - sourcePages.Count) % repeatPages.Count] - 1;
        }
    }
}
