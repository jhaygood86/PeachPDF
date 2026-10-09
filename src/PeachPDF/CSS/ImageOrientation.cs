#nullable disable

using System;
using System.Collections.Generic;

namespace PeachPDF.CSS
{
    /// <summary>
    /// The computed value of <c>image-orientation</c> (CSS Images 4 §5.2): <c>from-image</c>, or an explicit
    /// rotation in whole quarter turns plus an optional horizontal flip (<c>none</c> being 0 turns, no flip).
    /// It is also the shape an Exif orientation resolves to, so a single type describes "how to turn this raster
    /// upright" whichever way it was chosen. The rotation is clockwise and applied first; the flip mirrors the
    /// rotated image horizontally, as the property specifies.
    /// </summary>
    /// <remarks>
    /// One grammar serves both layers: the CSS-OM converter validates a declaration with <see cref="TryParse(IReadOnlyList{Token})"/>
    /// and the layout/paint code parses the stored declaration text with the same method, so there is no second
    /// parser to drift (the <see cref="BackgroundPositionGrammar"/> precedent).
    /// </remarks>
    /// <param name="FromImage">True for <c>from-image</c>: the image's own Exif orientation decides.</param>
    /// <param name="QuarterTurns">Clockwise quarter turns, 0 to 3. Meaningful only when <paramref name="FromImage"/> is false.</param>
    /// <param name="Flip">Mirror horizontally after rotating. Meaningful only when <paramref name="FromImage"/> is false.</param>
    internal readonly record struct ImageOrientation(bool FromImage, int QuarterTurns, bool Flip)
    {
        private const string FromImageKeyword = "from-image";
        private const string FlipKeyword = "flip";

        /// <summary>The initial value, <c>from-image</c>.</summary>
        public static ImageOrientation FromImageValue => new(true, 0, false);

        /// <summary>No rotation and no flip (<c>none</c>).</summary>
        public static ImageOrientation Upright => new(false, 0, false);

        /// <summary>True when applying this orientation changes nothing (never true for <c>from-image</c>, which is not yet resolved).</summary>
        public bool IsIdentity => !FromImage && QuarterTurns == 0 && !Flip;

        /// <summary>True when the rotation exchanges the image's width and height.</summary>
        public bool SwapsAxes => !FromImage && (QuarterTurns & 1) == 1;

        /// <summary>
        /// The rotation and flip that turn a raster stored with Exif Orientation <paramref name="exif"/> upright
        /// (1 upright, 2 mirrored horizontally, 3 rotated 180, 4 mirrored vertically, 5 transposed, 6 rotated 90
        /// clockwise, 7 transversed, 8 rotated 270 clockwise). Anything else is upright.
        /// </summary>
        public static ImageOrientation FromExif(int exif) => exif switch
        {
            2 => new ImageOrientation(false, 0, true),
            3 => new ImageOrientation(false, 2, false),
            4 => new ImageOrientation(false, 2, true),
            5 => new ImageOrientation(false, 1, true),
            6 => new ImageOrientation(false, 1, false),
            7 => new ImageOrientation(false, 3, true),
            8 => new ImageOrientation(false, 3, false),
            _ => Upright,
        };

        /// <summary>Parses declaration text such as <c>from-image</c>, <c>none</c>, <c>90deg</c>, <c>flip</c> or <c>0.25turn flip</c>; null when invalid.</summary>
        public static ImageOrientation? TryParse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            using var pooled = Html.Core.Parse.CssValueParser.GetCssTokensPooled(text.Trim());
            List<Token> tokens = pooled;
            tokens.RemoveAll(static t => t.Type == TokenType.Whitespace);
            return TryParse(tokens);
        }

        /// <summary>
        /// <c>from-image | none | [ &lt;angle&gt; || flip ]</c> over whitespace-free tokens. The angle is rounded
        /// to the nearest quarter turn; <c>flip</c> alone is a valid <c>0deg flip</c>.
        /// </summary>
        public static ImageOrientation? TryParse(IReadOnlyList<Token> tokens)
        {
            if (tokens is null || tokens.Count is < 1 or > 2) return null;

            if (tokens.Count == 1 && tokens[0].Type == TokenType.Ident)
            {
                if (tokens[0].Data.Isi(FromImageKeyword)) return FromImageValue;
                if (tokens[0].Data.Isi(Keywords.None)) return Upright;
            }

            var turns = 0;
            var haveAngle = false;
            var flip = false;

            foreach (var token in tokens)
            {
                if (token.Type == TokenType.Ident && token.Data.Isi(FlipKeyword))
                {
                    if (flip) return null;
                    flip = true;
                }
                else if (!haveAngle && token.Type == TokenType.Dimension
                    && Angle.GetUnit(token.Unit.ToLowerInvariant()) is var unit && unit != Angle.Unit.None)
                {
                    var quarters = Math.Floor(new Angle(token.Value, unit).ToTurns() * 4.0 + 0.5);
                    if (double.IsNaN(quarters) || double.IsInfinity(quarters)) return null;

                    turns = (int)(((quarters % 4) + 4) % 4);
                    haveAngle = true;
                }
                else
                {
                    return null;
                }
            }

            return new ImageOrientation(false, turns, flip);
        }
    }
}
