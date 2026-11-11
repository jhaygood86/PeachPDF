using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using PeachBrackets.Css;
using PeachBrackets.Html;
using PeachBrackets.Html.Animation;
using PeachBrackets.Html.Parse;
using PeachBrackets.Svg.Smil;
using PeachDrawing.Core;

namespace PeachBrackets.Svg
{
    /// <summary>What a standalone SVG's source nodes need to be animated: its SMIL animations and keyframes, and the time they are at.</summary>
    internal sealed class SvgAnimationContext(SmilSet? smil, IReadOnlyDictionary<string, KeyframeSet> keyframes, double seconds, CssValueParser parser)
    {
        public SmilSet? Smil => smil;

        public IReadOnlyDictionary<string, KeyframeSet> Keyframes => keyframes;

        public double Seconds => seconds;

        public CssValueParser Parser => parser;

        /// <summary>The CSS animations of each element at this time, worked out once for each.</summary>
        public Dictionary<XElement, AnimationApplier.ElementPlan?> Plans { get; } = [];
    }

    /// <summary>
    /// Plays a standalone SVG (the one an <c>&lt;img src="x.svg"&gt;</c> or an <c>&lt;object&gt;</c> shows) the way an animated image is played: the clock says how long it has
    /// been showing, and <see cref="Current"/> is the scene graph built for that moment. Every box that shows the image shows the same moment, as they show one frame of a GIF.
    /// </summary>
    internal sealed class StandaloneSvgPlayer
    {
        private readonly XElement _root;
        private readonly CssData? _cssData;
        private readonly CssVarResolver.VarContext? _varContext;
        private readonly RenderContext _renderContext;
        private readonly IReadOnlyDictionary<string, SvgTreeBuilder.SvgImageResource>? _prefetchedImages;
        private readonly IReadOnlyDictionary<(string Name, string Family), RegisteredFontPalette>? _paletteValues;
        private readonly IReadOnlyDictionary<(string Family, FontFeatureValueBlockKind Kind, string Name), RegisteredFontFeatureValues>? _featureValues;
        private readonly CssValueParser _parser;
        private readonly SmilSet? _smil;
        private readonly IReadOnlyDictionary<string, KeyframeSet> _keyframes;
        private readonly double? _cssEnd;
        private double? _builtAt;

        private StandaloneSvgPlayer(XElement root, CssData? cssData, CssVarResolver.VarContext? varContext, RenderContext renderContext,
            IReadOnlyDictionary<string, SvgTreeBuilder.SvgImageResource>? prefetchedImages,
            IReadOnlyDictionary<(string Name, string Family), RegisteredFontPalette>? paletteValues,
            IReadOnlyDictionary<(string Family, FontFeatureValueBlockKind Kind, string Name), RegisteredFontFeatureValues>? featureValues,
            SmilSet? smil, IReadOnlyDictionary<string, KeyframeSet> keyframes, double? cssEnd, SvgDocument first)
        {
            _root = root;
            _cssData = cssData;
            _varContext = varContext;
            _renderContext = renderContext;
            _prefetchedImages = prefetchedImages;
            _paletteValues = paletteValues;
            _featureValues = featureValues;
            _smil = smil;
            _keyframes = keyframes;
            _cssEnd = cssEnd;
            _parser = new CssValueParser(renderContext);
            Current = first;
        }

        /// <summary>
        /// The player for <paramref name="first"/>, built from <paramref name="root"/>, or null when nothing in the document animates (no SMIL animation element, and no element
        /// that names an animation its style sheets define).
        /// </summary>
        internal static StandaloneSvgPlayer? TryCreate(XElement root, CssData? cssData, CssVarResolver.VarContext? varContext, RenderContext renderContext,
            IReadOnlyDictionary<string, SvgTreeBuilder.SvgImageResource>? prefetchedImages,
            IReadOnlyDictionary<(string Name, string Family), RegisteredFontPalette>? paletteValues,
            IReadOnlyDictionary<(string Family, FontFeatureValueBlockKind Kind, string Name), RegisteredFontFeatureValues>? featureValues, SvgDocument first)
        {
            var smil = SmilSet.Read(new XElementSmilNode(root));

            IReadOnlyDictionary<string, KeyframeSet> keyframes = cssData is null
                ? [with(StringComparer.Ordinal)]
                : RegisteredKeyframes.BuildRegistry(cssData, MediaQueryContext.TypeOnly("print"));

            // When the last CSS animation of any element ends, which says whether there are frames left to build.
            double? cssEnd = null;
            if (keyframes.Count > 0)
            {
                foreach (var element in root.DescendantsAndSelf())
                {
                    var declarations = ((ISvgSourceNode)new XElementSvgSourceNode(element, root, cssData, "print", varContext)).GetMatchedCssDeclarations();
                    if (declarations is null) continue;

                    if (AnimationApplier.StandaloneAnimationEnd(name => declarations.TryGetValue(name, out var value) ? value : null, keyframes) is { } end)
                        cssEnd = Math.Max(cssEnd ?? 0, end);
                }
            }

            if (smil is null && cssEnd is null) return null;

            return new StandaloneSvgPlayer(root, cssData, varContext, renderContext, prefetchedImages, paletteValues, featureValues, smil, keyframes, cssEnd, first);
        }

        /// <summary>The scene graph at the time the player was last stepped to (the first one built, before any step).</summary>
        internal SvgDocument Current { get; private set; }

        /// <summary>Whether anything can still change after <paramref name="seconds"/> of showing.</summary>
        private bool PlayingAt(double seconds) => (_smil?.ChangesAfter(seconds) ?? false) || (_cssEnd is { } end && end > seconds);

        /// <summary>
        /// Builds the graphic as it looks <paramref name="seconds"/> after it began to show, when that is a moment its animations change in (or the first one built for a clock).
        /// </summary>
        /// <returns>whether <see cref="Current"/> changed, and whether the graphic plays on after this moment</returns>
        internal (bool Changed, bool Running) Step(double seconds)
        {
            var running = PlayingAt(seconds);
            var changed = _builtAt is not { } built || (seconds != built && (running || PlayingAt(built)));
            if (!changed) return (false, running);

            _builtAt = seconds;
            var context = new SvgAnimationContext(_smil, _keyframes, seconds, _parser);
            var node = new XElementSvgSourceNode(_root, _root, _cssData, "print", _varContext, context);
            var document = SvgTreeBuilder.Build(node, _renderContext, prefetchedImages: _prefetchedImages, fontPaletteValues: _paletteValues, fontFeatureValues: _featureValues);
            document.IsTimeVarying = true;
            document.Player = this;
            Current = document;
            return (true, running);
        }
    }
}
