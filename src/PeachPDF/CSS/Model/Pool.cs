using System;
using System.Collections.Generic;
using System.Text;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Scratch-object pools for the CSS tokenizer/parser. Per-thread rather than a single shared,
    /// lock-guarded pool: a CSS parse never spans more than one thread at a time - <see cref="TextSource"/>
    /// is an already-decoded, synchronous cursor with no mid-parse <c>await</c> of its own regardless of
    /// whether the original input was a <see langword="string"/> or a <see cref="System.IO.Stream"/>
    /// (stream decoding happens entirely up front, in <see cref="CssStreamLoader"/>, before a
    /// <see cref="TextSource"/> is even constructed) - and a
    /// <see cref="StringBuilder"/>/<see cref="SelectorConstructor"/>/<see cref="ValueBuilder"/> parked
    /// here is pure scratch space with no reason to be handed to a different thread anyway - even a
    /// rent/return pair that did straddle an await could, at worst, donate a spare instance to another
    /// thread's pool rather than corrupt shared state. A `dotnet-trace` CPU profile of the full
    /// showcase corpus found the single shared lock this replaced responsible for over a third of the
    /// run's total CPU time (<c>Monitor.Enter_Slowpath</c>, almost entirely under
    /// <see cref="NewStringBuilder"/>) - by far the largest cost in the whole pipeline, ahead of
    /// layout, cascade, and PDF writing combined.
    /// </summary>
    internal static class Pool
    {
        [ThreadStatic] private static Stack<StringBuilder>? _builder;
        [ThreadStatic] private static Stack<SelectorConstructor>? _selector;
        [ThreadStatic] private static Stack<ValueBuilder>? _value;
        [ThreadStatic] private static Stack<List<Token>>? _tokenList;

        public static StringBuilder NewStringBuilder()
        {
            var stack = _builder ??= new();
            return stack.Count == 0 ? new StringBuilder(1024) : stack.Pop().Clear();
        }

        // Only for call sites individually audited as never retaining the list itself (or a sub-list/
        // enumerable slice of it) past the immediate call - see CssValueParser.GetCssTokensPooled's own
        // doc comment. A Token is a value type, so a Token copied out of a pooled list (e.g. via pattern
        // matching or indexing) stays valid after the list is returned; only the List<Token> instance
        // itself - its backing array - must not be read again once returned.
        public static List<Token> NewTokenList()
        {
            var stack = _tokenList ??= new();
            if (stack.Count == 0) return new List<Token>();
            var list = stack.Pop();
            list.Clear();
            return list;
        }

        public static void ReturnTokenList(List<Token> list)
        {
            (_tokenList ??= new()).Push(list);
        }

        [ThreadStatic] private static Stack<Dictionary<string, IProperty>>? _propertyDictionary;

        // Scratch lookup StylesheetComposer.FillDeclarations uses to decide !important precedence while
        // walking one declaration block's properties - never read after the block finishes, so it's both
        // lazily created (a block with only nested rules/comments never needs one) and pool-eligible.
        public static Dictionary<string, IProperty> NewPropertyDictionary()
        {
            var stack = _propertyDictionary ??= new();
            if (stack.Count == 0) return new Dictionary<string, IProperty>(StringComparer.OrdinalIgnoreCase);
            var dict = stack.Pop();
            dict.Clear();
            return dict;
        }

        public static void ReturnPropertyDictionary(Dictionary<string, IProperty> dict)
        {
            (_propertyDictionary ??= new()).Push(dict);
        }

        public static SelectorConstructor NewSelectorConstructor(AttributeSelectorFactory attributeSelector,
            PseudoClassSelectorFactory pseudoClassSelector, PseudoElementSelectorFactory pseudoElementSelector)
        {
            var stack = _selector ??= new();
            return stack.Count == 0
                ? new SelectorConstructor(attributeSelector, pseudoClassSelector, pseudoElementSelector)
                : stack.Pop().Reset(attributeSelector, pseudoClassSelector, pseudoElementSelector);
        }

        public static ValueBuilder NewValueBuilder()
        {
            var stack = _value ??= new();
            return stack.Count == 0
                ? new ValueBuilder()
                : stack.Pop().Reset();
        }

        public static string ToPool(this StringBuilder sb)
        {
            var result = sb.ToString();
            (_builder ??= new()).Push(sb);
            return result;
        }

        public static ISelector ToPool(this SelectorConstructor ctor)
        {
            var result = ctor.GetResult();
            (_selector ??= new()).Push(ctor);
            return result;
        }

        public static TokenValue ToPool(this ValueBuilder vb)
        {
            var result = vb.GetResult();
            (_value ??= new()).Push(vb);
            return result;
        }
    }

    /// <summary>
    /// RAII handle for a pooled <see cref="List{Token}"/>, returned by
    /// <see cref="PeachPDF.Html.Core.Parse.CssValueParser.GetCssTokensPooled"/>. Use as
    /// <c>using var tokens = CssValueParser.GetCssTokensPooled(value); // tokens: List&lt;Token&gt;</c> -
    /// the implicit conversion hands back the real list for normal use inside the <c>using</c> block, and
    /// <see cref="Dispose"/> returns it to the pool. A <see langword="ref struct"/> so it can't
    /// accidentally be boxed, stored in a field, or captured by a closure - the one thing that must never
    /// outlive the <c>using</c> block is the <see cref="List{Token}"/> reference itself (individual
    /// <see cref="Token"/> values copied out of it are fine; see <see cref="Pool.NewTokenList"/>).
    /// </summary>
    internal readonly ref struct PooledTokenList
    {
        private readonly List<Token> _list;

        internal PooledTokenList(List<Token> list) => _list = list;

        public static implicit operator List<Token>(PooledTokenList pooled) => pooled._list;

        public void Dispose() => Pool.ReturnTokenList(_list);
    }
}