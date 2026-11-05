using PeachDrawing.Text;
using PeachBrackets.Css;

namespace PeachBrackets.Html.Utils
{
    /// <summary>
    /// The CSS generic font families (<c>serif</c>, <c>sans-serif</c>, <c>monospace</c>, <c>cursive</c>,
    /// <c>fantasy</c>, <c>math</c> and the <c>ui-*</c> families) that map to a real installed family name, paired with the engine's own name
    /// for each. Which real family each one means on the current platform is the engine's to say - see
    /// <see cref="FontSet.ResolveGeneric"/> - so that the CSS keyword grammar stays here and the platform tables
    /// do not.
    /// </summary>
    internal static class GenericFontFamilyResolver
    {
        /// <summary>
        /// Every CSS generic family this mapping covers (excludes <c>system-ui</c>, handled separately - see
        /// <see cref="DefaultFontResolver.DefaultFont"/>). <c>math</c> is in the list but is not a single-name
        /// mapping: it resolves to the first installed family of a platform's chain of math fonts.
        /// </summary>
        internal static readonly (string Keyword, GenericFamily Family)[] Generics =
        [
            (Keywords.Serif, GenericFamily.Serif),
            (Keywords.SansSerif, GenericFamily.SansSerif),
            (Keywords.Monospace, GenericFamily.Monospace),
            (Keywords.Cursive, GenericFamily.Cursive),
            (Keywords.Fantasy, GenericFamily.Fantasy),
            (Keywords.Math, GenericFamily.Math),
            (Keywords.UiSerif, GenericFamily.UiSerif),
            (Keywords.UiSansSerif, GenericFamily.UiSansSerif),
            (Keywords.UiMonospace, GenericFamily.UiMonospace),
            (Keywords.UiRounded, GenericFamily.UiRounded)
        ];

        /// <summary>
        /// The names the system user interface font goes by besides <c>system-ui</c>: the ones WebKit and Blink
        /// accept so that style sheets written for macOS and iOS keep working.
        /// </summary>
        internal static readonly string[] SystemUiAliases = ["-apple-system", "BlinkMacSystemFont"];
    }
}
