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

        public IAttrSelector Create(string combinator, string match, string value, string prefix)
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
                Combinators.Exactly => new AttrMatchSelector(name, value),
                Combinators.InList => new AttrListSelector(name, value),
                Combinators.InToken => new AttrHyphenSelector(name, value),
                Combinators.Begins => new AttrBeginsSelector(name, value),
                Combinators.Ends => new AttrEndsSelector(name, value),
                Combinators.InText => new AttrContainsSelector(name, value),
                Combinators.Unlike => new AttrNotMatchSelector(name, value),
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