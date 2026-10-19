#nullable enable

using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Html.Core.Parse
{
    /// <summary>
    /// The pre-standard "flexbox 2009" model (<c>display: -webkit-box</c> and <c>-webkit-box-orient</c>/<c>-direction</c>/
    /// <c>-pack</c>/<c>-align</c>, plus the <c>-moz-</c> spellings), mapped onto the standard model rather than
    /// implemented a second time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The container properties are translated as they are cascaded (<see cref="TryApply"/>): <c>box-pack</c> and
    /// <c>box-align</c> onto <c>justify-content</c> and <c>align-items</c>, <c>box-orient</c> and <c>box-direction</c>
    /// onto the two halves of <c>flex-direction</c> (each composes onto the current value, so their relative order
    /// does not matter). <c>display: -webkit-box</c> itself is only known to be a horizontal or a vertical box once
    /// the whole cascade is done, so it is resolved afterwards (<see cref="Resolve"/>): a horizontal box is a flex
    /// container, and a <em>vertical</em> one - the container the <c>-webkit-line-clamp</c> idiom lives on - is a
    /// plain block, which is exactly how a vertical box stacks its children (and keeps a text child a directly
    /// clampable block container instead of an anonymous flex item).
    /// </para>
    /// <para>
    /// Known differences from a real 2009 box: a vertical box's <c>box-flex</c> children do not stretch to fill the
    /// height, <c>box-direction: reverse</c> on a vertical box is ignored, and (like the standard <c>line-clamp</c>)
    /// clamping counts the container's own lines, not those of block children.
    /// </para>
    /// </remarks>
    internal static class LegacyBox
    {
        /// <summary>
        /// Applies <paramref name="propertyName"/> if it is a legacy box container property; returns <see langword="false"/>
        /// for any other property so the caller proceeds normally. CSS-wide keywords are ignored - the legacy
        /// properties have no computed value of their own to inherit or reset.
        /// </summary>
        internal static bool TryApply(CssValueParser valueParser, CssBox box, string propertyName, string value)
        {
            var baseName = LegacyBoxProperty.BaseNameOf(propertyName);
            if (baseName is null) return false;

            if (CssGlobalKeywords.TryParse(value, out _)) return true;

            var keyword = value.Trim().ToLowerInvariant();

            switch (baseName)
            {
                case LegacyBoxProperty.Pack:
                    Set(valueParser, box, PropertyNames.JustifyContent, keyword switch
                    {
                        "start" => "flex-start",
                        "end" => "flex-end",
                        "justify" => "space-between",
                        _ => keyword,
                    });
                    break;

                case LegacyBoxProperty.Align:
                    Set(valueParser, box, PropertyNames.AlignItems, keyword switch
                    {
                        "start" => "flex-start",
                        "end" => "flex-end",
                        _ => keyword,
                    });
                    break;

                case LegacyBoxProperty.Orient:
                {
                    var reversed = IsReversed(CssUtils.GetPropertyValue(box, PropertyNames.FlexDirection) ?? "");
                    var vertical = keyword is "vertical" or "block-axis";
                    Set(valueParser, box, PropertyNames.FlexDirection, (vertical ? "column" : "row") + (reversed ? "-reverse" : ""));
                    break;
                }

                case LegacyBoxProperty.Direction:
                {
                    var current = CssUtils.GetPropertyValue(box, PropertyNames.FlexDirection) ?? "";
                    var axis = current.StartsWith("column", System.StringComparison.OrdinalIgnoreCase) ? "column" : "row";
                    Set(valueParser, box, PropertyNames.FlexDirection, axis + (keyword == "reverse" ? "-reverse" : ""));
                    break;
                }
            }

            return true;
        }

        /// <summary>Resolves a <c>display: -webkit-box</c>/<c>-moz-box</c> (or <c>-inline-box</c>) box to the standard display it stands for.</summary>
        internal static void Resolve(CssBox box)
        {
            var text = box.Display.ToString();
            var inline = text is Keywords.WebkitInlineBox or Keywords.MozInlineBox;
            if (!inline && text is not (Keywords.WebkitBox or Keywords.MozBox)) return;

            var vertical = box.FlexDirection.Value is FlexDirection.Column or FlexDirection.ColumnReverse;

            box.Display = (vertical, inline) switch
            {
                (true, false) => CssProperty<DisplayMode>.FromValue(Keywords.Block, DisplayMode.Block),
                (true, true) => CssProperty<DisplayMode>.FromValue(Keywords.InlineBlock, DisplayMode.InlineBlock),
                (false, false) => CssProperty<DisplayMode>.FromValue(Keywords.Flex, DisplayMode.Flex),
                (false, true) => CssProperty<DisplayMode>.FromValue(Keywords.InlineFlex, DisplayMode.InlineFlex),
            };
        }

        private static bool IsReversed(string flexDirection) =>
            flexDirection.EndsWith("-reverse", System.StringComparison.OrdinalIgnoreCase);

        private static void Set(CssValueParser valueParser, CssBox box, string standardName, string value) =>
            CssUtils.SetPropertyValue(valueParser, box, standardName, value);
    }
}
