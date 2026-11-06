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

using PeachBrackets.Css;
using PeachBrackets.Html;
using PeachBrackets.Html.Animation;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Parse;
using PeachBrackets.Svg.Smil;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachBrackets.Svg
{
    /// <summary>
    /// Wraps a live <see cref="CssBox"/> (built for free by <see cref="Html.Parse.HtmlParser"/>
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

        // Set on the nodes of an instance a <use> makes (see AsInstanceUnder): the box the instance starts at, and the <use> that is its parent.
        private readonly (CssBox Root, ISvgSourceNode Use)? _instance;

        public CssBoxSvgSourceNode(CssBox box)
            : this(box, box, box.HtmlContainer?.CssData, "print", BuildVarContext(box.HtmlContainer))
        {
        }

        private CssBoxSvgSourceNode(CssBox box, CssBox svgRoot, CssData? cssData, string media, CssVarResolver.VarContext? varContext,
            (CssBox Root, ISvgSourceNode Use)? instance = null)
        {
            _box = box;
            _svgRoot = svgRoot;
            _cssData = cssData;
            _media = media;
            _varContext = varContext;
            _instance = instance;
        }

        /// <summary>
        /// The host document's <c>@property</c> registry (built by the HTML cascade) as a <c>var()</c>
        /// context, so a registered custom property's <c>initial-value</c>/<c>syntax</c> is honored when the
        /// SVG re-resolves <c>var()</c> in its own paint values. Null when nothing is registered.
        /// </summary>
        private static CssVarResolver.VarContext? BuildVarContext(HtmlContainerInt? container) =>
            container?.RegisteredProperties is { Count: > 0 } registered
                ? new CssVarResolver.VarContext(registered, new CssValueParser(container.RenderContext))
                : null;

        /// <summary>The laid-out HTML of this node when it is a <c>&lt;foreignObject&gt;</c> of an inline <c>&lt;svg&gt;</c>.</summary>
        internal ISvgForeignContent? ForeignContent => (_svgRoot as CssBoxSvg)?.GetForeignContent(_box);

        /// <summary>The wrapped box, for the HTML layer's own bookkeeping.</summary>
        internal CssBox Box => _box;

        public string Name => _box.HtmlTag?.Name ?? "";

        public string? GetAttribute(string name)
        {
            var declared = _box.GetAttribute(name, null);

            // An attribute an animation element animates reads as what it is at the time (SMIL Animation §3.7).
            if (SmilTime is { } time && SmilAnimationSet!.Targets(_box)
                && SmilAnimationSet.Sample(_box, name, time, () => declared, ((CssBoxSvg)_svgRoot).SmilParser) is { } animated)
            {
                return animated;
            }

            return declared;
        }

        public IEnumerable<ISvgSourceNode> Children =>
            _box.Boxes.Select(b => (ISvgSourceNode)new CssBoxSvgSourceNode(b, _svgRoot, _cssData, _media, _varContext, _instance));

        public IEnumerable<SvgContentNode> ContentNodes =>
            // A box with no HtmlTag is a loose text node (its Text is the run's characters); one with an
            // HtmlTag is a child element. The HTML tokenizer already emits them as ordered child boxes.
            _box.Boxes.Select(b => b.HtmlTag is null
                ? SvgContentNode.OfText(b.Text ?? string.Empty)
                : SvgContentNode.OfElement(new CssBoxSvgSourceNode(b, _svgRoot, _cssData, _media, _varContext, _instance)));

        /// <summary>
        /// Mirrors <c>DomParser.CascadeParseStyles</c>'s own precedent for reading a <c>&lt;style&gt;</c>
        /// box's CSS text (direct child boxes are plain text nodes, not further nested elements - the
        /// HTML tokenizer doesn't give <c>&lt;style&gt;</c> any special raw-text handling, so its content
        /// arrives as ordinary child text boxes, same as any other element's text).
        /// </summary>
        public string GetTextContent() => string.Concat(_box.Boxes.Select(b => b.Text ?? string.Empty));

        // What the style sheets declare for an element is worked out by matching every rule against it, and the tree builder asks for it once for every
        // property it reads of every element (dozens of times each, and a second time to learn whether the winner was !important), so on a page of inline
        // icons that was most of the time laying it out. The answer is the same each time - it depends on the element, the rules, the media, the custom
        // properties in scope and, for a document restyled in place, the style generation - so it is kept with the element until one of those differs.
        private sealed class MatchedStyle(CssData? cssData, string media, CssVarResolver.VarContext? varContext, int generation,
            IReadOnlyDictionary<string, string?>? declarations, HashSet<string>? important)
        {
            internal readonly CssData? CssData = cssData;
            internal readonly string Media = media;
            internal readonly CssVarResolver.VarContext? VarContext = varContext;
            internal readonly int Generation = generation;
            internal readonly IReadOnlyDictionary<string, string?>? Declarations = declarations;
            internal readonly HashSet<string>? Important = important;
        }

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CssBox, MatchedStyle> MatchedStyles = new();

        private MatchedStyle Matched()
        {
            var generation = _box.HtmlContainer?.StyleGeneration ?? 0;
            if (MatchedStyles.TryGetValue(_box, out var cached)
                && ReferenceEquals(cached.CssData, _cssData) && ReferenceEquals(cached.VarContext, _varContext)
                && cached.Generation == generation && string.Equals(cached.Media, _media, StringComparison.Ordinal))
            {
                return cached;
            }

            var declarations = SvgCssStyling.GetMatchedDeclarations(new SvgCssBoxDomNode(_box, _svgRoot), _cssData, _media, _varContext, out var important);
            var fresh = new MatchedStyle(_cssData, _media, _varContext, generation, declarations, important);
            MatchedStyles.AddOrUpdate(_box, fresh);
            return fresh;
        }

        public IReadOnlyDictionary<string, string?>? GetMatchedCssDeclarations() => Matched().Declarations;

        public bool IsMatchedCssDeclarationImportant(string name) => Matched().Important?.Contains(name) == true;

        public ISvgSourceNode? Parent =>
            _instance is { } instance && ReferenceEquals(_box, instance.Root) ? instance.Use
            : ReferenceEquals(_box, _svgRoot) || _box.ParentBox is not { } parent ? null
            : new CssBoxSvgSourceNode(parent, _svgRoot, _cssData, _media, _varContext, _instance);

        public object? IdentityKey => _instance is { } instance && instance.Use.IdentityKey is { } useKey ? (_box, useKey) : _box;

        public ISvgSourceNode AsInstanceUnder(ISvgSourceNode use) => new CssBoxSvgSourceNode(_box, _svgRoot, _cssData, _media, _varContext, (_box, use));

        private bool CssAnimationsPlay => _box.HtmlContainer is { } container && (container.AnimationProgress is not null || container.SvgAnimationSeconds is not null);

        public bool IsAnimated => (CssAnimationsPlay && _box.GetSvgAnimationPlan() is not null) || SmilTime is not null && SmilAnimationSet!.Targets(_box);

        // SMIL: the animation elements of the <svg> this node is inside, and the time they are at. A running clock gives the time; a still frame takes its fraction of the
        // span the animations cover.
        private SmilSet? SmilAnimationSet => (_svgRoot as CssBoxSvg)?.SmilAnimations;

        private double? SmilTime
        {
            get
            {
                if (SmilAnimationSet is not { } smil || _box.HtmlContainer is not { } container) return null;
                if (container.SvgAnimationSeconds is { } seconds) return seconds;
                return container.AnimationProgress is { } fraction ? fraction * smil.StillFrameLength : null;
            }
        }

        public string? GetAnimatedValue(string name, Func<string?> underlying, Func<string?> parent, Func<string?> initial, bool inherited)
        {
            string? css = null;
            if (CssAnimationsPlay && _box.GetSvgAnimationPlan() is { } plan)
                css = AnimationApplier.SampleElement(_box, plan, name, underlying, parent, initial, inherited);

            // SMIL sits above CSS animations, over the value they give. A transform is read from the attribute, which is where its animation is applied.
            if (SmilTime is { } time && !name.Equals("transform", StringComparison.Ordinal)
                && SmilAnimationSet!.Sample(_box, name, time, () => css ?? underlying(), ((CssBoxSvg)_svgRoot).SmilParser) is { } smil)
            {
                return smil;
            }

            return css;
        }

        public string? ResolveVar(string value) =>
            _cssData is null ? value : CssVarResolver.Resolve(new SvgCssBoxDomNode(_box, _svgRoot), value, _varContext);

        /// <summary>An inline <c>&lt;svg&gt;</c> falls back to its surrounding HTML document's own
        /// resolved language (<see cref="CssBox.Language"/> - the box's own <c>lang</c> if set, else its
        /// nearest HTML ancestor's, else <see cref="HtmlContainerInt.DocumentLanguage"/>) when nothing in
        /// the SVG subtree itself sets <c>lang</c>/<c>xml:lang</c>.</summary>
        public string? DocumentLanguageFallback => _box.Language;

        /// <summary>The computed font size of the HTML parent, which the inline <c>&lt;svg&gt;</c> inherits. The svg's own declaration, if any, is read from the tree
        /// like any other element's, against this.</summary>
        public double? InheritedFontSizePx => HasFonts && _box.ParentBox is { } parent ? parent.GetEmHeight() / Length.PointsPerPx : null;

        /// <summary>The font size of the HTML root element, which is <c>rem</c>.</summary>
        public double? RootFontSizePx => HasFonts ? _box.GetRemHeight() / Length.PointsPerPx : null;

        public double? GetRootFontRatio(FontMetric metric) => HasFonts ? ((IFontMetricSource)_box.DerivedStyle).GetRatio(metric, true) : null;

        // A box tree read with no container (a unit test of the builder, say) has no render context to measure a font with.
        private bool HasFonts => _box.HtmlContainer?.RenderContext is not null;
    }
}
