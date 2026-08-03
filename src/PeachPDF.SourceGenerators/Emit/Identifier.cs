using System.Text;

namespace PeachPDF.SourceGenerators.Emit
{
    internal static class Identifier
    {
        /// <summary>"border-bottom-width" -> "BorderBottomWidth"; a leading vendor-prefix dash is just skipped.</summary>
        public static string PascalCase(string cssPropertyName)
        {
            var sb = new StringBuilder(cssPropertyName.Length);
            var capitalizeNext = true;

            foreach (var c in cssPropertyName)
            {
                if (c is '-' or '_')
                {
                    capitalizeNext = true;
                    continue;
                }

                sb.Append(capitalizeNext ? char.ToUpperInvariant(c) : c);
                capitalizeNext = false;
            }

            return sb.ToString();
        }

        public static string StringLiteral(string value) =>
            "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
