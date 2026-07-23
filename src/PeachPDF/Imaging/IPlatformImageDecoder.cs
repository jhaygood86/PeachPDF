using System;

namespace PeachPDF.Imaging
{
    /// <summary>
    /// Decodes an encoded image via an OS-native codec. Implementations must never throw - any failure
    /// (unsupported format, missing OS codec/library, malformed input) is reported by returning false so
    /// the caller can fall back to STB.
    /// </summary>
    internal interface IPlatformImageDecoder
    {
        bool TryDecode(ReadOnlySpan<byte> bytes, out DecodedImage result);
    }
}
