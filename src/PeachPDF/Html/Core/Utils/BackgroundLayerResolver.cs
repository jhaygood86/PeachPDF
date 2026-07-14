using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Resolves CSS <c>background-position</c> and <c>background-size</c> per-layer values into
    /// concrete pixel geometry, per CSS Backgrounds and Borders Module Level 3
    /// (https://www.w3.org/TR/css-backgrounds-3/#the-background-position,
    /// https://www.w3.org/TR/css-backgrounds-3/#the-background-size). Pure math, no rendering
    /// dependency, so it's directly unit-testable.
    /// </summary>
    internal static class BackgroundLayerResolver
    {
        private static readonly HashSet<string> AxisKeywords = new(StringComparer.OrdinalIgnoreCase)
        {
            CssConstants.Left, CssConstants.Right, CssConstants.Top, CssConstants.Bottom, CssConstants.Center
        };

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
            bool hasRatio = intrinsicRatio is > 0;

            if (value.Equals("cover", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("contain", StringComparison.OrdinalIgnoreCase))
            {
                if (!hasRatio || containerWidth <= 0 || containerHeight <= 0)
                    return (Math.Max(0, containerWidth), Math.Max(0, containerHeight));

                var ratio = intrinsicRatio!.Value;
                var isCover = value.Equals("cover", StringComparison.OrdinalIgnoreCase);

                var scaleWidth = containerWidth;
                var scaleHeight = containerWidth / ratio;
                if ((isCover && scaleHeight < containerHeight) || (!isCover && scaleHeight > containerHeight))
                {
                    scaleHeight = containerHeight;
                    scaleWidth = containerHeight * ratio;
                }

                return (scaleWidth, scaleHeight);
            }

            var tokens = CssValueParser.SplitTopLevelWhitespace(value).ToArray();
            var widthToken = tokens.Length > 0 ? tokens[0] : CssConstants.Auto;
            var heightToken = tokens.Length > 1 ? tokens[1] : CssConstants.Auto;

            var widthAuto = widthToken.Equals(CssConstants.Auto, StringComparison.OrdinalIgnoreCase);
            var heightAuto = heightToken.Equals(CssConstants.Auto, StringComparison.OrdinalIgnoreCase);

            double? resolvedWidth = widthAuto ? null : Math.Max(0, CssValueParser.ParseLength(widthToken, containerWidth, box));
            double? resolvedHeight = heightAuto ? null : Math.Max(0, CssValueParser.ParseLength(heightToken, containerHeight, box));

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
            var tokens = CssValueParser.SplitTopLevelWhitespace(value).ToArray();
            if (tokens.Length == 0)
                tokens = ["0%", "0%"];

            var items = GroupTokens(tokens);

            (string? Keyword, string? Offset) xItem, yItem;
            if (items.Count == 1)
            {
                var only = items[0];
                if (only.Keyword is CssConstants.Top or CssConstants.Bottom)
                {
                    yItem = only;
                    xItem = (CssConstants.Center, null);
                }
                else
                {
                    xItem = only;
                    yItem = (CssConstants.Center, null);
                }
            }
            else
            {
                var a = items[0];
                var b = items[1];
                var swapped = a.Keyword is CssConstants.Top or CssConstants.Bottom
                    || b.Keyword is CssConstants.Left or CssConstants.Right;
                if (swapped)
                {
                    yItem = a;
                    xItem = b;
                }
                else
                {
                    xItem = a;
                    yItem = b;
                }
            }

            var x = ResolveAxis(xItem.Keyword, xItem.Offset, containerWidth, tileWidth, box);
            var y = ResolveAxis(yItem.Keyword, yItem.Offset, containerHeight, tileHeight, box);
            return (x, y);
        }

        private static List<(string? Keyword, string? Offset)> GroupTokens(string[] tokens)
        {
            var items = new List<(string? Keyword, string? Offset)>();
            var allowMerge = tokens.Length >= 3;
            var i = 0;
            while (i < tokens.Length)
            {
                var tok = tokens[i];
                if (AxisKeywords.Contains(tok))
                {
                    string? offset = null;
                    if (allowMerge && i + 1 < tokens.Length && !AxisKeywords.Contains(tokens[i + 1]))
                    {
                        offset = tokens[i + 1];
                        i++;
                    }

                    items.Add((tok.ToLowerInvariant(), offset));
                }
                else
                {
                    items.Add((null, tok));
                }

                i++;
            }

            return items.Count > 2 ? items.Take(2).ToList() : items;
        }

        private static double ResolveAxis(string? keyword, string? offsetToken, double containerSize, double tileSize, CssBoxProperties box)
        {
            var available = containerSize - tileSize;

            if (keyword is null)
                return CssValueParser.ParseLength(offsetToken!, available, box);

            switch (keyword)
            {
                case CssConstants.Center:
                    return available / 2.0;
                case CssConstants.Right:
                case CssConstants.Bottom:
                    return offsetToken is null ? available : available - CssValueParser.ParseLength(offsetToken, available, box);
                default: // left or top
                    return offsetToken is null ? 0.0 : CssValueParser.ParseLength(offsetToken, available, box);
            }
        }
    }
}
