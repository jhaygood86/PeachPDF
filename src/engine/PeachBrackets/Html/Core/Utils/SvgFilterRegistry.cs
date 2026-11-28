using PeachBrackets.Html.Dom;
using PeachBrackets.Svg;
using System;
using System.Collections.Generic;

namespace PeachBrackets.Html.Utils
{
    /// <summary>
    /// A document-wide index of every SVG <c>&lt;filter&gt;</c> element in the HTML document, keyed by id, so an HTML element's own <c>filter: url(#id)</c> can
    /// reference one defined inside any inline <c>&lt;svg&gt;</c>, including one that is never painted (<c>display: none</c>, or a zero-size defs-only svg).
    /// Built like <see cref="SvgClipPathRegistry"/>: on first use during paint, from the box tree, and kept for the container's lifetime.
    /// </summary>
    internal sealed class SvgFilterRegistry(HtmlContainerInt container)
    {
        private Dictionary<string, SvgFilter>? _filters;

        public bool TryGet(string id, out SvgFilter? filter)
        {
            EnsureBuilt();
            return _filters!.TryGetValue(id, out filter);
        }

        private void EnsureBuilt()
        {
            if (_filters is not null)
                return;

            _filters = [with(StringComparer.Ordinal)];
            if (container.Root is { } root)
                Walk(root);
        }

        private void Walk(CssBox box)
        {
            if (box is CssBoxSvg svgBox)
            {
                svgBox.EnsureDocument();
                if (svgBox.Document is { } document)
                {
                    foreach (var (id, filter) in document.Filters)
                        _filters!.TryAdd(id, filter);
                }

                return;
            }

            foreach (var child in box.Boxes)
                Walk(child);
        }
    }
}
