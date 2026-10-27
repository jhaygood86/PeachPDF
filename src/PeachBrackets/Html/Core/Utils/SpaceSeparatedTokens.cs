using System;

namespace PeachBrackets.Html.Utils
{
    /// <summary>
    /// Walks the non-empty tokens of a value separated by U+0020 only, as spans of the source, so a
    /// <c>class</c> attribute can be read without the array and per-token strings of <c>Split(' ')</c>.
    /// Tabs and newlines are deliberately not separators, matching what the matcher always did.
    /// </summary>
    internal readonly ref struct SpaceSeparatedTokens
    {
        private readonly ReadOnlySpan<char> _value;

        public SpaceSeparatedTokens(ReadOnlySpan<char> value) => _value = value;

        public Enumerator GetEnumerator() => new(_value);

        public ref struct Enumerator
        {
            private ReadOnlySpan<char> _rest;

            internal Enumerator(ReadOnlySpan<char> value)
            {
                _rest = value;
                Current = default;
            }

            public ReadOnlySpan<char> Current { get; private set; }

            public bool MoveNext()
            {
                while (!_rest.IsEmpty)
                {
                    var end = _rest.IndexOf(' ');
                    ReadOnlySpan<char> token;
                    if (end < 0)
                    {
                        token = _rest;
                        _rest = default;
                    }
                    else
                    {
                        token = _rest[..end];
                        _rest = _rest[(end + 1)..];
                    }

                    if (token.IsEmpty) continue;

                    Current = token;
                    return true;
                }

                return false;
            }
        }
    }
}
