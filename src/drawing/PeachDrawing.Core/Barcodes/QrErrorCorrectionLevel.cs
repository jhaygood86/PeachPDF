// Portions derived from FreePDF (https://github.com/io7/FreePDF), MIT License,
// Copyright (c) 2001-2026 empira Software GmbH, Troisdorf (Cologne Area), Germany.
// See THIRD-PARTY-LICENSES.md in this package.

namespace PeachDrawing.Core.Barcodes
{
    /// <summary>
    /// The error correction level of a QR code (ISO/IEC 18004): how much of the symbol can be damaged or covered
    /// and still be read. A higher level makes the symbol larger for the same text.
    /// </summary>
    public enum QrErrorCorrectionLevel
    {
        /// <summary>About 7 % of the codewords can be restored.</summary>
        L,

        /// <summary>About 15 % of the codewords can be restored. The usual choice for print.</summary>
        M,

        /// <summary>About 25 % of the codewords can be restored.</summary>
        Q,

        /// <summary>About 30 % of the codewords can be restored, for example when a logo covers part of the symbol.</summary>
        H,
    }
}
