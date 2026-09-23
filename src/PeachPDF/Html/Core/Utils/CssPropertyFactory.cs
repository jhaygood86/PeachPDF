using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using System;
using System.Collections.Generic;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Applies a CSS property to a declaratively-built <see cref="CssBox"/> the same way the cascade
    /// itself would - by calling the generated <see cref="CssPropertyRegistry"/> dispatcher
    /// (<see cref="CssUtils.SetPropertyValue"/>) with the property's real name and a canonical CSS value
    /// string. This is not a second, independent property-application path: every declarative builder
    /// method (<see cref="Layout.DocumentBuilder"/> and its nested builders) goes through this one class
    /// instead of hand-constructing a typed <c>CssProperty&lt;T&gt;</c>/<c>CssKeywordOrValue&lt;,&gt;</c>
    /// value per property, which would need to know each property's own internal storage shape. One
    /// instance is shared across a whole document build (it owns the <see cref="CssValueParser"/>
    /// <see cref="CssUtils.SetPropertyValue"/> needs).
    /// </summary>
    internal sealed class CssPropertyFactory(RAdapter adapter)
    {
        private readonly CssValueParser _parser = new(adapter);

        /// <summary>
        /// The adapter this whole document build is running against - exposed so a declarative content
        /// method that needs to decode in-memory bytes directly into an engine type (<c>RImage</c> via
        /// <see cref="RAdapter.ImageFromStream"/>, an <c>SvgDocument</c> via <c>SvgTreeBuilder.Build</c>)
        /// can do so without a data-URI/<c>ImageLoadHandler</c> round trip - see <c>ContainerBuilder</c>'s
        /// <c>Image</c>/<c>Svg</c> overloads.
        /// </summary>
        public RAdapter Adapter => adapter;

        /// <summary>
        /// The <c>display: contents</c> shells every <c>IContainer.Html(...)</c> fragment built against this
        /// factory already spliced out of its own tree (<see cref="Parse.DomParser.GenerateFragmentCssTree"/>).
        /// They no longer hang off any box, and the container that will own the finished document does not
        /// exist yet, so they wait here for <see cref="HtmlContainerInt.SetDeclarativeRoot"/>.
        /// </summary>
        public List<CssBox> DisplayContentsShells { get; } = [];

        /// <summary>
        /// Sets <paramref name="box"/>'s <paramref name="propertyName"/> to the raw CSS text
        /// <paramref name="value"/>, and records <paramref name="propertyName"/> in
        /// <see cref="CssBox.BuilderSetProperties"/> so a later document-level stylesheet
        /// (<see cref="Parse.DomParser.ApplyDeclarativeStylesheet"/>) cannot silently override it with a
        /// non-<c>!important</c> rule.
        /// </summary>
        public void Set(CssBox box, string propertyName, string value)
        {
            (box.BuilderSetProperties ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase)).Add(propertyName);
            CssUtils.SetPropertyValue(_parser, box, propertyName, value);
        }

        /// <summary>
        /// Sets <paramref name="box"/>'s <c>text-align</c> to <paramref name="keyword"/> (<c>left</c>, <c>right</c>,
        /// <c>center</c>, <c>justify</c>, <c>start</c>, <c>end</c>).
        /// </summary>
        /// <remarks>
        /// <c>text-align</c> is a shorthand, and <see cref="Set(CssBox, string, string)"/> reaches only the generated
        /// <see cref="CssPropertyRegistry"/>, which is per-longhand: handed the shorthand it returns false and
        /// the value is silently dropped (a declarative <c>Alignment(...)</c> aligned nothing anywhere). The
        /// cascade expands the shorthand before it ever calls the registry, so this does the same expansion by
        /// hand - <c>text-align-all</c> takes the keyword and <c>text-align-last</c> resets to its initial
        /// <c>auto</c> (css-text-3 §6.1), which is exactly what the shorthand sets.
        /// </remarks>
        public void SetTextAlign(CssBox box, string keyword)
        {
            Set(box, "text-align-all", keyword);
            Set(box, "text-align-last", "auto");
        }

        /// <summary>Sets <paramref name="box"/>'s <paramref name="propertyName"/> to <paramref name="value"/>'s canonical CSS length token.</summary>
        public void Set(CssBox box, string propertyName, PdfLength value) =>
            Set(box, propertyName, value.ToCssText());

        /// <summary>Sets <paramref name="box"/>'s <paramref name="propertyName"/> to <paramref name="value"/>'s canonical CSS color token.</summary>
        public void Set(CssBox box, string propertyName, PdfColor value) =>
            Set(box, propertyName, value.ToCssText());

        /// <summary>
        /// Creates an anonymous declarative child box - a row/column container, a row/column item, a
        /// rich-text span, or a line element - none of which necessarily hold text anywhere in their own
        /// subtree. This uses a synthetic <see cref="HtmlTag"/> rather than a null one purely so
        /// <c>DomUtils.GeneratesFlexOrGridItem</c>'s "<c>box.HtmlTag is not null || ... || !box.IsSpaceOrEmpty</c>"
        /// check - which exists to keep a genuinely empty, whitespace-only anonymous box produced by HTML
        /// parsing out of flex/grid layout - never excludes a box a caller explicitly asked to exist just
        /// because it (and everything under it) happens to hold no text yet. The tag name is otherwise
        /// inert: a declaratively-built tree never runs selector-based cascade against the container's
        /// default <c>CssData</c>, so no tag-name-keyed default style applies to it.
        /// </summary>
        public static CssBox CreateAnonymousBox(CssBox parent) =>
            CssBox.CreateBox(parent, new HtmlTag("div", false));
    }
}
