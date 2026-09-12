using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.MathML;
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

        public CssBoxMath(CssBox? parent, HtmlTag tag)
            : base(parent, tag)
        {
            _mathWord = new CssRectMath(this);
            Words.Add(_mathWord);
        }

        internal override ValueTask MeasureWordsSize(RGraphics g)
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
                var pixelsPerPoint = (HtmlContainer?.Adapter as PdfSharpAdapter)?.PixelsPerPoint ?? 1.0;

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
        internal void EnsureLayout(RGraphics g)
        {
            if (_layout is not null)
                return;

            var sourceNode = new CssBoxMathSourceNode(this);
            _serializedSource = MathMlSerializer.Serialize(sourceNode);
            _document = MathTreeBuilder.Build(sourceNode, HtmlContainer!.Adapter);
            _layout = MathLayoutEngine.Layout(_document, g);

            // The parser builds a real (generic) CssBox for every MathML child element so
            // MathTreeBuilder can read tag names/attributes off them - but once the presentation tree
            // above is built, those boxes must not stick around: the base CssBox layout/paint pipeline
            // (which this class does not override) treats a box with children as a container and skips
            // populating its own Rectangles/line-box bookkeeping, breaking this box's own background/
            // border/content positioning. Clearing them makes this box a true leaf (one phantom word,
            // no children), exactly like CssBoxSvg/CssBoxImage.
            Boxes.Clear();
        }
    }
}
