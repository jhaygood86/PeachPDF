// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
// 
// - Sun Tsu,
// "The Art of War"

using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core
{
    /// <summary>
    /// Holds parsed stylesheet css blocks arranged by media and classes.<br/>
    /// </summary>
    /// <remarks>
    /// To learn more about CSS blocks visit CSS spec: http://www.w3.org/TR/CSS21/syndata.html#block
    /// </remarks>
    internal sealed class CssData
    {
        public List<Stylesheet> Stylesheets { get; } = [];

        /// <summary>
        /// Init.
        /// </summary>
        internal CssData()
        {
        }

        // --- Rule index -----------------------------------------------------------------------
        //
        // Matching every stylesheet rule against every CssBox in the document (the naive approach)
        // is O(rules x boxes) and dominated cascade cost on large documents. Real browser engines
        // avoid this by bucketing rules by the "subject" simple selector (the one that must match the
        // box itself, e.g. the tag/class/id) so a box only needs to test the handful of rules that
        // could plausibly match it, instead of the whole stylesheet. `DoesSelectorMatch` remains the
        // source of truth for whether a rule actually matches - the index only narrows the candidates.
        //
        // Built lazily, once, the first time this CssData's rules are queried. Safe because by the
        // time CascadeApplyStyles starts querying rules for the box tree, DomParser has already
        // finished building/cloning CssData from <style>/<link> tags (see DomParser.GenerateCssTree),
        // so Stylesheets doesn't change during the walk.
        private enum SelectorBucketKind { Tag, Class, Id, Universal }

        private readonly record struct IndexedRule(IStyleRule Rule, bool IsUserAgent);

        private Dictionary<string, List<IndexedRule>>? _tagIndex;
        private Dictionary<string, List<IndexedRule>>? _classIndex;
        private Dictionary<string, List<IndexedRule>>? _idIndex;
        private List<IndexedRule>? _universalRules;

        private void EnsureIndex()
        {
            if (_universalRules is not null) return;

            var tagIndex = new Dictionary<string, List<IndexedRule>>(StringComparer.InvariantCultureIgnoreCase);
            var classIndex = new Dictionary<string, List<IndexedRule>>(StringComparer.InvariantCultureIgnoreCase);
            var idIndex = new Dictionary<string, List<IndexedRule>>(StringComparer.InvariantCultureIgnoreCase);
            var universal = new List<IndexedRule>();
            var keys = new List<(SelectorBucketKind Kind, string Key)>();

            foreach (var stylesheet in Stylesheets)
            {
                foreach (var rule in stylesheet.StyleRules)
                {
                    var indexedRule = new IndexedRule(rule, stylesheet.IsUserAgent);

                    keys.Clear();
                    CollectIndexKeys(rule.Selector, keys);

                    foreach (var (kind, key) in keys)
                    {
                        var bucket = kind switch
                        {
                            SelectorBucketKind.Tag => tagIndex,
                            SelectorBucketKind.Class => classIndex,
                            SelectorBucketKind.Id => idIndex,
                            _ => null
                        };

                        if (bucket is null)
                        {
                            universal.Add(indexedRule);
                        }
                        else
                        {
                            if (!bucket.TryGetValue(key, out var list))
                                bucket[key] = list = [];
                            list.Add(indexedRule);
                        }
                    }
                }
            }

            _tagIndex = tagIndex;
            _classIndex = classIndex;
            _idIndex = idIndex;
            _universalRules = universal;
        }

        /// <summary>
        /// Determines which bucket(s) a rule's selector should be indexed under, based on the simple
        /// selector that must match the box itself (for <see cref="ComplexSelector"/>, that's its
        /// rightmost/subject selector - see the reversal in <see cref="DoesSelectorMatch(ComplexSelector, CssBox?)"/>).
        /// A <see cref="ListSelector"/> (comma-separated) contributes one key per alternative, since
        /// matching any alternative matches the rule.
        /// </summary>
        private static void CollectIndexKeys(ISelector selector, List<(SelectorBucketKind Kind, string Key)> keys)
        {
            switch (selector)
            {
                case ListSelector listSelector:
                    foreach (var sub in listSelector)
                        CollectIndexKeys(sub, keys);
                    break;

                case ComplexSelector complexSelector:
                    var last = complexSelector.LastOrDefault().Selector;
                    if (last is not null)
                        CollectIndexKeys(last, keys);
                    else
                        keys.Add((SelectorBucketKind.Universal, string.Empty));
                    break;

                case CompoundSelector compoundSelector:
                    // Pseudo-element and :first-child subjects need the box's own HtmlTag/ParentBox
                    // re-derived at match time (see DoesSelectorMatch), including for synthesized
                    // pseudo-element boxes whose HtmlTag is null - the tag/class/id index can't
                    // safely represent that, so fall back to a full scan for these.
                    if (compoundSelector.Any(s => s is PseudoElementSelector or FirstChildSelector))
                    {
                        keys.Add((SelectorBucketKind.Universal, string.Empty));
                        break;
                    }

                    IdSelector? id = null;
                    ClassSelector? cls = null;
                    TypeSelector? type = null;

                    foreach (var member in compoundSelector)
                    {
                        switch (member)
                        {
                            case IdSelector idSel: id ??= idSel; break;
                            case ClassSelector clsSel: cls ??= clsSel; break;
                            case TypeSelector typeSel: type ??= typeSel; break;
                        }
                    }

                    if (id is not null) keys.Add((SelectorBucketKind.Id, id.Id));
                    else if (cls is not null) keys.Add((SelectorBucketKind.Class, cls.Class));
                    else if (type is not null) keys.Add((SelectorBucketKind.Tag, type.Name));
                    else keys.Add((SelectorBucketKind.Universal, string.Empty));
                    break;

                case TypeSelector typeSelector:
                    keys.Add((SelectorBucketKind.Tag, typeSelector.Name));
                    break;

                case ClassSelector classSelector:
                    keys.Add((SelectorBucketKind.Class, classSelector.Class));
                    break;

                case IdSelector idSelector:
                    keys.Add((SelectorBucketKind.Id, idSelector.Id));
                    break;

                default:
                    // AllSelector, bare pseudo-class/element/attribute selectors, etc.
                    keys.Add((SelectorBucketKind.Universal, string.Empty));
                    break;
            }
        }

        /// <summary>
        /// Parse the given stylesheet to <see cref="CssData"/> object.<br/>
        /// If <paramref name="combineWithDefault"/> is true the parsed css blocks are added to the 
        /// default css data (as defined by W3), merged if class name already exists. If false only the data in the given stylesheet is returned.
        /// </summary>
        /// <seealso cref="http://www.w3.org/TR/CSS21/sample.html"/>
        /// <param name="adapter">Platform adapter</param>
        /// <param name="stylesheet">the stylesheet source to parse</param>
        /// <param name="combineWithDefault">true - combine the parsed css data with default css data, false - return only the parsed css data</param>
        /// <returns>the parsed css data</returns>
        public static async Task<CssData> Parse(RAdapter adapter, string stylesheet, bool combineWithDefault = true)
        {
            var parser = new CssParser(adapter, null);
            return await parser.ParseStyleSheet(stylesheet, combineWithDefault);
        }

        internal IEnumerable<IStyleRule> GetStyleRules(string media, CssBox box) =>
            GetStyleRulesByOrigin(media, box, userAgentOnly: null);

        internal IEnumerable<IStyleRule> GetUserAgentStyleRules(string media, CssBox box) =>
            GetStyleRulesByOrigin(media, box, userAgentOnly: true);

        internal IEnumerable<IStyleRule> GetAuthorStyleRules(string media, CssBox box) =>
            GetStyleRulesByOrigin(media, box, userAgentOnly: false);

        private IEnumerable<IStyleRule> GetStyleRulesByOrigin(string media, CssBox box, bool? userAgentOnly)
        {
            EnsureIndex();

            // Rules matched via the index below can, in rare cases (a comma-separated selector list
            // whose alternatives land in different buckets, e.g. "div, .foo" matching a <div class="foo">),
            // be reachable through more than one bucket. Dedup so callers never see the same rule twice.
            HashSet<IStyleRule>? seen = null;

            bool ShouldEmit(IndexedRule indexed)
            {
                if (userAgentOnly.HasValue && indexed.IsUserAgent != userAgentOnly.Value) return false;
                if (!DoesSelectorMatch(indexed.Rule.Selector, box)) return false;
                seen ??= [];
                return seen.Add(indexed.Rule);
            }

            foreach (var indexed in _universalRules!)
            {
                if (ShouldEmit(indexed))
                    yield return indexed.Rule;
            }

            if (box.HtmlTag is not null)
            {
                if (_tagIndex!.TryGetValue(box.HtmlTag.Name, out var tagRules))
                {
                    foreach (var indexed in tagRules)
                        if (ShouldEmit(indexed))
                            yield return indexed.Rule;
                }

                if (box.HtmlTag.Attributes is not null)
                {
                    if (box.HtmlTag.Attributes.TryGetValue("class", out var classAttr) && classAttr.Length > 0)
                    {
                        foreach (var className in classAttr.Split(' '))
                        {
                            if (className.Length == 0) continue;
                            if (_classIndex!.TryGetValue(className, out var classRules))
                                foreach (var indexed in classRules)
                                    if (ShouldEmit(indexed))
                                        yield return indexed.Rule;
                        }
                    }

                    if (box.HtmlTag.Attributes.TryGetValue("id", out var idAttr) && idAttr.Length > 0
                        && _idIndex!.TryGetValue(idAttr, out var idRules))
                    {
                        foreach (var indexed in idRules)
                            if (ShouldEmit(indexed))
                                yield return indexed.Rule;
                    }
                }
            }

            // Rules inside @media blocks are not pre-indexed (typically few of them in practice) -
            // keep a direct scan for these, same as before.
            foreach (var stylesheet in Stylesheets)
            {
                if (userAgentOnly.HasValue && stylesheet.IsUserAgent != userAgentOnly.Value)
                    continue;

                foreach (var mediaRule in stylesheet.MediaRules)
                {
                    foreach (var medium in mediaRule.Media)
                    {
                        var typeMatches = medium.Type == media || medium.Type == "all";
                        var matches = medium.IsInverse ? !typeMatches : typeMatches;
                        if (!matches) continue;

                        foreach (var rule in GetStyleRules(mediaRule.Rules.OfType<IStyleRule>(), box))
                        {
                            if (seen is not null && !seen.Add(rule)) continue;
                            yield return rule;
                        }
                    }
                }
            }
        }

        private static IEnumerable<IStyleRule> GetStyleRules(IEnumerable<IStyleRule> styleRules, CssBox box)
        {
            return styleRules.Where(rule => DoesSelectorMatch(rule.Selector, box));
        }

        private static bool DoesSelectorMatch(ISelector selector, CssBox? box)
        {
            return selector switch
            {
                AllSelector => true,
                ListSelector listSelector => DoesSelectorMatch(listSelector, box),
                TypeSelector typeSelector => DoesSelectorMatch(typeSelector, box),
                ComplexSelector complexSelector => DoesSelectorMatch(complexSelector, box),
                CompoundSelector compoundSelector => DoesSelectorMatch(compoundSelector, box),
                PseudoElementSelector pseudoElementSelector => DoesSelectorMatch(pseudoElementSelector, box),
                PseudoClassSelector pseudoClassSelector => DoesSelectorMatch(pseudoClassSelector, box),
                AttrMatchSelector attrMatchSelector => DoesSelectorMatch(attrMatchSelector, box),
                ClassSelector classSelector => DoesSelectorMatch(classSelector, box),
                IdSelector idSelector => DoesSelectorMatch(idSelector, box),
                AttrAvailableSelector attrAvailableSelector => DoesSelectorMatch(attrAvailableSelector, box),
                AttrContainsSelector attrContainsSelector => DoesSelectorMatch(attrContainsSelector, box),
                AttrListSelector attrListSelector => DoesSelectorMatch(attrListSelector, box),
                AttrBeginsSelector attrBeginsSelector => DoesSelectorMatch(attrBeginsSelector, box),
                AttrEndsSelector attrEndsSelector => DoesSelectorMatch(attrEndsSelector, box),
                AttrHyphenSelector attrHyphenSelector => DoesSelectorMatch(attrHyphenSelector, box),
                FirstChildSelector firstChildSelector => DoesSelectorMatch(firstChildSelector, box),
                _ => false
            };
        }
        private static bool DoesSelectorMatch(ListSelector listSelector, CssBox? box)
        {
            return listSelector.Any(selector => DoesSelectorMatch(selector, box));
        }

        private static bool DoesSelectorMatch(CompoundSelector compoundSelector, CssBox? box)
        {
            if (box is null)
            {
                return false;
            }

            var lastSelector = compoundSelector.Last();

            if (lastSelector is not PseudoElementSelector or FirstChildSelector)
                return compoundSelector.All(selector => DoesSelectorMatch(selector, box));

            if (lastSelector is PseudoElementSelector pseudoElementSelector)
            {
                var referenceBox = box.IsPseudoElement ? box.ParentBox : box;

                var isMatchWithoutPseudoElement = compoundSelector
                    .Where(x => x is not PseudoElementSelector)
                    .All(selector => DoesSelectorMatch(selector, referenceBox));

                if (!isMatchWithoutPseudoElement) return false;

                if (box.IsPseudoElement)
                {
                    return DoesSelectorMatch(pseudoElementSelector, box);
                }

                switch (pseudoElementSelector.Name)
                {
                    case CssConstants.Before:
                        {
                            var beforePseudoBox = new CssBox(box, null)
                            {
                                IsBeforePseudoElement = true
                            };

                            beforePseudoBox.InheritStyle(box);
                            box.Boxes.Remove(beforePseudoBox);
                            box.Boxes.Insert(0, beforePseudoBox);
                            break;
                        }
                    case CssConstants.After:
                        {
                            var afterPseudoBox = new CssBox(box, null)
                            {
                                IsAfterPseudoElement = true
                            };

                            afterPseudoBox.InheritStyle(box);
                            box.Boxes.Remove(afterPseudoBox);
                            box.Boxes.Add(afterPseudoBox);
                            break;
                        }
                }
            }

            if (lastSelector is FirstChildSelector firstChildSelector)
            {
                var referenceBox = DomUtils.GetNearestParentElementBox(box);

                var isMatchWithoutNthChildElement = compoundSelector
                    .Where(x => x is not FirstChildSelector)
                    .All(selector => DoesSelectorMatch(selector, referenceBox));

                return isMatchWithoutNthChildElement && DoesSelectorMatch(firstChildSelector, box);
            }

            return false;
        }

        private static bool DoesSelectorMatch(TypeSelector typeSelector, CssBox? box)
        {
            return box?.HtmlTag is not null && typeSelector.Name.Equals(box.HtmlTag.Name, StringComparison.InvariantCultureIgnoreCase);
        }

        private static bool DoesSelectorMatch(ClassSelector classSelector, CssBox? box)
        {
            if (box?.HtmlTag?.Attributes is not null && box.HtmlTag.Attributes.TryGetValue("class", out var classNames))
            {
                return classNames.Split(' ').Any(className =>
                    className.Equals(classSelector.Class, StringComparison.InvariantCultureIgnoreCase));
            }

            return false;
        }

        private static bool DoesSelectorMatch(IdSelector idSelector, CssBox? box)
        {
            if (box?.HtmlTag?.Attributes is not null && box.HtmlTag.Attributes.TryGetValue("id", out var id))
            {
                return id.Equals(idSelector.Id, StringComparison.InvariantCultureIgnoreCase);
            }

            return false;
        }

        private static bool DoesSelectorMatch(AttrAvailableSelector attrAvailableSelector, CssBox? box)
        {
            if (box?.HtmlTag?.Attributes is null)
            {
                return false;
            }

            foreach (var attribute in box.HtmlTag.Attributes)
            {
                if (attribute.Key.Equals(attrAvailableSelector.Attribute, StringComparison.InvariantCultureIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool DoesSelectorMatch(AttrMatchSelector attrMatchSelector, CssBox? box)
        {
            if (box?.HtmlTag?.Attributes is null)
            {
                return false;
            }

            foreach (var attribute in box.HtmlTag.Attributes)
            {
                if (attribute.Key.Equals(attrMatchSelector.Attribute, StringComparison.InvariantCultureIgnoreCase))
                {
                    return attribute.Value.Equals(attrMatchSelector.Value, StringComparison.InvariantCultureIgnoreCase);
                }
            }

            return false;
        }

        private static bool DoesSelectorMatch(AttrListSelector attrListSelector, CssBox? box)
        {
            if (box?.HtmlTag?.Attributes is null)
            {
                return false;
            }

            foreach (var attribute in box.HtmlTag.Attributes)
            {
                if (attribute.Key.Equals(attrListSelector.Attribute, StringComparison.InvariantCultureIgnoreCase))
                {
                    var attributeValues = attribute.Value.Split(' ').Where(x => x.Length > 0).ToArray();

                    return attributeValues.Any(value => value.Equals(attrListSelector.Value, StringComparison.InvariantCultureIgnoreCase));
                }
            }

            return false;
        }

        private static bool DoesSelectorMatch(AttrContainsSelector attrContainsSelector, CssBox? box)
        {
            if (box?.HtmlTag?.Attributes is null)
            {
                return false;
            }

            foreach (var attribute in box.HtmlTag.Attributes)
            {
                if (attribute.Key.Equals(attrContainsSelector.Attribute, StringComparison.InvariantCultureIgnoreCase))
                {
                    return attribute.Value.Contains(attrContainsSelector.Value, StringComparison.InvariantCultureIgnoreCase);
                }
            }

            return false;
        }

        private static bool DoesSelectorMatch(AttrBeginsSelector s, CssBox? box)
        {
            if (box?.HtmlTag?.Attributes is null) return false;
            foreach (var attr in box.HtmlTag.Attributes)
                if (attr.Key.Equals(s.Attribute, StringComparison.InvariantCultureIgnoreCase))
                    return attr.Value.StartsWith(s.Value, StringComparison.InvariantCultureIgnoreCase);
            return false;
        }

        private static bool DoesSelectorMatch(AttrEndsSelector s, CssBox? box)
        {
            if (box?.HtmlTag?.Attributes is null) return false;
            foreach (var attr in box.HtmlTag.Attributes)
                if (attr.Key.Equals(s.Attribute, StringComparison.InvariantCultureIgnoreCase))
                    return attr.Value.EndsWith(s.Value, StringComparison.InvariantCultureIgnoreCase);
            return false;
        }

        private static bool DoesSelectorMatch(AttrHyphenSelector s, CssBox? box)
        {
            if (box?.HtmlTag?.Attributes is null) return false;
            foreach (var attr in box.HtmlTag.Attributes)
                if (attr.Key.Equals(s.Attribute, StringComparison.InvariantCultureIgnoreCase))
                    return attr.Value.Equals(s.Value, StringComparison.InvariantCultureIgnoreCase)
                        || attr.Value.StartsWith(s.Value + "-", StringComparison.InvariantCultureIgnoreCase);
            return false;
        }

        private static bool DoesSelectorMatch(PseudoClassSelector pseudoClassSelector, CssBox? box)
        {
            return pseudoClassSelector.Class == "link" && box is not null && box.IsClickable;
        }

        private static bool DoesSelectorMatch(PseudoElementSelector pseudoElementSelector, CssBox? box)
        {
            if (box is null)
            {
                return false;
            }

            switch (pseudoElementSelector.Name)
            {
                case CssConstants.Before when box.IsBeforePseudoElement:
                case CssConstants.After when box.IsAfterPseudoElement:
                    return true;
                default:
                    return false;
            }
        }

        private static bool DoesSelectorMatch(FirstChildSelector firstChildSelector, CssBox? box)
        {
            if (box?.HtmlTag is null)
            {
                return false;
            }

            var parentBox = DomUtils.GetNearestParentElementBox(box);

            if (parentBox is null)
            {
                return false;
            }

            var currentIndex = parentBox.Boxes.Where(b => b.HtmlTag is not null).ToList();
            return currentIndex.IndexOf(box) == firstChildSelector.Offset;
        }

        private static bool DoesSelectorMatch(ComplexSelector complexSelector, CssBox? box)
        {
            var selectorsInReverse = complexSelector.Reverse().ToList();

            var isLowestItem = true;
            var currentRef = box;

            foreach (var selector in selectorsInReverse)
            {
                if (selector.Selector is null) return false;

                if (isLowestItem)
                {
                    if (!DoesSelectorMatch(selector.Selector, currentRef)) return false;
                    isLowestItem = false;
                    continue;
                }

                if (currentRef is null) return false;

                switch (selector.Delimiter)
                {
                    case ">":
                        currentRef = currentRef.ParentBox;
                        if (!DoesSelectorMatch(selector.Selector, currentRef)) return false;
                        break;

                    case " " or null:
                        currentRef = currentRef.ParentBox;
                        while (currentRef is not null && !DoesSelectorMatch(selector.Selector, currentRef))
                            currentRef = currentRef.ParentBox;
                        if (currentRef is null) return false;
                        break;

                    case "+":
                    {
                        var parent = currentRef.ParentBox;
                        if (parent is null) return false;
                        var siblings = parent.Boxes.Where(b => b.HtmlTag is not null).ToList();
                        var idx = siblings.IndexOf(currentRef);
                        if (idx <= 0) return false;
                        currentRef = siblings[idx - 1];
                        if (!DoesSelectorMatch(selector.Selector, currentRef)) return false;
                        break;
                    }

                    case "~":
                    {
                        var parent = currentRef.ParentBox;
                        if (parent is null) return false;
                        var siblings = parent.Boxes.Where(b => b.HtmlTag is not null).ToList();
                        var idx = siblings.IndexOf(currentRef);
                        if (idx <= 0) return false;
                        CssBox? match = null;
                        for (var i = idx - 1; i >= 0; i--)
                            if (DoesSelectorMatch(selector.Selector, siblings[i])) { match = siblings[i]; break; }
                        if (match is null) return false;
                        currentRef = match;
                        break;
                    }

                    default:
                        return false;
                }
            }

            return true;
        }

        public CssData Clone()
        {
            CssData cssData = new();
            cssData.Stylesheets.AddRange(Stylesheets);
            return cssData;
        }
    }
}