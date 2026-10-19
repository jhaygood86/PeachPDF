#nullable disable

using System;

namespace PeachPDF.CSS
{
    internal sealed class AttributeSelectorFactory
    {
        private static readonly Lazy<AttributeSelectorFactory> Lazy = new(() => new AttributeSelectorFactory());

        private AttributeSelectorFactory()
        {
        }

        internal static AttributeSelectorFactory Instance => Lazy.Value;

        public IAttrSelector Create(string combinator, string match, string value, string prefix,
            AttrCaseSensitivity caseSensitivity = AttrCaseSensitivity.Default)
        {
            var name = match;

            if (!string.IsNullOrEmpty(prefix))
            {
                name = AttributeSelectorFactory.FormFront(prefix, match);
                _ = AttributeSelectorFactory.FormMatch(prefix, match);
            }

            // A reflection-free dispatch (no Activator.CreateInstance) so the two-arg Attr*Selector
            // constructors are statically reachable and survive trimming/AOT (IsTrimmable=true) - see
            // upstream ExCSS commit c497ca7. Unknown combinators fall back to a presence selector.
            return combinator switch
            {
                Combinators.Exactly => new AttrMatchSelector(name, value, caseSensitivity),
                Combinators.InList => new AttrListSelector(name, value, caseSensitivity),
                Combinators.InToken => new AttrHyphenSelector(name, value, caseSensitivity),
                Combinators.Begins => new AttrBeginsSelector(name, value, caseSensitivity),
                Combinators.Ends => new AttrEndsSelector(name, value, caseSensitivity),
                Combinators.InText => new AttrContainsSelector(name, value, caseSensitivity),
                Combinators.Unlike => new AttrNotMatchSelector(name, value, caseSensitivity),
                _ => new AttrAvailableSelector(name, value),
            };
        }

        private static string FormFront(string prefix, string match)
        {
            return string.Concat(prefix, Combinators.Pipe, match);
        }

        private static string FormMatch(string prefix, string match)
        {
            return prefix.Is(Keywords.Asterisk) ? match : string.Concat(prefix, PseudoClassNames.Separator, match);
        }
    }
}