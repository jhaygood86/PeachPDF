using System;
using System.Collections.Generic;

namespace PeachDrawing.Core.Barcodes
{
    /// <summary>
    /// An encoded barcode: QR code, Data Matrix, Code 128, GS1-128, EAN-13, EAN-8 or UPC-A. Create one with a
    /// <c>Create</c> method, then draw it with
    /// <see cref="BarcodeDrawing.DrawBarcode(Canvas, Barcode, PaintPoint, double, PaintColor, PaintColor?, Func{double, Font}?)"/>
    /// or add it to a path with <see cref="BarcodeDrawing.AddBarcode(GraphicsPath, Barcode, PaintPoint, double)"/>.
    /// It is drawn as vector shapes: the dark modules become rectangles of one path.
    /// </summary>
    public sealed class Barcode
    {
        readonly bool[,]? _matrix;
        readonly bool[]? _bars;

        Barcode(BarcodeSymbology symbology, string text, string displayText, BarcodeOptions options, bool[,]? matrix, bool[]? bars)
        {
            Symbology = symbology;
            Text = text;
            DisplayText = displayText;
            Options = options;
            _matrix = matrix;
            _bars = bars;
        }

        /// <summary>The symbology.</summary>
        public BarcodeSymbology Symbology { get; }

        /// <summary>The text that was encoded: with the check digit for EAN and UPC, and with <see cref="Code128.Fnc1"/> markers for GS1-128.</summary>
        public string Text { get; }

        /// <summary>The text for a reader of the barcode: the digits of an EAN or UPC code, the text of a Code 128, the element strings of a GS1-128.</summary>
        public string DisplayText { get; }

        /// <summary>The options the barcode was created with.</summary>
        public BarcodeOptions Options { get; }

        /// <summary>Whether the symbol is a row of bars (Code 128, GS1-128, EAN, UPC) rather than a grid of modules.</summary>
        public bool IsLinear => _bars != null;

        /// <summary>The number of module columns of the symbol, without its light margin. For a linear code, the number of bar and space modules.</summary>
        public int Columns => _matrix?.GetLength(1) ?? _bars!.Length;

        /// <summary>The number of module rows of the symbol, without its light margin; 1 for a linear code.</summary>
        public int Rows => _matrix?.GetLength(0) ?? 1;

        /// <summary>Whether the module is dark. A linear code has the one row 0.</summary>
        /// <param name="row">The row, from the top.</param>
        /// <param name="column">The column, from the left.</param>
        /// <returns><c>true</c> for a dark module.</returns>
        public bool IsDark(int row, int column)
            => _matrix != null ? _matrix[row, column] : row == 0 ? _bars![column] : throw new ArgumentOutOfRangeException(nameof(row));

        /// <summary>Creates a QR code.</summary>
        /// <param name="text">The text, which must not be empty.</param>
        /// <param name="options">The options, or <c>null</c> for the defaults.</param>
        /// <returns>The barcode.</returns>
        /// <exception cref="ArgumentException">The text is empty or does not fit in a QR code.</exception>
        public static Barcode CreateQrCode(string text, BarcodeOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(text);
            options ??= new BarcodeOptions();
            if (text.Length == 0)
                throw new ArgumentException("A QR code needs at least one character; readers do not report empty codes.", nameof(text));
            if (options.QrVersion is < 0 or > 40)
                throw new ArgumentOutOfRangeException(nameof(options), "The QR code version must be 0 (the smallest that fits) or 1 to 40.");
            var matrix = QrEncoder.Encode(text, options.ErrorCorrection, options.QrVersion, options.Utf8Eci);
            return new Barcode(BarcodeSymbology.QrCode, text, text, options, matrix, null);
        }

        /// <summary>Creates a Data Matrix ECC 200 symbol.</summary>
        /// <param name="text">The text. <see cref="Code128.Fnc1"/> at the start makes it GS1 Data Matrix.</param>
        /// <param name="options">The options, or <c>null</c> for the defaults.</param>
        /// <returns>The barcode.</returns>
        /// <exception cref="ArgumentException">The text does not fit in the symbol size.</exception>
        public static Barcode CreateDataMatrix(string text, BarcodeOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(text);
            options ??= new BarcodeOptions();
            string encoding = new(options.DataMatrixEncoding == DataMatrixEncoding.Base256 ? 'b' : 'a', text.Length);
            var matrix = DataMatrixEncoder.Encode(text, encoding, options.DataMatrixRows, options.DataMatrixColumns);
            return new Barcode(BarcodeSymbology.DataMatrix, text, text.Replace(Code128.Fnc1.ToString(), ""), options, matrix, null);
        }

        /// <summary>Creates a Code 128 barcode.</summary>
        /// <param name="text">ASCII text, optionally with <see cref="Code128.Fnc1"/>.</param>
        /// <param name="options">The options, or <c>null</c> for the defaults.</param>
        /// <returns>The barcode.</returns>
        /// <exception cref="ArgumentException">The text is empty or has characters outside ASCII.</exception>
        public static Barcode CreateCode128(string text, BarcodeOptions? options = null)
        {
            Code128.Check(text);
            return new Barcode(BarcodeSymbology.Code128, text, Code128.HumanReadableText(text), options ?? new BarcodeOptions(), null, Code128.GetModules(text));
        }

        /// <summary>Creates a GS1-128 barcode from element strings such as <c>(01)09501101530003(10)AB-123</c>.</summary>
        /// <param name="elements">The element strings, each application identifier in parentheses.</param>
        /// <param name="options">The options, or <c>null</c> for the defaults.</param>
        /// <returns>The barcode.</returns>
        /// <exception cref="ArgumentException">The element strings are malformed.</exception>
        public static Barcode CreateGs1128(string elements, BarcodeOptions? options = null)
        {
            string text = Gs1Elements.ToCode128Text(elements);
            return new Barcode(BarcodeSymbology.Gs1128, text, Gs1Elements.ToHumanReadable(elements), options ?? new BarcodeOptions(), null, Code128.GetModules(text));
        }

        /// <summary>Creates an EAN-13 barcode.</summary>
        /// <param name="digits">12 digits, or 13 with the check digit.</param>
        /// <param name="options">The options, or <c>null</c> for the defaults.</param>
        /// <returns>The barcode.</returns>
        /// <exception cref="ArgumentException">The digits are the wrong count, not digits, or have a wrong check digit.</exception>
        public static Barcode CreateEan13(string digits, BarcodeOptions? options = null)
            => CreateEanUpc(BarcodeSymbology.Ean13, EanUpcSymbology.Kind.Ean13, digits, options);

        /// <summary>Creates an EAN-8 barcode.</summary>
        /// <param name="digits">7 digits, or 8 with the check digit.</param>
        /// <param name="options">The options, or <c>null</c> for the defaults.</param>
        /// <returns>The barcode.</returns>
        /// <exception cref="ArgumentException">The digits are the wrong count, not digits, or have a wrong check digit.</exception>
        public static Barcode CreateEan8(string digits, BarcodeOptions? options = null)
            => CreateEanUpc(BarcodeSymbology.Ean8, EanUpcSymbology.Kind.Ean8, digits, options);

        /// <summary>Creates a UPC-A barcode.</summary>
        /// <param name="digits">11 digits, or 12 with the check digit.</param>
        /// <param name="options">The options, or <c>null</c> for the defaults.</param>
        /// <returns>The barcode.</returns>
        /// <exception cref="ArgumentException">The digits are the wrong count, not digits, or have a wrong check digit.</exception>
        public static Barcode CreateUpcA(string digits, BarcodeOptions? options = null)
            => CreateEanUpc(BarcodeSymbology.UpcA, EanUpcSymbology.Kind.UpcA, digits, options);

        static Barcode CreateEanUpc(BarcodeSymbology symbology, EanUpcSymbology.Kind kind, string digits, BarcodeOptions? options)
        {
            EanUpcSymbology.Check(digits, kind);
            string code = EanUpcSymbology.Complete(digits, kind);
            return new Barcode(symbology, code, code, options ?? new BarcodeOptions(), null, EanUpcSymbology.Modules(code, kind));
        }

        /// <summary>
        /// Measures the barcode out in modules: its dark rectangles, light margin and text, to scale and position
        /// with the drawing methods or by hand.
        /// </summary>
        /// <returns>The layout.</returns>
        /// <exception cref="InvalidOperationException">The options hold a negative or non-finite <see cref="BarcodeOptions.QuietZone"/>.</exception>
        public BarcodeLayout CreateLayout()
        {
            if (Options.QuietZone is double quiet && !(quiet >= 0 && double.IsFinite(quiet)))
                throw new InvalidOperationException("The quiet zone must be a finite number of modules, not negative.");
            return _matrix != null ? MatrixLayout() : LinearLayout();
        }

        BarcodeLayout MatrixLayout()
        {
            var matrix = _matrix!;
            double quiet = Options.QuietZone ?? (Symbology == BarcodeSymbology.QrCode ? 4 : 1);
            int rows = matrix.GetLength(0), columns = matrix.GetLength(1);
            var bars = new List<Rect>();
            var open = new Dictionary<(int Start, int Length), int>(); // runs of the previous row, to the index in bars
            for (int r = 0; r < rows; r++)
            {
                var next = new Dictionary<(int Start, int Length), int>();
                int c = 0;
                while (c < columns)
                {
                    if (!matrix[r, c])
                    {
                        c++;
                        continue;
                    }
                    int start = c;
                    while (c < columns && matrix[r, c])
                        c++;
                    var key = (start, c - start);
                    if (open.TryGetValue(key, out int index))
                    {
                        var rect = bars[index];
                        bars[index] = new Rect(rect.X, rect.Y, rect.Width, rect.Height + 1);
                        next[key] = index;
                    }
                    else
                    {
                        bars.Add(new Rect(quiet + start, quiet + r, c - start, 1));
                        next[key] = bars.Count - 1;
                    }
                }
                open = next;
            }
            return new BarcodeLayout(columns + 2 * quiet, rows + 2 * quiet, new Rect(quiet, quiet, columns, rows), bars, []);
        }

        BarcodeLayout LinearLayout()
        {
            var modules = _bars!;
            bool ean = Symbology is BarcodeSymbology.Ean13 or BarcodeSymbology.Ean8 or BarcodeSymbology.UpcA;
            var kind = Symbology switch
            {
                BarcodeSymbology.Ean13 => EanUpcSymbology.Kind.Ean13,
                BarcodeSymbology.Ean8 => EanUpcSymbology.Kind.Ean8,
                _ => EanUpcSymbology.Kind.UpcA,
            };

            double left, right;
            if (ean)
                (left, right) = EanUpcSymbology.QuietZones(kind);
            else
                left = right = 10;
            if (Options.QuietZone is double quiet)
                left = right = quiet;
            // EAN-13 and UPC-A print a digit beside the bars, 8 modules to the left; it needs that much margin to stay inside the symbol.
            if (Options.ShowText && Symbology is BarcodeSymbology.Ean13 or BarcodeSymbology.UpcA)
                left = Math.Max(left, 8);

            double barHeight = Options.BarHeight > 0 ? Options.BarHeight : ean ? 58 : 50;
            // EAN and UPC digits are 11 modules high and the guard bars reach 5 modules into their line; Code 128 text is 10.
            double textBand = !Options.ShowText ? 0 : ean ? 11 : 12;
            double longHeight = ean && Options.ShowText ? barHeight + 5 : barHeight;
            double HeightAt(int index) => ean && EanUpcSymbology.IsLong(index, kind) ? longHeight : barHeight;

            var bars = new List<Rect>();
            int i = 0;
            while (i < modules.Length)
            {
                if (!modules[i])
                {
                    i++;
                    continue;
                }
                int start = i;
                double height = HeightAt(i);
                i++;
                while (i < modules.Length && modules[i] && HeightAt(i) == height)
                    i++;
                bars.Add(new Rect(left + start, 0, i - start, height));
            }

            var texts = new List<BarcodeText>();
            if (Options.ShowText)
            {
                if (ean)
                    AddDigits(texts, kind, Text, left, barHeight);
                else
                    texts.Add(new BarcodeText(DisplayText, new Rect(left, barHeight + 1, modules.Length, 10), 10));
            }
            return new BarcodeLayout(left + modules.Length + right, barHeight + textBand,
                new Rect(left, 0, modules.Length, barHeight + textBand), bars, texts);
        }

        // Digit slots are 7 modules wide (ISO/IEC 15420); EAN-13 and UPC-A show their outer digits beside the bars.
        static void AddDigits(List<BarcodeText> texts, EanUpcSymbology.Kind kind, string code, double barsLeft, double top)
        {
            void Digit(char digit, double slot, double size)
                => texts.Add(new BarcodeText(digit.ToString(), new Rect(barsLeft + slot, top, 7, 11), size));

            switch (kind)
            {
                case EanUpcSymbology.Kind.Ean13:
                    Digit(code[0], -8, 11);
                    for (int i = 0; i < 6; i++)
                        Digit(code[1 + i], 3 + 7 * i, 11);
                    for (int i = 0; i < 6; i++)
                        Digit(code[7 + i], 50 + 7 * i, 11);
                    break;

                case EanUpcSymbology.Kind.Ean8:
                    for (int i = 0; i < 4; i++)
                        Digit(code[i], 3 + 7 * i, 11);
                    for (int i = 0; i < 4; i++)
                        Digit(code[4 + i], 36 + 7 * i, 11);
                    break;

                default:
                    Digit(code[0], -8, 8);
                    for (int i = 1; i < 6; i++)
                        Digit(code[i], 3 + 7 * i, 11);
                    for (int i = 0; i < 5; i++)
                        Digit(code[6 + i], 50 + 7 * i, 11);
                    Digit(code[11], 96, 8);
                    break;
            }
        }
    }
}
