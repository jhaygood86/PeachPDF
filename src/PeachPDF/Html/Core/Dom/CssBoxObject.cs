using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Svg;
using System;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// CSS box for an <c>&lt;object&gt;</c> element, implementing (a static-renderer subset of) the
    /// HTML "replacement algorithm": if <c>data</c> resolves to a supported image, this box becomes a
    /// replaced element exactly like <see cref="CssBoxImage"/> and its DOM children (the fallback
    /// content) are discarded; otherwise it stays an ordinary container and those children - which may
    /// themselves be nested <c>&lt;object&gt;</c>/<c>&lt;img&gt;</c> elements - are laid out/painted
    /// normally, recursively re-running the same resolution. Mirrors <see cref="CssBoxSvg"/>'s lazy
    /// success/failure resolution pattern, since (like SVG) an object's children are always parsed
    /// eagerly by <see cref="Parse.HtmlParser"/> before it's known whether they're needed.
    /// </summary>
    internal class CssBoxObject : CssBox
    {
        private CssRectImage? _imageWord;
        private ImageLoadHandler? _imageLoadHandler;
        private SvgDocument? _svgDocument;
        private bool _resolved;
        private bool _isReplaced;

        public CssBoxObject(CssBox? parent, HtmlTag tag)
            : base(parent, tag)
        {
        }

        internal override async ValueTask MeasureWordsSize(RGraphics g)
        {
            if (_wordsSizeMeasured)
                return;

            await EnsureResolved();

            if (!_isReplaced)
            {
                await base.MeasureWordsSize(g);
                return;
            }

            // Replaced content takes this shortcut instead of the base implementation, but this box
            // can still have its OWN CSS background (painted around/behind the replaced image content)
            // - see EnsureAuxiliaryImagesLoadedAsync's doc comment for the real bug this fixes.
            await EnsureAuxiliaryImagesLoadedAsync();

            MeasureWordSpacing(g);
            _wordsSizeMeasured = true;

            if (_svgDocument is not null)
            {
                var (intrinsicWidth, intrinsicHeight) = SvgIntrinsicSize.Resolve(_svgDocument);
                CssLayoutEngine.MeasureIntrinsicSize(_imageWord!, intrinsicWidth, intrinsicHeight);
            }
            else
            {
                CssLayoutEngine.MeasureImageSize(_imageWord!);
            }
        }

        protected override async ValueTask PaintImpCore(RGraphics g)
        {
            await EnsureResolved();

            if (!_isReplaced)
            {
                await base.PaintImpCore(g);
                return;
            }

            var rect = CommonUtils.GetFirstValueOrDefault(Rectangles);
            var offset = RPoint.Empty;

            if (!IsFixed)
                offset = HtmlContainer!.ScrollOffset;

            rect.Offset(offset);

            var clipped = RenderUtils.ClipGraphicsByOverflow(g, this);

            PaintBackground(g, rect, true);
            BordersDrawHandler.DrawBoxBorders(g, this, rect, true, true);

            var r = _imageWord!.Rectangle;
            r.Offset(offset);
            r.Height -= ActualBorderTopWidth + ActualBorderBottomWidth + ActualPaddingTop + ActualPaddingBottom;
            r.Y += ActualBorderTopWidth + ActualPaddingTop;
            r.X = Math.Floor(r.X);
            r.Y = Math.Floor(r.Y);

            // object-fit / object-position honored via the shared replaced-content renderer.
            ReplacedContentRenderer.Paint(g, r, _imageWord.Image, _svgDocument, this);

            if (clipped)
                g.PopClip();
        }

        /// <summary>
        /// Resolves whether this object is replaced content (a supported image) or must fall back to
        /// its DOM children, without ever performing a network fetch for a <c>data</c> resource whose
        /// declared type (via the <c>type</c> attribute, or a <c>data:</c> URI's own MIME header) is
        /// already known not to be an image PeachPDF can decode - this keeps object-fallback
        /// resolution deterministic and independent of network reachability, which matters for the
        /// real Acid2 fixture's intentionally-unreachable middle <c>&lt;object&gt;</c>.
        /// </summary>
        private async ValueTask EnsureResolved()
        {
            if (_resolved)
                return;
            _resolved = true;

            var source = ResolveReplacedSource();
            if (string.IsNullOrEmpty(source))
            {
                _isReplaced = false;
                return;
            }

            _imageLoadHandler = new ImageLoadHandler(HtmlContainer!);
            await _imageLoadHandler.LoadImage(source);

            if (_imageLoadHandler.Image is null && _imageLoadHandler.SvgDocument is null)
            {
                _isReplaced = false;
                return;
            }

            _isReplaced = true;
            _svgDocument = _imageLoadHandler.SvgDocument;
            _imageWord = new CssRectImage(this) { Image = _imageLoadHandler.Image };
            Words.Add(_imageWord);

            // Success: this box is now a replaced leaf, like CssBoxImage - the fallback DOM children
            // are no longer relevant and must not be laid out/painted (see CssBoxSvg.EnsureDocument
            // for why clearing here, rather than never creating them, is the correct timing).
            Boxes.Clear();
        }

        /// <summary>
        /// Returns the URL of the resource to render as replaced content, or <c>null</c> to fall back to
        /// the element's DOM children. The <c>&lt;object&gt;</c> implementation resolves <c>data</c> and,
        /// per the HTML replacement algorithm, declines (returns null) when the <c>type</c> attribute or a
        /// <c>data:</c> URI's own MIME header already says the resource isn't an image. Overridden by
        /// <see cref="CssBoxVideo"/> to resolve the <c>poster</c> instead.
        /// </summary>
        protected virtual string? ResolveReplacedSource()
        {
            var data = GetAttribute("data", null);
            if (string.IsNullOrEmpty(data))
                return null;

            var type = GetAttribute("type", null);
            if (!string.IsNullOrEmpty(type) && !type.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return null;

            if (DataUriUtils.TryDecodeDataUri(data, out var mimeType, out _)
                && !mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return null;

            return data;
        }

        public override void Dispose()
        {
            _imageLoadHandler?.Dispose();
            base.Dispose();
        }
    }
}
