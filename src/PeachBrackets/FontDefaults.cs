using System.Collections.Generic;
using System.Linq;
using PeachDrawing.Text;
using PeachBrackets.Html.Utils;

namespace PeachBrackets
{
    /// <summary>
    /// The font defaults the HTML engine falls back to, and the helpers a host's <c>RenderContext</c> needs to honour CSS font
    /// features: the platform's default family, the CSS generic family keywords and <c>font-variation-settings</c>.
    /// </summary>
    public static class FontDefaults
    {
        /// <summary>The family used when a document names none, or none that is installed or registered.</summary>
        public static string DefaultFamily => DefaultFontResolver.DefaultFont;

        /// <summary>The size, in points, of the root font when a document does not set one (the CSS <c>medium</c> size).</summary>
        public static double DefaultSizePoints => DefaultFontResolver.FontSize;

        /// <summary>
        /// The CSS generic font family keywords (<c>serif</c>, <c>sans-serif</c>, <c>monospace</c>, <c>cursive</c>,
        /// <c>fantasy</c>, <c>math</c>) with the engine's generic family each one stands for. <c>system-ui</c> is not among them.
        /// </summary>
        public static IReadOnlyList<GenericFontKeyword> GenericFamilies { get; } =
            [.. GenericFontFamilyResolver.Generics.Select(g => new GenericFontKeyword(g.Keyword, g.Family))];

        /// <summary>
        /// Turns an encoded <c>font-variation-settings</c> value, as the engine hands it to a
        /// <c>RenderContext</c>, into the axis settings to request from a font set.
        /// </summary>
        /// <param name="encoded">The encoded settings, or <see langword="null"/> for none.</param>
        /// <param name="sizeInPixels">The font size in CSS pixels, used for the automatic optical size.</param>
        /// <returns>The axis settings to apply, optical size first.</returns>
        public static IReadOnlyList<AxisSetting> ToAxisSettings(string? encoded, double sizeInPixels) =>
            FontVariationSettingsResolver.ToAxes(encoded, sizeInPixels);
    }

    /// <summary>A CSS generic font family keyword and the engine's generic family it stands for.</summary>
    /// <param name="Keyword">The CSS keyword, such as <c>serif</c>.</param>
    /// <param name="Family">The engine's generic family.</param>
    public readonly record struct GenericFontKeyword(string Keyword, GenericFamily Family);
}
