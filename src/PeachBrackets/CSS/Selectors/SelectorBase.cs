using System.IO;

namespace PeachPDF.CSS
{
    internal abstract class SelectorBase : StylesheetNode, ISelector
    {
        protected SelectorBase(Priority specificity, string text)
        {
            Specificity = specificity;
            Text = text;
        }

        public override void ToCss(TextWriter writer, IStyleFormatter formatter)
        {
            writer.Write(Text);
        }

        public Priority Specificity { get; }
        public string Text { get; }
    }
}