namespace PeachPDF.PdfSharpCore.Pdf.Filters
{
    /// <summary>
    /// Picks the general-purpose stream filter for a document: <c>/BrotliDecode</c> when
    /// <see cref="PdfDocumentOptions.UseBrotli"/> and an encoder is available, <c>/FlateDecode</c> otherwise.
    /// </summary>
    internal static class StreamCompression
    {
        private static readonly BrotliDecode Brotli = new();

        /// <summary>Compresses <paramref name="data"/> and reports the <c>/Filter</c> name that decodes it.</summary>
        public static byte[] Encode(PdfDocumentOptions options, byte[] data, out string filterName)
        {
            if (options.UseBrotli)
            {
                var brotli = Brotli.Encode(data, options.FlateEncodeMode);
                if (brotli is not null)
                {
                    filterName = BrotliDecode.Name;
                    return brotli;
                }
            }

            filterName = "/FlateDecode";
            return Filtering.FlateDecode.Encode(data, options.FlateEncodeMode);
        }
    }
}
