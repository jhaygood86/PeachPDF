// Derived from PDFsharp (MIT) - see LICENSE.md

using PeachDrawing.PDF.Engine.Drawing;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PeachDrawing.PDF.Engine.Advanced;

/// <summary>
/// Contains all used images of a document.
/// </summary>
internal sealed class PdfImageTable : PdfResourceTable
{
    /// <summary>
    /// Initializes a new instance of this class, which is a singleton for each document.
    /// </summary>
    public PdfImageTable(PdfDocument document)
        : base(document)
    { }

    /// <summary>
    /// Gets a PdfImage from an PdfRasterImage, sized/embedded for use at the given on-page display size (PDF
    /// points). If no matching PdfImage already exists, a new one is created.
    /// </summary>
    public PdfImage GetImage(PdfRasterImage image, double widthPt, double heightPt)
    {
        ImageSelector selector;
        int? targetWidth = null;
        int? targetHeight = null;

        if (!Owner.Options.DownscaleImages)
        {
            // No resize ever applies while downscaling is off, so the selector never depends on
            // display size - the original single-selector-per-PdfRasterImage cache, unconditionally (but see
            // GetPlainSelector's own remarks on why "unconditionally" still has to check Interpolate).
            selector = GetPlainSelector(image);
        }
        else if (image._lastSizedSelector is not null
            && image._lastSizedSelectorWidthPt == widthPt
            && image._lastSizedSelectorHeightPt == heightPt
            && image._lastSizedSelector.Interpolate == image.Interpolate)
        {
            // Same (image, display size, Interpolate) as the immediately preceding call - e.g. a logo
            // redrawn identically across a repeating header/footer - reuse the selector without
            // recomputing ComputeTargetPixelSize or allocating a new ImageSelector. Safe to skip
            // straight to the dictionary lookup below without targetWidth/targetHeight: this selector,
            // if it needed a resize, is already a key in _images from whichever call first computed
            // it, so the TryGetValue below is guaranteed to hit and a fresh PdfImage is never
            // constructed here.
            selector = image._lastSizedSelector;
        }
        else
        {
            (targetWidth, targetHeight) = ComputeTargetPixelSize(image, widthPt, heightPt);
            selector = targetWidth.HasValue
                ? new ImageSelector(image, targetWidth, targetHeight)
                : GetPlainSelector(image);

            image._lastSizedSelector = selector;
            image._lastSizedSelectorWidthPt = widthPt;
            image._lastSizedSelectorHeightPt = heightPt;
        }

        if (!_images.TryGetValue(selector, out PdfImage? pdfImage))
        {
            pdfImage = new PdfImage(Owner, image, targetWidth, targetHeight);
            //pdfImage.Document = _document;
            Debug.Assert(pdfImage.Owner == Owner);
            _images[selector] = pdfImage;
        }
        return pdfImage;
    }

    /// <summary>
    /// Returns <see cref="PdfRasterImage._selector"/>, recomputing it first if it's missing or was built for a
    /// different <see cref="PdfRasterImage.Interpolate"/> than <paramref name="image"/> currently has. The
    /// cached field is a plain <c>??=</c> - fine while an PdfRasterImage had exactly one consumer, but an
    /// PdfRasterImage can now be shared across draw sites that transiently toggle Interpolate around one draw
    /// call (a repeating background tile, a border-image slice - see ImageSelector's own remarks), so
    /// a stale cached selector built under the wrong Interpolate would otherwise wrongly identify
    /// (and merge into) a PdfImage that doesn't match the image's current Interpolate request.
    /// </summary>
    private static ImageSelector GetPlainSelector(PdfRasterImage image)
    {
        if (image._selector is null || image._selector.Interpolate != image.Interpolate)
            image._selector = new ImageSelector(image);

        return image._selector;
    }

    /// <summary>
    /// Computes the pixel size to resize <paramref name="image"/> to before embedding, or
    /// <c>(null, null)</c> when no resize should happen: the image is CMYK
    /// (<see cref="PdfRasterImage.IsCmyk"/>), it's a PNG that <see cref="PdfImage.InitializeJpeg"/> is about to
    /// embed via byte-for-byte pass-through regardless of resize (see <see cref="IsPngPinnedToNaturalSize"/>),
    /// downscaling is off (<see cref="PdfDocumentOptions.DownscaleImages"/>), the display size isn't
    /// known/positive, or the image's natural size is already no larger than the (multiplier-adjusted)
    /// display size. Never upscales - the result is always clamped to the image's own natural pixel
    /// dimensions.
    /// </summary>
    private (int? width, int? height) ComputeTargetPixelSize(PdfRasterImage image, double widthPt, double heightPt)
    {
        // A CMYK image is always embedded at its natural pixel size, regardless of DownscaleImages or
        // display size - a CMYK JPEG embeds via byte-for-byte pass-through (PdfImage.EmbedJpegPassthrough,
        // reached via InitializeJpeg's fast path), which by definition can't be resized (PeachImage
        // has no CMYK JPEG encoder to re-encode a resized copy with either); a CMYK TIFF embeds its
        // decoded pixel buffer directly (PdfImage.InitializeCmykRaster, issue #1096) with no resize
        // step of its own. See issue #1085's plan notes on why this doesn't extend to an RGB/Gray
        // image with an embedded ICC profile - resizing that is fine, it just forfeits the
        // ICC-preserving pass-through for that specific embed (PdfImage.InitializeJpeg).
        if (image.IsCmyk) return (null, null);

        // Raster-backend output is rendered at a deliberately chosen physical resolution; resampling it
        // to the display size would throw that resolution away (see PdfRasterImage.IsRasterOutput).
        if (image.IsRasterOutput) return (null, null);

        if (IsJxlPinnedToNaturalSize(image)) return (null, null);
        if (IsPngPinnedToNaturalSize(image)) return (null, null);
        if (IsGifPinnedToNaturalSize(image)) return (null, null);

        if (!Owner.Options.DownscaleImages) return (null, null);
        if (!(widthPt > 0) || !(heightPt > 0)) return (null, null);

        var naturalWidth = image.PixelWidth;
        var naturalHeight = image.PixelHeight;
        if (naturalWidth <= 0 || naturalHeight <= 0) return (null, null);

        var multiplier = Owner.Options.MaximumDownscaleMultiplier;
        var widthPx = widthPt / PeachDrawing.PDF.Units.PointsPerCssPixel * multiplier;
        var heightPx = heightPt / PeachDrawing.PDF.Units.PointsPerCssPixel * multiplier;

        // Round rather than ceiling: the target size also doubles as the dedup key (see GetImage),
        // so two boxes a document author considers "the same display size" but that resolve to
        // e.g. 99.98px and 100.02px through different layout paths (a percentage width vs. a flex
        // basis, say) round to the same target and so still dedup to one embed, rather than
        // ceiling-splitting them into 100px/101px and two full copies. The up-to-half-pixel
        // softness this can add is well within MaximumDownscaleMultiplier's own headroom margin.
        var targetWidth = Math.Clamp((int)Math.Round(widthPx, MidpointRounding.AwayFromZero), 1, naturalWidth);
        var targetHeight = Math.Clamp((int)Math.Round(heightPx, MidpointRounding.AwayFromZero), 1, naturalHeight);

        if (targetWidth >= naturalWidth && targetHeight >= naturalHeight) return (null, null);

        return (targetWidth, targetHeight);
    }

    /// <summary>
    /// Whether <paramref name="image"/> is a pass-through-eligible PNG that <see cref="PdfImage.InitializeJpeg"/>
    /// is about to embed via <c>EmbedPngPassthrough</c> regardless of any resize target this method
    /// would otherwise compute - mirrors that method's own fast-path condition exactly, so this method
    /// never computes a resize the embed layer would just ignore.
    /// </summary>
    /// <remarks>
    /// Under <see cref="ImageCompression.Auto"/>, every pass-through-eligible PNG is pinned - by
    /// definition not resizable, same as CMYK. Under <see cref="ImageCompression.Lossless"/>, none are:
    /// a downscaled eligible PNG should still shrink (forfeiting pass-through for that specific embed
    /// in favor of a decode+resize+FlateDecode re-embed, exactly the same "resize forfeits pass-through"
    /// trade already made for an ICC-carrying RGB/Gray JPEG - see <c>IsLosslessSourceFormat</c>'s
    /// fallback in <see cref="PdfImage.InitializeJpeg"/>), or Lossless's own "still shrinks a downscaled
    /// lossless source" promise would be silently broken for the one format (PNG) that actually has a
    /// pass-through mechanism to forfeit. Under <see cref="ImageCompression.Lossy"/>, an <em>opaque</em>
    /// pass-through-eligible PNG resizes normally into the lossy JPEG path (the whole point of that
    /// mode) - but one with a <c>PngPassthroughData.ColorKeyMask</c> or <c>AlphaIdatData</c> (issue
    /// #1109 - a real per-pixel alpha channel split via <c>PngAlphaSplit</c>) is pinned even there:
    /// JPEG cannot represent either at all, so <see cref="PdfImage.InitializeJpeg"/> always takes the
    /// pass-through fast path for it regardless of <c>ImageCompression</c> (the same "the format can't
    /// hold this, so the setting doesn't apply" treatment).
    /// </remarks>
    private bool IsJxlPinnedToNaturalSize(PdfRasterImage image) =>
        image.JxlPassthrough is not null
        && Owner.Options.UseJxlPassthrough
        && Owner.Options.ImageCompression != ImageCompression.Lossy;

    private bool IsPngPinnedToNaturalSize(PdfRasterImage image)
    {
        if (image.PngPassthrough is not { } pngPassthrough) return false;

        return Owner.Options.ImageCompression switch
        {
            ImageCompression.Auto => true,
            ImageCompression.Lossy => pngPassthrough.ColorKeyMask is not null || pngPassthrough.AlphaIdatData is not null,
            _ => false,
        };
    }

    /// <summary>Same reasoning as <see cref="IsPngPinnedToNaturalSize"/>, mirrored for GIF pass-through (issue #1110).</summary>
    private bool IsGifPinnedToNaturalSize(PdfRasterImage image)
    {
        if (image.GifPassthrough is not { } gifPassthrough) return false;

        return Owner.Options.ImageCompression switch
        {
            ImageCompression.Auto => true,
            ImageCompression.Lossy => gifPassthrough.ColorKeyMask is not null,
            _ => false,
        };
    }

    /// <summary>
    /// Map from ImageSelector to PdfImage.
    /// </summary>
    readonly Dictionary<ImageSelector, PdfImage> _images = new Dictionary<ImageSelector, PdfImage>();

    /// <summary>
    /// A collection of information that uniquely identifies a particular PdfImage. When a resize
    /// target is in play, the target pixel size is part of the identity - the same source image used
    /// at two different display sizes embeds as two distinct PdfImages, each correctly sized, rather
    /// than one embed at whichever size happened to be requested first. <see cref="PdfRasterImage.Interpolate"/>
    /// is part of the identity for the same reason: an <see cref="PdfRasterImage"/> can now be shared across
    /// unrelated draw sites (the same decoded image referenced by more than one HTML element - see
    /// <c>PeachPDF.Html.Core.HtmlContainerInt</c>'s resolved-image cache) that each toggle it
    /// transiently around their own draw call (e.g. a repeating background tile forcing it off). Since
    /// a <see cref="PdfImage"/>'s own <c>/Interpolate</c> key is baked in once, at whichever call first
    /// creates it for a given selector, folding the flag into the selector keeps a tile draw's "off"
    /// request from being permanently inherited by every other draw of the same shared image at the
    /// same size - it simply gets its own <see cref="PdfImage"/> instead.
    /// </summary>
    internal class ImageSelector
    {
        /// <summary>
        /// Initializes a new instance of ImageSelector from an PdfRasterImage, with no resize target.
        /// </summary>
        public ImageSelector(PdfRasterImage image)
        {
            // HACK: implement a way to identify images when they are reused
            // TODO 4STLA Implementation that calculates MD5 hashes for images generated for the images can be found here: http://forum.pdfsharp.net/viewtopic.php?p=6959#p6959
            if (image._path is null)
                image._path = "*" + Guid.NewGuid().ToString("B");

            // HACK: just use full path to identify
            _path = image._path.ToLowerInvariant();
            _interpolate = image.Interpolate;
        }

        /// <summary>
        /// Initializes a new instance of ImageSelector from an PdfRasterImage and the pixel size it will be
        /// resized to before embedding.
        /// </summary>
        public ImageSelector(PdfRasterImage image, int? targetWidth, int? targetHeight) : this(image)
        {
            _targetWidth = targetWidth;
            _targetHeight = targetHeight;
        }

        public string Path
        {
            get => _path;
            set => _path = value;
        }
        string _path;
        readonly int? _targetWidth;
        readonly int? _targetHeight;
        readonly bool _interpolate;

        /// <summary>The <see cref="PdfRasterImage.Interpolate"/> value this selector was built from - read by
        /// <see cref="GetPlainSelector"/>/<see cref="GetImage"/> to detect a stale cached selector.</summary>
        public bool Interpolate => _interpolate;

        public override bool Equals(object? obj)
        {
            ImageSelector? selector = obj as ImageSelector;
            if (selector is null)
                return false;
            return _path == selector._path
                && _targetWidth == selector._targetWidth
                && _targetHeight == selector._targetHeight
                && _interpolate == selector._interpolate;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_path, _targetWidth, _targetHeight, _interpolate);
        }
    }
}
