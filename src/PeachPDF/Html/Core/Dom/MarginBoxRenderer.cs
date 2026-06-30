using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Parse;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PeachPDF.Html.Core.Dom
{
    internal static class MarginBoxRenderer
    {
        private const double DefaultFontSizePt = 10.0;

        public static void Render(
            XGraphics g,
            XSize pageSize,
            double marginLeft,
            double marginTop,
            double marginRight,
            double marginBottom,
            PageRule rule,
            int pageNumber,
            int totalPages,
            double pageY,
            IReadOnlyList<NamedString> namedStrings,
            RAdapter adapter)
        {
            foreach (var marginRule in rule.Margins)
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

                var rect = GetMarginBoxRect(boxName, pageSize, marginLeft, marginTop, marginRight, marginBottom);
                if (rect.Width <= 0 || rect.Height <= 0)
                    continue;

                var font = BuildFont(marginRule.Style, adapter);
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

        private static string ResolveNamedString(
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
        /// The 16 standard boxes are positioned within the page margins.
        /// </summary>
        private static XRect GetMarginBoxRect(string name, XSize page, double mL, double mT, double mR, double mB)
        {
            var contentLeft   = mL;
            var contentRight  = page.Width - mR;
            var contentWidth  = contentRight - contentLeft;
            var contentTop    = mT;
            var contentBottom = page.Height - mB;
            var contentHeight = contentBottom - contentTop;
            var third         = contentWidth / 3.0;
            var thirdH        = contentHeight / 3.0;

            return name switch
            {
                // ── top row ──
                "top-left-corner"   => new XRect(0,             0,   mL,     mT),
                "top-left"          => new XRect(contentLeft,   0,   third,  mT),
                "top-center"        => new XRect(contentLeft + third, 0, third, mT),
                "top-right"         => new XRect(contentLeft + third * 2, 0, third, mT),
                "top-right-corner"  => new XRect(contentRight,  0,   mR,     mT),

                // ── right column ──
                "right-top"         => new XRect(contentRight, contentTop,               mR, thirdH),
                "right-middle"      => new XRect(contentRight, contentTop + thirdH,      mR, thirdH),
                "right-bottom"      => new XRect(contentRight, contentTop + thirdH * 2,  mR, thirdH),

                // ── bottom row ──
                "bottom-right-corner" => new XRect(contentRight,            contentBottom, mR,     mB),
                "bottom-right"        => new XRect(contentLeft + third * 2, contentBottom, third,  mB),
                "bottom-center"       => new XRect(contentLeft + third,     contentBottom, third,  mB),
                "bottom-left"         => new XRect(contentLeft,             contentBottom, third,  mB),
                "bottom-left-corner"  => new XRect(0,                       contentBottom, mL,     mB),

                // ── left column ──
                "left-bottom"       => new XRect(0, contentTop + thirdH * 2, mL, thirdH),
                "left-middle"       => new XRect(0, contentTop + thirdH,     mL, thirdH),
                "left-top"          => new XRect(0, contentTop,              mL, thirdH),

                _ => XRect.Empty,
            };
        }

        private static XFont BuildFont(StyleDeclaration style, RAdapter adapter)
        {
            var fontResolver = (adapter as PdfSharpAdapter)?.FontResolver;

            var familyName = style.FontFamily?.Trim('"', '\'');
            if (string.IsNullOrWhiteSpace(familyName))
                familyName = "Arial";

            var sizeStr = style.FontSize;
            var sizePt = string.IsNullOrEmpty(sizeStr)
                ? DefaultFontSizePt
                : DomParser.ParseLengthToPdfPoints(sizeStr) ?? DefaultFontSizePt;

            var fontStyle = XFontStyle.Regular;
            if (style.FontWeight?.Equals("bold", StringComparison.OrdinalIgnoreCase) == true ||
                style.FontWeight?.Equals("700", StringComparison.OrdinalIgnoreCase) == true)
                fontStyle |= XFontStyle.Bold;
            if (style.FontStyle?.Equals("italic", StringComparison.OrdinalIgnoreCase) == true ||
                style.FontStyle?.Equals("oblique", StringComparison.OrdinalIgnoreCase) == true)
                fontStyle |= XFontStyle.Italic;

            return fontResolver != null
                ? new XFont(familyName, sizePt, fontStyle, fontResolver)
                : new XFont(familyName, sizePt, fontStyle, new PdfSharpAdapter().FontResolver);
        }

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
