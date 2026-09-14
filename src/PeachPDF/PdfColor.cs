using System;
using System.Globalization;

namespace PeachPDF
{
    /// <summary>
    /// An RGBA color for the declarative document-building API (<see cref="PdfGenerator.CreateDocument"/>).
    /// Deliberately not <c>System.Drawing.Color</c> - that assembly isn't available/trim-friendly on every
    /// target this library supports (WebAssembly, NativeAOT). Internally formats to a canonical
    /// <c>rgb()</c>/<c>rgba()</c> CSS color token, resolved by PeachPDF's own existing CSS color parser
    /// at layout time - not a new, independent color implementation.
    /// </summary>
    public readonly struct PdfColor
    {
        /// <summary>The red channel, 0-255.</summary>
        public byte R { get; }

        /// <summary>The green channel, 0-255.</summary>
        public byte G { get; }

        /// <summary>The blue channel, 0-255.</summary>
        public byte B { get; }

        /// <summary>The alpha (opacity) channel, 0-255 (255 = fully opaque).</summary>
        public byte A { get; }

        private PdfColor(byte r, byte g, byte b, byte a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        /// <summary>Creates a fully opaque color from its red/green/blue channels.</summary>
        public static PdfColor FromRgb(byte r, byte g, byte b) => new(r, g, b, 255);

        /// <summary>Creates a color from its alpha/red/green/blue channels.</summary>
        public static PdfColor FromArgb(byte a, byte r, byte g, byte b) => new(r, g, b, a);

        /// <summary>
        /// Parses a hex color: <c>#RGB</c>, <c>#RGBA</c>, <c>#RRGGBB</c>, or <c>#RRGGBBAA</c> (the leading
        /// <c>#</c> is optional).
        /// </summary>
        public static PdfColor FromHex(string hex)
        {
            ArgumentNullException.ThrowIfNull(hex);
            var span = hex.AsSpan().TrimStart('#');

            static byte Hex1(char c) => (byte)(Convert.ToByte(c.ToString(), 16) * 17);
            static byte Hex2(ReadOnlySpan<char> s) => byte.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

            switch (span.Length)
            {
                case 3:
                    return new PdfColor(Hex1(span[0]), Hex1(span[1]), Hex1(span[2]), 255);
                case 4:
                    return new PdfColor(Hex1(span[0]), Hex1(span[1]), Hex1(span[2]), Hex1(span[3]));
                case 6:
                    return new PdfColor(Hex2(span[..2]), Hex2(span[2..4]), Hex2(span[4..6]), 255);
                case 8:
                    return new PdfColor(Hex2(span[..2]), Hex2(span[2..4]), Hex2(span[4..6]), Hex2(span[6..8]));
                default:
                    throw new FormatException($"'{hex}' is not a valid hex color (expected #RGB, #RGBA, #RRGGBB, or #RRGGBBAA).");
            }
        }

        /// <summary>Fully transparent black.</summary>
        public static PdfColor Transparent => new(0, 0, 0, 0);

        /// <summary>Opaque black.</summary>
        public static PdfColor Black => FromRgb(0, 0, 0);

        /// <summary>Opaque white.</summary>
        public static PdfColor White => FromRgb(255, 255, 255);

        /// <summary>Opaque red.</summary>
        public static PdfColor Red => FromRgb(255, 0, 0);

        /// <summary>Opaque green.</summary>
        public static PdfColor Green => FromRgb(0, 128, 0);

        /// <summary>Opaque blue.</summary>
        public static PdfColor Blue => FromRgb(0, 0, 255);

        /// <summary>Opaque gray.</summary>
        public static PdfColor Gray => FromRgb(128, 128, 128);

        /// <summary>
        /// Formats this color as a canonical <c>rgb()</c>/<c>rgba()</c> CSS color token - the exact text
        /// every internal <see cref="Html.Core.Dom.CssBox"/> string-typed color property already accepts
        /// and resolves via the real CSS color parser.
        /// </summary>
        internal string ToCssText() => A == 255
            ? string.Create(CultureInfo.InvariantCulture, $"rgb({R}, {G}, {B})")
            : string.Create(CultureInfo.InvariantCulture, $"rgba({R}, {G}, {B}, {A / 255.0})");

        /// <summary>Returns the canonical <c>rgb()</c>/<c>rgba()</c> CSS color token this value represents.</summary>
        public override string ToString() => ToCssText();
    }
}
