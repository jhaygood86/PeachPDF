using PeachPDF.PdfSharpCore.Pdf;
using System.IO;

namespace PeachPDF;

/// <summary>
/// A PDF document produced by <see cref="PdfGenerator"/>. Instances are created by calling one of the
/// <c>PdfGenerator.GeneratePdf</c> overloads and are written out with <see cref="Save"/>.
/// </summary>
public class PeachPdfDocument
{
    private readonly PdfDocument _document;

    internal PeachPdfDocument(PdfDocument document)
    {
        _document = document;
    }

    internal PdfDocument PdfDocument => _document;

    /// <summary>
    /// The number of pages currently in the document.
    /// </summary>
    public int PageCount => _document.PageCount;

    /// <summary>
    /// Words this render drew and then truncated with a clip - the loss class reading the finished
    /// PDF back cannot detect. See <see cref="PeachPDF.ClipReport"/> for what it does and does not
    /// cover.
    /// </summary>
    /// <remarks>
    /// Public because the consumer of this library is a separate assembly and the internals are not
    /// visible to it. Everything else the render reports about itself is read back OUT of the bytes;
    /// this is the one fact only the engine holds, so it has to be handed over rather than inferred.
    /// </remarks>
    public ClipReport ClipReport { get; internal set; } = new();

    internal PdfPages Pages => _document.Pages;

    /// <summary>
    /// Writes the completed PDF to the given stream.
    /// </summary>
    /// <param name="stream">The destination stream. The caller owns the stream and is responsible for disposing it.</param>
    public void Save(Stream stream)
    {
        _document.Save(stream);
    }

    /// <summary>
    /// Merges embedded raster images that encode to byte-identical PDF image objects, keeping one copy
    /// and repointing every other reference at it. Call this after all pages have been added, and before
    /// <see cref="Save"/> - a page's resources are rewritten in place, but the size reduction only takes
    /// effect once <see cref="Save"/> drops the now-unreferenced duplicate objects.
    /// </summary>
    /// <remarks>
    /// Repeated references to the <em>same</em> resolved image source within one
    /// <c>PdfGenerator.GeneratePdf</c>/<c>AddPdfPages</c> call already share one decoded, embedded image
    /// automatically - see <see cref="PdfGenerator"/>. This method is for what that doesn't cover: images
    /// added across separate <c>AddPdfPages</c>/<c>AddPages</c> calls into the same document, or two
    /// different sources that happen to encode identical bytes. It isn't run automatically, since hashing
    /// every embedded image isn't a cost most documents should pay on every <see cref="Save"/>.
    /// </remarks>
    public void ConsolidateImages()
    {
        _document.ConsolidateImages();
    }
}
