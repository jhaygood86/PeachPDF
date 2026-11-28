namespace PeachBrackets.Html.Utils
{
    /// <summary>The HTML standard's rules for reading a number out of an attribute (infrastructure section 2.3.4).</summary>
    internal static class HtmlIntegerParser
    {
        /// <summary>
        /// The rules for parsing non-negative integers: skip ASCII white space, take an optional plus sign (a minus sign is an error unless what follows is zero), read the digits
        /// that follow and ignore whatever comes after them, so <c>"  100"</c>, <c>"100.999"</c>, <c>"100em"</c> and <c>"+100"</c> are 100 and <c>"0x100"</c> is 0. A value that
        /// does not fit an unsigned 32-bit integer is an error.
        /// </summary>
        /// <param name="text">the attribute's value, or null for none</param>
        /// <param name="value">the number</param>
        /// <returns>false when the text has no number to read</returns>
        public static bool TryParseNonNegative(string? text, out uint value)
        {
            value = 0;
            if (text is null)
                return false;

            var i = 0;
            while (i < text.Length && text[i] is '\t' or '\n' or '\f' or '\r' or ' ')
                i++;

            if (i >= text.Length)
                return false;

            var negative = false;
            if (text[i] == '-')
            {
                negative = true;
                i++;
            }
            else if (text[i] == '+')
            {
                i++;
            }

            if (i >= text.Length || text[i] is < '0' or > '9')
                return false;

            ulong number = 0;
            while (i < text.Length && text[i] is >= '0' and <= '9')
            {
                number = number * 10 + (uint)(text[i] - '0');
                if (number > uint.MaxValue)
                    return false;

                i++;
            }

            if (negative && number != 0)
                return false;

            value = (uint)number;
            return true;
        }
    }
}
