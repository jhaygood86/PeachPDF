#nullable enable

using System;
using System.Collections.Generic;

namespace PeachPDF.Html.Core
{
    /// <summary>
    /// Every eligible <c>@container</c> query container's resolved size for one layout pass, keyed by
    /// <see cref="Dom.CssBox.Id"/> - stable across the repeated re-parses of identical HTML the
    /// container-query convergence loop performs (see <see cref="HtmlContainerInt.PerformLayout"/>),
    /// since a real element's <c>Id</c> is assigned purely during HTML parsing, before any cascade or
    /// layout runs. <c>null</c> on the loop's first pass, when no container size is known yet - every
    /// <c>@container</c> condition is then falsy (<see cref="ContainerQueryMatcher.Matches"/>), which is
    /// also today's (pre-fix) behavior and the correct bootstrap for the loop.
    /// </summary>
    internal sealed class ContainerQuerySizes
    {
        private readonly Dictionary<uint, ContainerQueryContext> _byId;

        internal ContainerQuerySizes(Dictionary<uint, ContainerQueryContext> byId)
        {
            _byId = byId;
        }

        internal bool TryGet(uint boxId, out ContainerQueryContext context) => _byId.TryGetValue(boxId, out context);

        /// <summary>
        /// Whether this and <paramref name="other"/> record the same set of size containers with the
        /// same sizes, within a small epsilon - the convergence test <see cref="HtmlContainerInt.PerformLayout"/>'s
        /// container-query loop stops on. Real-element <c>CssBox.Id</c>s are stable across the loop's
        /// repeated re-parses of identical HTML (see this class's own remarks), so a differing key set
        /// would mean a size container's own existence changed pass-over-pass (a container-gated rule
        /// toggling display, e.g.) - also treated as "not converged", since that is itself a size-
        /// relevant change the next pass needs to react to.
        /// </summary>
        internal bool SizesEqual(ContainerQuerySizes other)
        {
            const double epsilonPt = 0.01;

            if (_byId.Count != other._byId.Count) return false;

            foreach (var (id, context) in _byId)
            {
                if (!other._byId.TryGetValue(id, out var otherContext)) return false;
                if (Math.Abs(context.WidthPt - otherContext.WidthPt) > epsilonPt) return false;
                if (Math.Abs(context.InlineSizePt - otherContext.InlineSizePt) > epsilonPt) return false;

                // WidthPt/InlineSizePt (and HeightPt/BlockSizePt below) only ever diverge when the
                // container's own writing-mode is vertical - comparing both pairs, not just one, is what
                // catches a container whose writing-mode itself changed pass-over-pass in a way that
                // happens to leave InlineSizePt/BlockSizePt coincidentally unchanged while the physical
                // width/height a width/height/aspect-ratio/orientation condition reads did not.
                var heightDiffers = (context.HeightPt, otherContext.HeightPt) switch
                {
                    (null, null) => false,
                    (double a, double b) => Math.Abs(a - b) > epsilonPt,
                    _ => true
                };
                if (heightDiffers) return false;

                var blockDiffers = (context.BlockSizePt, otherContext.BlockSizePt) switch
                {
                    (null, null) => false,
                    (double a, double b) => Math.Abs(a - b) > epsilonPt,
                    _ => true
                };
                if (blockDiffers) return false;
            }

            return true;
        }
    }
}
