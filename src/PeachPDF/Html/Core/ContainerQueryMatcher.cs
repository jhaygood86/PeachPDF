#nullable enable

using System.Linq;
using PeachPDF.CSS;
using PeachPDF.Html.Core.Dom;

namespace PeachPDF.Html.Core
{
    /// <summary>
    /// Evaluates an <c>@container</c> rule's enclosing-container chain (<see cref="CssData.ContainerCondition"/>[])
    /// against a specific candidate box, per CSS Containment 3 SS7. The per-element analogue of
    /// <see cref="MediaQueryMatcher"/>: where <c>@media</c>'s truthiness is a single fact resolved once
    /// per render (page geometry doesn't vary per box), <c>@container</c>'s truthiness depends on which
    /// ancestor the candidate box's own nearest matching query container resolves to
    /// (<see cref="CssBox.FindNearestQueryContainer"/>), so this is evaluated per candidate box, in
    /// <see cref="CssData"/>'s <c>GatherMatchedRules.Collect</c>.
    /// </summary>
    /// <remarks>
    /// A size condition's grammar is the same <see cref="MediaList"/>/<see cref="Medium"/>/<see cref="MediaFeature"/>
    /// tree <c>@media</c> already parses through (<c>ContainerRule</c> reuses <c>FillMediaList</c>
    /// wholesale) - conjunctive nesting levels, comma-OR within a level, <c>not</c> via
    /// <c>Medium.IsInverse</c>, exactly mirroring <see cref="MediaQueryMatcher.Matches"/>'s shape. A
    /// <c>Medium.Type</c> is never meaningfully set for a container condition (there is no container
    /// "type" keyword the way <c>screen</c>/<c>print</c> exist for <c>@media</c>), so it is not checked
    /// here.
    /// </remarks>
    internal static class ContainerQueryMatcher
    {
        internal static bool Matches(CssData.ContainerCondition[]? enclosingContainers, CssBox node, ContainerQuerySizes? sizes)
        {
            if (enclosingContainers is null) return true;

            foreach (var level in enclosingContainers)
            {
                if (level.StyleCondition is { } styleCondition)
                {
                    var styleContainer = node.FindNearestQueryContainer(level.Name, CssBox.QueryKind.Style);
                    // CSS Containment 3 §7.2: no eligible container at all is the "unknown" state, which
                    // is falsy for @container - a deliberate divergence from @media's permissive-on-
                    // missing-geometry stance (a missing container here is invalid authoring, not a
                    // missing render context).
                    if (styleContainer is null || !styleCondition.Check(styleContainer)) return false;
                    continue;
                }

                if (level.SizeCondition is not { } sizeCondition) return false;

                var sizeContainer = node.FindNearestQueryContainer(level.Name, CssBox.QueryKind.Size);
                if (sizeContainer is null) return false;

                // No size data yet (the convergence loop's bootstrap pass) or this container isn't a
                // size-tracking one after all (shouldn't happen given FindNearestQueryContainer's own
                // Size-kind filter, but the lookup can still miss if a size box hasn't been visited by
                // BuildContainerQuerySizes for some reason) - falsy either way, not permissive.
                if (sizes is null || !sizes.TryGet(sizeContainer.Id, out var ctx)) return false;

                var anyMediumMatches = false;
                foreach (var medium in sizeCondition)
                {
                    var matches = medium.Features.All(feature => FeatureMatches(feature, ctx));
                    if (medium.IsInverse) matches = !matches;
                    if (matches)
                    {
                        anyMediumMatches = true;
                        break;
                    }
                }

                if (!anyMediumMatches) return false;
            }

            return true;
        }

        /// <summary>
        /// The container-valid feature subset (CSS Containment 3 SS7.3) - narrower than <c>@media</c>'s:
        /// no <c>color</c>/<c>resolution</c>/<c>hover</c>/etc., since those describe the output device,
        /// not a container's own box. <c>height</c>/<c>block-size</c>/<c>aspect-ratio</c>/<c>orientation</c>
        /// are false (not permissive) against an <c>inline-size</c>-only container, since it tracks only
        /// the inline axis (<see cref="ContainerQueryContext.BlockSizePt"/> is <c>null</c> there).
        /// </summary>
        private static bool FeatureMatches(MediaFeature feature, ContainerQueryContext ctx)
        {
            var name = feature.Name.ToLowerInvariant();
            if (name.StartsWith("min-", System.StringComparison.Ordinal)) name = name[4..];
            else if (name.StartsWith("max-", System.StringComparison.Ordinal)) name = name[4..];

            switch (name)
            {
                case "width":
                case "inline-size":
                    return MediaQueryMatcher.CompareLength(feature, ctx.InlineSizePt);

                case "height":
                case "block-size":
                    return ctx.BlockSizePt is { } blockPt && MediaQueryMatcher.CompareLength(feature, blockPt);

                case "aspect-ratio":
                    if (ctx.BlockSizePt is not { } aspectHeight || aspectHeight <= 0) return false;
                    if (feature.AsRatio() is not { } ratio) return false;
                    return MediaQueryMatcher.CompareNumeric(ctx.InlineSizePt / aspectHeight, ratio, feature.Comparison);

                case "orientation":
                    if (ctx.BlockSizePt is not { } orientationHeight) return false;
                    var orientation = ctx.InlineSizePt > orientationHeight ? "landscape" : "portrait";
                    return !feature.HasValue
                        || string.Equals(feature.Value, orientation, System.StringComparison.OrdinalIgnoreCase);

                default:
                    // Any other feature (color, resolution, hover, ...) is @media-only.
                    return false;
            }
        }
    }
}
