using PeachBrackets.Css;
using PeachDrawing.Core;

namespace PeachBrackets.Html.Dom
{
    /// <summary>
    /// Forced colors mode (CSS Color Adjust 1 §3): while <see cref="UserPreferences.ForcedColors"/> is on, the colours an element resolves to are
    /// replaced by those of the system palette, unless <c>forced-color-adjust</c> says otherwise. Each <c>DerivedStyle.Actual*Color</c> getter
    /// passes its resolved colour through here, so painting reads the adjusted colour without knowing the mode exists; outside the mode every method
    /// returns its argument untouched.
    /// </summary>
    internal static class ForcedColorsAdjuster
    {
        private enum Kind
        {
            Text,
            Link,
            Button,
            Field,
            Mark,
        }

        /// <summary>The text colour of <paramref name="owner"/>.</summary>
        public static PaintColor Text(CssBox owner, PaintColor resolved)
        {
            if (Palette(owner, isTextColor: true) is not { } palette)
                return resolved;

            return KindOf(owner) switch
            {
                Kind.Link => palette.LinkText,
                Kind.Button => palette.ButtonText,
                Kind.Field => palette.FieldText,
                Kind.Mark => palette.MarkText,
                _ => palette.CanvasText,
            };
        }

        /// <summary>The background colour of <paramref name="owner"/>; a fully transparent one stays transparent.</summary>
        public static PaintColor Background(CssBox owner, PaintColor resolved)
        {
            if (resolved.A == 0 || Palette(owner, isTextColor: false) is not { } palette)
                return resolved;

            return KindOf(owner) switch
            {
                Kind.Button => palette.ButtonFace,
                Kind.Field => palette.Field,
                Kind.Mark => palette.Mark,
                _ => palette.Canvas,
            };
        }

        /// <summary>A border colour of <paramref name="owner"/>; a fully transparent one stays transparent.</summary>
        public static PaintColor Border(CssBox owner, PaintColor resolved)
        {
            if (resolved.A == 0 || Palette(owner, isTextColor: false) is not { } palette)
                return resolved;

            return KindOf(owner) is Kind.Button or Kind.Field ? palette.ButtonBorder : palette.CanvasText;
        }

        /// <summary>An outline or rule colour of <paramref name="owner"/>; a fully transparent one stays transparent.</summary>
        public static PaintColor Line(CssBox owner, PaintColor resolved)
        {
            if (resolved.A == 0 || Palette(owner, isTextColor: false) is not { } palette)
                return resolved;

            return palette.CanvasText;
        }

        /// <summary>Whether box shadows, text shadows and gradient backgrounds are dropped for <paramref name="owner"/>.</summary>
        public static bool DropsDecorations(CssBox owner) => Palette(owner, isTextColor: false) is not null;

        private static SystemColorPalette? Palette(CssBox owner, bool isTextColor)
        {
            if (owner.HtmlContainer?.Preferences is not { ForcedColors: true } preferences)
                return null;

            var mode = owner.ForcedColorAdjust.Value;
            if (mode == ForcedColorAdjustMode.None || (isTextColor && mode == ForcedColorAdjustMode.PreserveParentColor))
                return null;

            return preferences.ForcedPalette;
        }

        // What the element is decides which of the palette's colours it takes: a link in LinkText, a control in ButtonText on ButtonFace. A
        // descendant of one takes the same, since inheritance is what the forced colour replaces.
        private static Kind KindOf(CssBox owner)
        {
            for (var box = owner; box is not null; box = box.ParentBox)
            {
                if (box.HtmlTag is not { } tag)
                    continue;

                var name = tag.Name;
                if (name.Equals("a", System.StringComparison.OrdinalIgnoreCase) && tag.TryGetAttribute("href") is not null)
                    return Kind.Link;
                if (name.Equals("button", System.StringComparison.OrdinalIgnoreCase) || name.Equals("select", System.StringComparison.OrdinalIgnoreCase))
                    return Kind.Button;
                if (name.Equals("input", System.StringComparison.OrdinalIgnoreCase) || name.Equals("textarea", System.StringComparison.OrdinalIgnoreCase))
                    return Kind.Field;
                if (name.Equals("mark", System.StringComparison.OrdinalIgnoreCase))
                    return Kind.Mark;
            }

            return Kind.Text;
        }
    }
}
