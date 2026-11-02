using PeachBrackets.Css;
using System;
using System.Collections.Generic;

namespace PeachBrackets.Html
{
    /// <summary>
    /// What the user is doing to the document right now, as the selectors <c>:hover</c>, <c>:active</c>, <c>:focus</c>, <c>:focus-visible</c> and
    /// <c>:focus-within</c> see it. An element is named by its position in the source (<see cref="Dom.HtmlTag.SourceOrdinal"/>), which survives
    /// the re-parse that applies a change of state.
    /// </summary>
    internal sealed class InteractionState
    {
        public HashSet<int> Hover { get; init; } = [];

        public HashSet<int> Active { get; init; } = [];

        public HashSet<int> FocusWithin { get; init; } = [];

        /// <summary>The focused element, or -1.</summary>
        public int Focused { get; init; } = -1;

        public bool FocusVisible { get; init; }

        public static bool IsStatePseudoClass(string name) =>
            name.Equals(PseudoClassNames.Hover, StringComparison.OrdinalIgnoreCase)
            || name.Equals(PseudoClassNames.Active, StringComparison.OrdinalIgnoreCase)
            || name.Equals(PseudoClassNames.Focus, StringComparison.OrdinalIgnoreCase)
            || name.Equals(PseudoClassNames.FocusVisible, StringComparison.OrdinalIgnoreCase)
            || name.Equals(PseudoClassNames.FocusWithin, StringComparison.OrdinalIgnoreCase);

        public bool Matches(string name, int ordinal)
        {
            if (ordinal < 0)
                return false;

            if (name.Equals(PseudoClassNames.Hover, StringComparison.OrdinalIgnoreCase)) return Hover.Contains(ordinal);
            if (name.Equals(PseudoClassNames.Active, StringComparison.OrdinalIgnoreCase)) return Active.Contains(ordinal);
            if (name.Equals(PseudoClassNames.Focus, StringComparison.OrdinalIgnoreCase)) return Focused == ordinal;
            if (name.Equals(PseudoClassNames.FocusVisible, StringComparison.OrdinalIgnoreCase)) return Focused == ordinal && FocusVisible;
            return FocusWithin.Contains(ordinal);
        }

        public bool SameAs(InteractionState other) =>
            Focused == other.Focused && FocusVisible == other.FocusVisible
            && Hover.SetEquals(other.Hover) && Active.SetEquals(other.Active) && FocusWithin.SetEquals(other.FocusWithin);

        /// <summary>The elements whose state differs between two states, in any of the five pseudo-classes.</summary>
        public static HashSet<int> Difference(InteractionState a, InteractionState b)
        {
            var changed = new HashSet<int>(a.Hover);
            changed.SymmetricExceptWith(b.Hover);

            var active = new HashSet<int>(a.Active);
            active.SymmetricExceptWith(b.Active);
            changed.UnionWith(active);

            var within = new HashSet<int>(a.FocusWithin);
            within.SymmetricExceptWith(b.FocusWithin);
            changed.UnionWith(within);

            if (a.Focused != b.Focused || a.FocusVisible != b.FocusVisible)
            {
                if (a.Focused >= 0) changed.Add(a.Focused);
                if (b.Focused >= 0) changed.Add(b.Focused);
            }

            return changed;
        }
    }
}
