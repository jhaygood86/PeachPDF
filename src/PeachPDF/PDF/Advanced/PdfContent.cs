// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachPDF.PDF.Rendering;
using PeachPDF.PDF.Filters;
using PeachPDF.PDF.IO;
using System;
using System.Diagnostics;

namespace PeachPDF.PDF.Advanced;

/// <summary>
/// Represents the content of a page. PDFsharp supports only one content stream per page.
/// </summary>
internal sealed class PdfContent : PdfDictionary
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PdfContent"/> class.
    /// </summary>
    public PdfContent(PdfDocument document)
        : base(document)
    { }

    /// <summary>
    /// Initializes a new instance of the <see cref="PdfContent"/> class.
    /// </summary>
    internal PdfContent(PdfPage page)
        : base(page is not null ? page.Owner : null!)
    {
        //_pageContent = new PageContent(page);
    }

    /// <summary>
    /// Sets a value indicating whether the content is compressed with the ZIP algorithm.
    /// </summary>
    public bool Compressed
    {
        set
        {
            if (value)
            {
                PdfItem? filter = Elements[PdfStream.Keys.Filter];
                if (filter is null)
                {
                    byte[] bytes = StreamCompression.Encode(_document.Options, Stream.Value, out var filterName);
                    Stream.Value = bytes;
                    Elements.SetInteger(PdfStream.Keys.Length, Stream.Length);
                    Elements.SetName(PdfStream.Keys.Filter, filterName);
                }
            }
        }
    }

    internal override void WriteObject(PdfWriter writer)
    {
        if (_pdfRenderer is not null)
        {
            // GetContent also disposes the underlying PdfGraphics object, if one exists
            //Stream = new PdfStream(PdfEncoders.LatinOneByteEncoding.GetBytes(pdfRenderer.GetContent()), this);
            _pdfRenderer.Close();
            Debug.Assert(_pdfRenderer is null);
        }

        if (Stream is not null)
        {
            //if (Owner.Options.CompressContentStreams)
            if (Owner.Options.CompressContentStreams && Elements.GetName("/Filter").Length == 0)
            {
                Stream.Value = StreamCompression.Encode(_document.Options, Stream.Value, out var filterName);
                Elements.SetName("/Filter", filterName);
            }
            Elements.SetInteger("/Length", Stream.Length);
        }

        base.WriteObject(writer);
    }

    internal PdfGraphicsRenderer _pdfRenderer = null!;

    /// <summary>
    /// Predefined keys of this dictionary.
    /// </summary>
    internal sealed class Keys : PdfStream.Keys
    {
        /// <summary>
        /// Gets the KeysMeta for these keys.
        /// </summary>
        public static PdfDictionaryMetadata Meta { get; } = CreateMeta(typeof(Keys));
    }

    /// <summary>
    /// Gets the KeysMeta of this dictionary type.
    /// </summary>
    internal override PdfDictionaryMetadata Meta => Keys.Meta;
}
