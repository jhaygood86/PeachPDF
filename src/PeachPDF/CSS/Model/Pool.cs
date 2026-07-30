using System;
using System.Collections.Generic;
using System.Text;

namespace PeachPDF.CSS
{
    /// <summary>
    /// Scratch-object pools for the CSS tokenizer/parser. Per-thread rather than a single shared,
    /// lock-guarded pool: a CSS parse never spans more than one thread at a time (production parsing
    /// always reaches <see cref="TextSource"/> through its synchronous string constructor - the only
    /// path with a genuine mid-parse <c>await</c>, <see cref="TextSource.PrefetchAllAsync"/> on a
    /// <see cref="System.IO.Stream"/> source, is reachable only from test code), and a
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

        public static StringBuilder NewStringBuilder()
        {
            var stack = _builder ??= new();
            return stack.Count == 0 ? new StringBuilder(1024) : stack.Pop().Clear();
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
}