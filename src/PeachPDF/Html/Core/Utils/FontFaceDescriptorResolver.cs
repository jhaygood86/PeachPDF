using PeachPDF.CSS;
using PeachPDF.Fonts;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Resolves an <c>@font-face</c> rule's own <c>font-weight</c>/<c>font-style</c>/<c>font-stretch</c>
    /// descriptor strings (<see cref="PeachPDF.CSS.IFontFaceRule"/>) into the override values
    /// <c>PeachPDF.Fonts.FontResolver.AddFont</c> takes - these are authoritative for how a specific
    /// registered face participates in matching, independent of what the font file's own internal tables
    /// say (see <c>DomParser.CascadeApplyStyleFonts</c>). Returns null for any descriptor this can't
    /// confidently resolve to a single concrete value (absent, or a variable-font weight/stretch *range*
    /// like <c>100 900</c>/<c>50% 200%</c> - real interpolated variable fonts are out of scope, see
    /// CLAUDE.md's accepted-gaps list), so the caller falls back to the value sniffed from the file
    /// itself instead of silently forcing a wrong/arbitrary one.
    /// </summary>
    internal static class FontFaceDescriptorResolver
    {
        /// <summary>
        /// Resolves a <c>font-weight</c> descriptor (<c>normal</c>/<c>bold</c>/a number/absent) to a
        /// concrete CSS Fonts numeric weight. A two-token range (variable-font syntax) resolves to its
        /// lower bound as a reasonable single-value approximation, since PeachPDF has no variable-font
        /// interpolation. Any other multi-token or unparseable value returns null.
        /// </summary>
        internal static int? ResolveWeight(string? weightDescriptor)
        {
            if (string.IsNullOrWhiteSpace(weightDescriptor))
                return null;

            var tokens = weightDescriptor.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);

            return tokens.Length switch
            {
                1 when int.TryParse(tokens[0], out var numeric) => numeric,
                1 when tokens[0] is Keywords.Bold => 700,
                1 when tokens[0] is Keywords.Normal => 400,
                2 when int.TryParse(tokens[0], out var lowerBound) => lowerBound,
                _ => null
            };
        }

        /// <summary>
        /// Resolves a <c>font-style</c> descriptor (<c>normal</c>/<c>italic</c>/<c>oblique</c>/
        /// <c>oblique &lt;angle&gt;</c>/absent) to whether the face should be treated as italic for
        /// matching purposes. Returns null for absent/unrecognized values.
        /// </summary>
        internal static bool? ResolveIsItalic(string? styleDescriptor)
        {
            if (string.IsNullOrWhiteSpace(styleDescriptor))
                return null;

            var firstToken = styleDescriptor.Split(' ', System.StringSplitOptions.RemoveEmptyEntries)[0];

            return firstToken switch
            {
                Keywords.Italic or Keywords.Oblique => true,
                Keywords.Normal => false,
                _ => null
            };
        }

        /// <summary>
        /// Resolves a <c>font-stretch</c> descriptor (one of the 9 CSS Fonts keywords, or absent) to the
        /// matching 1-9 numeric scale via <see cref="FontStretchResolver"/>. Percentage values/ranges
        /// (variable-font syntax) and any other unrecognized value return null rather than being silently
        /// coerced to normal.
        /// </summary>
        internal static int? ResolveStretch(string? stretchDescriptor)
        {
            if (string.IsNullOrWhiteSpace(stretchDescriptor))
                return null;

            var trimmed = stretchDescriptor.Trim();

            return trimmed switch
            {
                Keywords.UltraCondensed or Keywords.ExtraCondensed or Keywords.Condensed
                    or Keywords.SemiCondensed or Keywords.Normal or Keywords.SemiExpanded
                    or Keywords.Expanded or Keywords.ExtraExpanded or Keywords.UltraExpanded
                    => FontStretchResolver.Resolve(trimmed),
                _ => null
            };
        }
    }
}
