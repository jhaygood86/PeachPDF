#nullable disable

using System.IO;

namespace PeachBrackets.Css
{
    /// <summary>
    /// <c>@starting-style { rules }</c> (CSS Transitions 2 §4): style rules that give an element the style a transition starts from the first time it is styled,
    /// or when it stops being <c>display: none</c>. They take no part in the cascade for the element's ordinary style.
    /// </summary>
    internal interface IStartingStyleRule : IGroupingRule
    {
    }

    /// <summary>The <c>@starting-style</c> grouping rule.</summary>
    internal sealed class StartingStyleRule : GroupingRule, IStartingStyleRule
    {
        internal StartingStyleRule(StylesheetParser parser)
            : base(RuleType.StartingStyle, parser)
        {
        }

        public override void ToCss(TextWriter writer, IStyleFormatter formatter)
        {
            var rules = formatter.Block(Rules);
            writer.Write(formatter.Rule("@starting-style", null, rules));
        }
    }
}
