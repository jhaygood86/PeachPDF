#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    public sealed class PseudoElementSelectorFactory
    {
        private static readonly Lazy<PseudoElementSelectorFactory> Lazy =
            new(() => new PseudoElementSelectorFactory());

        private readonly StylesheetParser _parser;

        #region Selectors

        private readonly Dictionary<string, ISelector> _selectors =
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
                .ToDictionary(x => x, PseudoElementSelector.Create);

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