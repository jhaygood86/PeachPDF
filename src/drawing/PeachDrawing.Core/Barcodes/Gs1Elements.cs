using System;
using System.Linq;
using System.Text;

namespace PeachDrawing.Core.Barcodes
{
    /// <summary>
    /// Turns GS1 element strings written with parenthesized application identifiers, such as
    /// <c>(01)09501101530003(10)AB-123</c>, into the text a GS1-128 or GS1 Data Matrix symbol carries.
    /// </summary>
    public static class Gs1Elements
    {
        // Application identifiers whose data has a predefined length (GS1 General Specifications, 5.4.2), by their first digits.
        static int? PredefinedLength(string ai) => ai switch
        {
            "00" => 18,
            "01" or "02" => 14,
            "11" or "12" or "13" or "15" or "16" or "17" => 6,
            "20" => 2,
            _ when ai.Length == 4 && ai[0] == '3' && ai[1] is >= '1' and <= '6' => 6,
            _ when ai.Length == 3 && ai[0] == '4' && ai[1] == '1' && ai[2] is >= '0' and <= '7' => 13,
            _ => null,
        };

        /// <summary>
        /// Converts element strings to the symbol text: <see cref="Code128.Fnc1"/> first, and after every element
        /// string of variable length that is followed by another one.
        /// </summary>
        /// <param name="elements">The element strings, each application identifier in parentheses.</param>
        /// <returns>The text to encode.</returns>
        /// <exception cref="ArgumentException">The text is not a list of parenthesized element strings.</exception>
        public static string ToCode128Text(string elements)
        {
            var parsed = Parse(elements);
            var text = new StringBuilder().Append(Code128.Fnc1);
            for (int i = 0; i < parsed.Count; i++)
            {
                var (ai, data) = parsed[i];
                text.Append(ai).Append(data);
                bool fixedLength = PredefinedLength(ai) == data.Length;
                if (!fixedLength && i < parsed.Count - 1)
                    text.Append(Code128.Fnc1);
            }
            return text.ToString();
        }

        /// <summary>
        /// The human-readable form of element strings: the text as given, with parentheses.
        /// </summary>
        /// <param name="elements">The element strings.</param>
        /// <returns>The text to print with the bars.</returns>
        public static string ToHumanReadable(string elements)
        {
            Parse(elements);
            return elements;
        }

        static System.Collections.Generic.List<(string Ai, string Data)> Parse(string elements)
        {
            ArgumentNullException.ThrowIfNull(elements);
            var result = new System.Collections.Generic.List<(string, string)>();
            int i = 0;
            while (i < elements.Length)
            {
                if (elements[i] != '(')
                    throw new ArgumentException($"Expected '(' at position {i} of the GS1 element strings.", nameof(elements));
                int close = elements.IndexOf(')', i);
                string ai = close < 0 ? "" : elements.Substring(i + 1, close - i - 1);
                if (ai.Length is < 2 or > 4 || ai.Any(ch => ch is < '0' or > '9'))
                    throw new ArgumentException($"Invalid application identifier at position {i} of the GS1 element strings.", nameof(elements));
                int next = elements.IndexOf('(', close);
                string data = elements.Substring(close + 1, (next < 0 ? elements.Length : next) - close - 1);
                if (data.Length == 0 || data.Any(ch => ch is < ' ' or > '~'))
                    throw new ArgumentException($"Invalid data for application identifier ({ai}).", nameof(elements));
                result.Add((ai, data));
                i = next < 0 ? elements.Length : next;
            }
            if (result.Count == 0)
                throw new ArgumentException("The GS1 element strings are empty.", nameof(elements));
            return result;
        }
    }
}
