namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// CSS box for a <c>&lt;video&gt;</c> element. A static renderer can't play video, so it renders the
    /// element's <c>poster</c> image (the frame browsers show before playback) as replaced content -
    /// honoring <c>object-fit</c>/<c>object-position</c> exactly like an image. This is
    /// <see cref="CssBoxObject"/>'s replaced/fallback behavior with the resource resolved from
    /// <c>poster</c> instead of <c>data</c>; with no <c>poster</c> the box falls back to being an
    /// ordinary container for any fallback DOM content.
    /// </summary>
    internal sealed class CssBoxVideo : CssBoxObject
    {
        public CssBoxVideo(CssBox? parent, HtmlTag tag)
            : base(parent, tag)
        {
        }

        protected override string? ResolveReplacedSource() => GetAttribute("poster", null);
    }
}
