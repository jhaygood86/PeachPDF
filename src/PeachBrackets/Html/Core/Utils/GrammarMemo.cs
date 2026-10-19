using System;
using System.Collections.Concurrent;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Remembers whether a property grammar accepted a given declaration value, so a document with thousands of boxes
    /// carrying the same <c>font-family</c> (or <c>transition</c>, <c>content</c>, ...) tokenizes and converts that text once
    /// instead of once per box.
    /// </summary>
    /// <remarks>
    /// Whether a grammar accepts a string is a pure function of the string, so the answer can be shared across documents and
    /// threads. It is keyed on the grammar's own delegate too (a <c>static</c> lambda at each generated call site, one object per
    /// site), and bounded: once full, an unseen value is simply evaluated each time.
    /// </remarks>
    internal sealed class GrammarMemo(int maxEntries)
    {
        /// <summary>How many (grammar, value) answers the shared instance keeps.</summary>
        internal const int DefaultMaxEntries = 8192;

        /// <summary>Values longer than this are evaluated each time rather than kept (an inlined data URI, say).</summary>
        internal const int MaxValueLength = 512;

        private static readonly GrammarMemo Shared = new(DefaultMaxEntries);

        private readonly ConcurrentDictionary<(Func<string, bool> Grammar, string Value), bool> _answers = new();

        /// <summary>Whether <paramref name="grammar"/> accepts <paramref name="value"/>, remembered process-wide.</summary>
        internal static bool Accepts(string value, Func<string, bool> grammar) => Shared.Check(value, grammar);

        /// <summary>How many answers are kept.</summary>
        internal int Count => _answers.Count;

        /// <summary>Whether <paramref name="grammar"/> accepts <paramref name="value"/>, remembered by this instance.</summary>
        internal bool Check(string value, Func<string, bool> grammar)
        {
            if (value.Length > MaxValueLength) return grammar(value);

            if (_answers.TryGetValue((grammar, value), out var known)) return known;

            var accepted = grammar(value);
            if (_answers.Count < maxEntries) _answers.TryAdd((grammar, value), accepted);

            return accepted;
        }
    }
}
