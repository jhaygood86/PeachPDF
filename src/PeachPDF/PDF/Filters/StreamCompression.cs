namespace PeachPDF.PDF.Filters
{
    /// <summary>
    /// Picks the general-purpose stream filter for a document: <c>/BrotliDecode</c> when
    /// <see cref="PdfDocumentOptions.UseBrotli"/> and an encoder is available, <c>/FlateDecode</c> otherwise.
    /// </summary>
    internal static class StreamCompression
    {

        /// <summary>Compresses <paramref name="data"/> and reports the <c>/Filter</c> name that decodes it.</summary>
        public static byte[] Encode(PdfDocumentOptions options, byte[] data, out string filterName)
        {
            if (options.UseBrotli)
            {
                var brotli = PdfBrotliEncoder.TryEncode(data, options.FlateEncodeMode);
                if (brotli is not null)
                {
                    filterName = PdfBrotliEncoder.Name;
                    return brotli;
                }
            }

            filterName = PdfFlateEncoder.Name;
            return PdfFlateEncoder.Encode(data, options.FlateEncodeMode);
        }
    }
}
