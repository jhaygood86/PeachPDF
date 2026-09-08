namespace PeachPDF.CSS
{
    /// <summary>
    /// A one-string-per-possible-<see cref="char"/>-value lookup table, so producing a single-character
    /// <see cref="Token"/> (whitespace, a delimiter) never allocates a fresh 1-character string via
    /// <c>character.ToString()</c>. Bounded by the closed 16-bit <c>char</c> domain (unlike
    /// <see cref="System.String.Intern"/>'s unbounded, process-lifetime pool), built once at type-init
    /// time - the CLR guarantees that is thread-safe with no lock needed - then read-only forever.
    /// </summary>
    internal static class SingleCharStrings
    {
        private static readonly string[] Table = BuildTable();

        private static string[] BuildTable()
        {
            var table = new string[char.MaxValue + 1];

            for (var i = 0; i < table.Length; i++)
            {
                table[i] = ((char)i).ToString();
            }

            return table;
        }

        public static string Of(char c) => Table[c];
    }
}
