namespace PeachPDF.PdfSharpCore.Fonts
{
    internal static class FontFormatConverter
    {
        public static byte[] ToOpenType(byte[] bytes)
        {
            if (WoffConverter.IsWoff(bytes)) return WoffConverter.Convert(bytes);
            if (Woff2Converter.IsWoff2(bytes)) return Woff2Converter.Convert(bytes);
            return bytes;
        }
    }
}
