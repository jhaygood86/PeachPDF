#nullable disable

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    internal sealed class PseudoElementSelectorFactory
    {
        private static readonly Lazy<PseudoElementSelectorFactory> Lazy =
            new(() => new PseudoElementSelectorFactory());

        private readonly StylesheetParser _parser;

        #region Selectors

        // Pseudo-element names are ASCII case-insensitive (CSS Pseudo-Elements 4 §2) and the lexer does
        // not normalize an ident's case, so the lookup has to.
        private readonly FrozenDictionary<string, ISelector> _selectors =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    // ::before/::after/::marker/::first-line/::first-letter are the ones that generate a
                    // real box; ::selection and ::content parse and paint nothing, like everything in
                    // UnmatchableSelectors.PseudoElements below them.
                    PseudoElementNames.Before,
                    PseudoElementNames.After,
                    PseudoElementNames.Marker,
                    PseudoElementNames.Selection,
                    PseudoElementNames.FirstLine,
                    PseudoElementNames.FirstLetter,
                    PseudoElementNames.Content,
                }
                .Union(UnmatchableSelectors.PseudoElements, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x, PseudoElementSelector.Create, StringComparer.OrdinalIgnoreCase)
                .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        #endregion

        internal PseudoElementSelectorFactory(StylesheetParser parser = null)
        {
            _parser = parser;
        }

        internal static PseudoElementSelectorFactory Instance => Lazy.Value;

        public ISelector Create(string name)
        {
            if (_selectors.TryGetValue(name, out var selector)) return selector;

            // A vendor extension (`::-webkit-file-upload-button`, `::-moz-focus-inner`, …) parses and
            // never matches, rather than invalidating the selector list it appears in. The functional
            // forms (`::part()`, `::slotted()`, …) never reach here — SelectorConstructor.OnPseudoElement
            // builds those itself, since their argument is part of the selector's text. See
            // UnmatchableSelectors.
            return UnmatchableSelectors.IsVendorExtension(name) ||
                   (_parser?.Options.AllowInvalidSelectors ?? false)
                ? PseudoElementSelector.Create(name)
                : null;
        }
    }
}