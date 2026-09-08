#nullable disable

using System;

namespace PeachPDF.CSS
{
    /// <summary>
    /// A character cursor over an already-fully-decoded <see cref="ReadOnlyMemory{T}"/> of
    /// <see langword="char"/>. Every caller already has (or can get) fully decoded text before reaching
    /// this type: a literal <see langword="string"/> needs no decoding at all, and a
    /// <see cref="System.IO.Stream"/> source is decoded up front by <see cref="CssStreamLoader"/> - this
    /// type itself never touches a <see cref="System.IO.Stream"/>, does no BOM sniffing, and has no
    /// encoding-correction/replay logic, since that only ever applied to the (now separate)
    /// stream-decoding step.
    /// <para>
    /// <see cref="ReadOnlyMemory{T}"/>, not <see cref="ReadOnlySpan{T}"/>: unlike <c>Span&lt;T&gt;</c>,
    /// <c>Memory&lt;T&gt;</c> is not a ref struct, so it can be stored as a field here and retained for as
    /// long as callers need it - <c>StylesheetComposer.CreateView</c> hands a reference to this same
    /// <see cref="TextSource"/> to every <see cref="StylesheetText"/> it creates, for a lazy <c>.Text</c>
    /// read that can happen well after the parse that produced it returns.
    /// </para>
    /// </summary>
    internal sealed class TextSource
    {
        private readonly ReadOnlyMemory<char> _data;

        public TextSource(string source)
        {
            _data = source.AsMemory();
            Index = 0;
        }

        public TextSource(ReadOnlyMemory<char> source)
        {
            _data = source;
            Index = 0;
        }

        // ToString() on a ReadOnlyMemory<char> that spans an entire original string returns that exact
        // string instance with no copy - true for both constructors above (AsMemory() over the whole
        // string, or the whole decoded buffer CssStreamLoader already materialized as a string) - so this
        // never allocates.
        public string Text => _data.ToString();

        public char this[int index] => _data.Span[index];
        public int Index { get; set; }
        public int Length => _data.Length;

        public char ReadCharacter()
        {
            // Index must advance on every call, even past the end. LexerBase.NormalizeForward relies on
            // this: after reading a trailing '\r' with nothing after it, it peeks one more character and
            // backs Index up by one when the peek isn't '\n'; if Index hadn't advanced on the EOF peek,
            // that decrement would leave Index one short of the true end.
            var sourceIndex = Index++;
            return sourceIndex < _data.Length ? _data.Span[sourceIndex] : Symbols.EndOfFile;
        }

        public string ReadCharacters(int characters)
        {
            var start = Index;
            // The cursor always advances by the full requested count (even past the end of the source),
            // while the returned string is clamped to what's actually available.
            Index += characters;
            var available = Math.Max(0, Math.Min(characters, _data.Length - start));
            return available > 0 ? _data.Span.Slice(start, available).ToString() : string.Empty;
        }

        /// <summary>The requested slice of the shared source memory - the allocation-free backing for
        /// <see cref="Token"/>'s <c>Data</c>/<c>DataSpan</c> in the common case. Callers must only use this
        /// when nothing between <paramref name="start"/> and <paramref name="start"/> + <paramref
        /// name="length"/> could have diverged from a literal copy of the source (no escape sequence, no
        /// normalized <c>\r</c>); otherwise the accumulated buffer content is what must be used
        /// instead.</summary>
        public ReadOnlyMemory<char> Slice(int start, int length) => _data.Slice(start, length);
    }
}
