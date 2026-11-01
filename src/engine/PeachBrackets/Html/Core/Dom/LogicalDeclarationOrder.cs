using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace PeachBrackets.Html.Dom
{
    /// <summary>
    /// The order in which a box's margin/padding/inset/border longhands were applied by the cascade, kept
    /// only for boxes that have a logical longhand. CSS Logical Properties 1 §1.1 puts a logical longhand and
    /// the physical one on the same edge in one cascade, so the later declaration wins; the two live in
    /// separate fields and the logical one is only mapped to its edge once the box's direction and writing
    /// mode are known, so the order is stamped as each declaration is applied and compared at that point.
    /// </summary>
    internal sealed class LogicalDeclarationOrder
    {
        private static readonly FrozenSet<string> PhysicalEdgeNames = BuildPhysicalEdgeNames();

        private readonly Dictionary<string, int> _stamps = new(StringComparer.Ordinal);
        private int _next;

        internal static bool IsPhysicalEdgeName(string propName) => PhysicalEdgeNames.Contains(propName);

        internal void Stamp(string propName) => _stamps[propName] = ++_next;

        /// <summary>The stamp of <paramref name="propName"/>, or 0 (earlier than any stamp) if never recorded.</summary>
        internal int Of(string propName) => _stamps.TryGetValue(propName, out var stamp) ? stamp : 0;

        private static FrozenSet<string> BuildPhysicalEdgeNames()
        {
            var names = new List<string>();
            foreach (var side in new[] { "top", "right", "bottom", "left" })
            {
                names.Add(side);
                names.Add("margin-" + side);
                names.Add("padding-" + side);
                names.Add("border-" + side + "-width");
                names.Add("border-" + side + "-style");
                names.Add("border-" + side + "-color");
            }

            return names.ToFrozenSet(StringComparer.Ordinal);
        }
    }
}
