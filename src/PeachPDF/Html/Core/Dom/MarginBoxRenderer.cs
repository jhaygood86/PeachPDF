using PeachPDF;
using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PeachPDF.Html.Core.Dom
{
    internal static class MarginBoxRenderer
    {
        /// <summary>
        /// Renders every applicable margin box. <paramref name="margins"/> is the effective, already
        /// cascade-merged set of margin-box declarations for this page (see
        /// <see cref="PdfGenerator.SelectApplicableMarginRules"/>) — not necessarily all from the same
        /// <c>@page</c> rule, since <c>@page</c> rules cascade per-declaration like any other CSS.
        /// <paramref name="pageStyle"/> is the page context's own top-level declarations (see
        /// <see cref="PdfGenerator.SelectApplicablePageStyle"/>), consulted as a font-property
        /// inheritance fallback for margin boxes that don't set their own <c>font-family</c>/
        /// <c>font-size</c>/<c>font-weight</c>/<c>font-style</c> — per CSS Paged Media, margin boxes
        /// inherit these from their page context.
        /// </summary>
        public static void Render(
            XGraphics g,
            XSize pageSize,
            double marginLeft,
            double marginTop,
            double marginRight,
            double marginBottom,
            IReadOnlyList<MarginStyleRule> margins,
            int pageNumber,
            int totalPages,
            double pageY,
            IReadOnlyList<NamedString> namedStrings,
            RAdapter adapter,
            StyleDeclaration? pageStyle)
        {
            foreach (var marginRule in margins)
            {
                var boxName = marginRule.Selector?.Text?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(boxName))
                    continue;

                var contentValue = marginRule.Style.Content;
                if (string.IsNullOrEmpty(contentValue) ||
                    contentValue.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                    contentValue.Equals("normal", StringComparison.OrdinalIgnoreCase))
                    continue;

                var text = ResolveContent(contentValue, pageNumber, totalPages, pageY, pageSize.Height - marginTop - marginBottom, namedStrings);
                if (text == null)
                    continue;

                var rect = GetMarginBoxRect(boxName, pageSize, marginLeft, marginTop, marginRight, marginBottom, margins);
                if (rect.Width <= 0 || rect.Height <= 0)
                    continue;

                var font = BuildFont(marginRule.Style, pageStyle, adapter);
                var brush = BuildBrush(marginRule.Style);
                var format = BuildStringFormat(marginRule.Style, boxName);

                g.DrawString(text, font, brush, rect, format);
            }
        }

        private static string? ResolveContent(
            string contentValue,
            int pageNumber,
            int totalPages,
            double pageY,
            double pageHeight,
            IReadOnlyList<NamedString> namedStrings)
        {
            if (contentValue.Equals("none", StringComparison.OrdinalIgnoreCase))
                return null;

            var tokens = CssValueParser.GetCssTokens(contentValue);
            var sb = new StringBuilder();

            foreach (var token in tokens)
            {
                switch (token)
                {
                    case StringToken stringToken:
                        sb.Append(stringToken.Data);
                        break;
                    case FunctionToken { Data: "counter" } counterToken:
                    {
                        var args = counterToken.ArgumentTokens
                            .Where(t => t.Type != TokenType.Whitespace)
                            .ToArray();
                        if (args.Length > 0 && args[0] is KeywordToken nameToken)
                        {
                            sb.Append(nameToken.Data.Equals("pages", StringComparison.OrdinalIgnoreCase)
                                ? totalPages.ToString()
                                : pageNumber.ToString());
                        }
                        break;
                    }
                    case FunctionToken { Data: "string" } stringFunctionToken:
                    {
                        var args = stringFunctionToken.ArgumentTokens
                            .Where(t => t.Type != TokenType.Whitespace && t.Type != TokenType.Comma)
                            .ToArray();
                        if (args.Length > 0 && args[0] is KeywordToken nameToken)
                        {
                            var keyword = args.Length > 1 && args[1] is KeywordToken kw ? kw.Data : "first";
                            sb.Append(ResolveNamedString(nameToken.Data, keyword, pageY, pageHeight, namedStrings));
                        }
                        break;
                    }
                }
            }

            return sb.Length > 0 ? sb.ToString() : null;
        }

        internal static string ResolveNamedString(
            string name,
            string keyword,
            double pageY,
            double pageHeight,
            IReadOnlyList<NamedString> namedStrings)
        {
            var pageEnd = pageY + pageHeight;

            return keyword.ToLowerInvariant() switch
            {
                // last assignment that started before this page (running header)
                "start" => namedStrings
                    .LastOrDefault(s => s.Name == name && s.Y < pageY)?.Value ?? string.Empty,
                // first assignment on this page
                "first" => namedStrings
                    .FirstOrDefault(s => s.Name == name && s.Y >= pageY && s.Y < pageEnd)?.Value
                    ?? namedStrings.LastOrDefault(s => s.Name == name && s.Y < pageY)?.Value
                    ?? string.Empty,
                // last assignment on this page
                "last" => namedStrings
                    .LastOrDefault(s => s.Name == name && s.Y >= pageY && s.Y < pageEnd)?.Value
                    ?? namedStrings.LastOrDefault(s => s.Name == name && s.Y < pageY)?.Value
                    ?? string.Empty,
                // first-except: return empty on the page where this string is first assigned
                "first-except" => namedStrings.Any(s => s.Name == name && s.Y >= pageY && s.Y < pageEnd)
                    ? string.Empty
                    : namedStrings.LastOrDefault(s => s.Name == name && s.Y < pageY)?.Value ?? string.Empty,
                _ => namedStrings.FirstOrDefault(s => s.Name == name)?.Value ?? string.Empty,
            };
        }

        /// <summary>
        /// Returns the rectangle (in PDF points) for a named margin box.
        /// Uses explicit width/height from each box's CSS rule if present; falls back to equal thirds.
        /// </summary>
        private static XRect GetMarginBoxRect(string name, XSize page, double mL, double mT, double mR, double mB, IReadOnlyList<MarginStyleRule> margins)
        {
            var contentLeft   = mL;
            var contentRight  = page.Width - mR;
            var contentWidth  = contentRight - contentLeft;
            var contentTop    = mT;
            var contentBottom = page.Height - mB;
            var contentHeight = contentBottom - contentTop;

            var tlR = FindMargin(margins, "top-left");
            var tcR = FindMargin(margins, "top-center");
            var trR = FindMargin(margins, "top-right");
            var (tL, tC, tR) = ComputeThreeBoxSizes(contentWidth,
                PW(tlR), PMinW(tlR), PMaxW(tlR),
                PW(tcR), PMinW(tcR), PMaxW(tcR),
                PW(trR), PMinW(trR), PMaxW(trR));

            var blR = FindMargin(margins, "bottom-left");
            var bcR = FindMargin(margins, "bottom-center");
            var brR = FindMargin(margins, "bottom-right");
            var (bL, bC, bR) = ComputeThreeBoxSizes(contentWidth,
                PW(blR), PMinW(blR), PMaxW(blR),
                PW(bcR), PMinW(bcR), PMaxW(bcR),
                PW(brR), PMinW(brR), PMaxW(brR));

            var rtR = FindMargin(margins, "right-top");
            var rmR = FindMargin(margins, "right-middle");
            var rbR = FindMargin(margins, "right-bottom");
            var (rT, rM, rB) = ComputeThreeBoxSizes(contentHeight,
                PH(rtR), PMinH(rtR), PMaxH(rtR),
                PH(rmR), PMinH(rmR), PMaxH(rmR),
                PH(rbR), PMinH(rbR), PMaxH(rbR));

            var ltR = FindMargin(margins, "left-top");
            var lmR = FindMargin(margins, "left-middle");
            var lbR = FindMargin(margins, "left-bottom");
            var (lT, lM, lB) = ComputeThreeBoxSizes(contentHeight,
                PH(ltR), PMinH(ltR), PMaxH(ltR),
                PH(lmR), PMinH(lmR), PMaxH(lmR),
                PH(lbR), PMinH(lbR), PMaxH(lbR));

            return name switch
            {
                // ── top row ──
                "top-left-corner"   => new XRect(0,              0, mL,  mT),
                "top-left"          => new XRect(contentLeft,    0, tL,  mT),
                "top-center"        => new XRect(contentLeft + tL, 0, tC, mT),
                "top-right"         => new XRect(contentLeft + tL + tC, 0, tR, mT),
                "top-right-corner"  => new XRect(contentRight,   0, mR,  mT),

                // ── right column ──
                "right-top"         => new XRect(contentRight, contentTop,          mR, rT),
                "right-middle"      => new XRect(contentRight, contentTop + rT,     mR, rM),
                "right-bottom"      => new XRect(contentRight, contentTop + rT + rM, mR, rB),

                // ── bottom row ──
                "bottom-right-corner" => new XRect(contentRight,              contentBottom, mR,  mB),
                "bottom-right"        => new XRect(contentLeft + bL + bC,     contentBottom, bR,  mB),
                "bottom-center"       => new XRect(contentLeft + bL,          contentBottom, bC,  mB),
                "bottom-left"         => new XRect(contentLeft,               contentBottom, bL,  mB),
                "bottom-left-corner"  => new XRect(0,                         contentBottom, mL,  mB),

                // ── left column ──
                "left-top"          => new XRect(0, contentTop,          mL, lT),
                "left-middle"       => new XRect(0, contentTop + lT,     mL, lM),
                "left-bottom"       => new XRect(0, contentTop + lT + lM, mL, lB),

                _ => XRect.Empty,
            };
        }

        private static MarginStyleRule? FindMargin(IReadOnlyList<MarginStyleRule> margins, string name) =>
            margins.FirstOrDefault(m =>
                (m.Selector?.Text?.Trim().ToLowerInvariant() ?? "") == name);

        private static double? PW(MarginStyleRule? r)  => r == null ? null : DomParser.ParseLengthToPdfPoints(r.Style.Width);
        private static double? PMinW(MarginStyleRule? r) => r == null ? null : DomParser.ParseLengthToPdfPoints(r.Style.MinWidth);
        private static double? PMaxW(MarginStyleRule? r) => r == null ? null : DomParser.ParseLengthToPdfPoints(r.Style.MaxWidth);
        private static double? PH(MarginStyleRule? r)  => r == null ? null : DomParser.ParseLengthToPdfPoints(r.Style.Height);
        private static double? PMinH(MarginStyleRule? r) => r == null ? null : DomParser.ParseLengthToPdfPoints(r.Style.MinHeight);
        private static double? PMaxH(MarginStyleRule? r) => r == null ? null : DomParser.ParseLengthToPdfPoints(r.Style.MaxHeight);

        /// <summary>
        /// Distributes <paramref name="available"/> space among three boxes (a, b, c).
        /// Explicit sizes are honoured and clamped to min/max; remaining space is split equally
        /// among auto (null) boxes. Returns equal thirds if all are auto.
        /// </summary>
        private static (double a, double b, double c) ComputeThreeBoxSizes(
            double available,
            double? sizeA, double? minA, double? maxA,
            double? sizeB, double? minB, double? maxB,
            double? sizeC, double? minC, double? maxC)
        {
            static double Clamp(double v, double? min, double? max) =>
                Math.Max(min ?? 0, Math.Min(max ?? double.MaxValue, v));

            // All auto → equal thirds
            if (sizeA == null && sizeB == null && sizeC == null)
            {
                var third = available / 3.0;
                return (third, third, third);
            }

            double fixedSum = 0;
            int autoCount = 0;

            double a = sizeA.HasValue ? Clamp(sizeA.Value, minA, maxA) : 0;
            double b = sizeB.HasValue ? Clamp(sizeB.Value, minB, maxB) : 0;
            double c = sizeC.HasValue ? Clamp(sizeC.Value, minC, maxC) : 0;

            if (sizeA != null) fixedSum += a; else autoCount++;
            if (sizeB != null) fixedSum += b; else autoCount++;
            if (sizeC != null) fixedSum += c; else autoCount++;

            var remaining = Math.Max(0, available - fixedSum);
            var autoShare = autoCount > 0 ? remaining / autoCount : 0;

            if (sizeA == null) a = autoShare;
            if (sizeB == null) b = autoShare;
            if (sizeC == null) c = autoShare;

            return (a, b, c);
        }

        internal static XFont BuildFont(StyleDeclaration style, StyleDeclaration? pageStyle, RAdapter adapter)
        {
            var familyList = FirstNonEmpty(style.FontFamily, pageStyle?.FontFamily) ?? CssConstants.DefaultFont;
            var sizeStr = FirstNonEmpty(style.FontSize, pageStyle?.FontSize);
            var weightStr = FirstNonEmpty(style.FontWeight, pageStyle?.FontWeight);
            var styleStr = FirstNonEmpty(style.FontStyle, pageStyle?.FontStyle);

            var sizePt = string.IsNullOrEmpty(sizeStr)
                ? CssConstants.FontSize
                : DomParser.ParseLengthToPdfPoints(sizeStr)
                  ?? FontSizeResolver.Resolve(sizeStr, CssConstants.FontSize, CssConstants.FontSize);

            var fontStyle = RFontStyle.Regular;
            if (weightStr is not null &&
                (weightStr.Equals("bold", StringComparison.OrdinalIgnoreCase) ||
                 (int.TryParse(weightStr, out var weight) && weight >= 700)))
                fontStyle |= RFontStyle.Bold;
            if (styleStr is not null &&
                (styleStr.Equals("italic", StringComparison.OrdinalIgnoreCase) ||
                 styleStr.Equals("oblique", StringComparison.OrdinalIgnoreCase)))
                fontStyle |= RFontStyle.Italic;

            // MarginBoxRenderer paints in raw, unshrunk PDF-point space (margin-box rects are computed
            // directly from orgPageSize/margins), but RAdapter.GetFont -> PdfSharpAdapter.CreateFontInt
            // divides its `size` argument by PixelsPerPoint (matching in-flow content, whose entire
            // coordinate system - including font size - is uniformly in "pixel" space and shrunk together
            // by that same later division). Since margin-box rect positions never go through that
            // division, pre-multiply so the two cancel out to the CSS-specified point size regardless of
            // PixelsPerPoint (normally 1.0, but not under ShrinkToFit/ScaleToPageSize or non-72
            // PixelsPerInch).
            var pixelsPerPoint = (adapter as PdfSharpAdapter)?.PixelsPerPoint ?? 1.0;
            var pixelSize = sizePt * pixelsPerPoint;
            var resolvedFont = FontFamilyResolver.Resolve(adapter, familyList, pixelSize, fontStyle)
                                ?? FontFamilyResolver.Resolve(adapter, CssConstants.DefaultFont, pixelSize, fontStyle);

            if (resolvedFont is not FontAdapter fontAdapter)
            {
                throw new HtmlRenderException(
                    $"Cannot find font: {familyList} and Default Font {CssConstants.DefaultFont} is not installed",
                    HtmlRenderErrorType.General);
            }

            return fontAdapter.Font;
        }

        private static string? FirstNonEmpty(string? a, string? b) =>
            !string.IsNullOrEmpty(a) ? a : (!string.IsNullOrEmpty(b) ? b : null);

        private static XBrush BuildBrush(StyleDeclaration style)
        {
            var colorStr = style.Color;
            if (!string.IsNullOrEmpty(colorStr))
            {
                var color = ParseColor(colorStr);
                if (color.HasValue)
                    return new XSolidBrush(color.Value);
            }
            return XBrushes.Black;
        }

        private static XColor? ParseColor(string value)
        {
            if (string.IsNullOrEmpty(value))
                return null;

            value = value.Trim();

            if (value.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase))
            {
                var inner = value.Substring(4, value.Length - 5);
                var parts = inner.Split(',');
                if (parts.Length >= 3 &&
                    double.TryParse(parts[0].Trim(), out var r) &&
                    double.TryParse(parts[1].Trim(), out var gg) &&
                    double.TryParse(parts[2].Trim(), out var b))
                {
                    return XColor.FromArgb((int)r, (int)gg, (int)b);
                }
            }

            if (value.StartsWith("#"))
            {
                try { return XColor.FromArgb(int.Parse(value.Substring(1), System.Globalization.NumberStyles.HexNumber)); }
                catch { }
            }

            return null;
        }

        private static XStringFormat BuildStringFormat(StyleDeclaration style, string boxName)
        {
            var textAlign = style.TextAlign?.ToLowerInvariant() ?? InferAlignment(boxName);
            var verticalAlign = style.VerticalAlign?.ToLowerInvariant() ?? "middle";

            return (textAlign, verticalAlign) switch
            {
                ("left",   "top")    => XStringFormats.TopLeft,
                ("left",   "bottom") => XStringFormats.BottomLeft,
                ("left",   _)        => XStringFormats.CenterLeft,
                ("right",  "top")    => XStringFormats.TopRight,
                ("right",  "bottom") => XStringFormats.BottomRight,
                ("right",  _)        => XStringFormats.CenterRight,
                ("center", "top")    => XStringFormats.TopCenter,
                ("center", "bottom") => XStringFormats.BottomCenter,
                _                    => XStringFormats.Center,
            };
        }

        private static string InferAlignment(string boxName) => boxName switch
        {
            "top-left" or "top-left-corner" or "bottom-left" or "bottom-left-corner"
                or "left-top" or "left-middle" or "left-bottom" => "left",
            "top-right" or "top-right-corner" or "bottom-right" or "bottom-right-corner"
                or "right-top" or "right-middle" or "right-bottom" => "right",
            _ => "center",
        };
    }
}
