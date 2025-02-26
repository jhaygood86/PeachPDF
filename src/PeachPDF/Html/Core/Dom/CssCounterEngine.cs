using PeachPDF.CSS;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;
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
            if (box.CounterReset is CssConstants.None) return;

            var valueParts = box.CounterReset.Split(' ');

            string? previousCounterName = null;

            foreach (var valuePart in valueParts)
            {
                var (counterName, isReversed) = GetCounterName(valuePart);
                var counterValue = 0;

                if (previousCounterName is not null && CommonUtils.IsInteger(valuePart.AsSpan()))
                {
                    counterName = previousCounterName;
                    counterValue = int.Parse(valuePart);
                }

                previousCounterName = counterName;

                var parentScopeCounter = box.Counters.GetValueOrDefault(counterName);
                box.Counters[counterName] = new CssCounter(counterName, counterValue, isReversed, true, parentScopeCounter);
            }
        }

        private static void ApplyCounterIncrements(CssBox box)
        {
            if (box.CounterIncrement is CssConstants.None) return;

            var valueParts = box.CounterIncrement.Split(' ');

            string? previousCounterName = null;

            Dictionary<string, int> incrementValues = [];

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

        private static void InheritAndApplyCounter(CssBox? currentBox, string counterName)
        {
            if (currentBox is null)
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
            else
            {
                var counterName = keywordToken!.Data;

                return (counterName, false);
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
