#nullable disable

using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PeachPDF.CSS
{
    internal sealed class StyleRule : Rule, IStyleRule
    {
        // CSS Nesting: rules written inside this rule's block, resolved to absolute selectors against
        // this parent. Kept in a dedicated list (not Children) so Selector/Style lookups and ToCss are
        // unaffected. Populated by StylesheetComposer.FillDeclarations.
        private readonly List<IStyleRule> _nestedRules = [];

        public StyleRule(StylesheetParser parser) : base(RuleType.Style, parser)
        {
            AppendChild(AllSelector.Create());
            AppendChild(new StyleDeclaration(this));
        }

        public IReadOnlyList<IStyleRule> NestedRules => _nestedRules;

        public void AddNestedRule(IStyleRule rule) => _nestedRules.Add(rule);

        public override void ToCss(TextWriter writer, IStyleFormatter formatter)
        {
            writer.Write(formatter.Style(SelectorText, Style));
        }

        public ISelector Selector
        {
            // Indexed rather than Children.OfType<...>().FirstOrDefault(): this is read per box per
            // candidate rule during selector matching, and the LINQ form allocates an OfType iterator
            // plus a boxed list enumerator on every read.
            get
            {
                for (var i = 0; i < ChildList.Count; i++)
                {
                    if (ChildList[i] is ISelector selector) return selector;
                }

                return null;
            }
            set => ReplaceSingle(Selector, value);
        }

        public string SelectorText
        {
            // Null-safe because a rule whose selector failed to parse has no Selector child at all (the
            // setter removes it), and such a rule is observable through ToCss above while it is still on
            // StylesheetComposer's node stack. Callers that must tell "no selector" from "empty selector"
            // read Selector?.Text instead - see CreateNestedStyleRule.
            get => Selector?.Text ?? string.Empty;
            set => Selector = Parser.ParseSelector(value);
        }

        public StyleDeclaration Style
        {
            // Indexed for the same reason as Selector above. Null when the declaration block child
            // has been removed, which is what the LINQ FirstOrDefault it replaced also returned.
            get
            {
                for (var i = 0; i < ChildList.Count; i++)
                {
                    if (ChildList[i] is StyleDeclaration style) return style;
                }

                return null;
            }
        }
    }
}