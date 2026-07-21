#nullable disable

using System.Collections.Generic;
using System.Globalization;

namespace PeachPDF.CSS
{
    internal sealed class RangeToken : Token
    {
        private string[] GetRange()
        {
            var index = int.Parse(Start, NumberStyles.HexNumber);

            if (index > Symbols.MaximumCodepoint) return null;

            if (End == null) return new[] { index.ConvertFromUtf32() };

            var list = new List<string>();
            var f = int.Parse(End, NumberStyles.HexNumber);

            if (f > Symbols.MaximumCodepoint) f = Symbols.MaximumCodepoint;

            while (index <= f)
            {
                list.Add(index.ConvertFromUtf32());
                index++;
            }

            return list.ToArray();
        }

        public RangeToken(string range, TextPosition position)
            : base(TokenType.Range, range, position)
        {
            Start = range.Replace(Symbols.QuestionMark, '0');
            End = range.Replace(Symbols.QuestionMark, 'F');
        }

        public RangeToken(string start, string end, TextPosition position)
            : base(TokenType.Range, string.Concat(start, "-", end), position)
        {
            Start = start;
            End = end;
        }

        //public bool IsEmpty => (SelectedRange == null) || (SelectedRange.Length == 0);
        public string Start { get; }
        public string End { get; }

        // Computed lazily on first access: materializing the full codepoint list is only needed by a
        // caller that actually enumerates it, and a wide range (e.g. U+0-10FFFF) would otherwise
        // allocate over a million strings the instant the token is created. Consumers that only read
        // the Start/End hex bounds (e.g. @font-face unicode-range parsing) never trigger it.
        private string[] _selectedRange;
        public string[] SelectedRange => _selectedRange ??= GetRange();
    }
}