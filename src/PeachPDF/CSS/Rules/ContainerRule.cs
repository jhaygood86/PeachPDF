#nullable disable

using System.IO;
using System.Linq;

namespace PeachPDF.CSS
{
    internal sealed class ContainerRule : ConditionRule, IContainerRule
    {
        internal ContainerRule(StylesheetParser parser) : base(RuleType.Container, parser)
        {
            AppendChild(new MediaList(parser));
        }

        public override void ToCss(TextWriter writer, IStyleFormatter formatter)
        {
            var rules = formatter.Block(Rules);
            var name = "@container";
            if (!string.IsNullOrEmpty(Name))
                name = $"{name} {Name}";
            writer.Write(formatter.Rule(name, ConditionText, rules));
        }

        public MediaList Media => Children.OfType<MediaList>().FirstOrDefault();

        public string Name { get; set; }

        public IStyleQueryCondition StyleCondition { get; set; }

        public string ConditionText
        {
            // A style() condition has no MediaList content (Media stays empty) - StylesheetComposer.
            // CreateContainer parses it into StyleCondition instead, so ToCss falls back to a fixed
            // placeholder rather than reconstructing the original style() text (this rule's condition
            // tree isn't retained in a serializable form - a known simplification, since re-serializing
            // a style() query is not needed by anything layout/evaluation-side reads).
            get => StyleCondition is not null ? "style(...)" : Media.MediaText;
            set => Media.MediaText = value;
        }
    }
}
