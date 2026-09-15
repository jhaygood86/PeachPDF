using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;

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

        /// <summary>Sets <paramref name="box"/>'s <paramref name="propertyName"/> to the raw CSS text <paramref name="value"/>.</summary>
        public void Set(CssBox box, string propertyName, string value) =>
            CssUtils.SetPropertyValue(_parser, box, propertyName, value);

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
