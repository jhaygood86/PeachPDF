// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
//
// - Sun Tsu,
// "The Art of War"

using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Svg
{
    /// <summary>
    /// Wraps a live <see cref="CssBox"/> (built for free by <see cref="Html.Core.Parse.HtmlParser"/>
    /// out of an inline <c>&lt;svg&gt;</c>'s markup) as an <see cref="ISvgSourceNode"/>. The wrapped
    /// boxes are read purely as a data source here - they are never laid out or painted through the
    /// generic box pipeline. Styling is matched through the full HTML CSS engine against the host
    /// document's <see cref="CssData"/> (which already contains both the SVG's own nested
    /// <c>&lt;style&gt;</c> and any document-level <c>&lt;style&gt;</c>), via a case-sensitive
    /// SVG-flavored <see cref="ICssDomNode"/> (<see cref="SvgCssBoxDomNode"/>).
    /// </summary>
    internal sealed class CssBoxSvgSourceNode : ISvgSourceNode
    {
        private readonly CssBox _box;
        private readonly CssBox _svgRoot;
        private readonly CssData? _cssData;
        private readonly string _media;
        private readonly CssVarResolver.VarContext? _varContext;

        public CssBoxSvgSourceNode(CssBox box)
            : this(box, box, box.HtmlContainer?.CssData, "print", BuildVarContext(box.HtmlContainer))
        {
        }

        private CssBoxSvgSourceNode(CssBox box, CssBox svgRoot, CssData? cssData, string media, CssVarResolver.VarContext? varContext)
        {
            _box = box;
            _svgRoot = svgRoot;
            _cssData = cssData;
            _media = media;
            _varContext = varContext;
        }

        /// <summary>
        /// The host document's <c>@property</c> registry (built by the HTML cascade) as a <c>var()</c>
        /// context, so a registered custom property's <c>initial-value</c>/<c>syntax</c> is honored when the
        /// SVG re-resolves <c>var()</c> in its own paint values. Null when nothing is registered.
        /// </summary>
        private static CssVarResolver.VarContext? BuildVarContext(HtmlContainerInt? container) =>
            container?.RegisteredProperties is { Count: > 0 } registered
                ? new CssVarResolver.VarContext(registered, new CssValueParser(container.Adapter))
                : null;

        public string Name => _box.HtmlTag?.Name ?? "";

        public string? GetAttribute(string name) => _box.GetAttribute(name, null);

        public IEnumerable<ISvgSourceNode> Children =>
            _box.Boxes.Select(b => (ISvgSourceNode)new CssBoxSvgSourceNode(b, _svgRoot, _cssData, _media, _varContext));

        /// <summary>
        /// Mirrors <c>DomParser.CascadeParseStyles</c>'s own precedent for reading a <c>&lt;style&gt;</c>
        /// box's CSS text (direct child boxes are plain text nodes, not further nested elements - the
        /// HTML tokenizer doesn't give <c>&lt;style&gt;</c> any special raw-text handling, so its content
        /// arrives as ordinary child text boxes, same as any other element's text).
        /// </summary>
        public string GetTextContent() => string.Concat(_box.Boxes.Select(b => b.Text ?? string.Empty));

        public IReadOnlyDictionary<string, string>? GetMatchedCssDeclarations() =>
            SvgCssStyling.GetMatchedDeclarations(new SvgCssBoxDomNode(_box, _svgRoot), _cssData, _media, _varContext);

        public string? ResolveVar(string value) =>
            _cssData is null ? value : CssVarResolver.Resolve(new SvgCssBoxDomNode(_box, _svgRoot), value, _varContext);
    }
}
