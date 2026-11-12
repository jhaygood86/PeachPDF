#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PeachBrackets.Css;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Parse;

namespace PeachBrackets.Html.Utils
{
    /// <summary>
    /// Picks the image an <c>&lt;img&gt;</c> loads from its <c>srcset</c>/<c>sizes</c> attributes and an
    /// enclosing <c>&lt;picture&gt;</c>'s <c>&lt;source&gt;</c> siblings (HTML Standard 4.8.4.3). A PDF has
    /// a fixed page rather than a viewport the reader resizes, so for paged output the highest pixel density
    /// among the candidates that apply wins (the best print quality). A screen document ranks them for the
    /// container's <see cref="ImageSelectionPolicy.BestForTarget"/> policy instead: the smallest candidate
    /// that is sharp enough for its device pixel ratio and zoom.
    /// </summary>
    internal static class ResponsiveImageSelector
    {
        internal readonly record struct Candidate(string Url, double Density, double? Width);

        /// <summary>One chosen image: its URL, the density its natural size is divided by, and the density it has against the width it is used at.</summary>
        /// <param name="Url">the image URL as written</param>
        /// <param name="Density">the candidate's pixel density (1 for a plain <c>src</c>), by which the image's natural size is divided; for a width descriptor it is measured against the <c>sizes</c> slot, so the box keeps its size when another candidate is swapped in</param>
        /// <param name="EffectiveDensity">the density against the width the image is actually used at; equal to <paramref name="Density"/> until a used width is known</param>
        internal readonly record struct Choice(string Url, double Density, double EffectiveDensity);

        /// <summary>The ranked outcome of a selection: the chosen image and the next-best ones to try when it cannot be loaded.</summary>
        /// <param name="Chosen">the selected image</param>
        /// <param name="Alternates">the remaining candidates best-first, ending with the <c>src</c> attribute</param>
        internal sealed record Selection(Choice Chosen, IReadOnlyList<Choice> Alternates);

        /// <summary>
        /// Resolves the image source for <paramref name="img"/>. <c>Density</c> is the selected
        /// candidate's pixel density (1 for a plain <c>src</c>), by which the image's natural size is divided.
        /// </summary>
        /// <param name="img">the <c>&lt;img&gt;</c> box</param>
        /// <param name="usedWidthCssPx">the width, in CSS pixels, the image is shown at when that is known more precisely than the <c>sizes</c> slot (after layout); null before</param>
        internal static Selection Select(CssBox img, double? usedWidthCssPx = null)
        {
            var src = img.GetAttribute("src");
            Choice? srcFallback = string.IsNullOrWhiteSpace(src) ? null : new Choice(src.Trim(), 1, 1);

            foreach (var source in EnumerateSources(img))
            {
                var srcset = source.GetAttribute("srcset");
                if (string.IsNullOrWhiteSpace(srcset)) continue;
                if (!MediaMatches(source.GetAttribute("media"), img)) continue;
                if (!TypeSupported(source.GetAttribute("type"), img)) continue;

                if (Rank(ParseSrcset(srcset), source.GetAttribute("sizes"), img, usedWidthCssPx) is { Count: > 0 } ranked)
                {
                    return Finish(ranked, srcFallback);
                }
            }

            var own = img.GetAttribute("srcset");

            if (!string.IsNullOrWhiteSpace(own))
            {
                var candidates = ParseSrcset(own);
                if (!string.IsNullOrWhiteSpace(src)) candidates.Add(new Candidate(src.Trim(), 1, null));

                if (Rank(candidates, img.GetAttribute("sizes"), img, usedWidthCssPx) is { Count: > 0 } ranked)
                {
                    return Finish(ranked, srcFallback);
                }
            }

            return new Selection(new Choice(src, 1, 1), []);
        }

        private static Selection Finish(List<Choice> ranked, Choice? srcFallback)
        {
            var alternates = ranked.Skip(1).ToList();
            if (srcFallback is { } fallback && ranked.TrueForAll(c => c.Url != fallback.Url)) alternates.Add(fallback);
            return new Selection(ranked[0], alternates);
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

        private static bool TypeSupported(string type, CssBox box) =>
            string.IsNullOrWhiteSpace(type)
            || (box.HtmlContainer?.RenderContext?.SupportsImageType(type) ?? true);

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

        /// <summary>
        /// Orders the candidates best-first for the container's policy. <see cref="ImageSelectionPolicy.HighestDensity"/> is the highest
        /// density first; <see cref="ImageSelectionPolicy.BestForTarget"/> is the smallest candidate sharp enough for the target density
        /// first, then the larger ones, then the insufficient ones largest first, with candidates over the decode budget last.
        /// </summary>
        private static List<Choice> Rank(List<Candidate> candidates, string sizes, CssBox box, double? usedWidthCssPx)
        {
            double? slot = null;
            var all = new List<(Choice Choice, double? WidthPx, int Index)>();

            foreach (var candidate in candidates)
            {
                var density = candidate.Density;
                var effective = density;
                double? widthPx = null;

                if (candidate.Width is { } w)
                {
                    slot ??= ResolveSlotWidth(sizes, box);
                    if (slot is not > 0) continue;
                    density = w / slot.Value;
                    effective = usedWidthCssPx is > 0 ? w / usedWidthCssPx.Value : density;
                    widthPx = w;
                }
                else if (usedWidthCssPx is > 0)
                {
                    widthPx = density * usedWidthCssPx.Value;
                }

                all.Add((new Choice(candidate.Url, density, effective), widthPx, all.Count));
            }

            var settings = box.HtmlContainer?.ImageSelection ?? ImageSelectionOptions.HighestDensityPolicy;
            var container = box.HtmlContainer;
            var target = container is null ? 1 : TargetDensity(container, settings);
            return RankChoices(all, settings, target, ImageAspect(box));
        }

        /// <summary>The pixel density a screen shows images at: the device pixel ratio, times the render scale when the policy lets zoom count.</summary>
        internal static double TargetDensity(HtmlContainerInt container, ImageSelectionOptions settings) =>
            container.DevicePixelRatio * (settings.UpgradeOnZoom ? container.RenderScale : 1);

        /// <summary>
        /// Picks the density among the <c>x</c> candidates of a CSS <c>image-set()</c>, by the same policy an <c>&lt;img srcset&gt;</c> is ranked with.
        /// </summary>
        /// <param name="densities">each candidate's resolution in dppx, in declaration order</param>
        /// <param name="container">the document whose selection policy and target density apply, or null for the paged-output policy</param>
        /// <returns>the index into <paramref name="densities"/> of the best candidate, or -1 when it is empty</returns>
        internal static int PickDensity(IReadOnlyList<double> densities, HtmlContainerInt? container)
        {
            if (densities.Count == 0) return -1;

            var all = new List<(Choice Choice, double? WidthPx, int Index)>();
            for (var i = 0; i < densities.Count; i++) all.Add((new Choice(i.ToString(CultureInfo.InvariantCulture), densities[i], densities[i]), null, i));

            var settings = container?.ImageSelection ?? ImageSelectionOptions.HighestDensityPolicy;
            var target = container is null ? 1 : TargetDensity(container, settings);
            return int.Parse(RankChoices(all, settings, target, 1)[0].Url, CultureInfo.InvariantCulture);
        }

        private static List<Choice> RankChoices(List<(Choice Choice, double? WidthPx, int Index)> all, ImageSelectionOptions settings, double target, double aspect)
        {
            if (settings.Policy == ImageSelectionPolicy.HighestDensity)
            {
                // Stable, so the earliest of equal densities wins as it always has.
                return [.. all.OrderByDescending(c => c.Choice.Density).ThenBy(c => c.Index).Select(c => c.Choice)];
            }

            var tolerance = settings.Tolerance >= 1 && double.IsFinite(settings.Tolerance) ? settings.Tolerance : 1.1;

            bool OverBudget(double? widthPx) =>
                settings.MaxDecodePixels is { } budget && widthPx is { } wpx && wpx * (wpx / aspect) > budget;

            var inBudget = all.Where(c => !OverBudget(c.WidthPx)).ToList();
            var overBudget = all.Where(c => OverBudget(c.WidthPx)).OrderBy(c => c.WidthPx).Select(c => c.Choice);

            var threshold = target / tolerance;
            var sufficient = inBudget.Where(c => c.Choice.EffectiveDensity >= threshold)
                .OrderBy(c => c.Choice.EffectiveDensity).ThenBy(c => c.Index);
            var rest = inBudget.Where(c => c.Choice.EffectiveDensity < threshold)
                .OrderByDescending(c => c.Choice.EffectiveDensity).ThenBy(c => c.Index);

            return [.. sufficient.Concat(rest).Select(c => c.Choice), .. overBudget];
        }

        /// <summary>The width over height given by the <c>&lt;img&gt;</c>'s <c>width</c>/<c>height</c> attributes, or 1 when they give none.</summary>
        private static double ImageAspect(CssBox box) =>
            double.TryParse(box.GetAttribute("width"), NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
            && double.TryParse(box.GetAttribute("height"), NumberStyles.Float, CultureInfo.InvariantCulture, out var h)
            && w > 0 && h > 0 ? w / h : 1;

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
            var pixelsPerPoint = box.HtmlContainer?.RenderContext?.LayoutUnitsPerPoint ?? 1.0;
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

                    if (length.Equals("auto", StringComparison.OrdinalIgnoreCase))
                    {
                        // sizes="auto" is the width the image is laid out at, and only counts on a lazy image (HTML Standard, "sizes"
                        // attribute). Layout has not run yet, so a definite width of its own is all there is; the next entry or 100vw
                        // stands in, and the refinement after layout chooses the bitmap against the real width.
                        if (IsLazy(box) && DefiniteWidth(box) is { } definite && definite > 0) return definite / unitsPerCssPx;
                        continue;
                    }

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

        private static bool IsLazy(CssBox box) => string.Equals(box.GetAttribute("loading")?.Trim(), "lazy", StringComparison.OrdinalIgnoreCase);

        /// <summary>The image's own absolute <c>width</c>, in layout units, or null when it is <c>auto</c>, a percentage or unparsable.</summary>
        private static double? DefiniteWidth(CssBox box)
        {
            var length = new CssLength(box.Width);
            if (length.HasError || length.IsPercentage || length.Number <= 0) return null;

            try
            {
                return CssValueParser.ParseLength(box.Width, 0, box);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                return null;
            }
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
