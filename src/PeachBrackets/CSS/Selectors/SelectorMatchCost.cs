namespace PeachPDF.CSS
{
    /// <summary>
    /// A static, ascending cost tier per <see cref="ISelector"/> kind, used to evaluate a compound or
    /// list selector's members cheapest-first so an <c>All</c>/<c>Any</c> short-circuits before an
    /// expensive check ever runs - the same technique real selector engines (e.g. WebKit's
    /// <c>SelectorChecker</c>) use for the same reason. See <see cref="Selectors.MatchOrder"/>.
    /// </summary>
    /// <remarks>
    /// Reordering is safe here specifically because matching every one of these kinds is a pure boolean
    /// predicate with no observable side effect - the one real side effect in this area,
    /// <c>CssData.DoesSelectorMatch(CompoundSelector, ICssDomNode?)</c>'s pseudo-element synthesis, only
    /// ever fires for the trailing pseudo-element itself, which is always pinned and evaluated
    /// separately from the members this cost function ranks. AND/OR over pure predicates is commutative,
    /// so changing evaluation order cannot change the final boolean result, only which predicates get
    /// skipped by short-circuiting.
    /// </remarks>
    internal static class SelectorMatchCost
    {
        public static int Of(ISelector selector) => selector switch
        {
            // Tier 0: O(1) direct field/string compare.
            IdSelector or ClassSelector or TypeSelector or AllSelector => 0,

            // Tier 1: attribute lookup plus a string/substring op.
            AttrAvailableSelector or AttrMatchSelector or AttrListSelector or AttrBeginsSelector
                or AttrEndsSelector or AttrHyphenSelector or AttrContainsSelector or AttrNotMatchSelector => 1,

            // Tier 2: simple document-tree checks (:root, :empty, :link, ...).
            PseudoClassSelector => 2,

            // Tier 3: needs to enumerate/count siblings. Covers FirstChildSelector/LastChildSelector/
            // FirstTypeSelector/LastTypeSelector/FirstColumnSelector/LastColumnSelector via subtype
            // matching against their common ChildSelector base.
            ChildSelector or OnlyChildSelector or OnlyOfTypeSelector => 3,

            // Tier 4: walks ancestors to the document root.
            LangSelector => 4,

            // Tier 5: recursive re-entry into DoesSelectorMatch.
            NotSelector or MatchesSelector => 5,

            // Tier 6: relative/subtree search - the most expensive kind.
            HasSelector => 6,

            // Anything else (PseudoElementSelector - always pinned/handled separately by its caller
            // rather than ranked here - and any unrecognized kind) sorts last, but is never actually
            // ranked against real work in practice.
            _ => 7
        };
    }
}
