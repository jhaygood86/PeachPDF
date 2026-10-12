// Derived from PDFsharp (MIT) - see LICENSE.md

#nullable disable warnings

using System;

namespace PeachPDF.PDF.Drawing;

/// <summary>
/// Specifies the format of the image.
/// </summary>
internal sealed class PdfImageFormat
{
    PdfImageFormat(Guid guid)
    {
        _guid = guid;
    }

    internal Guid Guid
    {
        get { return _guid; }
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current object.
    /// </summary>
    public override bool Equals(object? obj)
    {
        PdfImageFormat format = obj as PdfImageFormat;
        if (format == null)
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
    public static PdfImageFormat Png
    {
        get { return _png; }
    }

    /// <summary>
    /// Gets the Graphics Interchange Format (GIF) image format.
    /// </summary>
    public static PdfImageFormat Gif
    {
        get { return _gif; }
    }

    /// <summary>
    /// Gets the Joint Photographic Experts Group (JPEG) image format.
    /// </summary>
    public static PdfImageFormat Jpeg
    {
        get { return _jpeg; }
    }

    /// <summary>
    /// Gets the Tag Image File Format (TIFF) image format.
    /// </summary>
    public static PdfImageFormat Tiff
    {
        get { return _tiff; }
    }

    /// <summary>
    /// Gets the Portable Document Format (PDF) image format
    /// </summary>
    public static PdfImageFormat Pdf
    {
        get { return _pdf; }
    }

    /// <summary>
    /// Gets the Windows icon image format.
    /// </summary>
    public static PdfImageFormat Icon
    {
        get { return _icon; }
    }

    readonly Guid _guid;

    // Guids used in GDI+
    //ImageFormat.memoryBMP = new ImageFormat(new Guid("{b96b3caa-0728-11d3-9d7b-0000f81ef32e}"));
    //ImageFormat.bmp = new ImageFormat(new Guid("{b96b3cab-0728-11d3-9d7b-0000f81ef32e}"));
    //ImageFormat.emf = new ImageFormat(new Guid("{b96b3cac-0728-11d3-9d7b-0000f81ef32e}"));
    //ImageFormat.wmf = new ImageFormat(new Guid("{b96b3cad-0728-11d3-9d7b-0000f81ef32e}"));
    //ImageFormat.jpeg = new ImageFormat(new Guid("{b96b3cae-0728-11d3-9d7b-0000f81ef32e}"));
    //ImageFormat.png = new ImageFormat(new Guid("{b96b3caf-0728-11d3-9d7b-0000f81ef32e}"));
    //ImageFormat.gif = new ImageFormat(new Guid("{b96b3cb0-0728-11d3-9d7b-0000f81ef32e}"));
    //ImageFormat.tiff = new ImageFormat(new Guid("{b96b3cb1-0728-11d3-9d7b-0000f81ef32e}"));
    //ImageFormat.exif = new ImageFormat(new Guid("{b96b3cb2-0728-11d3-9d7b-0000f81ef32e}"));
    //ImageFormat.photoCD = new ImageFormat(new Guid("{b96b3cb3-0728-11d3-9d7b-0000f81ef32e}"));
    //ImageFormat.flashPIX = new ImageFormat(new Guid("{b96b3cb4-0728-11d3-9d7b-0000f81ef32e}"));
    //ImageFormat.icon = new ImageFormat(new Guid("{b96b3cb5-0728-11d3-9d7b-0000f81ef32e}"));

    // #??? Why Guids?
    private static readonly PdfImageFormat _png = new PdfImageFormat(new Guid("{B96B3CAF-0728-11D3-9D7B-0000F81EF32E}"));
    private static readonly PdfImageFormat _gif = new PdfImageFormat(new Guid("{B96B3CB0-0728-11D3-9D7B-0000F81EF32E}"));
    private static readonly PdfImageFormat _jpeg = new PdfImageFormat(new Guid("{B96B3CAE-0728-11D3-9D7B-0000F81EF32E}"));
    private static readonly PdfImageFormat _tiff = new PdfImageFormat(new Guid("{B96B3CB1-0728-11D3-9D7B-0000F81EF32E}"));
    private static readonly PdfImageFormat _icon = new PdfImageFormat(new Guid("{B96B3CB5-0728-11D3-9D7B-0000F81EF32E}"));
    // not GDI+ conform
    private static readonly PdfImageFormat _pdf = new PdfImageFormat(new Guid("{84570158-DBF0-4C6B-8368-62D6A3CA76E0}"));
}
