using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PeachPDF.CSS
{
    internal sealed class TokenValue : StylesheetNode, IReadOnlyList<Token>
    {
        private readonly List<Token> _tokens;
        public static TokenValue Initial = FromString(Keywords.Initial);
        public static TokenValue Empty = new(Enumerable.Empty<Token>());

        public override void ToCss(TextWriter writer, IStyleFormatter formatter)
        {
            writer.Write(_tokens.ToText());
        }

        private TokenValue(Token token)
        {
            _tokens = new List<Token> { token };
        }

        public TokenValue(IEnumerable<Token> tokens)
        {
            _tokens = new List<Token>(tokens);
        }

        /// <summary>
        /// The overload nearly every caller actually binds to, and the reason it exists is capacity.
        /// </summary>
        /// <remarks>
        /// <c>new List&lt;T&gt;(IEnumerable&lt;T&gt;)</c> presizes only when the source is an
        /// <see cref="ICollection{T}"/>. <see cref="IReadOnlyList{T}"/> is not one - and neither is
        /// <see cref="TokenValue"/> itself - so the general overload enumerated and grew the backing
        /// array 4, 8, 16, 32..., reallocating and copying at every step, for a sequence whose length
        /// was known before the first element was read. Converters build one of these per declared
        /// value, so it was the single largest frame in a sampled CPU profile of the engine.
        /// Indexing rather than enumerating also skips the interface enumerator the general overload
        /// would allocate.
        /// </remarks>
        public TokenValue(IReadOnlyList<Token> tokens)
        {
            _tokens = new List<Token>(tokens.Count);
            for (var i = 0; i < tokens.Count; i++)
            {
                _tokens.Add(tokens[i]);
            }
        }

        public static TokenValue FromString(string text)
        {
            var token = new Token(TokenType.Ident, text.AsMemory(), TextPosition.Empty);
            return new TokenValue(token);
        }

        public static TokenValue FromNumber(int number)
        {
            var token = Token.NewNumber(number.ToString(CultureInfo.InvariantCulture).AsMemory(), TextPosition.Empty);
            return new TokenValue(token);
        }

        public Token this[int index] => _tokens[index];

        public int Count => _tokens.Count;

        public string Text => this.ToCss();

        // A concrete (non-interface-typed) GetEnumerator lets `foreach` duck-type directly to
        // List<Token>.Enumerator (a struct) instead of going through the interface, which would box the
        // enumerator once per foreach - the standard List<T>-style pattern.
        public List<Token>.Enumerator GetEnumerator()
        {
            return _tokens.GetEnumerator();
        }

        IEnumerator<Token> IEnumerable<Token>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}