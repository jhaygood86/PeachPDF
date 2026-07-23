namespace PeachPDF.Imaging
{
    /// <summary>
    /// Re-encodes a decoded image to JPEG or BMP via an OS-native codec. Implementations must never
    /// throw - any failure is reported by returning false so the caller can fall back to
    /// StbImageWriteSharp on the same <see cref="DecodedImage"/>.
    /// </summary>
    internal interface IPlatformImageEncoder
    {
        bool TryEncodeJpeg(in DecodedImage image, int quality, out byte[] jpegBytes);

        bool TryEncodeBmp(in DecodedImage image, out byte[] bmpBytes);
    }
}
