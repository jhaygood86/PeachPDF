using System.Collections.Generic;
using System.Linq;
using PeachBrackets.Css;

namespace PeachBrackets.Html.Utils
{
    /// <summary>
    /// Whether a selector gives two elements the same answer when they have the same parent, tag and attributes: the question the
    /// cascade asks before it lets one sibling take another's computed style (<c>DomParser.CascadeApplyStyles</c>).
    /// </summary>
    /// <remarks>
    /// What a selector asks of its <i>subject</i> (the element being matched) decides it: the tag, class, id and any other attribute
    /// are the same for such siblings, and so is everything above them - the ancestors, the ancestors' siblings and the ancestors'
    /// own positions - which is why a selector may be as structural as it likes in every part but the last
    /// (<c>li:first-child a</c>, <c>h1 + p span</c>). The subject itself is unstable when it asks about its position among its
    /// siblings (<c>:first-child</c>, <c>:nth-child()</c>, <c>:only-of-type</c>, the column forms, an adjacent or general sibling
    /// combinator in front of it), about what it contains (<c>:empty</c>, <c>:has()</c>, <c>:placeholder-shown</c>, which reads a
    /// textarea's text), or about anything this does not know the answer to: the list below is of what is known to be stable, not
    /// of what is known not to be.
    /// </remarks>
    internal static class SelectorSiblingStability
    {
        /// <summary>Whether <paramref name="selector"/> matches (or fails to match) every sibling of the same tag and attributes alike.</summary>
        internal static bool IsStable(ISelector selector) => selector switch
        {
            ListSelector list => list.All(IsStable),
            ComplexSelector complex => IsStableComplex(complex),
            CompoundSelector compound => compound.All(IsStable),
            _ => IsStableSimple(selector)
        };

        private static bool IsStableComplex(ComplexSelector complex)
        {
            var parts = complex.ToList();
            if (parts.Count == 0) return true;

            // A part's delimiter is the combinator between it and the one after it (see CssData.DoesSelectorMatch(ComplexSelector)),
            // so the one in front of the subject is the last but one's. Everything left of it is reached by walking up the tree.
            if (parts.Count > 1 && parts[^2].Delimiter is not (null or " " or ">")) return false;

            return parts[^1].Selector is { } subject && IsStable(subject);
        }

        private static bool IsStableSimple(ISelector selector)
        {
            switch (selector)
            {
                case TypeSelector or ClassSelector or IdSelector or AllSelector or LangSelector or PseudoElementSelector:
                    return true;

                case IAttrSelector:
                    return true;

                // Named pseudo-classes: most answer a constant (a static PDF has no hover, focus or history), :root and :scope the
                // element's place at the top of the tree, :link and :any-link its tag and href. These two read the element's content.
                case PseudoClassSelector pseudoClass:
                    return pseudoClass.Class != PseudoClassNames.Empty && pseudoClass.Class != PseudoClassNames.PlaceholderShown;

                case NotSelector not:
                    return IsStable(not.Inner);

                case MatchesSelector matches:
                    return IsStable(matches.Inner);

                default:
                    return false;
            }
        }

        /// <summary>
        /// The compound selectors in <paramref name="selector"/> whose subject ends in a pseudo-element that, matched against an element,
        /// adds a box to it or marks it (<c>::before</c>, <c>::after</c>, <c>::marker</c>, <c>::first-letter</c>): matching has that
        /// effect on the element, so an element that takes another's style would go without it. The pseudo-element on its own, or at the
        /// end of a complex selector with nothing in front of it, only matches a box that is already there and does neither.
        /// </summary>
        internal static void CollectBoxCreatingSubjects(ISelector selector, List<CompoundSelector> subjects)
        {
            switch (selector)
            {
                case ListSelector list:
                    foreach (var alternative in list)
                        CollectBoxCreatingSubjects(alternative, subjects);
                    break;

                case ComplexSelector complex:
                    if (complex.LastOrDefault().Selector is { } subject)
                        CollectBoxCreatingSubjects(subject, subjects);
                    break;

                case CompoundSelector compound:
                    if (compound.LastOrDefault() is PseudoElementSelector { Name: PseudoElementNames.Before or PseudoElementNames.After
                            or PseudoElementNames.Marker or PseudoElementNames.FirstLetter })
                        subjects.Add(compound);
                    break;
            }
        }
    }
}
