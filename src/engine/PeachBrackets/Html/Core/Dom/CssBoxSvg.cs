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

using PeachDrawing.Core;
using PeachBrackets.Html.Animation;
using PeachBrackets.Html.Parse;
using PeachBrackets.Svg;
using PeachBrackets.Svg.Smil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachBrackets.Html.Dom
{
    /// <summary>
    /// CSS box for an inline <c>&lt;svg&gt;</c> element. Mirrors <see cref="CssBoxImage"/>'s
    /// replaced-element pattern (a phantom <see cref="CssRectSvg"/> word makes it participate in
    /// normal inline flow/line-breaking/min-max-width), but takes full manual control of what its
    /// descendant boxes mean: they are never laid out or painted through the generic box pipeline -
    /// <see cref="SvgTreeBuilder"/> reads them once (via <see cref="CssBoxSvgSourceNode"/>) as a plain
    /// tag/attribute data source and converts them into an internal <see cref="SvgDocument"/> scene
    /// graph, which <see cref="SvgRenderer"/> then paints directly.
    /// </summary>
    internal sealed class CssBoxSvg : CssBox
    {
        private readonly CssRectSvg _svgWord;
        private SvgDocument? _document;
        private IReadOnlyDictionary<string, SvgTreeBuilder.SvgImageResource>? _prefetchedImages;
        private Dictionary<CssBox, ISvgForeignContent>? _foreignContent;

        // The graphic's children, kept when a clock plays it so the scene graph can be built again for each moment (they are dropped from Boxes in between).
        private List<CssBox>? _retained;

        // The children the scene graph was built from, kept when a rule that reads hover/focus state styles an element inside it (`a:hover .icon`): a change of
        // that state builds the graphic again from them (RebuildForState) instead of the whole document being loaded again.
        private List<CssBox>? _stateBoxes;
        private SmilSet? _smil;
        private double? _cssAnimationEnd;
        private double? _builtAt;
        private CssValueParser? _smilParser;

        public CssBoxSvg(CssBox? parent, HtmlTag tag)
            : base(parent, tag)
        {
            _svgWord = new CssRectSvg(this);
            Words.Add(_svgWord);
        }

        /// <summary>
        /// Assigns the word its width and height
        /// </summary>
        /// <param name="g">the device to use</param>
        internal override async ValueTask MeasureWordsSize(Canvas g)
        {
            if (!_wordsSizeMeasured)
            {
                // Fetch any network/file <image> hrefs through the async resource pipeline before the
                // synchronous SvgTreeBuilder runs (in EnsureDocument). This is the first async touch
                // point for this box and runs before paint/link-collection, whose EnsureDocument calls
                // then reuse the already-built _document. A null base override resolves relative hrefs
                // against the host document base, correct for inline SVG.
                _prefetchedImages = await SvgTreeBuilder.PrefetchImageResourcesAsync(new CssBoxSvgSourceNode(this), HtmlContainer!);
                await LayoutForeignObjectsAsync(g, this);
                EnsureDocument();
                MeasureWordSpacing(g);
                _wordsSizeMeasured = true;
            }

            var (intrinsicWidth, intrinsicHeight, hasRatio) = SvgIntrinsicSize.ResolveForBox(_document);
            CssLayoutEngine.MeasureIntrinsicSize(_svgWord, intrinsicWidth, intrinsicHeight, hasRatio, SvgIntrinsicSize.RatioOnly(_document));
        }

        /// <summary>The laid-out HTML of <paramref name="foreignObject"/>, or null when it has none.</summary>
        internal ISvgForeignContent? GetForeignContent(CssBox foreignObject) =>
            _foreignContent is not null && _foreignContent.TryGetValue(foreignObject, out var content) ? content : null;

        /// <summary>
        /// Lays out the HTML of every <c>&lt;foreignObject&gt;</c> in this SVG (nested <c>&lt;svg&gt;</c>s included),
        /// ahead of the synchronous scene-graph build and before <see cref="EnsureDocument"/> drops the boxes.
        /// Not descended into: HTML inside a foreignObject is not SVG, and a nested foreignObject there is
        /// laid out as part of its parent's content.
        /// </summary>
        private async ValueTask LayoutForeignObjectsAsync(Canvas g, CssBox parent)
        {
            // Copied: laying a box out temporarily reparents it.
            foreach (var child in parent.Boxes.ToArray())
            {
                var index = parent.Boxes.IndexOf(child);

                if (child.HtmlTag is null)
                    continue;

                if (child.HtmlTag.Name == "foreignObject")
                {
                    var width = SvgValueParsers.ParseLength(child.GetAttribute("width", null)) ?? 0;
                    var height = SvgValueParsers.ParseLength(child.GetAttribute("height", null)) ?? 0;

                    if (await SvgForeignObjectContent.LayoutAsync(g, child, width, height, HtmlContainer!) is { } content)
                        (_foreignContent ??= [])[child] = content;

                    // Putting the box back appends it to its parent's children; document order (z-order, and which
                    // child a <switch> picks) must not change.
                    parent.Boxes.Remove(child);
                    parent.Boxes.Insert(index, child);
                }
                else
                {
                    await LayoutForeignObjectsAsync(g, child);
                }
            }
        }

        /// <summary>
        /// The phantom word carrying this box's replaced content, whose own rectangle positions the
        /// rendered SVG within the box. Read by <c>SvgFragmentPainter</c>.
        /// </summary>
        internal CssRectSvg SvgWord => _svgWord;

        /// <summary>
        /// The built scene graph. Null only if <see cref="EnsureDocument"/> could not build one.
        /// </summary>
        internal SvgDocument? Document => _document;

        /// <summary>The text of the SVG's own <c>&lt;title&gt;</c> child, read before its child boxes were dropped: what a reader calls the graphic. Null if it has none.</summary>
        internal string? AccessibleTitle { get; private set; }

        /// <summary>The text of the SVG's own <c>&lt;desc&gt;</c> child: more about the graphic than its title says. Null if it has none.</summary>
        internal string? AccessibleDescription { get; private set; }

        /// <summary>
        /// Builds the scene graph from this element's own children, once. Synchronous and idempotent:
        /// any network <c>&lt;image&gt;</c> hrefs it needs were prefetched during measurement.
        /// </summary>
        internal void EnsureDocument()
        {
            if (_document is not null)
                return;

            var source = new CssBoxSvgSourceNode(this);
            (AccessibleTitle, AccessibleDescription) = ReadAccessibleText(source);

            // What animates in it: SMIL elements, and CSS animations on its elements (which need the document's keyframes). A document a clock plays keeps the
            // children to build the graphic again for each moment; a still frame (or a document that is not animated) builds it once.
            if (HtmlContainer!.AnimationsEnabled || HtmlContainer.AnimationProgress is not null)
            {
                _smil = SmilSet.Read(this);
                _cssAnimationEnd = ReadCssAnimationEnd(this);
                if (HtmlContainer.AnimationsEnabled && (_smil is not null || _cssAnimationEnd is not null)) _retained = [.. Boxes];
            }

            _builtAt = HtmlContainer.SvgAnimationSeconds;
            _document = SvgTreeBuilder.Build(source, HtmlContainer.RenderContext, ActualColor, _prefetchedImages,
                fontPaletteValues: HtmlContainer.FontPaletteValues, fontFeatureValues: HtmlContainer.FontFeatureValues);
            _document.IsTimeVarying = _retained is not null;

            ForcedColorsAdjuster.RecolorSvg(this, _document);

            // Known only now: whether a rule that reads state matched an element of it is noted while the graphic is built.
            if (HasSvgInnerStatefulCandidates) _stateBoxes = _retained ?? [.. Boxes];

            // The parser builds a real (generic) CssBox for every SVG child element so
            // SvgTreeBuilder can read tag names/attributes off them - but once the scene graph above
            // is built, those boxes must not stick around: the base CssBox layout/paint pipeline
            // (which this class does not override) treats a box with children as a container and
            // skips populating its own Rectangles/line-box bookkeeping, breaking this box's own
            // background/border/content positioning. Clearing them makes this box a true leaf (one
            // phantom word, no children), exactly like CssBoxImage.
            Boxes.Clear();
        }

        /// <summary>Whether <see cref="RebuildForState"/> can build this graphic again: it has an element a rule that reads hover or focus state styles, and its children were kept.</summary>
        internal bool CanRebuildForState => _stateBoxes is not null && _document is not null;

        /// <summary>
        /// Builds the graphic again under the current interaction state, from the children it was built from. What an element of it matches is read afresh, so the
        /// caller raises <see cref="HtmlContainerInt.StyleGeneration"/> first (the matched declarations are kept for the generation they were read in).
        /// </summary>
        internal void RebuildForState()
        {
            if (_stateBoxes is null || _document is null || HtmlContainer is not { } container) return;

            Boxes.AddRange(_stateBoxes);
            try
            {
                var document = SvgTreeBuilder.Build(new CssBoxSvgSourceNode(this), container.RenderContext, ActualColor, _prefetchedImages,
                    fontPaletteValues: container.FontPaletteValues, fontFeatureValues: container.FontFeatureValues);
                document.IsTimeVarying = _retained is not null;
                ForcedColorsAdjuster.RecolorSvg(this, document);
                _document = document;
            }
            finally
            {
                Boxes.Clear();
            }
        }

        /// <summary>The SMIL animation elements of this graphic, or null when it has none (or nothing plays it).</summary>
        internal SmilSet? SmilAnimations => _smil;

        /// <summary>The parser that settles colours and keywords while SMIL values are mixed.</summary>
        internal CssValueParser SmilParser => _smilParser ??= new CssValueParser(HtmlContainer!.RenderContext);

        /// <summary>Whether a clock plays this graphic: SMIL or CSS animations of its elements that its frames have to be built again for.</summary>
        internal bool TimeVarying => _retained is not null;

        private static double? ReadCssAnimationEnd(CssBox box)
        {
            double? end = null;
            foreach (var child in box.Boxes)
            {
                if (child.HtmlTag is null) continue;
                if (AnimationApplier.ElementAnimationEnd(child) is { } own) end = Math.Max(end ?? 0, own);
                if (ReadCssAnimationEnd(child) is { } below) end = Math.Max(end ?? 0, below);
            }

            return end;
        }

        private bool PlayingAt(double seconds) => (_smil?.ChangesAfter(seconds) ?? false) || (_cssAnimationEnd is { } end && end > seconds);

        /// <summary>
        /// Builds the graphic as it looks at <paramref name="seconds"/> when that is a moment its animations change in (or the first one built for a clock),
        /// and says whether it still has frames to play. Nothing is built for a moment that looks like the last.
        /// </summary>
        /// <returns>whether the graphic changed, and whether it plays on after this moment</returns>
        internal (bool Changed, bool Running) StepAnimation(double seconds)
        {
            if (_retained is null || _document is null || HtmlContainer is not { } container) return (false, false);

            var running = PlayingAt(seconds);
            var changed = _builtAt is not { } built || (seconds != built && (running || PlayingAt(built)));
            if (!changed) return (false, running);

            container.SvgAnimationSeconds = seconds;
            _builtAt = seconds;

            Boxes.AddRange(_retained);
            try
            {
                var document = SvgTreeBuilder.Build(new CssBoxSvgSourceNode(this), container.RenderContext, ActualColor, _prefetchedImages,
                    fontPaletteValues: container.FontPaletteValues, fontFeatureValues: container.FontFeatureValues);
                document.IsTimeVarying = true;
                ForcedColorsAdjuster.RecolorSvg(this, document);
                _document = document;
            }
            finally
            {
                Boxes.Clear();
            }

            return (true, running);
        }

        // The first <title> and <desc> directly inside the <svg>, which is where SVG puts the name and the description of the graphic.
        private static (string? Title, string? Description) ReadAccessibleText(ISvgSourceNode root)
        {
            string? title = null;
            string? description = null;
            foreach (var child in root.Children)
            {
                var text = System.Text.RegularExpressions.Regex.Replace(child.GetTextContent() ?? string.Empty, @"\s+", " ").Trim();
                if (text.Length == 0)
                    continue;

                if (title is null && child.Name.Equals("title", StringComparison.OrdinalIgnoreCase))
                    title = text;
                else if (description is null && child.Name.Equals("desc", StringComparison.OrdinalIgnoreCase))
                    description = text;
            }

            return (title, description);
        }

        /// <summary>
        /// The built <see cref="SvgDocument"/> and its rendered rectangle in full document space, for
        /// <c>&lt;a&gt;</c> link-annotation discovery (see <see cref="SvgRenderer.CollectLinks"/>) -
        /// deliberately not reusing the painter's own rect, which is local to whichever fragmentainer
        /// is being painted and would have to be mapped back out again.
        /// </summary>
        internal (SvgDocument Document, Rect Rect)? GetLinkSource()
        {
            EnsureDocument();

            if (_document is null)
                return null;

            var r = _svgWord.Rectangle;
            r.Height -= ActualBorderTopWidth + ActualBorderBottomWidth + ActualPaddingTop + ActualPaddingBottom;
            r.Y += ActualBorderTopWidth + ActualPaddingTop;
            r.X = Math.Floor(r.X);
            r.Y = Math.Floor(r.Y);

            return r is { Width: > 0, Height: > 0 } ? (_document, r) : null;
        }
    }
}
