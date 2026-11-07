using System;
using System.Collections.Frozen;

namespace PeachBrackets.Css
{
    /// <summary>
    /// The colour scheme the colour being read belongs to (CSS Color Adjust 1 section 2): the element's <em>used</em> <c>color-scheme</c>
    /// decides which arm <c>light-dark()</c> takes and which palette the system colour keywords (<c>Canvas</c>, <c>CanvasText</c>...) name.
    /// A colour is parsed from text that knows nothing of its element, so the element opens a scope around the read; with no scope
    /// open the scheme is light.
    /// </summary>
    internal static class ColorSchemeScope
    {
        [ThreadStatic]
        private static bool s_dark;

        /// <summary>Whether colours read now belong to a dark colour scheme.</summary>
        public static bool IsDark => s_dark;

        /// <summary>Opens a scope: until it is disposed, colours read on this thread belong to the dark scheme when <paramref name="dark"/>.</summary>
        public static Restore Enter(bool dark)
        {
            var previous = s_dark;
            s_dark = dark;
            return new Restore(previous);
        }

        /// <summary>Restores the scheme that was in force when the scope was opened.</summary>
        public readonly struct Restore(bool previous) : IDisposable
        {
            public void Dispose() => s_dark = previous;
        }

        // The dark values of the system colours the UA gives a dark page (the palette Chromium and Firefox use under color-scheme: dark).
        private static readonly FrozenDictionary<string, Color> DarkSystemColors =
            new System.Collections.Generic.Dictionary<string, Color>
            {
                {"canvas", new Color(18, 18, 18)},
                {"canvastext", new Color(255, 255, 255)},
                {"window", new Color(18, 18, 18)},
                {"windowtext", new Color(255, 255, 255)},
                {"linktext", new Color(158, 158, 255)},
                {"visitedtext", new Color(208, 173, 240)},
                {"activetext", new Color(255, 158, 158)},
                {"buttonface", new Color(107, 107, 107)},
                {"buttontext", new Color(255, 255, 255)},
                {"buttonborder", new Color(107, 107, 107)},
                {"field", new Color(59, 59, 59)},
                {"fieldtext", new Color(255, 255, 255)},
                {"mark", new Color(102, 102, 0)},
                {"marktext", new Color(255, 255, 255)},
                {"graytext", new Color(169, 169, 169)},
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        /// <summary>The system colour keywords whose value differs between the schemes.</summary>
        public static System.Collections.Generic.IEnumerable<string> SchemeDependentNames => DarkSystemColors.Keys;

        /// <summary>The dark scheme's value of a system colour keyword; false when <paramref name="name"/> is not one the dark scheme changes.</summary>
        public static bool TryGetDarkSystemColor(string name, out Color color) => DarkSystemColors.TryGetValue(name, out color);

        /// <summary>The dark scheme's <c>CanvasText</c>: the default text colour of a dark page.</summary>
        public static Color DarkCanvasText => new(255, 255, 255);

        /// <summary>The dark scheme's <c>Canvas</c>: the background a dark page is drawn on.</summary>
        public static Color DarkCanvas => new(18, 18, 18);
    }
}
