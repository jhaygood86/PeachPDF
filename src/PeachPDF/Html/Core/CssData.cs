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

        // DocumentOrder is assigned while walking stylesheets (recursing into @media blocks in
        // place) so that, regardless of which bucket a rule is later retrieved through, the true
        // source order - including across the plain-rule/@media boundary - can be reconstructed
        // for specificity tie-breaking (see GetStyleRulesByOrigin). EnclosingMedia carries the
        // chain of @media conditions (outermost first) a rule was nested under, so media matching
        // no longer needs a separate unindexed scan.
        private readonly record struct IndexedRule(IStyleRule Rule, bool IsUserAgent, int DocumentOrder, MediaList[]? EnclosingMedia);

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
            var order = 0;

            foreach (var stylesheet in Stylesheets)
            {
                IndexRules(stylesheet.Rules, stylesheet.IsUserAgent, null, tagIndex, classIndex, idIndex, universal, keys, ref order);
            }

            _tagIndex = tagIndex;
            _classIndex = classIndex;
            _idIndex = idIndex;
            _universalRules = universal;
        }

        /// <summary>
        /// Walks a rule list in true document order, recursing into <c>@media</c> blocks (any
        /// nesting depth) in place, and buckets every style rule found - assigning each an
        /// ever-increasing <see cref="IndexedRule.DocumentOrder"/> and recording the chain of
        /// enclosing media conditions it's nested under, if any.
        /// </summary>
        private static void IndexRules(
            IEnumerable<IRule> rules,
            bool isUserAgent,
            MediaList[]? enclosingMedia,
            Dictionary<string, List<IndexedRule>> tagIndex,
            Dictionary<string, List<IndexedRule>> classIndex,
            Dictionary<string, List<IndexedRule>> idIndex,
            List<IndexedRule> universal,
            List<(SelectorBucketKind Kind, string Key)> keys,
            ref int order)
        {
            foreach (var rule in rules)
            {
                switch (rule)
                {
                    case IStyleRule styleRule:
                        var indexedRule = new IndexedRule(styleRule, isUserAgent, order++, enclosingMedia);

                        keys.Clear();
                        CollectIndexKeys(styleRule.Selector, keys);

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
                        break;

                    case IMediaRule mediaRule:
                        var nestedMedia = enclosingMedia is null
                            ? [mediaRule.Media]
                            : (MediaList[])[.. enclosingMedia, mediaRule.Media];
                        IndexRules(mediaRule.Rules, isUserAgent, nestedMedia, tagIndex, classIndex, idIndex, universal, keys, ref order);
                        break;
                }
            }
        }

        /// <summary>
        /// True if every level of a rule's enclosing <c>@media</c> chain matches <paramref name="media"/>
        /// (nesting is conjunctive - all levels must match), or if the rule isn't nested in any
        /// <c>@media</c> block at all.
        /// </summary>
        private static bool EnclosingMediaMatches(MediaList[]? enclosingMedia, string media)
        {
            if (enclosingMedia is null) return true;

            foreach (var mediaList in enclosingMedia)
            {
                var anyMatches = false;
                foreach (var medium in mediaList)
                {
                    var typeMatches = medium.Type == media || medium.Type == "all";
                    if (medium.IsInverse ? !typeMatches : typeMatches)
                    {
                        anyMatches = true;
                        break;
                    }
                }

                if (!anyMatches) return false;
            }

            return true;
        }

        /// <summary>
        /// Determines which bucket(s) a rule's selector should be indexed under, based on the simple
        /// selector that must match the box itself (for <see cref="ComplexSelector"/>, that's its
        /// rightmost/subject selector - see the reversal in <see cref="DoesSelectorMatch(ComplexSelector, ICssDomNode?)"/>).
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
        /// <seealso href="http://www.w3.org/TR/CSS21/sample.html">CSS 2.1 default stylesheet for HTML</seealso>
        /// <param name="adapter">Platform adapter</param>
        /// <param name="stylesheet">the stylesheet source to parse</param>
        /// <param name="combineWithDefault">true - combine the parsed css data with default css data, false - return only the parsed css data</param>
        /// <returns>the parsed css data</returns>
        public static async Task<CssData> Parse(RAdapter adapter, string stylesheet, bool combineWithDefault = true)
        {
            var parser = new CssParser(adapter, null);
            return await parser.ParseStyleSheet(stylesheet, combineWithDefault);
        }

        internal IEnumerable<IStyleRule> GetStyleRules(string media, ICssDomNode node) =>
            GetStyleRulesByOrigin(media, node, userAgentOnly: null);

        internal IEnumerable<IStyleRule> GetUserAgentStyleRules(string media, ICssDomNode node) =>
            GetStyleRulesByOrigin(media, node, userAgentOnly: true);

        internal IEnumerable<IStyleRule> GetAuthorStyleRules(string media, ICssDomNode node) =>
            GetStyleRulesByOrigin(media, node, userAgentOnly: false);

        /// <summary>
        /// Same candidate-gathering (tag/class/id/universal index buckets) as
        /// <see cref="GetUserAgentStyleRules"/>/<see cref="GetAuthorStyleRules"/>, but matched via
        /// <see cref="MatchesAsFirstLineSelector"/> instead of the ordinary <see cref="DoesSelectorMatch(ISelector, ICssDomNode?)"/>.
        /// Needed because the ordinary matcher deliberately returns false for a real (non-pseudo-element)
        /// box against any `*::first-line` selector - unlike `::before`/`::after`/`::marker`/
        /// `::first-letter`, first-line has no synthesized box of its own for a later, separate cascade
        /// pass to re-match against, so this is the only way its declarations are ever gathered at all.
        /// See <c>DomParser.ResolveFirstLineStyle</c>, its only caller.
        /// </summary>
        internal IEnumerable<IStyleRule> GetFirstLineStyleRules(string media, CssBox box, bool userAgentOnly) =>
            GetStyleRulesByOrigin(media, box, userAgentOnly, firstLineOnly: true);

        private IEnumerable<IStyleRule> GetStyleRulesByOrigin(string media, ICssDomNode node, bool? userAgentOnly, bool firstLineOnly = false)
        {
            EnsureIndex();

            // Rules matched via the index below can, in rare cases (a comma-separated selector list
            // whose alternatives land in different buckets, e.g. "div, .foo" matching a <div class="foo">),
            // be reachable through more than one bucket. Dedup so callers never see the same rule twice.
            HashSet<IStyleRule>? seen = null;
            var matched = new List<IndexedRule>();

            void Collect(IndexedRule indexed)
            {
                if (userAgentOnly.HasValue && indexed.IsUserAgent != userAgentOnly.Value) return;
                if (!EnclosingMediaMatches(indexed.EnclosingMedia, media)) return;
                // firstLineOnly is HTML-cascade-only (DomParser.ResolveFirstLineStyle), always a CssBox.
                var isMatch = firstLineOnly
                    ? MatchesAsFirstLineSelector(indexed.Rule.Selector, (CssBox)node)
                    : DoesSelectorMatch(indexed.Rule.Selector, node);
                if (!isMatch) return;
                seen ??= [];
                if (!seen.Add(indexed.Rule)) return;
                matched.Add(indexed);
            }

            foreach (var indexed in _universalRules!)
                Collect(indexed);

            // The index buckets are keyed on the stylesheet's own selector names; candidate gathering is a
            // superset pre-filter (may over-gather, e.g. a case-mismatched bucket), and per-candidate
            // DoesSelectorMatch applies this node's own case-sensitivity, so no bucket-key change is needed
            // for SVG's case-sensitive matching.
            if (node.TagName is { } tagName)
            {
                if (_tagIndex!.TryGetValue(tagName, out var tagRules))
                {
                    foreach (var indexed in tagRules)
                        Collect(indexed);
                }

                var classAttr = node.GetAttribute("class");
                if (!string.IsNullOrEmpty(classAttr))
                {
                    foreach (var className in classAttr.Split(' '))
                    {
                        if (className.Length == 0) continue;
                        if (_classIndex!.TryGetValue(className, out var classRules))
                            foreach (var indexed in classRules)
                                Collect(indexed);
                    }
                }

                var idAttr = node.GetAttribute("id");
                if (!string.IsNullOrEmpty(idAttr) && _idIndex!.TryGetValue(idAttr, out var idRules))
                {
                    foreach (var indexed in idRules)
                        Collect(indexed);
                }
            }

            // Stable sort by specificity, tie-broken by DocumentOrder (true source order, including
            // across the plain-rule/@media boundary, assigned once up front by IndexRules) - equal-
            // specificity rules keep true document order for correct source-order tiebreaking.
            // GetMatchedSpecificity's own ListSelector-alternative resolution calls the ordinary
            // DoesSelectorMatch, which (like the main Collect filter above) can't correctly identify
            // which alternative of a mixed list actually matched via ::first-line - firstLineOnly uses
            // the selector's own overall specificity instead, a documented simplification for the rare
            // case of multiple ::first-line rules of equal origin/importance whose relative order
            // depends on a mixed-list specificity nuance.
            return matched
                .OrderBy(indexed => firstLineOnly ? indexed.Rule.Selector.Specificity : GetMatchedSpecificity(indexed.Rule.Selector, node))
                .ThenBy(indexed => indexed.DocumentOrder)
                .Select(indexed => indexed.Rule);
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
        private static Priority GetMatchedSpecificity(ISelector selector, ICssDomNode? node)
        {
            if (selector is ListSelector list)
            {
                return list
                    .Where(s => DoesSelectorMatch(s, node))
                    .Select(s => s.Specificity)
                    .DefaultIfEmpty(Priority.Zero)
                    .Max();
            }

            return selector.Specificity;
        }

        private static bool DoesSelectorMatch(ISelector selector, ICssDomNode? node)
        {
            return selector switch
            {
                // The universal selector must match any REAL element, never PeachPDF's own synthetic
                // root wrapper box (DomParser.GenerateCssTree's "root" - a container above the actual
                // parsed <html> element, not itself part of the document; see its own doc comment).
                // Without the IsRoot exclusion, a descendant-combinator walk that reaches past <html>
                // (e.g. evaluating "* html X" right-to-left: X, then html, then "*" above html) finds
                // this synthetic wrapper as html's ParentBox and matches it unconditionally - making
                // "* html X" (the classic quirks-mode-only hack, which must NEVER match in a
                // standards-mode-only engine like this one, since real browsers have no element above
                // <html> for "*" to match) incorrectly match. Acid2's own "* html .parser" rule
                // exercises exactly this.
                AllSelector => node is { IsRoot: false },
                ListSelector listSelector => DoesSelectorMatch(listSelector, node),
                TypeSelector typeSelector => DoesSelectorMatch(typeSelector, node),
                ComplexSelector complexSelector => DoesSelectorMatch(complexSelector, node),
                CompoundSelector compoundSelector => DoesSelectorMatch(compoundSelector, node),
                PseudoElementSelector pseudoElementSelector => DoesSelectorMatch(pseudoElementSelector, node),
                PseudoClassSelector pseudoClassSelector => DoesSelectorMatch(pseudoClassSelector, node),
                AttrMatchSelector attrMatchSelector => DoesSelectorMatch(attrMatchSelector, node),
                ClassSelector classSelector => DoesSelectorMatch(classSelector, node),
                IdSelector idSelector => DoesSelectorMatch(idSelector, node),
                AttrAvailableSelector attrAvailableSelector => DoesSelectorMatch(attrAvailableSelector, node),
                AttrContainsSelector attrContainsSelector => DoesSelectorMatch(attrContainsSelector, node),
                AttrListSelector attrListSelector => DoesSelectorMatch(attrListSelector, node),
                AttrBeginsSelector attrBeginsSelector => DoesSelectorMatch(attrBeginsSelector, node),
                AttrEndsSelector attrEndsSelector => DoesSelectorMatch(attrEndsSelector, node),
                AttrHyphenSelector attrHyphenSelector => DoesSelectorMatch(attrHyphenSelector, node),
                ChildSelector childSelector => DoesSelectorMatch(childSelector, node),
                OnlyChildSelector onlyChildSelector => DoesSelectorMatch(onlyChildSelector, node),
                OnlyOfTypeSelector onlyOfTypeSelector => DoesSelectorMatch(onlyOfTypeSelector, node),
                NotSelector notSelector => DoesSelectorMatch(notSelector, node),
                MatchesSelector matchesSelector => DoesSelectorMatch(matchesSelector, node),
                HasSelector hasSelector => DoesSelectorMatch(hasSelector, node),
                _ => false
            };
        }

        /// <summary>The nearest ancestor that is an element node (has a <see cref="ICssDomNode.TagName"/>), skipping anonymous/text nodes - the node-agnostic analogue of <c>DomUtils.GetNearestParentElementBox</c>.</summary>
        private static ICssDomNode? GetNearestParentElement(ICssDomNode node)
        {
            var parent = node.Parent;
            while (parent is not null)
            {
                if (parent.TagName is not null) return parent;
                parent = parent.Parent;
            }
            return null;
        }

        private static bool DoesSelectorMatch(ListSelector listSelector, ICssDomNode? node)
        {
            return listSelector.Any(selector => DoesSelectorMatch(selector, node));
        }

        /// <summary>
        /// Pure (non-mutating) check: does an alternative within <paramref name="selector"/> whose
        /// subject is a <c>::first-line</c> pseudo-element match <paramref name="box"/> on its
        /// non-pseudo-element part? Used by <c>DomParser</c> to gather exactly the declarations that
        /// apply to <c>box::first-line</c> - deliberately does NOT dispatch through the ordinary
        /// <see cref="DoesSelectorMatch(CompoundSelector, ICssDomNode?)"/>/
        /// <see cref="DoesSelectorMatch(ComplexSelector, ICssDomNode?)"/> entry points, since those
        /// synthesize <c>::before</c>/<c>::after</c>/<c>::marker</c> boxes as a side effect when a
        /// compound selector's subject ends in one of those (different) pseudo-elements - this method
        /// must never trigger that, so it re-derives the "match ignoring the trailing pseudo-element"
        /// check itself instead of calling the stateful compound matcher.
        /// </summary>
        /// <remarks>
        /// Ancestor-combinator selectors (e.g. <c>article p::first-line</c>) are matched more loosely
        /// here than a dedicated matcher would: for a <see cref="ComplexSelector"/> subject, this
        /// trusts that the rule already appears in the caller's pre-matched rule list (from
        /// <see cref="GetUserAgentStyleRules"/>/<see cref="GetAuthorStyleRules"/>) rather than
        /// re-verifying the ancestor chain itself, and does not special-case a <see cref="ListSelector"/>
        /// alternative whose *other* (non-first-line) members matched a different element than
        /// <paramref name="box"/>. Both are accepted, narrow simplifications for what is already an
        /// uncommon combination (::first-line qualified by an ancestor combinator, or mixed into a
        /// comma-list alongside unrelated selectors) - a plain <c>selector::first-line</c> or
        /// <c>selector1, selector2::first-line</c> (the overwhelming majority of real usage) is matched
        /// with full precision via the <see cref="CompoundSelector"/> case below.
        /// </remarks>
        internal static bool MatchesAsFirstLineSelector(ISelector selector, CssBox box)
        {
            switch (selector)
            {
                case ListSelector list:
                    return list.Any(s => MatchesAsFirstLineSelector(s, box));

                case ComplexSelector complex:
                    var lastSegmentSelector = complex.LastOrDefault().Selector;
                    return lastSegmentSelector is not null && HasFirstLineSubject(lastSegmentSelector);

                case CompoundSelector compound:
                    return HasFirstLineSubject(compound) &&
                           compound.Where(x => x is not PseudoElementSelector).All(s => DoesSelectorMatch(s, box));

                default:
                    return false;
            }
        }

        private static bool HasFirstLineSubject(ISelector selector) => selector switch
        {
            CompoundSelector compound => compound.LastOrDefault() is PseudoElementSelector { Name: CssConstants.FirstLine },
            PseudoElementSelector pseudo => pseudo.Name == CssConstants.FirstLine,
            _ => false
        };

        /// <summary>
        /// Does <paramref name="selector"/> (a single reversed-chain item's compound, as seen by
        /// <see cref="DoesSelectorMatch(ComplexSelector, ICssDomNode?)"/>) end in a pseudo-element - i.e.
        /// is it the same structural shape that makes <see cref="DoesSelectorMatch(CompoundSelector, ICssDomNode?)"/>
        /// take its <c>box.IsPseudoElement</c> branch when tested against an existing pseudo box?
        /// </summary>
        private static bool EndsWithPseudoElement(ISelector selector) => selector switch
        {
            CompoundSelector compound => compound.LastOrDefault() is PseudoElementSelector,
            PseudoElementSelector => true,
            _ => false
        };

        private static bool DoesSelectorMatch(CompoundSelector compoundSelector, ICssDomNode? node)
        {
            if (node is null)
            {
                return false;
            }

            var lastSelector = compoundSelector.Last();

            // Structural pseudo-classes (ChildSelector subtypes, OnlyChildSelector, OnlyOfTypeSelector)
            // are matched against `node` itself here, same as every other compound member - their own
            // DoesSelectorMatch overload re-derives sibling scope from `node` as needed, so no special
            // handling is required for them beyond the plain path below.
            if (lastSelector is not PseudoElementSelector)
                return compoundSelector.All(selector => DoesSelectorMatch(selector, node));

            // Pseudo-elements (::before/::after/::marker/::first-letter) exist only in the HTML box tree
            // and the synthesis below mutates it; an SVG node never has one, so a pseudo-element compound
            // simply never matches an SVG node.
            if (node is not CssBox box)
                return false;

            if (lastSelector is PseudoElementSelector pseudoElementSelector)
            {
                var referenceBox = box.IsFirstLetterPseudoElement ? box.FirstLetterOriginatingBox
                    : box.IsPseudoElement ? box.ParentBox : box;

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
                    case CssConstants.Before when !box.Boxes.Any(b => b.IsBeforePseudoElement):
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
                    case CssConstants.After when !box.Boxes.Any(b => b.IsAfterPseudoElement):
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
                    // Same Remove-then-Insert synthesis pattern as Before/After above, and the
                    // same "only create it once, even if matched again by a rule from a later
                    // cascade origin/phase" guard - a box already present here just gets picked
                    // up normally by that later phase's own cascade, layering its declarations on
                    // top per the usual UA/author/inline precedence rather than creating a sibling
                    // duplicate. No "is this actually a list-item" gate here (unlike the full CSS
                    // Lists spec) - which elements this fires for is left entirely to the selector
                    // itself (e.g. the UA stylesheet's "li::marker" rule already only ever matches
                    // real <li> elements via its own type-selector part, the same way "li::before"
                    // would). Gating on box.Display == list-item here doesn't work: rule matching
                    // for a whole cascade pass happens before any of that same pass's declarations
                    // (including "li { display: list-item }" itself) are applied, so Display isn't
                    // reliably resolved yet at match time. Unlike Before/After, this only covers the
                    // common tag-selector-matched case (e.g. the UA "li::marker" rule) - an element
                    // that only reaches Display: list-item via computed style (not matched by any
                    // ::marker selector) still gets one too, via DomParser's separate post-cascade
                    // EnsureListItemMarkers pass, since selector matching can't key off a computed
                    // Display value. See CssBoxMarker for how the marker box owns its own content
                    // resolution, sizing, and painting from here on.
                    case CssConstants.Marker when !box.Boxes.Any(b => b.IsMarkerPseudoElement):
                        {
                            var markerPseudoBox = new CssBoxMarker(box);

                            markerPseudoBox.InheritStyle(box);
                            box.Boxes.Remove(markerPseudoBox);
                            box.Boxes.Insert(0, markerPseudoBox);
                            break;
                        }
                    // Unlike Before/After/Marker, no box is synthesized here directly - the actual
                    // descendant-text split is deferred to a post-cascade pass (see
                    // CssBox.MatchesFirstLetterSelector's doc comment and
                    // DomParser.ApplyFirstLetterPseudoElements) since it needs to know which of this
                    // box's descendants are block-level, which isn't reliably resolved until each
                    // descendant's own cascade pass has run.
                    case CssConstants.FirstLetter:
                        box.MatchesFirstLetterSelector = true;
                        break;
                }
            }

            return false;
        }

        private static bool DoesSelectorMatch(TypeSelector typeSelector, ICssDomNode? node)
        {
            return node?.TagName is { } tagName && typeSelector.Name.Equals(tagName, node.NameComparison);
        }

        private static bool DoesSelectorMatch(ClassSelector classSelector, ICssDomNode? node)
        {
            if (node is null) return false;
            var classNames = node.GetAttribute("class");
            if (string.IsNullOrEmpty(classNames)) return false;

            return classNames.Split(' ').Any(className =>
                className.Equals(classSelector.Class, node.NameComparison));
        }

        private static bool DoesSelectorMatch(IdSelector idSelector, ICssDomNode? node)
        {
            if (node is null) return false;
            var id = node.GetAttribute("id");
            return id is not null && id.Equals(idSelector.Id, node.NameComparison);
        }

        // Attribute selectors read the value once via node.GetAttribute (case-sensitivity of the
        // attribute-name lookup is the node's own: case-insensitive for HTML/CssBox, case-sensitive for
        // SVG), then compare the value with node.NameComparison (InvariantCultureIgnoreCase for HTML -
        // byte-identical to the previous behaviour - Ordinal for SVG).
        private static bool DoesSelectorMatch(AttrAvailableSelector s, ICssDomNode? node)
        {
            return node?.GetAttribute(s.Attribute) is not null;
        }

        private static bool DoesSelectorMatch(AttrMatchSelector s, ICssDomNode? node)
        {
            if (node is null) return false;
            var value = node.GetAttribute(s.Attribute);
            return value is not null && value.Equals(s.Value, node.NameComparison);
        }

        private static bool DoesSelectorMatch(AttrListSelector s, ICssDomNode? node)
        {
            if (node is null) return false;
            var value = node.GetAttribute(s.Attribute);
            if (value is null) return false;
            return value.Split(' ').Where(x => x.Length > 0).Any(x => x.Equals(s.Value, node.NameComparison));
        }

        private static bool DoesSelectorMatch(AttrContainsSelector s, ICssDomNode? node)
        {
            if (node is null) return false;
            var value = node.GetAttribute(s.Attribute);
            return value is not null && value.Contains(s.Value, node.NameComparison);
        }

        private static bool DoesSelectorMatch(AttrBeginsSelector s, ICssDomNode? node)
        {
            if (node is null) return false;
            var value = node.GetAttribute(s.Attribute);
            return value is not null && value.StartsWith(s.Value, node.NameComparison);
        }

        private static bool DoesSelectorMatch(AttrEndsSelector s, ICssDomNode? node)
        {
            if (node is null) return false;
            var value = node.GetAttribute(s.Attribute);
            return value is not null && value.EndsWith(s.Value, node.NameComparison);
        }

        private static bool DoesSelectorMatch(AttrHyphenSelector s, ICssDomNode? node)
        {
            if (node is null) return false;
            var value = node.GetAttribute(s.Attribute);
            return value is not null && (value.Equals(s.Value, node.NameComparison)
                || value.StartsWith(s.Value + "-", node.NameComparison));
        }

        private static bool DoesSelectorMatch(PseudoClassSelector pseudoClassSelector, ICssDomNode? node)
        {
            if (pseudoClassSelector.Class == PseudoClassNames.Root)
            {
                // :root is the document's root element - <html> for HTML, or the outermost element of a
                // (standalone/inline) SVG fragment, which is any element node with no parent element.
                if (node?.TagName is null) return false;
                if (GetNearestParentElement(node) is not null) return false;
                return node is not CssBox
                    || node.TagName.Equals("html", StringComparison.OrdinalIgnoreCase);
            }

            // :link never matches an SVG node (no interaction/link state); IsClickable is CssBox-only.
            return pseudoClassSelector.Class == "link" && node is CssBox { IsClickable: true };
        }

        private static bool DoesSelectorMatch(PseudoElementSelector pseudoElementSelector, ICssDomNode? node)
        {
            // Pseudo-elements exist only in the HTML box tree; an SVG node never matches one.
            if (node is not CssBox box)
            {
                return false;
            }

            switch (pseudoElementSelector.Name)
            {
                case CssConstants.Before when box.IsBeforePseudoElement:
                case CssConstants.After when box.IsAfterPseudoElement:
                case CssConstants.Marker when box.IsMarkerPseudoElement:
                case CssConstants.FirstLetter when box.IsFirstLetterPseudoElement:
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
        private static bool DoesSelectorMatch(ChildSelector childSelector, ICssDomNode? node)
        {
            if (node?.TagName is not { } nodeTag)
            {
                return false;
            }

            switch (childSelector)
            {
                case FirstColumnSelector or LastColumnSelector:
                    return DoesColumnSelectorMatch(childSelector, node, fromEnd: childSelector is LastColumnSelector);
            }

            var parent = GetNearestParentElement(node);
            if (parent is null)
            {
                return false;
            }

            IEnumerable<ICssDomNode> siblingNodes = parent.Children.Where(b => b.TagName is not null);

            var sameTypeOnly = childSelector is FirstTypeSelector or LastTypeSelector;
            if (sameTypeOnly)
            {
                siblingNodes = siblingNodes.Where(b => b.TagName!.Equals(nodeTag, node.NameComparison));
            }

            // CSS4 "of <selector>" extension (:nth-child(An+B of S)/:nth-last-child(An+B of S)).
            // Kind defaults to AllSelector (matches unconditionally) whenever no "of" clause was
            // written, and the parser only ever allows a non-default Kind on FirstChildSelector/
            // LastChildSelector - so this filter is always a no-op for the four other subtypes.
            // Applying it unconditionally is simpler than special-casing which subtypes can have it.
            siblingNodes = siblingNodes.Where(b => DoesSelectorMatch(childSelector.Kind, b));

            var siblings = siblingNodes.ToList();
            var index = siblings.IndexOf(node);
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
        private static bool DoesColumnSelectorMatch(ChildSelector childSelector, ICssDomNode node, bool fromEnd)
        {
            var row = node.Parent;
            if (row is null)
            {
                return false;
            }

            var cellsInRow = row.Children.Where(b => b.TagName is not null).ToList();
            var columnIndex = 0;
            var totalColumns = 0;
            var found = false;

            foreach (var cell in cellsInRow)
            {
                if (cell.Equals(node))
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

            var boxSpan = GetColSpan(node);
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
        private static int GetColSpan(ICssDomNode node)
        {
            var value = node.GetAttribute("colspan") ?? "1";
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

        private static bool DoesSelectorMatch(OnlyChildSelector _, ICssDomNode? node)
        {
            if (node?.TagName is null)
            {
                return false;
            }

            var parent = GetNearestParentElement(node);
            return parent is not null && parent.Children.Count(b => b.TagName is not null) == 1;
        }

        private static bool DoesSelectorMatch(OnlyOfTypeSelector _, ICssDomNode? node)
        {
            if (node?.TagName is not { } nodeTag)
            {
                return false;
            }

            var parent = GetNearestParentElement(node);
            if (parent is null)
            {
                return false;
            }

            return parent.Children.Count(b =>
                b.TagName is not null &&
                b.TagName.Equals(nodeTag, node.NameComparison)) == 1;
        }

        private static bool DoesSelectorMatch(NotSelector notSelector, ICssDomNode? node)
        {
            return node is not null && !DoesSelectorMatch(notSelector.Inner, node);
        }

        private static bool DoesSelectorMatch(MatchesSelector matchesSelector, ICssDomNode? node)
        {
            return DoesSelectorMatch(matchesSelector.Inner, node);
        }

        private static bool DoesSelectorMatch(HasSelector hasSelector, ICssDomNode? node)
        {
            return node is not null && HasDescendantMatch(node, hasSelector.Inner);
        }

        /// <summary>
        /// Recursively searches box's descendants (at any depth) for one matching inner, short-
        /// circuiting on the first hit - mirrors the shape of DomUtils.GetBoxById/GetBoxByTagName.
        /// Backs :has()'s default (descendant) relative-selector form; leading-combinator forms
        /// (":has(&gt; x)"/"+"/"~") aren't supported - see HasSelector's doc comment.
        /// </summary>
        private static bool HasDescendantMatch(ICssDomNode node, ISelector inner)
        {
            foreach (var child in node.Children)
            {
                if (DoesSelectorMatch(inner, child)) return true;
                if (HasDescendantMatch(child, inner)) return true;
            }

            return false;
        }

        private static bool DoesSelectorMatch(ComplexSelector complexSelector, ICssDomNode? node)
        {
            var selectorsInReverse = complexSelector.Reverse().ToList();

            var isLowestItem = true;
            ICssDomNode? currentRef = node;

            foreach (var selector in selectorsInReverse)
            {
                if (selector.Selector is null) return false;

                if (isLowestItem)
                {
                    if (!DoesSelectorMatch(selector.Selector, currentRef)) return false;

                    // currentRef may itself already be a pseudo-element box (e.g. re-verifying an
                    // existing ::before against its owning selector on a later cascade pass) - when
                    // the lowest compound matched it via DoesSelectorMatch(CompoundSelector, box)'s
                    // box.IsPseudoElement branch, the match was really about currentRef's OWNER
                    // (referenceBox = currentRef.ParentBox), not currentRef's own position in the
                    // tree. Advance to that owner before walking remaining ancestor compounds -
                    // otherwise the owner gets reused as if it were ALSO a distinct ancestor level
                    // for the next compound, letting a pseudo-element compound "borrow" one extra
                    // ancestor level it was never entitled to (e.g. ".nose div div:before" would
                    // wrongly match ".nose"'s own child <div>'s ::before, since that child's owner
                    // (".nose") would satisfy both "div" AND, one level further up, get reused... -
                    // this is exactly the bug that made a bogus ::before appear on `.nose > div`
                    // instead of only on `.nose > div > div` in the Acid2 fixture).
                    if (currentRef is CssBox { IsPseudoElement: true } pseudoRef && EndsWithPseudoElement(selector.Selector))
                    {
                        currentRef = pseudoRef.ParentBox;
                    }

                    isLowestItem = false;
                    continue;
                }

                if (currentRef is null) return false;

                switch (selector.Delimiter)
                {
                    case ">":
                        currentRef = currentRef.Parent;
                        if (!DoesSelectorMatch(selector.Selector, currentRef)) return false;
                        break;

                    case " " or null:
                        currentRef = currentRef.Parent;
                        while (currentRef is not null && !DoesSelectorMatch(selector.Selector, currentRef))
                        {
                            currentRef = currentRef.Parent;
                        }
                        if (currentRef is null) return false;
                        break;

                    case "+":
                    {
                        var parent = currentRef.Parent;
                        if (parent is null) return false;
                        var siblings = parent.Children.Where(b => b.TagName is not null).ToList();
                        var idx = siblings.IndexOf(currentRef);
                        if (idx <= 0) return false;
                        currentRef = siblings[idx - 1];
                        if (!DoesSelectorMatch(selector.Selector, currentRef)) return false;
                        break;
                    }

                    case "~":
                    {
                        var parent = currentRef.Parent;
                        if (parent is null) return false;
                        var siblings = parent.Children.Where(b => b.TagName is not null).ToList();
                        var idx = siblings.IndexOf(currentRef);
                        if (idx <= 0) return false;
                        ICssDomNode? match = null;
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