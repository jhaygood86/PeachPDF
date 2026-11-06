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

using PeachBrackets.Html;
using PeachBrackets.Html.Animation;
using PeachBrackets.Html.Parse;
using PeachBrackets.Svg.Smil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace PeachBrackets.Svg
{
    /// <summary>
    /// Wraps a <see cref="XElement"/> from a standalone SVG document (fetched for
    /// <c>&lt;img src="x.svg"&gt;</c>, parsed via <see cref="System.Xml.Linq"/> rather than the HTML
    /// tokenizer, since it's expected to be well-formed XML) as an <see cref="ISvgSourceNode"/>. Styling
    /// is matched through the full CSS engine against the SVG's own <see cref="CssData"/> (built from its
    /// nested <c>&lt;style&gt;</c> elements - host-document CSS does not apply to a standalone SVG) via a
    /// case-sensitive SVG-flavored <see cref="ICssDomNode"/> (<see cref="SvgXmlDomNode"/>).
    /// </summary>
    internal sealed class XElementSvgSourceNode : ISvgSourceNode
    {
        private static readonly XNamespace XlinkNamespace = "http://www.w3.org/1999/xlink";

        private readonly XElement _element;
        private readonly XElement _svgRoot;
        private readonly CssData? _cssData;
        private readonly string _media;
        private readonly CssVarResolver.VarContext? _varContext;

        // Set for a standalone SVG a clock plays: the SMIL animations of the document and the time they are at.
        private readonly SvgAnimationContext? _animation;

        public XElementSvgSourceNode(XElement element)
            : this(element, element, null, "print", null)
        {
        }

        internal XElementSvgSourceNode(XElement element, XElement svgRoot, CssData? cssData, string media, CssVarResolver.VarContext? varContext = null,
            SvgAnimationContext? animation = null)
        {
            _animation = animation;
            _element = element;
            _svgRoot = svgRoot;
            _cssData = cssData;
            _media = media;
            _varContext = varContext;
        }

        public string Name => _element.Name.LocalName;

        public string? GetAttribute(string name)
        {
            var declared = GetDeclaredAttribute(name);

            // An attribute an animation element animates reads as what it is at the time (SMIL Animation §3.7); a presentation property is animated where the builder
            // resolves it against the CSS (GetAnimatedValue), so it is left as written here and not animated twice.
            if (_animation is { Smil: { } smil } animation && !SmilSet.IsPresentationProperty(name) && smil.Targets(_element)
                && smil.Sample(new XElementSmilNode(_element), name, animation.Seconds, () => declared, animation.Parser) is { } animated)
            {
                return animated;
            }

            return declared;
        }

        // The CSS animations of this element at the time: its own style sheets name them, and the keyframes are the document's.
        private AnimationApplier.ElementPlan? CssPlan
        {
            get
            {
                if (_animation is not { Keyframes.Count: > 0 } animation) return null;
                if (animation.Plans.TryGetValue(_element, out var known)) return known;

                var declarations = GetMatchedCssDeclarations();
                var plan = declarations is null
                    ? null
                    : AnimationApplier.PlanStandaloneElement(name => declarations.TryGetValue(name, out var value) ? value : null, animation.Keyframes, animation.Seconds, animation.Parser);
                animation.Plans[_element] = plan;
                return plan;
            }
        }

        public bool IsAnimated => CssPlan is not null || (_animation is { Smil: { } smil } && smil.Targets(_element));

        public ISvgSourceNode? Parent =>
            _animation is not null && !ReferenceEquals(_element, _svgRoot) && _element.Parent is { } parent
                ? new XElementSvgSourceNode(parent, _svgRoot, _cssData, _media, _varContext, _animation)
                : null;

        public object? IdentityKey => _animation is null ? null : _element;

        public string? GetAnimatedValue(string name, Func<string?> underlying, Func<string?> parent, Func<string?> initial, bool inherited)
        {
            if (_animation is not { } animation) return null;

            var css = CssPlan is { } plan ? AnimationApplier.SampleElement(null, plan, name, underlying, parent, initial, inherited) : null;

            // SMIL sits above the CSS animations, over the value they give.
            if (animation.Smil is { } smil && SmilSet.IsPresentationProperty(name)
                && smil.Sample(new XElementSmilNode(_element), name, animation.Seconds, () => css ?? underlying(), animation.Parser) is { } animated)
            {
                return animated;
            }

            return css;
        }

        private string? GetDeclaredAttribute(string name)
        {
            if (name == "xlink:href")
                return _element.Attribute(XlinkNamespace + "href")?.Value ?? _element.Attribute("href")?.Value;

            // xml:lang is bound to the reserved XML namespace, not a bare "xml:lang"-named attribute -
            // XNamespace.Xml is System.Xml.Linq's own constant for it (mirrors the xlink:href handling
            // just above).
            if (name == "xml:lang")
                return _element.Attribute(XNamespace.Xml + "lang")?.Value;

            if (name == "xml:space")
                return _element.Attribute(XNamespace.Xml + "space")?.Value;

            return _element.Attribute(name)?.Value;
        }

        public IEnumerable<ISvgSourceNode> Children =>
            _element.Elements().Select(e => (ISvgSourceNode)new XElementSvgSourceNode(e, _svgRoot, _cssData, _media, _varContext, _animation));

        public IEnumerable<SvgContentNode> ContentNodes =>
            _element.Nodes().Select(n => n switch
            {
                XText text => SvgContentNode.OfText(text.Value),
                XElement element => SvgContentNode.OfElement(new XElementSvgSourceNode(element, _svgRoot, _cssData, _media, _varContext, _animation)),
                _ => (SvgContentNode?)null,
            }).Where(c => c is not null).Select(c => c!.Value);

        /// <summary>
        /// Only this element's own direct text-node children - deliberately NOT <see cref="XElement.Value"/>,
        /// which recurses into descendant elements' text too. Matches <see cref="CssBoxSvgSourceNode"/>'s
        /// (accidental but relied-upon) behavior: its own <c>GetTextContent</c> only sees direct child
        /// boxes, so a nested element's text never bleeds into its parent's. This matters once a node
        /// can have both loose text and element children (e.g. <c>&lt;text&gt;Hello &lt;tspan&gt;World&lt;/tspan&gt;&lt;/text&gt;</c>) -
        /// callers that need only the element's own run rely on that distinction (<see cref="SvgTreeBuilder"/>'s
        /// text handling).
        /// </summary>
        public string GetTextContent() => string.Concat(_element.Nodes().OfType<XText>().Select(t => t.Value));

        public IReadOnlyDictionary<string, string?>? GetMatchedCssDeclarations() =>
            SvgCssStyling.GetMatchedDeclarations(new SvgXmlDomNode(_element, _svgRoot), _cssData, _media, _varContext);

        public bool IsMatchedCssDeclarationImportant(string name)
        {
            SvgCssStyling.GetMatchedDeclarations(new SvgXmlDomNode(_element, _svgRoot), _cssData, _media, _varContext, out var important);
            return important?.Contains(name) == true;
        }

        public string? ResolveVar(string value) =>
            _cssData is null ? value : CssVarResolver.Resolve(new SvgXmlDomNode(_element, _svgRoot), value, _varContext);
    }
}
