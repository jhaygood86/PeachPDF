#nullable disable

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    internal sealed class PseudoClassSelectorFactory
    {
        private static readonly Lazy<PseudoClassSelectorFactory> Lazy =
            new(() => new PseudoClassSelectorFactory());

        #region Selectors

        private static Dictionary<string, ISelector> BuildSelectors()
        {
            // :root and :link are the only two ident-form pseudo-classes CssData.DoesSelectorMatch can
            // actually answer; everything else here parses and selects nothing, and lives in the one
            // list that says so (UnmatchableSelectors.PseudoClasses).
            var selectors = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    PseudoClassNames.Root,
                    PseudoClassNames.Link,
                }
                .Union(UnmatchableSelectors.PseudoClasses, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x, PseudoClassSelector.Create, StringComparer.OrdinalIgnoreCase);

            selectors.Add(PseudoElementNames.Before,
                PseudoElementSelectorFactory.Instance.Create(PseudoElementNames.Before));
            selectors.Add(PseudoElementNames.After,
                PseudoElementSelectorFactory.Instance.Create(PseudoElementNames.After));
            selectors.Add(PseudoElementNames.FirstLine,
                PseudoElementSelectorFactory.Instance.Create(PseudoElementNames.FirstLine));
            selectors.Add(PseudoElementNames.FirstLetter,
                PseudoElementSelectorFactory.Instance.Create(PseudoElementNames.FirstLetter));

            // Structural pseudo-classes: bare idents are equivalent to their nth-*(1) function form
            // (see CSS Selectors spec), so wire them to the same ChildSelector subtypes that
            // "nth-child(...)" etc. produce, rather than a generic PseudoClassSelector with no
            // positional semantics. Kind uses AllSelector.Create() (never null) to match the
            // invariant ChildFunctionState<T>.Produce() upholds for the function-form parse path.
            selectors.Add(PseudoClassNames.FirstChild, new FirstChildSelector().With(0, 1, AllSelector.Create()));
            selectors.Add(PseudoClassNames.LastChild, new LastChildSelector().With(0, 1, AllSelector.Create()));
            selectors.Add(PseudoClassNames.FirstOfType, new FirstTypeSelector().With(0, 1, AllSelector.Create()));
            selectors.Add(PseudoClassNames.LastOfType, new LastTypeSelector().With(0, 1, AllSelector.Create()));
            selectors.Add(PseudoClassNames.OnlyChild, new OnlyChildSelector());
            selectors.Add(PseudoClassNames.OnlyType, new OnlyOfTypeSelector());

            return selectors;
        }

        // Pseudo-class names are ASCII case-insensitive (CSS Syntax 3 §3.3 / Selectors 4 §3.1) and the
        // lexer does not normalize an ident's case, so the lookup has to.
        private static readonly FrozenDictionary<string, ISelector> Selectors =
            BuildSelectors().ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        #endregion

        internal static PseudoClassSelectorFactory Instance => Lazy.Value;

        public ISelector Create(string name)
        {
            if (Selectors.TryGetValue(name, out var selector)) return selector;

            // A vendor extension (`:-moz-focusring`, `:-moz-placeholder`, …) parses and never matches,
            // rather than invalidating the selector list it appears in. See UnmatchableSelectors.
            return UnmatchableSelectors.IsVendorExtension(name)
                ? PseudoClassSelector.Create(name)
                : null;
        }
    }
}