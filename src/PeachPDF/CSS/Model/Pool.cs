using System;
using System.Collections.Generic;
using System.Text;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Scratch-object pools for the CSS tokenizer/parser. Per-thread rather than a single shared,
    /// lock-guarded pool: a CSS parse never spans more than one thread at a time (the only
    /// <c>await</c>s in the parsing path - <see cref="TextSource.PrefetchAllAsync"/> - complete
    /// before tokenizing starts, so a rented object's Rent and Return always happen on whatever one
    /// thread is running the parse), and a <see cref="StringBuilder"/>/<see cref="SelectorConstructor"/>/
    /// <see cref="ValueBuilder"/> parked here is pure scratch space with no reason to be handed to a
    /// different thread anyway. A `dotnet-trace` CPU profile of the full showcase corpus found the
    /// single shared lock this replaced responsible for over a third of the run's total CPU time
    /// (<c>Monitor.Enter_Slowpath</c>, almost entirely under <see cref="NewStringBuilder"/>) - by far
    /// the largest cost in the whole pipeline, ahead of layout, cascade, and PDF writing combined.
    /// </summary>
    internal static class Pool
    {
        [ThreadStatic] private static Stack<StringBuilder>? _builder;
        [ThreadStatic] private static Stack<SelectorConstructor>? _selector;
        [ThreadStatic] private static Stack<ValueBuilder>? _value;

        public static StringBuilder NewStringBuilder()
        {
            var stack = _builder ??= new Stack<StringBuilder>();
            return stack.Count == 0 ? new StringBuilder(1024) : stack.Pop().Clear();
        }

        public static SelectorConstructor NewSelectorConstructor(AttributeSelectorFactory attributeSelector,
            PseudoClassSelectorFactory pseudoClassSelector, PseudoElementSelectorFactory pseudoElementSelector)
        {
            var stack = _selector ??= new Stack<SelectorConstructor>();
            return stack.Count == 0
                ? new SelectorConstructor(attributeSelector, pseudoClassSelector, pseudoElementSelector)
                : stack.Pop().Reset(attributeSelector, pseudoClassSelector, pseudoElementSelector);
        }

        public static ValueBuilder NewValueBuilder()
        {
            var stack = _value ??= new Stack<ValueBuilder>();
            return stack.Count == 0
                ? new ValueBuilder()
                : stack.Pop().Reset();
        }

        public static string ToPool(this StringBuilder sb)
        {
            var result = sb.ToString();
            (_builder ??= new Stack<StringBuilder>()).Push(sb);
            return result;
        }

        public static ISelector ToPool(this SelectorConstructor ctor)
        {
            var result = ctor.GetResult();
            (_selector ??= new Stack<SelectorConstructor>()).Push(ctor);
            return result;
        }

        public static TokenValue ToPool(this ValueBuilder vb)
        {
            var result = vb.GetResult();
            (_value ??= new Stack<ValueBuilder>()).Push(vb);
            return result;
        }
    }
}