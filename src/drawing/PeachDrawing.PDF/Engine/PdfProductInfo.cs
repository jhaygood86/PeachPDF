namespace PeachDrawing.PDF.Engine;

/// <summary>The name and version this package writes into a document that does not name its own creator and producer.</summary>
internal static class PdfProductInfo
{
    /// <summary>
    /// <c>PeachPDF {version}</c>, the product name at the one version every package carries. It was the embedded engine's own PDFsharp
    /// identification (an old branded string with a project address), which no document of ours should name.
    /// </summary>
    public static string Producer => PdfComposerProductInfo.Generator;
}
