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

        /// <summary>
        /// Formats a resolved counter value as a string using the given CSS counter style
        /// (<c>decimal</c>, <c>decimal-leading-zero</c>, <c>lower-roman</c>, <c>upper-alpha</c>, etc.).
        /// This is the single counter-style resolver shared by <c>content: counter()</c>
        /// (<see cref="CssContentEngine"/>) and the list-item marker (<see cref="CssBoxMarker"/>).
        /// Per <see href="https://www.w3.org/TR/css-counter-styles-3/">CSS Counter Styles Level 3 §2</see>,
        /// an unknown or invalid style falls back to <c>decimal</c> rather than rendering nothing.
        /// </summary>
        public static string FormatCounterValue(int number, string style)
        {
            if (style.Equals(Keywords.DecimalLeadingZero, StringComparison.OrdinalIgnoreCase))
            {
                return number.ToString("00", CultureInfo.InvariantCulture);
            }

            // "numeric" system (CSS Counter Styles Level 3 §6.1) - positional digit substitution, e.g.
            // arabic-indic, devanagari, thai, cjk-decimal.
            var positional = CommonUtils.ConvertToPositionalNumber(number, style);
            if (positional is not null)
            {
                return positional;
            }

            // "fixed" system (§6.4) - a finite, non-repeating symbol list; out of range falls through
            // to decimal below, same as every other unrepresentable case.
            var fixedSymbol = CommonUtils.ConvertToFixedCjkSymbol(number, style);
            if (fixedSymbol is not null)
            {
                return fixedSymbol;
            }

            if (style.Equals(Keywords.EthiopicNumeric, StringComparison.OrdinalIgnoreCase) && number >= 1)
            {
                return CommonUtils.ConvertToEthiopicNumber(number);
            }

            // "symbolic" system (§6.3) - a single fixed glyph, the same for every counter value (not
            // just every list item), unlike every style above/below. Handled here rather than only in
            // CssBoxMarker so content: counter(name, disclosure-open) agrees with the default marker.
            if (style.Equals(Keywords.DisclosureOpen, StringComparison.OrdinalIgnoreCase))
            {
                return "▾"; // U+25BE BLACK DOWN-POINTING SMALL TRIANGLE
            }

            if (style.Equals(Keywords.DisclosureClosed, StringComparison.OrdinalIgnoreCase))
            {
                return "▸"; // U+25B8 BLACK RIGHT-POINTING SMALL TRIANGLE
            }

            if (CommonUtils.IsAlphabeticCounterStyle(style))
            {
                var formatted = CommonUtils.ConvertToAlphaNumber(number, style);

                // Alphabetic/symbolic styles have no representation for values outside their range
                // (e.g. 0 or negatives, for which ConvertToAlphaNumber yields the empty string) - such
                // values fall back to decimal too, per CSS Counter Styles Level 3 §2.
                return formatted.Length > 0 ? formatted : number.ToString(CultureInfo.InvariantCulture);
            }

            // `decimal` (explicit) and any unknown/invalid style fall back to decimal.
            return number.ToString(CultureInfo.InvariantCulture);
        }

        private static void ApplyCounterResets(CssBox box)
        {
            foreach (var entry in CounterListGrammar.Parse(GetEffectiveCounterReset(box)))
            {
                // A bare reversed(name) with no explicit value starts one more than the count of
                // descendants in this scope that will increment the counter: each of the N contributors
                // applies its own -1 AFTER inheriting the reset value, so starting at N+1 makes the first
                // contributor land on N and the last on 1 - see MDN's counter-reset docs for
                // reversed(<counter-name>). A non-reversed bare name just uses the CSS-default initial
                // value of 0.
                var initialValue = entry.Value
                    ?? (entry.IsReversed ? CountScopeIncrements(box, entry.Name) + 1 : 0);

                var parentScopeCounter = box.Counters.GetValueOrDefault(entry.Name);
                box.Counters[entry.Name] = new CssCounter(entry.Name, initialValue, entry.IsReversed, true, parentScopeCounter);
            }
        }

        private static void ApplyCounterSets(CssBox box)
        {
            foreach (var entry in CounterListGrammar.Parse(GetEffectiveCounterSet(box)))
            {
                // counter-set's own per-property default when a name carries no explicit integer is 0.
                SetCounterValue(box, entry.Name, entry.Value ?? 0);
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

            foreach (var entry in CounterListGrammar.Parse(box.CounterIncrement))
            {
                // counter-increment's own per-property default when a name carries no explicit integer
                // is 1, which is what makes "counter-increment: a b" increment both by one.
                incrementValues[entry.Name] = entry.Value ?? 1;
            }

            // Per the CSS Lists spec, any box whose Display resolves to list-item automatically
            // increments the implicit "list-item" counter - equivalent to a UA-stylesheet rule
            // "display: list-item { counter-increment: list-item }" - regardless of what HTML tag
            // produced that Display value. This can't be expressed as a literal UA-stylesheet
            // selector (selectors can't match on a computed Display value), so it's applied here
            // directly. An author's own explicit counter-increment targeting list-item on the same
            // element still wins (already captured above, so it's not overwritten below).
            if (box.DerivedStyle.ActualDisplay == Keywords.ListItem && !incrementValues.ContainsKey(Keywords.ListItem))
            {
                var currentListItemCounter = box.Counters.GetValueOrDefault(Keywords.ListItem);
                incrementValues[Keywords.ListItem] = currentListItemCounter is { IsReversed: true } ? -1 : 1;
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
            return counterName == Keywords.ListItem && box.DerivedStyle.ActualDisplay == Keywords.ListItem;
        }

        private static bool CounterNameAppears(string counterPropertyValue, string counterName) =>
            CounterListGrammar.Mentions(counterPropertyValue, counterName);

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
                counterReset.Trim().Equals(Keywords.ListItem, StringComparison.OrdinalIgnoreCase))
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
                        ? $"reversed({Keywords.ListItem}) {startValue + 1}"
                        : $"reversed({Keywords.ListItem})";
                }

                if (hasStart)
                {
                    return $"{Keywords.ListItem} {startValue - 1}";
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
                counterSet is Keywords.None &&
                int.TryParse(box.GetAttribute("value"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return $"{Keywords.ListItem} {value}";
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

        private static CssBox? GetPreviousSibling(CssBox b)
        {
            if (b.ParentBox == null) return null;

            var index = b.ParentBox.Boxes.IndexOf(b);
            if (index <= 0) return null;
            var diff = 1;
            var sib = b.ParentBox.Boxes[index - diff];

            // A captioned table's grid decoration box (CssBox.TableGridDecorationBox, issue #721) is a
            // synthetic paint-only Boxes[0] with no counters of its own to inherit or increment - stepped
            // over exactly like DomUtils.GetPreviousSibling steps over it, so the caption's own counter
            // resolution sees whatever preceded the table itself.
            while ((sib.DerivedStyle.ActualDisplay == Keywords.None || sib.IsTableGridDecorationBox) && index - diff - 1 >= 0)
            {
                sib = b.ParentBox.Boxes[index - ++diff];
            }

            sib = sib.DerivedStyle.ActualDisplay == Keywords.None || sib.IsTableGridDecorationBox ? null : sib;

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
