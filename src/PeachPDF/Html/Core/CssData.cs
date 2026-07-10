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
            var matched = new List<IStyleRule>();

            foreach (var stylesheet in Stylesheets)
            {
                if (userAgentOnly.HasValue && stylesheet.IsUserAgent != userAgentOnly.Value)
                    continue;

                CollectMatchingRulesInOrder(stylesheet.Rules, media, box, matched);
            }

            // Stable sort: equal-specificity rules keep the true document order CollectMatchingRulesInOrder
            // produced them in, giving correct source-order tiebreaking without any extra index field.
            return matched.OrderBy(rule => GetMatchedSpecificity(rule.Selector, box));
        }

        /// <summary>
        /// Walks a rule list in true document order, expanding matching <c>@media</c> blocks in place
        /// (rather than the old two-pass "all plain rules, then all media rules" approach, which didn't
        /// preserve real source order across that boundary) so the caller's stable specificity-sort has
        /// a genuinely ordered sequence to break ties with.
        /// </summary>
        private static void CollectMatchingRulesInOrder(IEnumerable<IRule> rules, string media, CssBox box, List<IStyleRule> matched)
        {
            foreach (var rule in rules)
            {
                switch (rule)
                {
                    case IStyleRule styleRule:
                        if (DoesSelectorMatch(styleRule.Selector, box))
                            matched.Add(styleRule);
                        break;

                    case IMediaRule mediaRule:
                        var mediaMatches = false;
                        foreach (var medium in mediaRule.Media)
                        {
                            var typeMatches = medium.Type == media || medium.Type == "all";
                            if (medium.IsInverse ? !typeMatches : typeMatches)
                            {
                                mediaMatches = true;
                                break;
                            }
                        }

                        if (mediaMatches)
                            CollectMatchingRulesInOrder(mediaRule.Rules, media, box, matched);
                        break;
                }
            }
        }

        /// <summary>
        /// The effective specificity of a matched rule's selector for cascade-ordering purposes,
        /// relative to a specific matched box. This deliberately differs from calling
        /// <c>selector.Specificity</c> directly for a top-level <see cref="ListSelector"/> (comma-
        /// separated rule selector, e.g. "h1, h2 {}"): per spec a selector list used as a whole
        /// rule's selector is cascade-equivalent to separate rules, so the specificity that governs
        /// THIS box's cascade is whichever one alternative actually matched it - not every
        /// alternative's static max (which is what <see cref="ListSelector.Specificity"/> reports,
        /// correctly, for its OTHER use as an argument to :is()/:not()/:has()). One level of
        /// ListSelector-checking is sufficient since the grammar can't nest one directly inside
        /// another top-level list.
        /// </summary>
        private static Priority GetMatchedSpecificity(ISelector selector, CssBox? box)
        {
            if (selector is ListSelector list)
            {
                return list
                    .Where(s => DoesSelectorMatch(s, box))
                    .Select(s => s.Specificity)
                    .DefaultIfEmpty(Priority.Zero)
                    .Max();
            }

            return selector.Specificity;
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
                ChildSelector childSelector => DoesSelectorMatch(childSelector, box),
                OnlyChildSelector onlyChildSelector => DoesSelectorMatch(onlyChildSelector, box),
                OnlyOfTypeSelector onlyOfTypeSelector => DoesSelectorMatch(onlyOfTypeSelector, box),
                NotSelector notSelector => DoesSelectorMatch(notSelector, box),
                MatchesSelector matchesSelector => DoesSelectorMatch(matchesSelector, box),
                HasSelector hasSelector => DoesSelectorMatch(hasSelector, box),
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

            // Structural pseudo-classes (ChildSelector subtypes, OnlyChildSelector, OnlyOfTypeSelector)
            // are matched against `box` itself here, same as every other compound member - their own
            // DoesSelectorMatch overload re-derives sibling scope from `box` as needed, so no special
            // handling is required for them beyond the plain path below.
            if (lastSelector is not PseudoElementSelector)
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

        /// <summary>
        /// Matches the six structural "An+B" pseudo-classes (:nth-child, :nth-last-child,
        /// :nth-of-type, :nth-last-of-type, :nth-column, :nth-last-column - including their bare-ident
        /// forms like :first-child, which are wired to Step=0/Offset=1 by PseudoClassSelectorFactory).
        /// Each variant differs only in (a) which sibling scope to count within and (b) whether to
        /// count from the start or the end; the actual "does this position satisfy An+B" test is
        /// shared via <see cref="MatchesAnPlusB"/>.
        /// </summary>
        private static bool DoesSelectorMatch(ChildSelector childSelector, CssBox? box)
        {
            if (box?.HtmlTag is null)
            {
                return false;
            }

            switch (childSelector)
            {
                case FirstColumnSelector or LastColumnSelector:
                    return DoesColumnSelectorMatch(childSelector, box, fromEnd: childSelector is LastColumnSelector);
            }

            var parentBox = DomUtils.GetNearestParentElementBox(box);
            if (parentBox is null)
            {
                return false;
            }

            IEnumerable<CssBox> siblingBoxes = parentBox.Boxes.Where(b => b.HtmlTag is not null);

            var sameTypeOnly = childSelector is FirstTypeSelector or LastTypeSelector;
            if (sameTypeOnly)
            {
                siblingBoxes = siblingBoxes.Where(b =>
                    b.HtmlTag!.Name.Equals(box.HtmlTag.Name, StringComparison.InvariantCultureIgnoreCase));
            }

            // CSS4 "of <selector>" extension (:nth-child(An+B of S)/:nth-last-child(An+B of S)).
            // Kind defaults to AllSelector (matches unconditionally) whenever no "of" clause was
            // written, and the parser only ever allows a non-default Kind on FirstChildSelector/
            // LastChildSelector - so this filter is always a no-op for the four other subtypes.
            // Applying it unconditionally is simpler than special-casing which subtypes can have it.
            siblingBoxes = siblingBoxes.Where(b => DoesSelectorMatch(childSelector.Kind, b));

            var siblings = siblingBoxes.ToList();
            var index = siblings.IndexOf(box);
            if (index < 0)
            {
                return false;
            }

            var fromEndPosition = childSelector is LastChildSelector or LastTypeSelector;
            var position = fromEndPosition ? siblings.Count - index : index + 1;

            return MatchesAnPlusB(position, childSelector.Step, childSelector.Offset);
        }

        /// <summary>
        /// Matches :nth-column()/:nth-last-column() against the cell's occupied column position(s)
        /// within its own row. Only accounts for colspan of preceding cells in the SAME row - unlike
        /// <see cref="Dom.CssLayoutEngineTable"/>'s column bookkeeping (which additionally accounts for
        /// rowspan carried over from earlier rows via placeholder boxes inserted during layout), this
        /// runs during cascade, before layout, when that placeholder bookkeeping doesn't exist yet.
        /// A colspan-N cell occupies N columns; per spec this matches if ANY occupied column position
        /// satisfies the An+B formula, not just the cell's starting column.
        /// </summary>
        private static bool DoesColumnSelectorMatch(ChildSelector childSelector, CssBox box, bool fromEnd)
        {
            var row = box.ParentBox;
            if (row is null)
            {
                return false;
            }

            var cellsInRow = row.Boxes.Where(b => b.HtmlTag is not null).ToList();
            var columnIndex = 0;
            var totalColumns = 0;
            var found = false;

            foreach (var cell in cellsInRow)
            {
                if (cell == box)
                {
                    columnIndex = totalColumns;
                    found = true;
                }

                totalColumns += GetColSpan(cell);
            }

            if (!found)
            {
                return false;
            }

            var boxSpan = GetColSpan(box);
            for (var column = columnIndex; column < columnIndex + boxSpan; column++)
            {
                var position = fromEnd ? totalColumns - column : column + 1;
                if (MatchesAnPlusB(position, childSelector.Step, childSelector.Offset))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Reads a box's "colspan" HTML attribute (default 1 if absent/invalid). Deliberately
        /// duplicated from <see cref="Dom.CssLayoutEngineTable"/>'s private equivalent rather than
        /// shared - this runs at cascade time, before the layout-phase state that method's column
        /// bookkeeping otherwise depends on exists.
        /// </summary>
        private static int GetColSpan(CssBox box)
        {
            var value = box.GetAttribute("colspan", "1");
            return !int.TryParse(value, out var colSpan) || colSpan < 1 ? 1 : colSpan;
        }

        /// <summary>
        /// The standard CSS "An+B" existence test: does there exist a non-negative integer n such that
        /// position == step*n + offset? position is always the 1-based index within whatever sibling
        /// scope the caller has already resolved.
        /// </summary>
        private static bool MatchesAnPlusB(int position, int step, int offset)
        {
            if (step == 0)
            {
                return position == offset;
            }

            var diff = position - offset;
            return diff % step == 0 && (step > 0 ? diff >= 0 : diff <= 0);
        }

        private static bool DoesSelectorMatch(OnlyChildSelector _, CssBox? box)
        {
            if (box?.HtmlTag is null)
            {
                return false;
            }

            var parentBox = DomUtils.GetNearestParentElementBox(box);
            return parentBox is not null && parentBox.Boxes.Count(b => b.HtmlTag is not null) == 1;
        }

        private static bool DoesSelectorMatch(OnlyOfTypeSelector _, CssBox? box)
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

            return parentBox.Boxes.Count(b =>
                b.HtmlTag is not null &&
                b.HtmlTag.Name.Equals(box.HtmlTag.Name, StringComparison.InvariantCultureIgnoreCase)) == 1;
        }

        private static bool DoesSelectorMatch(NotSelector notSelector, CssBox? box)
        {
            return box is not null && !DoesSelectorMatch(notSelector.Inner, box);
        }

        private static bool DoesSelectorMatch(MatchesSelector matchesSelector, CssBox? box)
        {
            return DoesSelectorMatch(matchesSelector.Inner, box);
        }

        private static bool DoesSelectorMatch(HasSelector hasSelector, CssBox? box)
        {
            return box is not null && HasDescendantMatch(box, hasSelector.Inner);
        }

        /// <summary>
        /// Recursively searches box's descendants (at any depth) for one matching inner, short-
        /// circuiting on the first hit - mirrors the shape of DomUtils.GetBoxById/GetBoxByTagName.
        /// Backs :has()'s default (descendant) relative-selector form; leading-combinator forms
        /// (":has(&gt; x)"/"+"/"~") aren't supported - see HasSelector's doc comment.
        /// </summary>
        private static bool HasDescendantMatch(CssBox box, ISelector inner)
        {
            foreach (var child in box.Boxes)
            {
                if (DoesSelectorMatch(inner, child)) return true;
                if (HasDescendantMatch(child, inner)) return true;
            }

            return false;
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