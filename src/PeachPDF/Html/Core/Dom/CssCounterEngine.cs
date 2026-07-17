using PeachPDF.CSS;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PeachPDF.Html.Core.Dom
{
    internal static class CssCounterEngine
    {
        public static CssCounter? GetCounter(CssBox box, string counterName)
        {
            InheritAndApplyCounter(box, counterName);
            return box.Counters.GetValueOrDefault(counterName);
        }

        private static void ApplyCounterResets(CssBox box)
        {
            var counterReset = GetEffectiveCounterReset(box);

            if (counterReset is CssConstants.None) return;

            var valueParts = counterReset.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            (string Name, bool IsReversed)? previousCounter = null;

            for (int i = 0; i < valueParts.Length; i++)
            {
                var valuePart = valueParts[i];

                // Check if this is a number following a counter name
                if (previousCounter is not null && CommonUtils.IsInteger(valuePart.AsSpan()))
                {
                    var counterValue = int.Parse(valuePart);
                    var parentScopeCounter = box.Counters.GetValueOrDefault(previousCounter.Value.Name);
                    box.Counters[previousCounter.Value.Name] = new CssCounter(previousCounter.Value.Name, counterValue, previousCounter.Value.IsReversed, true, parentScopeCounter);
                    previousCounter = null; // Reset so we don't process this again
                }
                else
                {
                    // This is a counter name
                    var (counterName, isReversed) = GetCounterName(valuePart);

                    // Check if the next value is a number
                    if (i + 1 < valueParts.Length && CommonUtils.IsInteger(valueParts[i + 1].AsSpan()))
                    {
                        // Let the next iteration handle the number
                        previousCounter = (counterName, isReversed);
                    }
                    else
                    {
                        // No number following - reversed(name) with no explicit value means the
                        // initial value is one more than the count of descendants in this scope that
                        // will increment the counter: each of the N contributors applies its own -1
                        // AFTER inheriting the reset value, so starting at N+1 makes the first
                        // contributor land on N and the last on 1 - see MDN's counter-reset docs for
                        // reversed(<counter-name>). A non-reversed bare name just uses the CSS-default
                        // initial value of 0.
                        var initialValue = isReversed ? CountScopeIncrements(box, counterName) + 1 : 0;
                        var parentScopeCounter = box.Counters.GetValueOrDefault(counterName);
                        box.Counters[counterName] = new CssCounter(counterName, initialValue, isReversed, true, parentScopeCounter);
                        previousCounter = null;
                    }
                }
            }
        }

        /// <summary>
        /// <c>counter-set</c> - sets a counter already visible in the current scope to a specific
        /// value without creating a new scope (unlike <see cref="ApplyCounterResets"/>). Mirrors its
        /// parsing structure. Applied between resets and increments in <see cref="InheritAndApplyCounter"/>,
        /// matching the CSS-defined counter-reset -> counter-increment -> counter-set processing order
        /// (counter-set applies last on a given element, so it wins over that same element's own
        /// counter-increment - e.g. an &lt;li value="100"&gt;'s counter-set presentational hint must
        /// override the implicit list-item counter-increment that would otherwise also apply to it,
        /// so the item displays exactly 100, not 101).
        /// </summary>
        private static void ApplyCounterSets(CssBox box)
        {
            var counterSet = GetEffectiveCounterSet(box);

            if (counterSet is CssConstants.None) return;

            var valueParts = counterSet.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            string? previousCounterName = null;

            for (int i = 0; i < valueParts.Length; i++)
            {
                var valuePart = valueParts[i];

                if (previousCounterName is not null && CommonUtils.IsInteger(valuePart.AsSpan()))
                {
                    var counterValue = int.Parse(valuePart);
                    SetCounterValue(box, previousCounterName, counterValue);
                    previousCounterName = null;
                }
                else
                {
                    var (counterName, _) = GetCounterName(valuePart);

                    if (i + 1 < valueParts.Length && CommonUtils.IsInteger(valueParts[i + 1].AsSpan()))
                    {
                        previousCounterName = counterName;
                    }
                    else
                    {
                        // No number following - default counter-set value is 0.
                        SetCounterValue(box, counterName, 0);
                        previousCounterName = null;
                    }
                }
            }
        }

        private static void SetCounterValue(CssBox box, string counterName, int value)
        {
            var existing = box.Counters.GetValueOrDefault(counterName);
            box.Counters[counterName] = existing is not null
                ? existing with { Value = value }
                : new CssCounter(counterName, value, false, false, null);
        }

        private static void ApplyCounterIncrements(CssBox box)
        {
            Dictionary<string, int> incrementValues = [];

            if (box.CounterIncrement is not CssConstants.None)
            {
                var valueParts = box.CounterIncrement.Split(' ');

                string? previousCounterName = null;

                foreach (var valuePart in valueParts)
                {
                    var counterName = valuePart;
                    var counterValue = 1;

                    if (previousCounterName is not null && CommonUtils.IsInteger(valuePart.AsSpan()))
                    {
                        counterName = previousCounterName;
                        counterValue = int.Parse(valuePart);
                    }

                    previousCounterName = counterName;

                    incrementValues[counterName] = counterValue;
                }
            }

            // Per the CSS Lists spec, any box whose Display resolves to list-item automatically
            // increments the implicit "list-item" counter - equivalent to a UA-stylesheet rule
            // "display: list-item { counter-increment: list-item }" - regardless of what HTML tag
            // produced that Display value. This can't be expressed as a literal UA-stylesheet
            // selector (selectors can't match on a computed Display value), so it's applied here
            // directly. An author's own explicit counter-increment targeting list-item on the same
            // element still wins (already captured above, so it's not overwritten below).
            if (box.Display == CssConstants.ListItem && !incrementValues.ContainsKey(CssConstants.ListItem))
            {
                var currentListItemCounter = box.Counters.GetValueOrDefault(CssConstants.ListItem);
                incrementValues[CssConstants.ListItem] = currentListItemCounter is { IsReversed: true } ? -1 : 1;
            }

            foreach (var incrementEntry in incrementValues)
            {
                if (box.Counters.TryGetValue(incrementEntry.Key, out var counterValue))
                {
                    var targetValue = counterValue.Value + incrementEntry.Value;

                    var incrementedCounter = counterValue with
                    {
                        Value = targetValue
                    };

                    box.Counters[incrementEntry.Key] = incrementedCounter;
                }
                else
                {
                    var newCounter = new CssCounter(incrementEntry.Key, 1, false, true, null);
                    box.Counters[incrementEntry.Key] = newCounter;
                }
            }
        }

        /// <summary>
        /// Whether <paramref name="box"/> would contribute a <c>counter-increment</c> for
        /// <paramref name="counterName"/> - either explicitly declared, or (for "list-item" only)
        /// implicitly via a computed <c>Display: list-item</c>, mirroring the same default
        /// <see cref="ApplyCounterIncrements"/> applies. Shared so <see cref="CountScopeIncrements"/>
        /// (used to resolve a bare <c>reversed(name)</c> counter-reset's initial value) counts exactly
        /// the same set of contributors that will actually increment the counter later.
        /// </summary>
        private static bool WouldIncrementCounter(CssBox box, string counterName)
        {
            if (CounterNameAppears(box.CounterIncrement, counterName)) return true;
            return counterName == CssConstants.ListItem && box.Display == CssConstants.ListItem;
        }

        private static bool CounterNameAppears(string counterPropertyValue, string counterName)
        {
            if (counterPropertyValue is CssConstants.None) return false;

            foreach (var valuePart in counterPropertyValue.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (CommonUtils.IsInteger(valuePart.AsSpan())) continue;
                var (name, _) = GetCounterName(valuePart);
                if (name.Equals(counterName, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        /// <summary>
        /// Counts descendants of <paramref name="scopeBox"/> that would increment
        /// <paramref name="counterName"/> within this counter's scope - used to resolve a bare
        /// <c>counter-reset: reversed(name)</c> (no explicit value)'s initial value, per MDN's
        /// documented behavior: the counter starts at the number of elements that will increment it,
        /// so the last one lands on 1 (assuming a +-1 increment magnitude). Does not descend into a
        /// nested descendant that establishes its own new scope for the same counter name (its own
        /// <c>counter-reset</c> mentions it) - that forms an independent inner counter.
        /// </summary>
        private static int CountScopeIncrements(CssBox scopeBox, string counterName)
        {
            var count = 0;
            CountScopeIncrementsRecursive(scopeBox, counterName, ref count);
            return count;
        }

        private static void CountScopeIncrementsRecursive(CssBox box, string counterName, ref int count)
        {
            foreach (var child in box.Boxes)
            {
                if (WouldIncrementCounter(child, counterName))
                {
                    count++;
                }

                var childCreatesNewScope = CounterNameAppears(GetEffectiveCounterReset(child), counterName);

                if (!childCreatesNewScope)
                {
                    CountScopeIncrementsRecursive(child, counterName, ref count);
                }
            }
        }

        /// <summary>
        /// Resolves <paramref name="box"/>'s effective <c>counter-reset</c> value, folding in the
        /// WHATWG HTML presentational hint for <c>&lt;ol start&gt;</c>/<c>&lt;ol reversed&gt;</c> (see
        /// https://html.spec.whatwg.org/multipage/rendering.html#the-ol-element-2) when the cascaded
        /// value is still exactly the bare UA default ("list-item", set in CssDefaults.cs for
        /// ol/ul/menu/dir) - i.e. no author CSS has actually overridden counter-reset on this element,
        /// matching real presentational-hint precedence (lowest priority; any literal author/UA CSS
        /// declaration - even one only targeting a different, unrelated counter name - wins outright).
        /// </summary>
        private static string GetEffectiveCounterReset(CssBox box)
        {
            var counterReset = box.CounterReset;

            if (box.HtmlTag is not null &&
                box.HtmlTag.Name.Equals("ol", StringComparison.OrdinalIgnoreCase) &&
                counterReset.Trim().Equals(CssConstants.ListItem, StringComparison.OrdinalIgnoreCase))
            {
                var hasStart = int.TryParse(box.GetAttribute("start"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var startValue);
                // "reversed" is an HTML boolean attribute - its mere presence means true, regardless
                // of value. A bare `<ol reversed>` (the common form) parses with a null attribute
                // value, not an empty string, so a truthiness check on GetAttribute's return value
                // would incorrectly treat it as absent; existence is what matters here.
                var isReversed = box.HtmlTag.HasAttribute("reversed");

                if (isReversed)
                {
                    return hasStart
                        ? $"reversed({CssConstants.ListItem}) {startValue + 1}"
                        : $"reversed({CssConstants.ListItem})";
                }

                if (hasStart)
                {
                    return $"{CssConstants.ListItem} {startValue - 1}";
                }
            }

            return counterReset;
        }

        /// <summary>
        /// Resolves <paramref name="box"/>'s effective <c>counter-set</c> value, folding in the WHATWG
        /// HTML presentational hint for <c>&lt;li value&gt;</c> when the cascaded value is still the
        /// untouched initial value ("none" - no UA/author CSS sets counter-set for lists at all today).
        /// </summary>
        private static string GetEffectiveCounterSet(CssBox box)
        {
            var counterSet = box.CounterSet;

            if (box.HtmlTag is not null &&
                box.HtmlTag.Name.Equals("li", StringComparison.OrdinalIgnoreCase) &&
                counterSet is CssConstants.None &&
                int.TryParse(box.GetAttribute("value"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return $"{CssConstants.ListItem} {value}";
            }

            return counterSet;
        }

        private static void InheritAndApplyCounter(CssBox? currentBox, string counterName)
        {
            if (currentBox is null)
            {
                return;
            }

            // A box can be reached by more than one independent resolution chain - its own top-down
            // ancestor walk, and also as the "last child in scope" a later sibling resolves through
            // when looking up its own inheritance. Only the first visit should actually apply this
            // box's own reset/increment/set contribution; a later revisit must leave the
            // already-finalized value alone (see FinalizedCounterNames's own doc comment).
            if (!currentBox.FinalizedCounterNames.Add(counterName))
            {
                return;
            }

            var hasNewScope = false;

            if (currentBox.Counters.TryGetValue(counterName, out var currentBoxValue))
            {
                hasNewScope = currentBoxValue.IsNewScope;
            }

            if (!hasNewScope)
            {
                var parentBox = currentBox.ParentBox;

                InheritAndApplyCounter(parentBox, counterName);

                if (parentBox is not null && parentBox.Counters.TryGetValue(counterName, out var parentCounterValue))
                {
                    currentBox.Counters[counterName] = parentCounterValue with
                    {
                        IsNewScope = false
                    };
                }

                var previousSibling = GetPreviousSibling(currentBox);

                if (previousSibling is not null)
                {
                    var lastChildInScope = GetLastChildInScope(previousSibling, counterName);

                    InheritAndApplyCounter(lastChildInScope, counterName);

                    if (lastChildInScope.Counters.TryGetValue(counterName, out var lastChildCounterValue))
                    {
                        currentBox.Counters[counterName] = lastChildCounterValue with
                        {
                            IsNewScope = false
                        };
                    }
                }

            }

            ApplyCounterResets(currentBox);
            ApplyCounterIncrements(currentBox);
            ApplyCounterSets(currentBox);

        }

        private static (string CounterName, bool IsReversed) GetCounterName(string propValue)
        {
            var tokens = CssValueParser.GetCssTokens(propValue);

            var reversedToken = tokens.OfType<FunctionToken>().SingleOrDefault(x => x.Data == "reversed");
            var keywordToken = tokens.OfType<KeywordToken>().FirstOrDefault();

            if (reversedToken is not null)
            {
                var counterName = reversedToken.ArgumentTokens.FirstOrDefault()?.Data;

                return (counterName!, true);
            }
            else if (keywordToken is not null)
            {
                var counterName = keywordToken.Data;

                return (counterName, false);
            }
            else
            {
                // If no keyword token found, this might be a number or invalid value
                // Return the original value as counter name (will be handled by caller)
                return (propValue, false);
            }
        }

        private static CssBox? GetPreviousSibling(CssBox b)
        {
            if (b.ParentBox == null) return null;

            var index = b.ParentBox.Boxes.IndexOf(b);
            if (index <= 0) return null;
            var diff = 1;
            var sib = b.ParentBox.Boxes[index - diff];

            while (sib.Display == CssConstants.None && index - diff - 1 >= 0)
            {
                sib = b.ParentBox.Boxes[index - ++diff];
            }

            sib = sib.Display == CssConstants.None ? null : sib;

            return sib;
        }

        private static CssBox GetLastChildInScope(CssBox box, string counterName)
        {
            while (true)
            {
                if (box.Boxes.Count == 0)
                {
                    return box;
                }

                var lastChild = box;

                foreach (var childBox in box.Boxes)
                {
                    if (childBox.Counters.TryGetValue(counterName, out var counter))
                    {
                        if (counter.IsNewScope)
                        {
                            continue;
                        }
                    }

                    lastChild = childBox;
                }

                box = lastChild;
            }
        }
    }
}
