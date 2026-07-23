#nullable enable

using System;
using System.Linq;
using PeachPDF.CSS;

namespace PeachPDF.Html.Core
{
    /// <summary>
    /// Evaluates a rule's enclosing <c>@media</c> chain against a <see cref="MediaQueryContext"/>, so
    /// feature conditions (<c>min-width</c>, range syntax like <c>width &gt;= 48rem</c>,
    /// <c>orientation</c>, <c>resolution</c>, <c>prefers-color-scheme</c>, …) actually gate which rules
    /// apply — the fix for issue #235's <c>@media</c> half. Media Queries 4.
    /// </summary>
    /// <remarks>
    /// Any feature this matcher cannot actually evaluate causes its <c>@media</c> block to be ignored — a
    /// feature name the CSS-OM does not register fails to parse and its query becomes <c>not all</c>
    /// (matches nothing) upstream, and a registered feature this matcher does not model hits the
    /// <c>default</c> arm and returns <c>false</c>. Both are Media Queries 4's "unknown feature is false":
    /// rather than apply rules guarded by a condition we can't test, the block is dropped. A feature that
    /// IS modeled but whose runtime context is missing (e.g. a <c>width</c> query with no page geometry,
    /// as in standalone SVG) stays permissive — that is an unknown *context*, not an unsupported feature.
    /// </remarks>
    internal static class MediaQueryMatcher
    {
        /// <summary>
        /// True if every level of the enclosing <c>@media</c> chain matches (nesting is conjunctive), or
        /// the rule isn't nested in any <c>@media</c> at all. Within one level's comma-separated media
        /// query list, at least one query must match.
        /// </summary>
        internal static bool Matches(MediaList[]? enclosingMedia, MediaQueryContext context)
        {
            if (enclosingMedia is null) return true;

            foreach (var mediaList in enclosingMedia)
            {
                var anyMatches = false;
                foreach (var medium in mediaList)
                {
                    if (MediumMatches(medium, context))
                    {
                        anyMatches = true;
                        break;
                    }
                }

                if (!anyMatches) return false;
            }

            return true;
        }

        private static bool MediumMatches(Medium medium, MediaQueryContext context)
        {
            // A feature-only query (e.g. `@media (min-width: 768px)`) has no type - treat as `all`.
            var typeMatches = string.IsNullOrEmpty(medium.Type)
                || string.Equals(medium.Type, context.MediaType, StringComparison.OrdinalIgnoreCase)
                || string.Equals(medium.Type, "all", StringComparison.OrdinalIgnoreCase);

            var result = typeMatches && medium.Features.All(feature => FeatureMatches(feature, context));

            // `not` negates the whole query (type + features), per Media Queries 4 §3.1.
            return medium.IsInverse ? !result : result;
        }

        private static bool FeatureMatches(MediaFeature feature, MediaQueryContext context)
        {
            var name = feature.Name.ToLowerInvariant();
            if (name.StartsWith("min-", StringComparison.Ordinal)) name = name[4..];
            else if (name.StartsWith("max-", StringComparison.Ordinal)) name = name[4..];

            switch (name)
            {
                case "width":
                case "device-width":
                case "inline-size":
                    return CompareLength(feature, context.ViewportWidthPt);
                case "height":
                case "device-height":
                case "block-size":
                    return CompareLength(feature, context.ViewportHeightPt);

                case "orientation":
                    if (context.ViewportWidthPt is not { } ow || context.ViewportHeightPt is not { } oh)
                        return true;
                    // MQ4 §4.1: portrait is height ≥ width, so a square viewport is portrait.
                    var orientation = ow > oh ? "landscape" : "portrait";
                    return !feature.HasValue
                        || string.Equals(feature.Value, orientation, StringComparison.OrdinalIgnoreCase);

                case "aspect-ratio":
                case "device-aspect-ratio":
                    if (context.ViewportWidthPt is not { } aw || context.ViewportHeightPt is not { } ah || ah <= 0)
                        return true;
                    if (feature.AsRatio() is not { } ratio) return true;
                    return CompareNumeric(aw / ah, ratio, feature.Comparison);

                case "resolution":
                    if (feature.AsResolution() is not { } res) return true;
                    return CompareNumeric(context.ResolutionDpi, res.To(Resolution.Unit.Dpi), feature.Comparison);

                case "device-pixel-ratio":
                    // Non-standard feature; the value is a plain number of device pixels per CSS px,
                    // i.e. the same as `resolution` in dppx.
                    if (feature.AsRatio() is not { } dpr) return true;
                    return CompareNumeric(context.ResolutionDpi / 96d, dpr, feature.Comparison);

                // Color output: 8 bits/channel, not a color-index or monochrome device, not a grid/tty.
                case "color":
                    return CompareCountOrBoolean(feature, 8);
                case "color-index":
                case "monochrome":
                    return CompareCountOrBoolean(feature, 0);
                case "grid":
                    return CompareCountOrBoolean(feature, 0);

                case "prefers-color-scheme":
                    var scheme = context.PreferredColorScheme == PdfColorScheme.Dark ? "dark" : "light";
                    return !feature.HasValue
                        || string.Equals(feature.Value, scheme, StringComparison.OrdinalIgnoreCase);

                // A static PDF cannot animate, hover, or be pointed at, and once painted cannot update.
                // The renderer's stance is `reduce`, which is NOT the `no-preference` resting value, so the
                // boolean form `(prefers-reduced-motion)` is true (MQ5 §5.3).
                case "prefers-reduced-motion":
                    return MatchesKeyword(feature, "reduce", booleanContextResult: true);
                case "hover":
                case "any-hover":
                case "pointer":
                case "any-pointer":
                case "update":
                case "scripting":
                    return MatchesKeyword(feature, "none", booleanContextResult: false);
                case "prefers-contrast":
                case "prefers-reduced-transparency":
                    return MatchesKeyword(feature, "no-preference", booleanContextResult: false);

                default:
                    // A registered feature this matcher does not evaluate (e.g. `scan`): treat as not
                    // matching so the whole @media block is ignored, rather than applying rules guarded by
                    // a condition we can't test (see the class remarks).
                    return false;
            }
        }

        private static bool CompareLength(MediaFeature feature, double? actualPt)
        {
            if (actualPt is not { } actual) return true;   // no page geometry → permissive
            if (feature.AsLength() is not { } length) return true;

            // In a media query, `em`/`rem` resolve against the initial font size (16px), NOT the
            // document root's font size (Media Queries 4 §1.3 / §6). 16px = 12pt.
            const double initialFontPt = 16d * PeachPDF.CSS.Length.PointsPerPx;
            var valuePt = length.ToPixels(initialFontPt, initialFontPt, actual);

            return CompareNumeric(actual, valuePt, feature.Comparison);
        }

        private static bool CompareCountOrBoolean(MediaFeature feature, int actual)
        {
            if (!feature.HasValue) return actual > 0;                       // boolean context, e.g. `(color)`
            if (!int.TryParse(feature.Value, out var value)) return true;   // unparseable → permissive
            return CompareNumeric(actual, value, feature.Comparison);
        }

        private static bool MatchesKeyword(MediaFeature feature, string deviceValue, bool booleanContextResult)
        {
            // The value form `(feature: value)` matches iff the queried value equals the device's value.
            // The boolean form `(feature)` is true iff the device value is not the feature's "none"/
            // "no-preference" resting state — false for hover/pointer/update/scripting/prefers-contrast/
            // prefers-reduced-transparency (all at their resting default), true for prefers-reduced-motion
            // (its `reduce` stance is not the resting `no-preference`).
            if (!feature.HasValue) return booleanContextResult;
            return string.Equals(feature.Value, deviceValue, StringComparison.OrdinalIgnoreCase);
        }

        private static bool CompareNumeric(double actual, double value, MediaFeatureComparison comparison)
        {
            const double epsilon = 1e-6;
            return comparison switch
            {
                MediaFeatureComparison.Minimum => actual >= value - epsilon,
                MediaFeatureComparison.Maximum => actual <= value + epsilon,
                MediaFeatureComparison.GreaterThan => actual > value + epsilon,
                MediaFeatureComparison.LessThan => actual < value - epsilon,
                _ => Math.Abs(actual - value) <= epsilon
            };
        }
    }
}
