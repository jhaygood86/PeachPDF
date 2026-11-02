using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using PeachDrawing.Core;

namespace PeachBrackets.Html.Paint.Content
{
    /// <summary>
    /// Paints a <c>&lt;textarea&gt;</c> on a page that is not drawn as a host's live control and is not an interactive PDF field: the box as any
    /// other box is painted (background, border, shadow), then its value - or its placeholder - broken into lines, as a screen shows it. A textarea's
    /// text is its value, not flowed content, so without this a static page would show an empty box.
    /// </summary>
    internal sealed class StaticTextAreaPainter : IFragmentContentPainter
    {
        public void Paint(FragmentPainter painter, Canvas g, BoxFragment fragment)
        {
            painter.PaintBoxContent(g, fragment);

            var rect = fragment.PrimaryRect;
            if (rect is { Width: > 0, Height: > 0 })
                ScreenFormControlPainter.PaintTextArea(g, (CssBoxFormField)fragment.Box, rect, ((CssBoxFormField)fragment.Box).GetAttribute("value", string.Empty), ((CssBoxFormField)fragment.Box).HtmlTag!.HasAttribute("disabled"));
        }
    }
}
