namespace PeachPDF.Tests.CSS
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using PeachPDF.CSS;
    using Xunit;

    /// <summary>
    /// Building a <see cref="TokenValue"/> from a list whose length is already known allocates one
    /// backing array, not a succession of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>new List&lt;T&gt;(IEnumerable&lt;T&gt;)</c> presizes only when the source is an
    /// <see cref="ICollection{T}"/>. <see cref="IReadOnlyList{T}"/> is not one, and neither is
    /// <c>TokenValue</c> itself, so the general overload enumerated the source and grew its array
    /// 4, 8, 16, 32... — reallocating and copying at every step, for a sequence whose length was
    /// known before the first element was read.
    /// </para>
    /// <para>
    /// Converters build one of these per declared value, which is why it showed up as the largest
    /// single frame in a sampled CPU profile of the engine (7.29% of non-waiting work, down to
    /// 1.67% with the presizing overload). The cost is mostly the enumeration and the copying, not
    /// the bytes, so this asserts on allocation only because allocation is the part a test can pin
    /// exactly and identically on every machine.
    /// </para>
    /// </remarks>
    public class TokenValueCapacityTests
    {
        [Fact]
        public void BuildingFromAKnownLengthList_GrowsItsArrayOnlyOnce()
        {
            var tokens = new List<Token>();
            for (var i = 0; i < 64; i++)
                tokens.Add(new Token(TokenType.Ident, $"t{i}".AsMemory(), TextPosition.Empty));

            // Deliberately NOT the List<Token> itself: List<T> is an ICollection<T>, so the general
            // overload would presize from it and this test would pass against unfixed code. The
            // wrapper exposes only IReadOnlyList<Token>, which is the shape every converter actually
            // hands over.
            IReadOnlyList<Token> source = new ReadOnlyListOnly(tokens);

            // Warm: JIT both the constructor and the token list before anything is counted.
            for (var i = 0; i < 3; i++) _ = new TokenValue(source);

            // Per THREAD, not process-wide: the suite runs collections in parallel, so
            // GC.GetTotalAllocatedBytes would count every other test's work too. This body is
            // synchronous and never awaits, so it stays on one thread throughout.
            const int builds = 200;
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < builds; i++) _ = new TokenValue(source);
            var allocated = (GC.GetAllocatedBytesForCurrentThread() - before) / builds;

            // One Token[64] is the floor. Growing 4->8->16->32->64 instead allocates all five arrays
            // plus a boxed enumerator, which is a little over twice that. The bound sits between the
            // two rather than at either, so it states "grew once" without pinning the exact byte
            // count of a Token or of List<T>'s header.
            var oneArray = 64 * System.Runtime.CompilerServices.Unsafe.SizeOf<Token>();
            Assert.True(allocated < oneArray * 3 / 2,
                $"building a 64-token TokenValue allocated {allocated:N0} bytes, more than 1.5x the "
                + $"{oneArray:N0} bytes one Token[64] needs. The backing array is being grown "
                + "repeatedly, which means the constructor did not presize from the known count.");
        }

        [Fact]
        public void BuildingFromAKnownLengthList_KeepsEveryTokenInOrder()
        {
            var tokens = new List<Token>
            {
                new(TokenType.Ident, "solid".AsMemory(), TextPosition.Empty),
                new(TokenType.Whitespace, " ".AsMemory(), TextPosition.Empty),
                new(TokenType.Ident, "red".AsMemory(), TextPosition.Empty),
            };

            var value = new TokenValue(new ReadOnlyListOnly(tokens));

            Assert.Equal(tokens.Count, value.Count);
            for (var i = 0; i < tokens.Count; i++)
            {
                Assert.Equal(tokens[i].Type, value[i].Type);
                Assert.Equal(tokens[i].Data, value[i].Data);
            }
        }

        /// <summary>Exposes a list as <see cref="IReadOnlyList{T}"/> and nothing more, so nothing
        /// downstream can discover an <see cref="ICollection{T}"/> and presize by accident.</summary>
        private sealed class ReadOnlyListOnly(IReadOnlyList<Token> inner) : IReadOnlyList<Token>
        {
            public Token this[int index] => inner[index];
            public int Count => inner.Count;
            public IEnumerator<Token> GetEnumerator() => inner.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
