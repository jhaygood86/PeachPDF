using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using System;
using System.Linq;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Resolves CSS <c>background-position</c> and <c>background-size</c> per-layer values into
    /// concrete pixel geometry, per CSS Backgrounds and Borders Module Level 3
    /// (https://www.w3.org/TR/css-backgrounds-3/#the-background-position,
    /// https://www.w3.org/TR/css-backgrounds-3/#the-background-size). Grammar/tokenization is shared
    /// with the CSS-OM layer via <see cref="BackgroundPositionGrammar"/>/<see cref="BackgroundSizeGrammar"/>
    /// (see those classes) - this class does only the pixel arithmetic against a runtime box size,
    /// which isn't known until paint time.
    /// </summary>
    internal static class BackgroundLayerResolver
    {
        /// <summary>
        /// Splits a comma-separated multi-layer property value (background-position or
        /// background-size) into its per-layer segments, trimmed. Returns a single-element array
        /// for a value with no commas.
        /// </summary>
        internal static string[] SplitLayers(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return [string.Empty];

            return CssValueParser.SplitTopLevelCommas(value).Select(s => s.Trim()).ToArray();
        }

        /// <summary>
        /// Per CSS Backgrounds §3.8/3.9: if there are fewer layer-values than background-image
        /// layers, the list of values is repeated (cycled) to match.
        /// </summary>
        internal static string LayerAt(string[] layers, int layerIndex)
        {
            if (layers.Length == 0)
                return string.Empty;

            return layers[layerIndex % layers.Length];
        }

        /// <summary>
        /// Resolves one background-size layer value ("auto", "cover", "contain", a single or double
        /// length/percentage/auto) against the background positioning area and the image's intrinsic
        /// dimensions/ratio. Intrinsic width/height/ratio are all null for images with no natural size
        /// concept (CSS gradients, which are "generated images" per spec) - this correctly makes
        /// cover/contain/auto-auto all resolve to exactly the container size for them.
        /// </summary>
        internal static (double Width, double Height) ResolveSize(
            string sizeLayerValue,
            double containerWidth, double containerHeight,
            double? intrinsicWidth, double? intrinsicHeight, double? intrinsicRatio,
            CssBoxProperties box)
        {
            var value = string.IsNullOrWhiteSpace(sizeLayerValue) ? CssConstants.Auto : sizeLayerValue.Trim();
            var hasRatio = intrinsicRatio is > 0;

            var tokens = CssValueParser.GetCssTokens(value);
            var parsed = BackgroundSizeGrammar.TryParse(tokens);
            if (parsed is null)
                return (Math.Max(0, containerWidth), Math.Max(0, containerHeight));

            if (parsed.IsCover || parsed.IsContain)
            {
                if (!hasRatio || containerWidth <= 0 || containerHeight <= 0)
                    return (Math.Max(0, containerWidth), Math.Max(0, containerHeight));

                var ratio = intrinsicRatio!.Value;

                var scaleWidth = containerWidth;
                var scaleHeight = containerWidth / ratio;
                if ((parsed.IsCover && scaleHeight < containerHeight) || (parsed.IsContain && scaleHeight > containerHeight))
                {
                    scaleHeight = containerHeight;
                    scaleWidth = containerHeight * ratio;
                }

                return (scaleWidth, scaleHeight);
            }

            double? resolvedWidth = parsed.Width.IsAuto ? null : Math.Max(0, CssValueParser.ParseLength(parsed.Width.Value.ToValue(), containerWidth, box));
            double? resolvedHeight = parsed.Height.IsAuto ? null : Math.Max(0, CssValueParser.ParseLength(parsed.Height.Value.ToValue(), containerHeight, box));

            if (resolvedWidth is null && resolvedHeight is null)
            {
                if (intrinsicWidth is > 0 && intrinsicHeight is > 0)
                    return (intrinsicWidth.Value, intrinsicHeight.Value);

                return (Math.Max(0, containerWidth), Math.Max(0, containerHeight));
            }

            if (resolvedWidth is not null && resolvedHeight is null)
            {
                var width = resolvedWidth.Value;
                var height = hasRatio ? width / intrinsicRatio!.Value
                    : intrinsicHeight is > 0 ? intrinsicHeight.Value
                    : containerHeight;
                return (width, Math.Max(0, height));
            }

            if (resolvedWidth is null && resolvedHeight is not null)
            {
                var height = resolvedHeight.Value;
                var width = hasRatio ? height * intrinsicRatio!.Value
                    : intrinsicWidth is > 0 ? intrinsicWidth.Value
                    : containerWidth;
                return (Math.Max(0, width), height);
            }

            return (resolvedWidth!.Value, resolvedHeight!.Value);
        }

        /// <summary>
        /// Resolves one background-position layer value (the 1/2/3/4-token grammar, including the
        /// edge-relative offset form, e.g. "right 20px bottom 10px") against the positioning area and
        /// the already-resolved tile size. Returns the offset of the tile's top-left corner from the
        /// container's top-left corner.
        /// </summary>
        internal static (double X, double Y) ResolvePosition(
            string positionLayerValue,
            double containerWidth, double containerHeight,
            double tileWidth, double tileHeight,
            CssBoxProperties box)
        {
            var value = string.IsNullOrWhiteSpace(positionLayerValue) ? "0% 0%" : positionLayerValue.Trim();
            var tokens = CssValueParser.GetCssTokens(value);
            var parsed = BackgroundPositionGrammar.TryParse(tokens);
            if (parsed is null)
                return (0, 0);

            var x = ResolveAxis(parsed.X, containerWidth, tileWidth, box);
            var y = ResolveAxis(parsed.Y, containerHeight, tileHeight, box);
            return (x, y);
        }

        private static double ResolveAxis(BackgroundPositionGrammar.Component component, double containerSize, double tileSize, CssBoxProperties box)
        {
            var available = containerSize - tileSize;

            switch (component.Keyword)
            {
                case BackgroundPositionGrammar.AxisKeyword.None:
                    return CssValueParser.ParseLength(component.Offset.ToValue(), available, box);
                case BackgroundPositionGrammar.AxisKeyword.Center:
                    return available / 2.0;
                case BackgroundPositionGrammar.AxisKeyword.Right:
                case BackgroundPositionGrammar.AxisKeyword.Bottom:
                    return component.Offset is null ? available : available - CssValueParser.ParseLength(component.Offset.ToValue(), available, box);
                default: // Left or Top
                    return component.Offset is null ? 0.0 : CssValueParser.ParseLength(component.Offset.ToValue(), available, box);
            }
        }
    }
}
