#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Picks the image an <c>&lt;img&gt;</c> loads from its <c>srcset</c>/<c>sizes</c> attributes and an
    /// enclosing <c>&lt;picture&gt;</c>'s <c>&lt;source&gt;</c> siblings (HTML Standard 4.8.4.3). A PDF has
    /// a fixed page rather than a viewport the reader resizes, so among the candidates that apply the
    /// highest pixel density wins (the best print quality) instead of the one closest to a device's DPR.
    /// </summary>
    internal static class ResponsiveImageSelector
    {
        internal readonly record struct Candidate(string Url, double Density, double? Width);

        private static readonly HashSet<string> _supportedTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "image/png", "image/jpeg", "image/jpg", "image/pjpeg", "image/gif", "image/webp", "image/avif", "image/jxl",
            "image/bmp", "image/x-ms-bmp", "image/tiff", "image/svg+xml", "image/x-icon", "image/vnd.microsoft.icon"
        };

        /// <summary>
        /// Resolves the image source for <paramref name="img"/>. <c>Density</c> is the selected
        /// candidate's pixel density (1 for a plain <c>src</c>), by which the image's natural size is divided.
        /// </summary>
        internal static (string Url, double Density) Select(CssBox img)
        {
            foreach (var source in EnumerateSources(img))
            {
                var srcset = source.GetAttribute("srcset");
                if (string.IsNullOrWhiteSpace(srcset)) continue;
                if (!MediaMatches(source.GetAttribute("media"), img)) continue;
                if (!TypeSupported(source.GetAttribute("type"))) continue;

                if (Pick(ParseSrcset(srcset), source.GetAttribute("sizes"), img) is { } hit)
                {
                    return (hit.Url, hit.Density);
                }
            }

            var own = img.GetAttribute("srcset");
            var src = img.GetAttribute("src");

            if (!string.IsNullOrWhiteSpace(own))
            {
                var candidates = ParseSrcset(own);
                if (!string.IsNullOrWhiteSpace(src)) candidates.Add(new Candidate(src.Trim(), 1, null));

                if (Pick(candidates, img.GetAttribute("sizes"), img) is { } hit) return (hit.Url, hit.Density);
            }

            return (src, 1);
        }

        /// <summary>The <c>&lt;source&gt;</c> elements preceding <paramref name="img"/> inside its <c>&lt;picture&gt;</c>, in order.</summary>
        private static List<CssBox> EnumerateSources(CssBox img)
        {
            var sources = new List<CssBox>();
            var picture = img.ParentBox;
            // The block-in-inline correction can wrap the img in anonymous boxes; the picture is the nearest tagged ancestor.
            while (picture is { HtmlTag: null }) picture = picture.ParentBox;

            if (picture?.HtmlTag is { Name: HtmlConstants.Picture })
            {
                Collect(picture, img, sources);
            }

            return sources;
        }

        private static bool Collect(CssBox parent, CssBox stop, List<CssBox> sources)
        {
            foreach (var child in parent.Boxes)
            {
                if (ReferenceEquals(child, stop)) return true;

                if (child.HtmlTag is { Name: HtmlConstants.Source })
                {
                    sources.Add(child);
                }
                else if (child.HtmlTag is null && Collect(child, stop, sources))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TypeSupported(string type) =>
            string.IsNullOrWhiteSpace(type) || _supportedTypes.Contains(type.Trim());

        private static bool MediaMatches(string? media, CssBox box)
        {
            if (string.IsNullOrWhiteSpace(media)) return true;
            if (box.HtmlContainer is not { } container) return true;

            try
            {
                var list = new MediaList(new StylesheetParser()) { MediaText = media };
                return MediaQueryMatcher.Matches([list], MediaQueryContext.FromContainer(container, container.Media));
            }
            catch (ParseException)
            {
                return false;
            }
        }

        private static Candidate? Pick(List<Candidate> candidates, string sizes, CssBox box)
        {
            double? slot = null;
            Candidate? best = null;
            var bestDensity = double.NegativeInfinity;

            foreach (var candidate in candidates)
            {
                var density = candidate.Density;
                if (candidate.Width is { } w)
                {
                    slot ??= ResolveSlotWidth(sizes, box);
                    if (slot is not > 0) continue;
                    density = w / slot.Value;
                }

                if (density > bestDensity)
                {
                    bestDensity = density;
                    best = candidate with { Density = density };
                }
            }

            return best;
        }

        /// <summary>HTML "parse a srcset attribute": invalid candidates (bad or multiple descriptors) are dropped.</summary>
        internal static List<Candidate> ParseSrcset(string input)
        {
            var result = new List<Candidate>();
            var pos = 0;

            while (pos < input.Length)
            {
                while (pos < input.Length && (char.IsWhiteSpace(input[pos]) || input[pos] == ',')) pos++;
                if (pos >= input.Length) break;

                var start = pos;
                while (pos < input.Length && !char.IsWhiteSpace(input[pos])) pos++;
                var url = input[start..pos];
                var descriptors = string.Empty;

                if (url.EndsWith(','))
                {
                    url = url.TrimEnd(',');
                }
                else
                {
                    var descStart = pos;
                    var depth = 0;
                    while (pos < input.Length)
                    {
                        var c = input[pos];
                        if (c == '(') depth++;
                        else if (c == ')') depth--;
                        else if (c == ',' && depth <= 0) break;
                        pos++;
                    }

                    descriptors = input[descStart..pos];
                }

                if (url.Length == 0) continue;

                var tokens = descriptors.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length == 0)
                {
                    result.Add(new Candidate(url, 1, null));
                }
                else if (tokens.Length == 1)
                {
                    var token = tokens[0];
                    var number = token[..^1];

                    if (token[^1] is 'w' or 'W'
                        && int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var width) && width > 0)
                    {
                        result.Add(new Candidate(url, 0, width));
                    }
                    else if (token[^1] is 'x' or 'X'
                        && double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var density)
                        && density > 0 && double.IsFinite(density))
                    {
                        result.Add(new Candidate(url, density, null));
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// The slot width in CSS px from a <c>sizes</c> list: the first entry whose media condition
        /// matches, else <c>100vw</c> (the page-box width, the only viewport a PDF has).
        /// </summary>
        private static double? ResolveSlotWidth(string sizes, CssBox box)
        {
            var pixelsPerPoint = (box.HtmlContainer?.Adapter as PdfSharpAdapter)?.PixelsPerPoint ?? 1.0;
            var unitsPerCssPx = Length.PointsPerPx * pixelsPerPoint;
            var (viewportWidth, _, _, _) = box.GetViewportUnitBasis();
            var basis = viewportWidth ?? 0;

            if (!string.IsNullOrWhiteSpace(sizes))
            {
                foreach (var entry in SplitTopLevel(sizes))
                {
                    var (condition, length) = SplitSizeEntry(entry.Trim());
                    if (length.Length == 0) continue;
                    if (condition.Length > 0 && !MediaMatches(condition, box)) continue;

                    try
                    {
                        var units = CssValueParser.ParseLength(length, basis, box);
                        if (units > 0) return units / unitsPerCssPx;
                    }
                    catch (Exception ex) when (ex is FormatException or ArgumentException)
                    {
                        // An unparsable length invalidates this entry only.
                    }
                }
            }

            return basis > 0 ? basis / unitsPerCssPx : null;
        }

        private static List<string> SplitTopLevel(string value)
        {
            var parts = new List<string>();
            var depth = 0;
            var start = 0;

            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] == '(') depth++;
                else if (value[i] == ')') depth--;
                else if (value[i] == ',' && depth == 0)
                {
                    parts.Add(value[start..i]);
                    start = i + 1;
                }
            }

            parts.Add(value[start..]);
            return parts;
        }

        /// <summary>Splits <c>[&lt;media-condition&gt;] &lt;length&gt;</c>; the length is the trailing token (a function call keeps its parentheses).</summary>
        internal static (string Condition, string Length) SplitSizeEntry(string entry)
        {
            var start = entry.LastIndexOfAny([' ', '\t', '\r', '\n']) + 1;

            if (entry.EndsWith(')'))
            {
                var depth = 0;
                for (var i = entry.Length - 1; i >= 0; i--)
                {
                    if (entry[i] == ')') depth++;
                    else if (entry[i] == '(') depth--;

                    if (depth == 0)
                    {
                        start = i;
                        break;
                    }
                }

                // A leading function name (calc/min/max/clamp) belongs to the length; a bare "(...)" is a media condition.
                var nameStart = start;
                while (nameStart > 0 && (char.IsLetter(entry[nameStart - 1]) || entry[nameStart - 1] == '-')) nameStart--;
                if (nameStart == start) return (entry, string.Empty);
                start = nameStart;
            }

            return (entry[..start].Trim(), entry[start..].Trim());
        }
    }
}
