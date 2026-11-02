using PeachBrackets.Css;
using PeachDrawing.Core;
using System;

namespace PeachBrackets.Html.Dom
{
    internal partial class CssBox
    {
        /// <summary>
        /// What <c>user-select</c> comes to for this box (CSS Basic User Interface 4 section 6.2): a declared value is itself (<c>contain</c> is
        /// <c>text</c> for a reader that has no editing host), and <c>auto</c> is <c>none</c> or <c>all</c> when the parent's is, and <c>text</c>
        /// otherwise.
        /// </summary>
        public UserSelect EffectiveUserSelect
        {
            get
            {
                var declared = UserSelect.Value;
                if (declared == Css.UserSelect.Contain)
                    return Css.UserSelect.Text;

                if (declared != Css.UserSelect.Auto)
                    return declared;

                var parent = ParentBox?.EffectiveUserSelect ?? Css.UserSelect.Text;
                return parent is Css.UserSelect.None or Css.UserSelect.All ? parent : Css.UserSelect.Text;
            }
        }

        /// <summary>The colour of the text caret: <c>auto</c> is the text colour.</summary>
        public PaintColor ActualCaretColor =>
            IsOwnColor(CaretColor) ? ActualColor : GetActualColor(CaretColor);

        /// <summary>The accent colour for form controls, or <see langword="null"/> for <c>auto</c> (the built-in colours).</summary>
        public PaintColor? ActualAccentColor =>
            IsAutoColor(AccentColor) ? null : IsOwnColor(AccentColor) ? ActualColor : GetActualColor(AccentColor);

        // auto, and currentcolor, are the box's own text color (currentcolor on the box that declared it, which is not inherited as a keyword).
        private static bool IsOwnColor(string? value) =>
            IsAutoColor(value) || string.Equals(value!.Trim(), "currentcolor", StringComparison.OrdinalIgnoreCase);

        private static bool IsAutoColor(string? value) =>
            string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "auto", StringComparison.OrdinalIgnoreCase);
    }
}
