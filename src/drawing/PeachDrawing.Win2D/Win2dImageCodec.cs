namespace PeachDrawing.Win2D;

/// <summary>Which decoder turns an encoded image (PNG, JPEG, ...) into the pixels a <see cref="Win2dRenderContext"/> draws.</summary>
public enum Win2dImageCodec
{
    /// <summary>The shared PeachImage decoder: the same formats and the same pixels as on every other PeachDrawing canvas.</summary>
    PeachImage,

    /// <summary>
    /// The Windows Imaging Component, through <c>BitmapDecoder</c>: the codecs installed on the machine, which can read formats PeachImage does
    /// not (whatever codec extensions the user has added) and hand decoding to hardware where the system does. A format Windows cannot decode is
    /// handed to PeachImage instead unless <see cref="Win2dRenderContext.FallbackToPeachImage"/> is switched off.
    /// </summary>
    Native,

    /// <summary>
    /// The default. An image Windows has a codec for is kept as its encoded bytes and decoded by Windows straight into the bitmap of each device
    /// it is drawn on, so no managed copy of its pixels is made unless something asks for them (<c>GetPixels</c>); any other image is decoded by
    /// PeachImage. Pixels can differ from PeachImage's where Windows converts differently (some colour-managed or CMYK files).
    /// </summary>
    Auto = 2,
}
