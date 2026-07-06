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

        private readonly FrozenDictionary<string, ISelector> _selectors =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    //TODO some lack implementation (selection, content, ...)
                    // some implementations are dubious (first-line, first-letter, ...)
                    PseudoElementNames.Before,
                    PseudoElementNames.After,
                    PseudoElementNames.Selection,
                    PseudoElementNames.FirstLine,
                    PseudoElementNames.FirstLetter,
                    PseudoElementNames.Content,
                }
                .ToDictionary(x => x, PseudoElementSelector.Create)
                .ToFrozenDictionary();

        #endregion

        internal PseudoElementSelectorFactory(StylesheetParser parser = null)
        {
            _parser = parser;
        }

        internal static PseudoElementSelectorFactory Instance => Lazy.Value;

        public ISelector Create(string name)
        {
            return _selectors.TryGetValue(name, out var selector) ? selector :
                ((_parser?.Options.AllowInvalidSelectors ?? false) ?
                PseudoElementSelector.Create(name) : null);
        }
    }
}