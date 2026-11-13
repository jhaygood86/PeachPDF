using PeachDrawing.Text;
using System;
using System.IO;

namespace PeachBrackets.WinUI;

/// <summary>
/// The fonts every <see cref="HtmlView"/> shares by default. A font added here is parsed once and then used by every view and every document, however
/// many there are, instead of each view reading it again; a list of a hundred rows pays for a font once.
/// </summary>
/// <remarks>
/// <para>
/// Add the application's fonts at start-up, before the first view loads, and refer to them by family name from the HTML (<c>font-family</c>), or from
/// an <c>@font-face</c> rule that names the same family. A font added later is seen by views that load afterwards, and by views that are already
/// showing a document the next time they load one.
/// </para>
/// <para>
/// Adding the same font again with the same description is a cheap no-op. The set may be used from any thread.
/// </para>
/// </remarks>
public static class HtmlViewFonts
{
    /// <summary>The set of fonts views use unless they are given another with <see cref="HtmlView.FontSet"/>.</summary>
    public static FontSet Shared { get; } = new();

    /// <summary>Adds a font to the shared set.</summary>
    /// <param name="data">the font file (TrueType, OpenType, WOFF or WOFF2)</param>
    /// <param name="options">the family name and the weight, width and style the font is declared for, or <see langword="null"/> for what the file says</param>
    /// <returns>the family the font was added to</returns>
    public static TypefaceFamily AddData(ReadOnlyMemory<byte> data, AddOptions? options = null) => Shared.AddData(data, options);

    /// <summary>Adds a font file to the shared set.</summary>
    /// <param name="path">the path of the font file</param>
    /// <param name="options">the family name and the weight, width and style the font is declared for, or <see langword="null"/> for what the file says</param>
    /// <returns>the family the font was added to</returns>
    public static TypefaceFamily AddFile(string path, AddOptions? options = null) => Shared.AddFile(path, options);

    /// <summary>Adds a font read from a stream, which is read to its end and left open, to the shared set.</summary>
    /// <param name="stream">a stream positioned at the start of the font file</param>
    /// <param name="options">the family name and the weight, width and style the font is declared for, or <see langword="null"/> for what the file says</param>
    /// <returns>the family the font was added to</returns>
    public static TypefaceFamily AddStream(Stream stream, AddOptions? options = null) => Shared.AddStream(stream, options);
}
