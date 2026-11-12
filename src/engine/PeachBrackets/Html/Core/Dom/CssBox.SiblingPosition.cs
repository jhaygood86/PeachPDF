namespace PeachBrackets.Html.Dom
{
    internal partial class CssBox
    {
        /// <summary>
        /// Whether a declaration of this element used <c>sibling-index()</c> or <c>sibling-count()</c>. Its style
        /// then depends on its place among its siblings, so a sibling with the same tag and attributes must not
        /// adopt it (see <c>DomParser</c>'s sibling style sharing).
        /// </summary>
        internal bool UsesSiblingPosition { get; set; }
    }
}
