using PeachPDF.Html.Core.Dom;
using PeachPDF.Svg;
using System;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// A document-wide index of every SVG <c>&lt;clipPath&gt;</c> element in the HTML document, keyed by
    /// id, so an HTML element's own <c>clip-path: url(#id)</c> (<see cref="CssClipPathResolver"/>) can
    /// reference one defined inside <b>any</b> inline <c>&lt;svg&gt;</c> - including one that is never
    /// otherwise painted, such as the common <c>&lt;svg style="display:none"&gt;</c> defs-only pattern.
    /// <para>
    /// Each inline <c>&lt;svg&gt;</c> (<see cref="CssBoxSvg"/>) normally only builds its own
    /// <see cref="SvgDocument"/> scene graph (and therefore only registers its own
    /// <c>&lt;clipPath&gt;</c> ids) when it participates in layout
    /// (<see cref="CssBoxSvg.MeasureWordsSize"/> calls <see cref="CssBoxSvg.EnsureDocument"/>) - a
    /// <c>display:none</c> box is skipped by layout entirely and would otherwise never build one. This
    /// registry instead walks the whole box tree itself, unfiltered by <c>display</c>, and force-builds
    /// every <see cref="CssBoxSvg"/>'s document (idempotent - safe even for one whose own paint path
    /// also calls <see cref="CssBoxSvg.EnsureDocument"/>). One known, narrow gap: a <c>display:none</c>
    /// SVG never runs the async <c>&lt;image&gt;</c>-prefetch step that normally precedes
    /// <see cref="CssBoxSvg.EnsureDocument"/>, so a raster <c>&lt;image&gt;</c> nested inside such a
    /// hidden SVG's <c>&lt;clipPath&gt;</c> won't resolve - basic shapes (the overwhelmingly common
    /// <c>&lt;clipPath&gt;</c> content) are unaffected.
    /// </para>
    /// <para>
    /// Built lazily on first use (always during paint, once layout has fully finished) and cached for
    /// the container's lifetime - the document doesn't change after it's laid out, so a one-time build
    /// is sufficient and avoids doing this work for documents that never use <c>clip-path: url()</c>.
    /// </para>
    /// </summary>
    internal sealed class SvgClipPathRegistry(HtmlContainerInt container)
    {
        private Dictionary<string, SvgClipPath>? _clipPaths;

        public bool TryGet(string id, out SvgClipPath? clipPath)
        {
            EnsureBuilt();
            return _clipPaths!.TryGetValue(id, out clipPath);
        }

        private void EnsureBuilt()
        {
            if (_clipPaths is not null) return;

            _clipPaths = new Dictionary<string, SvgClipPath>(StringComparer.Ordinal);

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
                    foreach (var (id, clipPath) in document.ClipPaths)
                        _clipPaths!.TryAdd(id, clipPath);
                }

                // CssBoxSvg.EnsureDocument() clears its own Boxes once the scene graph is built (they
                // were only ever a plain tag/attribute data source for SvgTreeBuilder) - nothing further
                // to recurse into even if it hadn't already run.
                return;
            }

            foreach (var child in box.Boxes)
                Walk(child);
        }
    }
}
