using PeachDrawing.Text;

namespace PeachBrackets.Avalonia;

/// <summary>
/// The fonts every <see cref="HtmlView"/> in the application shares, on top of the fonts installed on the machine. Add a font here before a document
/// that names it loads.
/// </summary>
public static class HtmlViewFonts
{
    /// <summary>The font set views use unless <see cref="HtmlView.FontSet"/> gives one of their own.</summary>
    public static FontSet Shared { get; } = new();

    /// <summary>Adds the font in <paramref name="data"/> to <see cref="Shared"/>.</summary>
    /// <param name="data">the bytes of a TrueType, OpenType, WOFF or WOFF2 font</param>
    /// <param name="options">how to name and describe it, or <see langword="null"/> to read that from the font</param>
    /// <returns>the family the font was added to</returns>
    public static TypefaceFamily AddData(ReadOnlyMemory<byte> data, AddOptions? options = null) => Shared.AddData(data, options);

    /// <summary>Adds the font in the file at <paramref name="path"/> to <see cref="Shared"/>.</summary>
    /// <param name="path">the font file</param>
    /// <param name="options">how to name and describe it, or <see langword="null"/> to read that from the font</param>
    /// <returns>the family the font was added to</returns>
    public static TypefaceFamily AddFile(string path, AddOptions? options = null) => Shared.AddFile(path, options);

    /// <summary>Adds the font in <paramref name="stream"/> to <see cref="Shared"/>.</summary>
    /// <param name="stream">a stream holding a font</param>
    /// <param name="options">how to name and describe it, or <see langword="null"/> to read that from the font</param>
    /// <returns>the family the font was added to</returns>
    public static TypefaceFamily AddStream(Stream stream, AddOptions? options = null) => Shared.AddStream(stream, options);
}
