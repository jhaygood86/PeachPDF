#nullable enable

using System.Linq;
using PeachBrackets.Css;
using PeachBrackets.Html.Dom;

namespace PeachBrackets.Html
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
                var result = level switch
                {
                    { Mixed: { } mixed } => mixed.Evaluate(node, level.Name, sizes),
                    { StyleCondition: { } styleCondition } => EvaluateStyle(styleCondition, node, level.Name),
                    { SizeCondition: { } sizeCondition } => EvaluateSize(sizeCondition, node, level.Name, sizes),
                    _ => false,
                };

                if (result != true) return false;
            }

            return true;
        }

        /// <summary>
        /// A <c>style()</c> query against the nearest style container. CSS Containment 3 §7.2: no eligible container at all is
        /// the "unknown" state (<c>null</c>), which is falsy for @container - a deliberate divergence from @media's
        /// permissive-on-missing-geometry stance (a missing container here is invalid authoring, not a missing render context).
        /// </summary>
        internal static bool? EvaluateStyle(IStyleQueryCondition condition, CssBox node, string? containerName)
        {
            var styleContainer = node.FindNearestQueryContainer(containerName, CssBox.QueryKind.Style);
            return styleContainer is null ? null : condition.Check(styleContainer);
        }

        /// <summary>A size-feature query against the nearest size container; <c>null</c> (unknown) when there is none or it has no size data yet.</summary>
        internal static bool? EvaluateSize(MediaList condition, CssBox node, string? containerName, ContainerQuerySizes? sizes)
        {
            var sizeContainer = node.FindNearestQueryContainer(containerName, CssBox.QueryKind.Size);
            if (sizeContainer is null) return null;

            // No size data yet (the convergence loop's bootstrap pass) or this container isn't a
            // size-tracking one after all (shouldn't happen given FindNearestQueryContainer's own
            // Size-kind filter, but the lookup can still miss if a size box hasn't been visited by
            // BuildContainerQuerySizes for some reason) - falsy either way, not permissive.
            if (sizes is null || !sizes.TryGet(sizeContainer.Id, out var ctx)) return null;

            foreach (var medium in condition)
            {
                var matches = medium.Features.All(feature => FeatureMatches(feature, ctx));
                if (medium.IsInverse) matches = !matches;
                if (matches) return true;
            }

            return false;
        }

        /// <summary>
        /// The container-valid feature subset (CSS Containment 3 SS7.3) - narrower than <c>@media</c>'s:
        /// no <c>color</c>/<c>resolution</c>/<c>hover</c>/etc., since those describe the output device,
        /// not a container's own box. <c>width</c>/<c>aspect-ratio</c>/<c>orientation</c> are always
        /// physical (never adjusted for the container's own <c>writing-mode</c>); <c>inline-size</c>/
        /// <c>block-size</c> follow the container's own inline/block axis instead, rotating onto the
        /// orthogonal physical axis under <c>vertical-rl</c>/<c>vertical-lr</c> (CSS Writing Modes 4
        /// §7.1) - <c>width</c>/<c>inline-size</c> and <c>height</c>/<c>block-size</c> only coincide
        /// under the container's default <c>horizontal-tb</c>. <c>height</c>/<c>block-size</c>/
        /// <c>aspect-ratio</c>/<c>orientation</c> are false (not permissive) against an
        /// <c>inline-size</c>-only container, since it tracks only its own inline axis
        /// (<see cref="ContainerQueryContext.HeightPt"/>/<see cref="ContainerQueryContext.BlockSizePt"/>
        /// are both <c>null</c> there).
        /// </summary>
        private static bool FeatureMatches(MediaFeature feature, ContainerQueryContext ctx)
        {
            var name = feature.Name.ToLowerInvariant();
            if (name.StartsWith("min-", System.StringComparison.Ordinal)) name = name[4..];
            else if (name.StartsWith("max-", System.StringComparison.Ordinal)) name = name[4..];

            switch (name)
            {
                case "width":
                    return MediaQueryMatcher.CompareLength(feature, ctx.WidthPt, ctx.PixelsPerPoint);

                case "inline-size":
                    return MediaQueryMatcher.CompareLength(feature, ctx.InlineSizePt, ctx.PixelsPerPoint);

                case "height":
                    return ctx.HeightPt is { } heightPt && MediaQueryMatcher.CompareLength(feature, heightPt, ctx.PixelsPerPoint);

                case "block-size":
                    return ctx.BlockSizePt is { } blockPt && MediaQueryMatcher.CompareLength(feature, blockPt, ctx.PixelsPerPoint);

                case "aspect-ratio":
                    if (ctx.HeightPt is not { } aspectHeight || aspectHeight <= 0) return false;
                    if (feature.AsRatio() is not { } ratio) return false;
                    return MediaQueryMatcher.CompareNumeric(ctx.WidthPt / aspectHeight, ratio, feature.Comparison);

                case "orientation":
                    if (ctx.HeightPt is not { } orientationHeight) return false;
                    var orientation = ctx.WidthPt > orientationHeight ? "landscape" : "portrait";
                    return !feature.HasValue
                        || string.Equals(feature.Value, orientation, System.StringComparison.OrdinalIgnoreCase);

                default:
                    // Any other feature (color, resolution, hover, ...) is @media-only.
                    return false;
            }
        }
    }
}
