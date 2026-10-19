// Derived from PDFsharp (MIT) - see LICENSE.md

using System;

namespace PeachDrawing.PDF.Engine.Drawing;

/// <summary>
/// Specifies the format of the image.
/// </summary>
internal sealed class PdfImageFormat
{
    PdfImageFormat(Guid guid)
    {
        _guid = guid;
    }

    internal Guid Guid => _guid;

    /// <summary>
    /// Determines whether the specified object is equal to the current object.
    /// </summary>
    public override bool Equals(object? obj)
    {
        PdfImageFormat? format = obj as PdfImageFormat;
        if (format is null)
            return false;
        return _guid == format._guid;
    }

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    public override int GetHashCode()
    {
        return _guid.GetHashCode();
    }

    /// <summary>
    /// Gets the Portable Network Graphics (PNG) image format. 
    /// </summary>
    public static PdfImageFormat Png => _png;

    /// <summary>
    /// Gets the Graphics Interchange Format (GIF) image format.
    /// </summary>
    public static PdfImageFormat Gif => _gif;

    /// <summary>
    /// Gets the Joint Photographic Experts Group (JPEG) image format.
    /// </summary>
    public static PdfImageFormat Jpeg => _jpeg;

    /// <summary>
    /// Gets the Tag Image File Format (TIFF) image format.
    /// </summary>
    public static PdfImageFormat Tiff => _tiff;

    /// <summary>
    /// Gets the Portable Document Format (PDF) image format
    /// </summary>
    public static PdfImageFormat Pdf => _pdf;

    /// <summary>
    /// Gets the Windows icon image format.
    /// </summary>
    public static PdfImageFormat Icon => _icon;

    readonly Guid _guid;

    // #??? Why Guids?
    private static readonly PdfImageFormat _png = new PdfImageFormat(new Guid("{B96B3CAF-0728-11D3-9D7B-0000F81EF32E}"));
    private static readonly PdfImageFormat _gif = new PdfImageFormat(new Guid("{B96B3CB0-0728-11D3-9D7B-0000F81EF32E}"));
    private static readonly PdfImageFormat _jpeg = new PdfImageFormat(new Guid("{B96B3CAE-0728-11D3-9D7B-0000F81EF32E}"));
    private static readonly PdfImageFormat _tiff = new PdfImageFormat(new Guid("{B96B3CB1-0728-11D3-9D7B-0000F81EF32E}"));
    private static readonly PdfImageFormat _icon = new PdfImageFormat(new Guid("{B96B3CB5-0728-11D3-9D7B-0000F81EF32E}"));
    // not GDI+ conform
    private static readonly PdfImageFormat _pdf = new PdfImageFormat(new Guid("{84570158-DBF0-4C6B-8368-62D6A3CA76E0}"));
}
