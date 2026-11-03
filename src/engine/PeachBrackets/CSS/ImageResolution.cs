#nullable disable

using System;
using System.Collections.Generic;

namespace PeachBrackets.Css
{
    /// <summary>
    /// The computed value of <c>image-resolution</c> (CSS Images 4 §6.1): <c>[ from-image || &lt;resolution&gt; ] &amp;&amp; snap?</c>. One grammar
    /// serves both layers, like <see cref="ImageOrientation"/>: the CSS-OM converter validates a declaration with
    /// <see cref="TryParse(IReadOnlyList{Token})"/> and layout parses the stored text with the same method.
    /// </summary>
    /// <param name="FromImage">True when the density recorded in the image file is used, falling back to <paramref name="Dppx"/>.</param>
    /// <param name="Dppx">The explicit resolution in dots per CSS pixel, or null when only <c>from-image</c> was given.</param>
    /// <param name="Snap">Round the resolution so one image pixel covers a whole number of device pixels.</param>
    internal readonly record struct ImageResolution(bool FromImage, double? Dppx, bool Snap)
    {
        private const string FromImageKeyword = "from-image";
        private const string SnapKeyword = "snap";

        /// <summary>The initial value, <c>1dppx</c>: a raster is 96 dpi.</summary>
        public static ImageResolution Initial => new(false, 1, false);

        /// <summary>Parses declaration text such as <c>from-image</c>, <c>300dpi</c> or <c>from-image 300dpi snap</c>; null when invalid.</summary>
        public static ImageResolution? TryParse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            using var pooled = Html.Parse.CssValueParser.GetCssTokensPooled(text.Trim());
            List<Token> tokens = pooled;
            tokens.RemoveAll(static t => t.Type == TokenType.Whitespace);
            return TryParse(tokens);
        }

        /// <summary>The grammar over whitespace-free tokens: a positive <c>&lt;resolution&gt;</c> (<c>dpi</c>, <c>dpcm</c>, <c>dppx</c>, <c>x</c>), <c>from-image</c> or both, then an optional <c>snap</c>, in any order but at most once each.</summary>
        public static ImageResolution? TryParse(IReadOnlyList<Token> tokens)
        {
            if (tokens is null || tokens.Count is < 1 or > 3) return null;

            var fromImage = false;
            var snap = false;
            double? dppx = null;

            foreach (var token in tokens)
            {
                if (token.Type == TokenType.Ident && token.Data.Isi(FromImageKeyword))
                {
                    if (fromImage) return null;
                    fromImage = true;
                }
                else if (token.Type == TokenType.Ident && token.Data.Isi(SnapKeyword))
                {
                    if (snap) return null;
                    snap = true;
                }
                else if (token.Type == TokenType.Dimension && dppx is null && ToDppx(token.Value, token.Unit) is { } value and > 0)
                {
                    dppx = value;
                }
                else
                {
                    return null;
                }
            }

            // snap only modifies a resolution or from-image, it is not a value on its own.
            if (!fromImage && dppx is null) return null;

            return new ImageResolution(fromImage, dppx, snap);
        }

        private static double? ToDppx(float value, string unit) => unit.ToLowerInvariant() switch
        {
            "dppx" or "x" => value,
            "dpi" => value / 96.0,
            "dpcm" => value * 2.54 / 96.0,
            _ => null,
        };

        /// <summary>
        /// The resolution in dots per CSS pixel to use for an image that records <paramref name="fileDpi"/> (null when it records none): the file's own
        /// with <c>from-image</c>, else the declared one, else <c>1dppx</c>, then <c>snap</c>-rounded.
        /// </summary>
        public double Resolve(double? fileDpi)
        {
            var dppx = FromImage && fileDpi is > 0 ? fileDpi.Value / 96.0 : Dppx ?? 1.0;
            return Snap ? Snapped(dppx) : dppx;
        }

        /// <summary>
        /// The nearest resolution that maps one image pixel to a whole number of device pixels (css-images-4 §6.1): a whole number of
        /// image pixels per device pixel at or above 1dppx (300dpi, 3.125dppx, becomes 3dppx), a whole number of device pixels per image
        /// pixel below it (72dpi, 0.75dppx, becomes 1dppx).
        /// </summary>
        internal static double Snapped(double dppx) =>
            dppx >= 1 ? Math.Round(dppx) : 1.0 / Math.Max(1.0, Math.Round(1.0 / dppx));
    }
}
