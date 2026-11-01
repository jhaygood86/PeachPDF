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
}
