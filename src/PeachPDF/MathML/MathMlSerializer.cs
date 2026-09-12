#region PeachPDF - A .NET library for rendering HTML to PDF
//
// Re-serializes a parsed <math> subtree back to canonical MathML XML text - the payload attached as
// a PDF 2.0 Associated File on a tagged Formula structure element (see StructureTagBuilder). Reads
// the IMathSourceNode tree directly (element names/attributes/text), not the built MathNode
// presentation tree - the AF payload should be the author's own markup, not this engine's internal
// (already-desugared/simplified) interpretation of it.
//
#endregion

using System.Linq;
using System.Text;

namespace PeachPDF.MathML
{
    internal static class MathMlSerializer
    {
        public static string Serialize(IMathSourceNode root) => Serialize(root, new StringBuilder()).ToString();

        static StringBuilder Serialize(IMathSourceNode node, StringBuilder sb)
        {
            sb.Append('<').Append(node.Name);

            foreach (var attribute in node.Attributes)
                sb.Append(' ').Append(attribute.Key).Append("=\"").Append(EscapeAttribute(attribute.Value)).Append('"');

            var children = node.Children.ToArray();
            if (children.Length == 0)
            {
                var text = node.GetTextContent();
                if (text.Length == 0)
                {
                    sb.Append("/>");
                    return sb;
                }

                sb.Append('>').Append(EscapeText(text)).Append("</").Append(node.Name).Append('>');
                return sb;
            }

            sb.Append('>');
            foreach (var child in children)
                Serialize(child, sb);
            sb.Append("</").Append(node.Name).Append('>');
            return sb;
        }

        static string EscapeAttribute(string value) => value
            .Replace("&", "&amp;")
            .Replace("\"", "&quot;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");

        static string EscapeText(string value) => value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }
}
