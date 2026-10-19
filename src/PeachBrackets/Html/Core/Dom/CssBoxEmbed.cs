using PeachBrackets.Html.Utils;
using System;

namespace PeachBrackets.Html.Dom
{
    /// <summary>
    /// CSS box for an <c>&lt;embed&gt;</c> element, a void element whose <c>src</c> is rendered as replaced
    /// content when it is an image or SVG PeachPDF can decode, sized by the <c>width</c>/<c>height</c>
    /// attributes. This is <see cref="CssBoxObject"/>'s replaced behavior with the resource resolved from
    /// <c>src</c> instead of <c>data</c>; any other resource (a plugin type, no <c>src</c>) leaves an empty
    /// atomic inline box that still reserves its specified size.
    /// </summary>
    internal sealed class CssBoxEmbed : CssBoxObject
    {
        public CssBoxEmbed(CssBox? parent, HtmlTag tag)
            : base(parent, tag)
        {
        }

        protected override string? ResolveReplacedSource()
        {
            var src = GetAttribute("src", null);
            if (string.IsNullOrEmpty(src))
                return null;

            var type = GetAttribute("type", null);
            if (!string.IsNullOrEmpty(type) && !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return null;

            if (DataUriUtils.TryDecodeDataUri(src, out var mimeType, out _)
                && !mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return null;

            return src;
        }
    }
}
