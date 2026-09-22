using PeachPDF.CSS;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// The one parser for the <c>[ &lt;counter-name&gt; &lt;integer&gt;? ]+ | none</c> value grammar shared by
    /// <c>counter-reset</c>, <c>counter-set</c> and <c>counter-increment</c>, wherever a caller needs the
    /// declaration's actual name/value pairs rather than a yes/no validity answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is deliberately *not* folded together with <c>CounterResetProperty.ValueGrammar</c>/
    /// <c>CounterIncrementProperty.ValueGrammar</c>: the CSS-OM side only ever validates a declaration, and
    /// never produces the name/value pairs a consumer needs, so there is nothing shareable there. What this
    /// class does replace is the four hand-rolled re-implementations that used to sit in
    /// <see cref="CssCounterEngine"/>, plus a fifth reader in <c>HtmlContainerInt</c>'s footnote-counter
    /// resolution - see PeachPDF's "don't write two independent parsers for the same CSS value grammar
    /// across layers" convention.
    /// </para>
    /// <para>
    /// Each caller keeps its own *semantics*: the per-property default applied when an entry carries no
    /// explicit integer (0 for <c>counter-reset</c>/<c>counter-set</c>, 1 for <c>counter-increment</c>),
    /// and what <see cref="CounterEntry.IsReversed"/> means to it (only <c>counter-reset</c> honours
    /// <c>reversed()</c>, resolving a bare one against its own descendant count). This class answers only
    /// "what does the declaration say", never "what should that do".
    /// </para>
    /// </remarks>
    internal static class CounterListGrammar
    {
        /// <summary>
        /// One <c>&lt;counter-name&gt; &lt;integer&gt;?</c> pair from a counter declaration.
        /// <paramref name="Value"/> is null when the declaration named the counter without an explicit
        /// integer, which is the distinction the per-property default depends on.
        /// </summary>
        internal readonly record struct CounterEntry(string Name, bool IsReversed, int? Value);

        /// <summary>
        /// The name/value pairs <paramref name="declaration"/> states, in declared order. Returns an empty
        /// list for <c>none</c>, for an empty/whitespace value, and for a declaration that states no name.
        /// A later repeat of the same name is returned as its own entry rather than collapsed, so a caller
        /// that wants last-wins gets it by applying the entries in order.
        /// </summary>
        internal static List<CounterEntry> Parse(string? declaration)
        {
            List<CounterEntry> entries = [];

            if (string.IsNullOrWhiteSpace(declaration)) return entries;
            if (declaration.Trim().Equals(Keywords.None, StringComparison.OrdinalIgnoreCase)) return entries;

            var parts = declaration.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            for (var i = 0; i < parts.Length; i++)
            {
                // A stray integer with no name before it (a leading one, or a second in a row) is not a
                // pair this grammar can attach to anything - skip it rather than inventing a counter named
                // after a number.
                if (CommonUtils.IsInteger(parts[i].AsSpan())) continue;

                var (name, isReversed) = GetCounterName(parts[i]);

                if (i + 1 < parts.Length && CommonUtils.IsInteger(parts[i + 1].AsSpan()))
                {
                    entries.Add(new CounterEntry(name, isReversed, int.Parse(parts[i + 1])));
                    i++;
                }
                else
                {
                    entries.Add(new CounterEntry(name, isReversed, null));
                }
            }

            return entries;
        }

        /// <summary>
        /// Whether <paramref name="declaration"/> names <paramref name="counterName"/> at all - the
        /// question "does this declaration establish/increment this counter", independent of any value.
        /// </summary>
        internal static bool Mentions(string? declaration, string counterName)
        {
            foreach (var entry in Parse(declaration))
            {
                if (entry.Name.Equals(counterName, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        /// <summary>
        /// The value <paramref name="declaration"/> states for <paramref name="counterName"/>, falling back
        /// to <paramref name="defaultValue"/> when the counter is named without an explicit integer. Returns
        /// false (leaving <paramref name="value"/> at its default) when the declaration does not name the
        /// counter at all, which is the caller's signal that its own default behaviour applies instead.
        /// The last entry for a name wins, matching how the applying loops overwrite as they go.
        /// </summary>
        internal static bool TryGetValue(string? declaration, string counterName, int defaultValue, out int value)
        {
            var found = false;
            value = defaultValue;

            foreach (var entry in Parse(declaration))
            {
                if (!entry.Name.Equals(counterName, StringComparison.OrdinalIgnoreCase)) continue;

                found = true;
                value = entry.Value ?? defaultValue;
            }

            return found;
        }

        /// <summary>
        /// Splits one counter-name token into its name and whether it was written as
        /// <c>reversed(&lt;name&gt;)</c>. A token that is neither a <c>reversed()</c> function nor an
        /// identifier is returned unchanged as the name, for the caller to reject or use as it sees fit.
        /// </summary>
        internal static (string CounterName, bool IsReversed) GetCounterName(string propValue)
        {
            using var pooledTokens = CssValueParser.GetCssTokensPooled(propValue);
            List<Token> tokens = pooledTokens;

            var reversedToken = tokens.SingleOrNull(x => x.Type == TokenType.Function && x.Data.Is("reversed"));
            var keywordToken = tokens.FirstOrNull(t => t.Type is TokenType.Hash or TokenType.AtKeyword or TokenType.Ident);

            if (reversedToken is { } reversed)
            {
                var args = reversed.ArgumentTokens;
                var counterName = args.Count > 0 ? args[0].Data.ToString() : null;

                return (counterName!, true);
            }

            if (keywordToken is { } keyword)
            {
                return (keyword.Data.ToString(), false);
            }

            // Neither a reversed() function nor an identifier - hand the raw token back unchanged.
            return (propValue, false);
        }
    }
}
