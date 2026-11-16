namespace PeachDrawing.Core.Barcodes
{
    /// <summary>
    /// How a <see cref="Barcode"/> is laid out. Distances are in modules, the width of the narrowest bar or the side
    /// of one square module; the module size itself is chosen when the barcode is drawn.
    /// </summary>
    public sealed class BarcodeOptions
    {
        /// <summary>
        /// The light margin around the symbol, in modules, or <c>null</c> for the symbology's own (4 for QR code, 1 for
        /// Data Matrix, 10 left and right for Code 128, 11/7 for EAN-13, 7 for EAN-8, 9 for UPC-A). Linear codes have
        /// no margin above and below.
        /// </summary>
        public double? QuietZone { get; set; }

        /// <summary>
        /// The error correction level of a QR code. The default is <see cref="QrErrorCorrectionLevel.M"/>.
        /// </summary>
        public QrErrorCorrectionLevel ErrorCorrection { get; set; } = QrErrorCorrectionLevel.M;

        /// <summary>
        /// The QR code version, 1 to 40, or 0 (the default) for the smallest that holds the text.
        /// </summary>
        public int QrVersion { get; set; }

        /// <summary>
        /// Whether a QR code with non-ASCII text announces UTF-8 with an ECI designator. The default is
        /// <c>true</c>.
        /// </summary>
        public bool Utf8Eci { get; set; } = true;

        /// <summary>
        /// How a Data Matrix encodes its data. The default is <see cref="DataMatrixEncoding.Ascii"/>.
        /// </summary>
        public DataMatrixEncoding DataMatrixEncoding { get; set; }

        /// <summary>
        /// The Data Matrix size in rows, or 0 (the default) for the smallest square symbol that holds the text. Give
        /// both <see cref="DataMatrixRows"/> and <see cref="DataMatrixColumns"/> for a fixed or rectangular size.
        /// </summary>
        public int DataMatrixRows { get; set; }

        /// <summary>
        /// The Data Matrix size in columns, or 0 (the default) for the smallest square symbol that holds the text.
        /// </summary>
        public int DataMatrixColumns { get; set; }

        /// <summary>
        /// The height of the bars of a linear code, in modules, or 0 (the default) for the symbology's own (50 for
        /// Code 128, 58 for EAN and UPC).
        /// </summary>
        public double BarHeight { get; set; }

        /// <summary>
        /// Whether a linear code shows its human-readable text under the bars. The default is <c>true</c>.
        /// </summary>
        public bool ShowText { get; set; } = true;
    }
}
