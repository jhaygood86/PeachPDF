using PeachReader.Core;

namespace PeachReader;

/// <summary>Draws the small picture of a page the page list shows; each platform supplies one with its own backend.</summary>
public interface IThumbnailRenderer
{
    /// <summary>Draws a page.</summary>
    /// <param name="document">the document</param>
    /// <param name="pageIndex">the zero-based page number</param>
    /// <param name="pixelWidth">the width of the picture in pixels</param>
    /// <returns>the picture as PNG bytes, or <see langword="null"/> when it could not be drawn</returns>
    Task<byte[]?> RenderAsync(ReaderDocument document, int pageIndex, int pixelWidth);
}

/// <summary>Where the app finds the platform's <see cref="IThumbnailRenderer"/>.</summary>
public static class Thumbnails
{
    /// <summary>The renderer of the platform the app runs on; <see langword="null"/> when the platform has none (the list shows page numbers only).</summary>
    public static IThumbnailRenderer? Renderer { get; set; }
}
