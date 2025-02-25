#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    public sealed class PseudoClassSelectorFactory
    {
        private static readonly Lazy<PseudoClassSelectorFactory> Lazy =
            new(() =>
                {
                    var factory = new PseudoClassSelectorFactory();
                    Selectors.Add(PseudoElementNames.Before,
                        PseudoElementSelectorFactory.Instance.Create(PseudoElementNames.Before));
                    Selectors.Add(PseudoElementNames.After,
                        PseudoElementSelectorFactory.Instance.Create(PseudoElementNames.After));
                    Selectors.Add(PseudoElementNames.FirstLine,
                        PseudoElementSelectorFactory.Instance.Create(PseudoElementNames.FirstLine));
                    Selectors.Add(PseudoElementNames.FirstLetter,
                        PseudoElementSelectorFactory.Instance.Create(PseudoElementNames.FirstLetter));
                    return factory;
                }
            );

        #region Selectors

        private static readonly Dictionary<string, ISelector> Selectors =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    PseudoClassNames.Root,
                    PseudoClassNames.Scope,
                    PseudoClassNames.OnlyType,
                    PseudoClassNames.FirstOfType,
                    PseudoClassNames.LastOfType,
                    PseudoClassNames.OnlyChild,
                    PseudoClassNames.FirstChild,
                    PseudoClassNames.LastChild,
                    PseudoClassNames.Empty,
                    PseudoClassNames.AnyLink,
                    PseudoClassNames.Link,
                    PseudoClassNames.Visited,
                    PseudoClassNames.Active,
                    PseudoClassNames.Hover,
                    PseudoClassNames.Focus,
                    PseudoClassNames.FocusVisible,
                    PseudoClassNames.FocusWithin,
                    PseudoClassNames.Target,
                    PseudoClassNames.Enabled,
                    PseudoClassNames.Disabled,
                    PseudoClassNames.Default,
                    PseudoClassNames.Checked,
                    PseudoClassNames.Indeterminate,
                    PseudoClassNames.PlaceholderShown,
                    PseudoClassNames.Unchecked,
                    PseudoClassNames.Valid,
                    PseudoClassNames.Invalid,
                    PseudoClassNames.Required,
                    PseudoClassNames.ReadOnly,
                    PseudoClassNames.ReadWrite,
                    PseudoClassNames.InRange,
                    PseudoClassNames.OutOfRange,
                    PseudoClassNames.Optional,
                    PseudoClassNames.Shadow,
                }
                .ToDictionary(x => x, PseudoClassSelector.Create);

        #endregion

        internal static PseudoClassSelectorFactory Instance => Lazy.Value;

        public ISelector Create(string name)
        {
            return Selectors.TryGetValue(name, out var selector) ? selector : null;
        }
    }
}