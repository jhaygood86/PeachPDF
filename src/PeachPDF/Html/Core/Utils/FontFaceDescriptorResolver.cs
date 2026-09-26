using PeachDrawing.Text;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using System;
using System.Globalization;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Resolves an <c>@font-face</c> rule's own <c>font-weight</c>/<c>font-style</c>/<c>font-stretch</c>
    /// descriptor strings (<see cref="PeachPDF.CSS.IFontFaceRule"/>) into what
    /// <c>PeachDrawing.Text.FontSet.AddData</c> takes: the weights, widths and oblique angles the face covers (CSS Fonts 4
    /// section 4.4: each descriptor is a value or a range of two). These are authoritative for how a specific
    /// registered face participates in matching, independent of what the font file's own internal tables
    /// say (see <c>DomParser.CascadeApplyStyleFonts</c>). Every method returns null for a descriptor that is absent, is
    /// <c>auto</c>, or cannot be read, so the caller falls back to what the file itself says (a variable font then covers the range
    /// of its own axes) instead of silently forcing a wrong value.
    /// </summary>
    internal static class FontFaceDescriptorResolver
    {
        /// <summary>The angle of the slant that a bare <c>oblique</c> stands for (CSS Fonts 4 section 2.3).</summary>
        internal const double DefaultObliqueAngle = 14;

        /// <summary>Resolves the three descriptors of a rule together.</summary>
        internal static FontFaceDescriptors Resolve(string? weightDescriptor, string? styleDescriptor, string? stretchDescriptor)
        {
            var (isItalic, oblique) = ResolveStyle(styleDescriptor);
            return new FontFaceDescriptors(ResolveWeight(weightDescriptor), isItalic, ResolveStretch(stretchDescriptor), oblique);
        }

        /// <summary>
        /// Resolves a <c>font-weight</c> descriptor (<c>normal</c>/<c>bold</c>/a number from 1 to 1000, or two of them for a range, or absent)
        /// to the weights the face covers. <c>auto</c>, <c>bolder</c>/<c>lighter</c> (which a descriptor cannot take), an out-of-range
        /// number and anything else return null.
        /// </summary>
        internal static AxisRange? ResolveWeight(string? weightDescriptor)
        {
            if (string.IsNullOrWhiteSpace(weightDescriptor))
                return null;

            return ResolveRange(weightDescriptor, TryParseWeight);
        }

        /// <summary>
        /// Resolves a <c>font-style</c> descriptor (<c>normal</c>/<c>italic</c>/<c>oblique</c>/<c>oblique &lt;angle&gt;{1,2}</c>/absent) to
        /// whether the face is italic or oblique, and the range of oblique angles it covers. A bare <c>oblique</c> is 14 degrees.
        /// Both are null for an absent, <c>auto</c> or unrecognized value.
        /// </summary>
        internal static (bool? IsItalic, AxisRange? Oblique) ResolveStyle(string? styleDescriptor)
        {
            if (string.IsNullOrWhiteSpace(styleDescriptor))
                return (null, null);

            var tokens = styleDescriptor.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (tokens[0].ToLowerInvariant())
            {
                case Keywords.Normal when tokens.Length == 1:
                    return (false, null);
                case Keywords.Italic when tokens.Length == 1:
                    return (true, null);
                case Keywords.Oblique when tokens.Length == 1:
                    return (true, new AxisRange(DefaultObliqueAngle));
                case Keywords.Oblique when tokens.Length is 2 or 3:
                    var angles = ResolveRange(string.Join(' ', tokens[1..]), TryParseAngle);
                    return angles is null ? (null, null) : (true, angles);
                default:
                    return (null, null);
            }
        }

        /// <summary>
        /// Resolves a <c>font-stretch</c> descriptor (one of the 9 CSS Fonts keywords, a non-negative percentage, or two of them for a
        /// range, or absent) to the widths the face covers, as percentages of the normal width (see <see cref="FontStretchResolver"/>).
        /// <c>auto</c> and anything else return null rather than being silently coerced to normal.
        /// </summary>
        internal static AxisRange? ResolveStretch(string? stretchDescriptor)
        {
            if (string.IsNullOrWhiteSpace(stretchDescriptor))
                return null;

            return ResolveRange(stretchDescriptor, static (string token, out double percent) => FontStretchResolver.TryResolve(token, out percent));
        }

        private delegate bool TryParseToken(string token, out double value);

        /// <summary>Reads one value or two, separated by whitespace, as a range.</summary>
        private static AxisRange? ResolveRange(string descriptor, TryParseToken tryParse)
        {
            var tokens = descriptor.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length is < 1 or > 2 || !tryParse(tokens[0], out var first))
                return null;

            if (tokens.Length == 1)
                return new AxisRange(first);

            return tryParse(tokens[1], out var second) ? new AxisRange(first, second) : null;
        }

        private static bool TryParseWeight(string token, out double weight)
        {
            switch (token.ToLowerInvariant())
            {
                case Keywords.Normal:
                    weight = 400;
                    return true;
                case Keywords.Bold:
                    weight = 700;
                    return true;
                default:
                    return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out weight)
                           && weight is >= 1 and <= 1000;
            }
        }

        private static bool TryParseAngle(string token, out double degrees)
        {
            if (PeachPDF.CSS.Angle.TryParse(token.ToLowerInvariant(), out var angle))
            {
                // Not through ToRadian(), which rounds to a float: a declared 10deg has to stay 10 for a range to compare exactly.
                degrees = angle.Type switch
                {
                    PeachPDF.CSS.Angle.Unit.Grad => angle.Value * 0.9,
                    PeachPDF.CSS.Angle.Unit.Turn => angle.Value * 360.0,
                    PeachPDF.CSS.Angle.Unit.Rad => angle.Value * 180.0 / Math.PI,
                    _ => angle.Value
                };
                return degrees is >= -90 and <= 90;
            }

            degrees = 0;
            return false;
        }
    }
}
