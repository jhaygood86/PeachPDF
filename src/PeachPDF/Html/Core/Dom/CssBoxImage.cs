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

using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Svg;
using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// CSS box for image element.
    /// </summary>
    internal sealed class CssBoxImage : CssBox
    {
        /// <summary>
        /// the image word of this image box
        /// </summary>
        private readonly CssRectImage _imageWord;

        /// <summary>
        /// handler used for image loading by source
        /// </summary>
        private ImageLoadHandler? _imageLoadHandler;

        /// <summary>
        /// the parsed SVG scene graph, set instead of <see cref="Image"/> when the src was detected to
        /// be an SVG image (by file extension or <c>Content-Type: image/svg+xml</c>).
        /// </summary>
        private SvgDocument? _svgDocument;

        /// <summary>
        /// True once this box's content was injected directly (<see cref="SetDecodedContent"/>), either
        /// as static bytes/markup already decoded at declarative-build time or as the resolved output of
        /// a dynamic content callback (<see cref="SetDynamicRasterContent"/>/<see cref="SetDynamicSvgContent"/>).
        /// <see cref="MeasureWordsSize"/> checks this before ever creating an <see cref="ImageLoadHandler"/> -
        /// a directly-injected box has no <c>src</c> to load from at all.
        /// </summary>
        private bool _contentInjectedDirectly;

        /// <summary>
        /// A pending declarative <c>Image(Func&lt;PdfSize,byte[]&gt;)</c> callback - resolved into a real
        /// <see cref="RImage"/> the first time <see cref="MeasureWordsSize"/> runs, once this box's own
        /// definite size is known (see <see cref="TryResolveDefiniteSize"/>), then cleared so the callback
        /// never runs a second time. See <c>ContainerBuilder.Image(Func&lt;PdfSize,byte[]&gt;)</c>.
        /// </summary>
        private Func<PdfSize, byte[]>? _dynamicRasterResolver;

        /// <summary>Same as <see cref="_dynamicRasterResolver"/>, for a declarative <c>Svg(Func&lt;PdfSize,string&gt;)</c> callback.</summary>
        private Func<PdfSize, string>? _dynamicSvgResolver;

        /// <summary>
        /// Init.
        /// </summary>
        /// <param name="parent">the parent box of this box</param>
        /// <param name="tag">the html tag data of this box</param>
        public CssBoxImage(CssBox? parent, HtmlTag tag)
            : base(parent, tag)
        {
            _imageWord = new CssRectImage(this);
            Words.Add(_imageWord);
        }

        /// <summary>
        /// Get the image of this image box.
        /// </summary>
        public RImage? Image => _imageWord.Image;

        public string ImageSource => GetAttribute("src");

        /// <summary>
        /// The phantom word carrying this box's replaced content, whose own rectangle positions the
        /// image within the box. Read by <c>ImageFragmentPainter</c>.
        /// </summary>
        internal CssRectImage ReplacedWord => _imageWord;

        /// <summary>
        /// The parsed SVG scene graph when the source was detected to be an SVG image, else null.
        /// </summary>
        internal SvgDocument? SvgDocument => _svgDocument;

        /// <summary>
        /// Assigns words its width and height
        /// </summary>
        /// <param name="g">the device to use</param>
        internal override async ValueTask MeasureWordsSize(RGraphics g)
        {
            if (!_wordsSizeMeasured)
            {
                // This replaced element (the <img> itself) takes a shortcut instead of the base
                // implementation below, but can still have its OWN CSS background (painted around/
                // behind the replaced image content) - see EnsureAuxiliaryImagesLoadedAsync's doc
                // comment for the bug this fixes (a sibling of this one, for CssBoxObject).
                await EnsureAuxiliaryImagesLoadedAsync();

                // A dynamic Image(Func<PdfSize,byte[]>)/Svg(Func<PdfSize,string>) callback resolves here,
                // once (not on every layout pass - the resolvers are cleared as soon as they run), now
                // that this box's own definite size is known - see ResolveDynamicContent's own remarks on
                // why here is the earliest that's true.
                if (_dynamicRasterResolver is not null || _dynamicSvgResolver is not null)
                {
                    ResolveDynamicContent();
                }

                // Static in-memory content (Image(byte[])/Svg(string), a shared PdfImage, or a dynamic
                // callback's own resolved output above) was already injected directly onto this box - no
                // src to load, so ImageLoadHandler never gets created at all for this box.
                if (!_contentInjectedDirectly && _imageLoadHandler == null)
                {
                    _imageLoadHandler = new ImageLoadHandler(HtmlContainer!);

                    if (Content != Keywords.Normal)
                    {
                        var imageContent = CssValueParser.GetImagePropertyValue(Content);

                        if (imageContent is CssImage.Url urlImage)
                        {
                            await _imageLoadHandler.LoadImage(urlImage.Href);
                        }

                    }
                    else
                        await _imageLoadHandler.LoadImage(ImageSource);

                    OnLoadImageComplete();
                }

                MeasureWordSpacing(g);
                _wordsSizeMeasured = true;
            }

            if (_svgDocument is not null)
            {
                var (intrinsicWidth, intrinsicHeight) = SvgIntrinsicSize.Resolve(_svgDocument);
                CssLayoutEngine.MeasureIntrinsicSize(_imageWord, intrinsicWidth, intrinsicHeight);
            }
            else
            {
                CssLayoutEngine.MeasureImageSize(_imageWord);
            }
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public override void Dispose()
        {
            _imageLoadHandler?.Dispose();
            base.Dispose();
        }

        /// <summary>
        /// On image load process is complete with image (or SVG document, or neither on failure)
        /// update the image box.
        /// </summary>
        private void OnLoadImageComplete()
        {
            _imageWord.Image = _imageLoadHandler!.Image;
            _svgDocument = _imageLoadHandler.SvgDocument;
            _wordsSizeMeasured = false;
        }

        /// <summary>
        /// Injects already-decoded content directly, bypassing <see cref="ImageLoadHandler"/>/a
        /// <c>src</c>/data-URI round trip entirely - the declarative API's <c>Image(byte[])</c>/
        /// <c>Svg(string)</c> (decoded eagerly at build time), a resolved dynamic-content callback (see
        /// <see cref="ResolveDynamicContent"/>), and a shared, resolve-once <c>PdfImage</c> all funnel
        /// through here. Exactly one of <paramref name="image"/>/<paramref name="svgDocument"/> should be
        /// non-null (mirrors <see cref="OnLoadImageComplete"/>'s own shape - a src ever resolves to one
        /// or the other, never both).
        /// </summary>
        internal void SetDecodedContent(RImage? image, SvgDocument? svgDocument)
        {
            _imageWord.Image = image;
            _svgDocument = svgDocument;
            _contentInjectedDirectly = true;
            _wordsSizeMeasured = false;
        }

        /// <summary>
        /// Stores a pending declarative <c>Image(Func&lt;PdfSize,byte[]&gt;)</c> callback, resolved by
        /// <see cref="ResolveDynamicContent"/> the first time <see cref="MeasureWordsSize"/> runs. Marks
        /// this box as directly-injected immediately (not only once the callback actually runs), so
        /// <see cref="MeasureWordsSize"/> never creates an <see cref="ImageLoadHandler"/> for it even
        /// before the callback has had a chance to resolve.
        /// </summary>
        internal void SetDynamicRasterContent(Func<PdfSize, byte[]> resolver)
        {
            _dynamicRasterResolver = resolver;
            _contentInjectedDirectly = true;
            _wordsSizeMeasured = false;
        }

        /// <summary>Same as <see cref="SetDynamicRasterContent"/>, for a declarative <c>Svg(Func&lt;PdfSize,string&gt;)</c> callback.</summary>
        internal void SetDynamicSvgContent(Func<PdfSize, string> resolver)
        {
            _dynamicSvgResolver = resolver;
            _contentInjectedDirectly = true;
            _wordsSizeMeasured = false;
        }

        /// <summary>
        /// Resolves a pending dynamic-content callback into real decoded content, once this box's own
        /// definite size is known. <see cref="MeasureWordsSize"/> is the earliest point that's true: for a
        /// replaced element (one word, always), <see cref="CssLayoutEngine.GetBoxWidth(RGraphics, CssBox, double?)"/> derives this
        /// box's own <c>ActualWidth</c> from that one word's already-measured size whenever the box has
        /// words - so by the time layout would otherwise resolve this box's width/height, it's already too
        /// late (circular). The declarative layer defaults a dynamic content box to <c>width:100%;
        /// height:100%</c> (fill-by-default - "available space" is whatever this box's own parent already
        /// resolves to, matching QuestPDF's own dynamic-image framing), and <see cref="TryResolveDefiniteSize"/>
        /// resolves that percentage against <see cref="CssBox.ContainingBlock"/>'s own size (the parent,
        /// already resolved by ordinary parent-first block-flow ordering) - non-circular for the same
        /// reason this box's own percentage isn't.
        /// </summary>
        /// <remarks>
        /// Once the resolved size is known, this box's own <c>width</c>/<c>height</c> are overwritten from
        /// <c>100%</c> to the resolved absolute point values, rather
        /// than left as a percentage for <see cref="CssLayoutEngine.MeasureIntrinsicSize"/>'s own later
        /// percentage-width/height resolution to re-derive. That re-derivation is a real, separate,
        /// currently-broken code path for a replaced element specifically (confirmed independently of this
        /// feature: a plain HTML <c>&lt;img style="width:100%;height:100%"&gt;</c> inside a definite-size
        /// container resolves to a zero/wrong size too - filed as a tracked follow-up, not something this
        /// PR's own scope should fix) - writing the already-known absolute value here sidesteps it
        /// entirely, onto the same absolute-length branch already proven correct elsewhere in this file.
        /// </remarks>
        /// <exception cref="InvalidOperationException">
        /// Neither this box's own width/height nor a percentage of its <see cref="CssBox.ContainingBlock"/>
        /// resolves to a definite size - the caller needs to give the container an explicit absolute
        /// <c>Width</c>/<c>Height</c>, or place it inside an ancestor that already has one.
        /// </exception>
        private void ResolveDynamicContent()
        {
            if (!TryResolveDefiniteSize(out var size))
            {
                throw new InvalidOperationException(
                    "Dynamic image/SVG content needs a definite size to render at. Set an explicit " +
                    "Width()/Height() on the container this content fills, or place it inside an " +
                    "ancestor whose own size is already definite - the content's own default " +
                    "width:100%/height:100% resolves against that ancestor.");
            }

            Width = string.Create(CultureInfo.InvariantCulture, $"{size.Width}pt");
            Height = string.Create(CultureInfo.InvariantCulture, $"{size.Height}pt");

            var adapter = HtmlContainer!.Adapter;

            if (_dynamicRasterResolver is { } rasterResolver)
            {
                var bytes = rasterResolver(size);
                SetDecodedContent(adapter.ImageFromStream(new MemoryStream(bytes)), null);
                _dynamicRasterResolver = null;
            }
            else if (_dynamicSvgResolver is { } svgResolver)
            {
                var markup = svgResolver(size);
                var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(XElement.Parse(markup)), adapter);
                SetDecodedContent(null, document);
                _dynamicSvgResolver = null;
            }
        }

        /// <summary>
        /// This box's own width/height, resolved to a definite point size. <see cref="SetDynamicRasterContent"/>/
        /// <see cref="SetDynamicSvgContent"/>'s only caller (<c>ContainerBuilder</c>'s fill-by-default
        /// design) always leaves this box's own <c>width</c>/<c>height</c> at the declarative layer's
        /// <c>100%</c> default, so the only case that can actually occur here is a percentage against
        /// <see cref="CssBox.ContainingBlock"/>'s own size - "available space" is whatever the parent
        /// already resolves to (see <see cref="ResolveDynamicContent"/>'s own remarks on why that's
        /// non-circular here, unlike this box's own <c>Words</c>-derived <c>ActualWidth</c>). False when
        /// that size isn't itself definite yet - e.g. a bare <c>Grow()</c> flex item with no explicit
        /// size of its own, still under negotiation.
        /// </summary>
        /// <remarks>
        /// Width and height are resolved differently here, and deliberately so: CSS block/flex layout
        /// resolves inline size (width) top-down - a containing block's own <c>ContainingBlock.Size.Width</c>
        /// is already committed before its children are visited, so reading it directly is safe - but
        /// resolves block size (height) bottom-up by default, so <c>ContainingBlock.Size.Height</c> can
        /// still be provisional (not yet finalized) at this point even when that ancestor has a perfectly
        /// definite, explicit <c>height</c> declared. <see cref="CssLayoutEngine.GetBoxHeight"/> is the
        /// existing, already-used-elsewhere fix for exactly this asymmetry (see its own callers'
        /// "resolve the ancestor's own declared CSS Height directly... only fall back to a provisional
        /// value for an auto-height ancestor" pattern) - it reads the containing block's own declared
        /// <c>Height</c> CSS text straight via <c>CssValueParser.ParseLength</c> when that ancestor has
        /// one, entirely independent of whether <c>Size.Height</c> has been committed by layout yet.
        /// </remarks>
        private bool TryResolveDefiniteSize(out PdfSize size)
        {
            var width = new CssLength(Width);
            var height = new CssLength(Height);

            if (width is not { Number: > 0, IsPercentage: true } ||
                height is not { Number: > 0, IsPercentage: true } ||
                CssLayoutEngine.GetBoxHeight(ContainingBlock) is not { } containingBlockHeight)
            {
                size = default;
                return false;
            }

            size = new PdfSize(width.Number * ContainingBlock.Size.Width, height.Number * containingBlockHeight);
            return true;
        }

        /// <summary>
        /// The parsed <see cref="Svg.SvgDocument"/> (for a <c>&lt;img src="x.svg"&gt;</c>) and its
        /// rendered rectangle in full document space, for <c>&lt;a&gt;</c> link-annotation discovery -
        /// see <see cref="CssBoxSvg.GetLinkSource"/> for why this doesn't reuse the painter's own
        /// (fragmentainer-local) rect computation. Null for an ordinary raster image.
        /// </summary>
        internal (SvgDocument Document, RRect Rect)? GetLinkSource()
        {
            if (_svgDocument is null)
                return null;

            var r = _imageWord.Rectangle;
            r.Height -= ActualBorderTopWidth + ActualBorderBottomWidth + ActualPaddingTop + ActualPaddingBottom;
            r.Y += ActualBorderTopWidth + ActualPaddingTop;
            r.X = Math.Floor(r.X);
            r.Y = Math.Floor(r.Y);

            return r is { Width: > 0, Height: > 0 } ? (_svgDocument, r) : null;
        }
    }
}