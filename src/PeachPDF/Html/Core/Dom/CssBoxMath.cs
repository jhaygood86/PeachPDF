using PeachPDF.PdfBackend;
using PeachDrawing.Core;
using PeachPDF.MathML;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// CSS box for an inline <c>&lt;math&gt;</c> element. Mirrors <see cref="CssBoxSvg"/>'s
    /// replaced-element pattern (a phantom <see cref="CssRectMath"/> word makes it participate in
    /// normal inline flow/line-breaking/min-max-width), but takes full manual control of what its
    /// descendant boxes mean: they are never laid out or painted through the generic box pipeline -
    /// <see cref="MathTreeBuilder"/> reads them once (via <see cref="MathML.CssBoxMathSourceNode"/>) as
    /// a plain tag/attribute data source and converts them into an internal <see cref="MathDocument"/>
    /// presentation tree, which <see cref="MathLayoutEngine"/> then lays out and
    /// <c>MathFragmentPainter</c>/<c>MathRenderer</c> paint directly.
    /// <para>
    /// Unlike <see cref="CssBoxSvg"/>, an explicit CSS <c>width</c>/<c>height</c> does not rescale the
    /// formula's own internal typesetting (MathML Core doesn't characterize <c>&lt;math&gt;</c> as a
    /// replaced element the way <c>&lt;img&gt;</c>/<c>&lt;svg&gt;</c> are, so there is no aspect-ratio-
    /// preserving stretch step to run) - it only resizes this box's own reported extent, each axis
    /// independently, matching real browser behavior: the content can then overflow or leave extra
    /// space, exactly like an ordinary (non-replaced) box whose content doesn't fit its author-specified
    /// size. See <see cref="MeasureWordsSize"/>.
    /// </para>
    /// </summary>
    internal sealed class CssBoxMath : CssBox
    {
        private readonly CssRectMath _mathWord;
        private MathDocument? _document;
        private MathBox? _layout;
        private string? _serializedSource;
        private MathSourceElement? _source;

        public CssBoxMath(CssBox? parent, HtmlTag tag)
            : base(parent, tag)
        {
            _mathWord = new CssRectMath(this);
            Words.Add(_mathWord);
        }

        /// <summary>
        /// The markup of this <c>&lt;math&gt;</c> as source elements instead of child boxes, while the parser has built it that way
        /// and no rule needs the elements to be boxes (see <see cref="SettleSource"/>); null once it has been materialized, once it
        /// has been laid out, or when the markup was parsed into boxes. Its root is this element's own node.
        /// </summary>
        internal MathSourceElement? Source => _source;

        /// <summary>Starts the source tree the parser then fills in, in place of child boxes.</summary>
        internal MathSourceElement BeginSource()
        {
            SourceStarted = true;
            return _source = new MathSourceElement(HtmlTag, this, null);
        }

        /// <summary>Whether the parser has started a source tree for this box, which ends the parser's offer of one for it.</summary>
        internal bool SourceStarted { get; private set; }

        internal override IReadOnlyList<ICssDomNode> SelectorChildren => _source is { } source ? source.ChildNodes : base.SelectorChildren;

        internal override bool IsEmptyForSelectors => _source is { } source ? source.IsEmpty : base.IsEmptyForSelectors;

        /// <summary>
        /// Decides at the cascade whether the source tree can stay as it is. It can when no element in it has a style of its own that
        /// anything reads: the math builder reads only an element's colour, and an element is never laid out or painted as a box, so
        /// with no rule that declares more than purely visual properties (see
        /// <see cref="CssData.AnyConsequentialRuleMatches"/>) matching it, and no <c>style</c>, <c>color</c> (a presentational hint for
        /// the colour) or <c>id</c> (an anchor target) attribute on it, every element's colour is this box's own. Otherwise the
        /// tree becomes the boxes the parser would have built, and the cascade goes on over those.
        /// </summary>
        internal void SettleSource(CssData cssData, MediaQueryContext media, ContainerQuerySizes? containerSizes)
        {
            if (_source is not null && !InheritsItsColour(_source, cssData, media, containerSizes))
                MaterializeSource();
        }

        private static bool InheritsItsColour(MathSourceElement parent, CssData cssData, MediaQueryContext media, ContainerQuerySizes? containerSizes)
        {
            foreach (var element in parent.ElementChildren)
            {
                var tag = element.Tag!;
                if (tag.HasAttribute("style") || tag.HasAttribute("color") || tag.HasAttribute("id")) return false;
                if (cssData.AnyConsequentialRuleMatches(media, element, containerSizes)) return false;
                if (!InheritsItsColour(element, cssData, media, containerSizes)) return false;
            }

            return true;
        }

        /// <summary>Turns the source tree into the child boxes the parser builds for markup, for a <c>&lt;math&gt;</c> whose content some rule can reach.</summary>
        internal void MaterializeSource() => MaterializeSource(null);

        /// <summary>As <see cref="MaterializeSource()"/>, and answers the box built for <paramref name="track"/> (this box for the root).</summary>
        internal CssBox? MaterializeSource(MathSourceElement? track)
        {
            if (_source is not { } source) return null;

            _source = null;
            CssBox? tracked = ReferenceEquals(track, source) ? this : null;

            // This box's own ::after was generated at its cascade, ahead of this: the children go in front of it, as they were when the
            // parser built them before the cascade ran.
            var generatedAfter = Boxes.Where(b => b.IsAfterPseudoElement).ToList();
            foreach (var box in generatedAfter) Boxes.Remove(box);

            foreach (var child in source.ChildNodes)
            {
                Materialize(this, child, track, ref tracked);
            }

            Boxes.AddRange(generatedAfter);

            return tracked;
        }

        private static void Materialize(CssBox parent, MathSourceElement node, MathSourceElement? track, ref CssBox? tracked)
        {
            if (node.Tag is null)
            {
                CreateBox(parent).Text = node.Text;
                return;
            }

            var box = CreateBox(node.Tag, parent);
            if (ReferenceEquals(node, track)) tracked = box;

            // A token's text sits on its element, as the parser keeps it.
            if (node.Text is { } text && !box.TryAbsorbForeignText(text)) CreateBox(box).Text = text;

            foreach (var child in node.ChildNodes)
            {
                Materialize(box, child, track, ref tracked);
            }
        }

        internal override ValueTask MeasureWordsSize(Canvas g)
        {
            if (!_wordsSizeMeasured)
            {
                EnsureLayout(g);
                MeasureWordSpacing(g);
                _wordsSizeMeasured = true;
            }

            if (_layout is { } layout)
            {
                // An explicit CSS width/height overrides this box's own reported size per axis,
                // independently - reusing CssLayoutEngine's own absolute-length resolution (the same one
                // img/svg's intrinsic-size override uses) plus the same percentage-of-containing-block
                // handling ordinary CSS boxes get, not the full aspect-ratio-driven MeasureIntrinsicSize,
                // since math content never stretches to fit.
                var pixelsPerPoint = (HtmlContainer?.RenderContext as PdfRenderContext)?.PixelsPerPoint ?? 1.0;

                _mathWord.Width = ResolveAxis(new CssLength(Width), layout.InlineSize, ContainingBlock.Size.Width, pixelsPerPoint);
                _mathWord.Height = ResolveAxis(new CssLength(Height), layout.Ascent + layout.Descent, ContainingBlock.Size.Height, pixelsPerPoint);
            }

            return ValueTask.CompletedTask;
        }

        static double ResolveAxis(CssLength length, double natural, double containingBlockSize, double pixelsPerPoint)
        {
            if (CssLayoutEngine.TryResolveAbsolute(length, pixelsPerPoint, out var absoluteUnits))
                return absoluteUnits;
            // >= 0, not > 0: an explicit "0%" is a real, zero-size override, not "no override given" -
            // unlike TryResolveAbsolute's own zero-rejection above (shared with img/svg, pre-existing,
            // out of scope here), this percentage branch is new/local to CssBoxMath, so it can get this
            // right without touching that shared helper's behavior.
            if (length is { Number: >= 0, IsPercentage: true })
                return length.Number * containingBlockSize;
            return natural;
        }

        /// <summary>
        /// The phantom word carrying this box's replaced content, whose own rectangle positions the
        /// rendered formula within the box. Read by <c>MathFragmentPainter</c>.
        /// </summary>
        internal CssRectMath MathWord => _mathWord;

        /// <summary>The laid-out formula. Null only if <see cref="EnsureLayout"/> has not run yet.</summary>
        internal MathBox? Layout => _layout;

        /// <summary>
        /// The original <c>&lt;math&gt;</c> markup, re-serialized (see <see cref="MathMlSerializer"/>)
        /// - captured before <c>Boxes</c> is cleared below. Used only for the PDF 2.0 <c>/AF</c>
        /// MathML-source attachment on a tagged <c>Formula</c> structure element (see
        /// <c>StructureTagBuilder.AttachMathMlSource</c>); null only if <see cref="EnsureLayout"/> has
        /// not run yet.
        /// </summary>
        internal string? SerializedSource => _serializedSource;

        /// <summary>
        /// Builds the presentation tree and lays it out from this element's own children, once.
        /// Synchronous: unlike <see cref="CssBoxSvg"/>, MathML has no external resources
        /// (<c>&lt;image&gt;</c> hrefs, etc.) to prefetch first.
        /// </summary>
        internal void EnsureLayout(Canvas g)
        {
            if (_layout is not null)
                return;

            IMathSourceNode sourceNode = _source is { } source ? source : new CssBoxMathSourceNode(this);
            _serializedSource = MathMlSerializer.Serialize(sourceNode);
            _document = MathTreeBuilder.Build(sourceNode, HtmlContainer!.RenderContext);

            // The root-relative units (rem/rex/rch/...) are the HTML root element's, found from this box; its size
            // is in the adapter's device-scaled measurement space, so it takes PixelsPerPoint to reach true points.
            _document.RootFonts = DerivedStyle;
            _document.RootFontSizePt = GetRemHeight() * ((HtmlContainer.RenderContext as PdfRenderContext)?.PixelsPerPoint ?? 1.0);
            _layout = MathLayoutEngine.Layout(_document, g);

            // The parser builds a real (generic) CssBox for every MathML child element so
            // MathTreeBuilder can read tag names/attributes off them - but once the presentation tree
            // above is built, those boxes must not stick around: the base CssBox layout/paint pipeline
            // (which this class does not override) treats a box with children as a container and skips
            // populating its own Rectangles/line-box bookkeeping, breaking this box's own background/
            // border/content positioning. Clearing them makes this box a true leaf (one phantom word,
            // no children), exactly like CssBoxSvg/CssBoxImage.
            Boxes.Clear();
            _source = null;
        }
    }
}
