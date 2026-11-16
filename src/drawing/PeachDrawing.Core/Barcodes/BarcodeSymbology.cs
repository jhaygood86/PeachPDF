namespace PeachDrawing.Core.Barcodes
{
    /// <summary>
    /// The barcode symbologies <see cref="Barcode"/> can encode.
    /// </summary>
    public enum BarcodeSymbology
    {
        /// <summary>QR code, model 2 (ISO/IEC 18004).</summary>
        QrCode,

        /// <summary>Data Matrix ECC 200 (ISO/IEC 16022).</summary>
        DataMatrix,

        /// <summary>Code 128 (ISO/IEC 15417).</summary>
        Code128,

        /// <summary>GS1-128: Code 128 carrying GS1 element strings, starting with FNC1.</summary>
        Gs1128,

        /// <summary>EAN-13 (ISO/IEC 15420).</summary>
        Ean13,

        /// <summary>EAN-8 (ISO/IEC 15420).</summary>
        Ean8,

        /// <summary>UPC-A (ISO/IEC 15420).</summary>
        UpcA,
    }

    /// <summary>
    /// How a Data Matrix symbol encodes its data.
    /// </summary>
    public enum DataMatrixEncoding
    {
        /// <summary>ASCII encodation: two digits per codeword, other ASCII one codeword each, other text as UTF-8 after ECI 26.</summary>
        Ascii,

        /// <summary>Base 256 encodation: the text's UTF-8 bytes after a length indicator.</summary>
        Base256,
    }
}
