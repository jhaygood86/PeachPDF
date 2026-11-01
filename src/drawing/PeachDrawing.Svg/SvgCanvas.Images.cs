using PeachDrawing.Core;
using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace PeachDrawing.Svg;

public sealed partial class SvgCanvas
{
    /// <summary>Images that did not come from this package (a raster canvas's tile, say), converted once to something that can be embedded.</summary>
    private static readonly ConditionalWeakTable<Image, SvgImage> ForeignImages = new();

    /// <summary>
    /// The id the document defines <paramref name="image"/> under: a decoded image as an <c>&lt;image&gt;</c> holding its encoded bytes, a
    /// tile as the vector group that was drawn into it. Defined once, however often it is drawn; every draw is a <c>&lt;use&gt;</c> of it.
    /// </summary>
    internal string ImageId(Image image)
    {
        switch (image)
        {
            case SvgImage svg:
                return _defs.DefineOnce(svg, "i", id =>
                    $"<image id=\"{id}\" width=\"{N(svg.Width)}\" height=\"{N(svg.Height)}\" preserveAspectRatio=\"none\" href=\"{svg.DataUri}\"/>");

            case SvgTileImage tile:
                var content = tile.Canvas.CompleteContent();
                return _defs.DefineOnce(tile, "t", id =>
                {
                    var clip = _defs.Define("c", $"<clipPath id=\"{{id}}\"><rect width=\"{N(tile.Width)}\" height=\"{N(tile.Height)}\"/></clipPath>");
                    return $"<g id=\"{id}\" clip-path=\"url(#{clip})\">\n{content}</g>";
                });

            default:
                var converted = ForeignImages.GetValue(image, static foreign =>
                    foreign.GetPixels() is { } pixels
                        ? SvgImage.FromPixels(pixels)
                        : throw new NotSupportedException("An image drawn on an SVG canvas must come from the SVG render context, a tile of this canvas, or expose its pixels."));
                return ImageId(converted);
        }
    }

    /// <summary>The <c>image-rendering</c> declaration for drawing <paramref name="image"/> with <paramref name="sampling"/>, or null for the viewer's smooth default.</summary>
    private static string? Rendering(Image image, ImageSampling sampling)
    {
        if (image is SvgTileImage)
            return null;

        var crisp = sampling is ImageSampling.Nearest or ImageSampling.Pixelated || sampling == ImageSampling.Automatic && !image.Interpolate;
        return crisp ? "image-rendering:pixelated" : null;
    }

    private string RenderingStyle(Image image, ImageSampling sampling) =>
        Rendering(image, sampling) is { } rendering ? $" style=\"{rendering}\"" : string.Empty;

    /// <summary>
    /// The element that draws <paramref name="image"/> (or <paramref name="source"/>, a part of it, in the image's own units) into
    /// <paramref name="dest"/>. <paramref name="attributes"/> go on its outermost element, which is a group when the image has to be clipped
    /// to the part shown. Empty when there is nothing to draw.
    /// </summary>
    private string ImageMarkup(Image image, Rect dest, Rect? source, ImageSampling sampling, string attributes = "", PaintBlendMode? blend = null)
    {
        double width = image.Width, height = image.Height;
        if (!(width > 0) || !(height > 0) || !(dest.Width > 0) || !(dest.Height > 0))
            return string.Empty;

        var src = source ?? new Rect(0, 0, width, height);
        if (!(src.Width > 0) || !(src.Height > 0))
            return string.Empty;

        var sx = dest.Width / src.Width;
        var sy = dest.Height / src.Height;
        var tx = dest.X - src.X * sx;
        var ty = dest.Y - src.Y * sy;
        var id = ImageId(image);
        var matrix = $"matrix({SvgDefs.Factor(sx)} 0 0 {SvgDefs.Factor(sy)} {N(tx)} {N(ty)})";
        var rendering = Rendering(image, sampling);

        var clipped = source is { } s && (s.X > 0 || s.Y > 0 || s.Right < width || s.Bottom < height);

        // The image is placed by a transform on its own element, and a clip is not allowed to share that element: the clip's
        // coordinates would be the transformed ones.
        if (!clipped && FlatClipAttribute().Length > 0)
            return $"<g{attributes}{Style(null, blend)}><use href=\"#{id}\" transform=\"{matrix}\"{(rendering is null ? string.Empty : $" style=\"{rendering}\"")}/></g>";

        if (!clipped)
            return $"<use href=\"#{id}\" transform=\"{matrix}\"{attributes}{Style(rendering, blend)}/>";

        var clip = _defs.Define("c", $"<clipPath id=\"{{id}}\"><rect x=\"{N(dest.X)}\" y=\"{N(dest.Y)}\" width=\"{N(dest.Width)}\" height=\"{N(dest.Height)}\"/></clipPath>");
        return $"<g clip-path=\"url(#{clip})\"{attributes}{Style(null, blend)}><use href=\"#{id}\" transform=\"{matrix}\"{(rendering is null ? string.Empty : $" style=\"{rendering}\"")}/></g>";
    }

    private void Place(string markup)
    {
        if (markup.Length > 0)
            Line(markup);
    }

    /// <inheritdoc/>
    public override void DrawImage(Image image, Rect destRect) => Place(ImageMarkup(image, destRect, null, ImageSampling.Automatic));

    /// <inheritdoc/>
    public override void DrawImage(Image image, Rect destRect, Rect srcRect) => Place(ImageMarkup(image, destRect, srcRect, ImageSampling.Automatic));

    /// <inheritdoc/>
    public override void DrawImage(Image image, Rect destRect, ImageSampling sampling) => Place(ImageMarkup(image, destRect, null, sampling));

    /// <inheritdoc/>
    public override void DrawImage(Image image, Rect destRect, Rect srcRect, ImageSampling sampling) => Place(ImageMarkup(image, destRect, srcRect, sampling));

    /// <inheritdoc/>
    public override void DrawImageWithOpacity(Image image, Rect destRect, double opacity, PaintBlendMode blendMode = PaintBlendMode.Normal)
    {
        var attributes = opacity < 1 ? $" opacity=\"{N(Math.Clamp(opacity, 0, 1))}\"" : string.Empty;
        Place(ImageMarkup(image, destRect, null, ImageSampling.Automatic, attributes, blendMode == PaintBlendMode.Normal ? null : blendMode));
    }

    /// <inheritdoc/>
    public override void DrawImageBlendedOver(Image top, Image bottom, Rect destRect, PaintBlendMode blendMode)
    {
        Line($"<g{Style("isolation:isolate")}>");
        _depth++;
        Place(ImageMarkup(bottom, destRect, null, ImageSampling.Automatic, blend: PaintBlendMode.Normal));
        Place(ImageMarkup(top, destRect, null, ImageSampling.Automatic, blend: blendMode));
        _depth--;
        Line("</g>");
    }

    /// <inheritdoc/>
    public override void DrawImageWithColorMatrix(Image image, Rect destRect, ColorMatrix matrix)
    {
        var inner = ImageMarkup(image, destRect, null, ImageSampling.Automatic, blend: PaintBlendMode.Normal);
        if (inner.Length == 0)
            return;

        var filter = RegionFilter(destRect, ColorMatrixPrimitive(matrix));
        Line($"<g filter=\"url(#{filter})\"{Style()}>");
        _depth++;
        Line(inner);
        _depth--;
        Line("</g>");
    }

    /// <inheritdoc/>
    public override void DrawImageMasked(Image image, Image maskImage, Rect destRect) => DrawMasked(image, maskImage, destRect, alphaMask: false, invert: false);

    /// <inheritdoc/>
    public override void DrawImageAlphaMasked(Image image, Image maskImage, Rect destRect, bool invert = false) => DrawMasked(image, maskImage, destRect, alphaMask: true, invert);

    /// <summary>
    /// Draws the image through a mask. A luminosity mask is an SVG luminance mask; an alpha mask is the same with the mask image first
    /// turned into white of its own (or its inverse) opacity, so its alpha is what the luminance reads.
    /// </summary>
    private void DrawMasked(Image image, Image maskImage, Rect dest, bool alphaMask, bool invert)
    {
        var content = ImageMarkup(image, dest, null, ImageSampling.Automatic, blend: PaintBlendMode.Normal);
        var maskContent = ImageMarkup(maskImage, dest, null, ImageSampling.Automatic, blend: PaintBlendMode.Normal);
        if (content.Length == 0 || maskContent.Length == 0)
            return;

        if (alphaMask)
        {
            var filter = RegionFilter(dest, $"<feColorMatrix type=\"matrix\" values=\"0 0 0 0 1 0 0 0 0 1 0 0 0 0 1 0 0 0 {(invert ? "-1 1" : "1 0")}\"/>");
            maskContent = $"<g filter=\"url(#{filter})\">{maskContent}</g>";
        }

        var mask = _defs.Define("m",
            $"<mask id=\"{{id}}\" maskUnits=\"userSpaceOnUse\" x=\"{N(dest.X)}\" y=\"{N(dest.Y)}\" width=\"{N(dest.Width)}\" height=\"{N(dest.Height)}\" color-interpolation=\"sRGB\">{maskContent}</mask>");
        Line($"<g mask=\"url(#{mask})\"{Style()}>");
        _depth++;
        Line(content);
        _depth--;
        Line("</g>");
    }

    /// <summary>A filter confined to <paramref name="region"/> (user space) running <paramref name="primitives"/> in sRGB, like the raster canvas's pixel maths.</summary>
    private string RegionFilter(Rect region, string primitives) =>
        _defs.Define("f",
            $"<filter id=\"{{id}}\" filterUnits=\"userSpaceOnUse\" x=\"{N(region.X)}\" y=\"{N(region.Y)}\" width=\"{N(region.Width)}\" height=\"{N(region.Height)}\" color-interpolation-filters=\"sRGB\">{primitives}</filter>");
}
