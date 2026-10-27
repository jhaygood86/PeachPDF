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

using PeachBrackets.Css;
using PeachDrawing.Core;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Handlers;
using PeachBrackets.Html.Parse;
using PeachBrackets.Html.Utils;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachBrackets.Html
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
        internal CssData(RenderContext renderContext)
        {
            _renderContext = renderContext;
        }

        // The context an @supports condition is checked against - the one the stylesheets were parsed with.
        private readonly RenderContext _renderContext;

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
        // LayerName records which @layer cascade layer (CSS Cascade 5 §6.4.2) a rule belongs to
        // (null = unlayered). The layer's numeric precedence rank is resolved lazily at sort time from
        // _layerRanks - which is only known after the whole layer tree has been walked (a nested
        // sub-layer's rank depends on where its parent first appeared), so it can't be baked in here.
        private readonly record struct IndexedRule(IStyleRule Rule, bool IsUserAgent, int DocumentOrder, MediaList[]? EnclosingMedia, ContainerCondition[]? EnclosingContainers, string? LayerName, RuleSharing Sharing);

        /// <summary>
        /// What the cascade needs to know of a rule to let siblings share a computed style (see <see cref="CanShareStyleAmongSiblings"/>),
        /// worked out once when the rule is indexed.
        /// </summary>
        private sealed class RuleSharing
        {
            internal static readonly RuleSharing Plain = new(stable: true, null);

            private RuleSharing(bool stable, CompoundSelector[]? boxCreatingSubjects)
            {
                Stable = stable;
                BoxCreatingSubjects = boxCreatingSubjects;
            }

            /// <summary>Whether its selector matches every sibling of the same tag and attributes alike (<see cref="SelectorSiblingStability"/>).</summary>
            internal bool Stable { get; }

            /// <summary>The compound selectors of it that add a box to, or mark, the element they match (see <see cref="SelectorSiblingStability.CollectBoxCreatingSubjects"/>).</summary>
            internal CompoundSelector[]? BoxCreatingSubjects { get; }

            internal static RuleSharing Of(ISelector selector)
            {
                var stable = SelectorSiblingStability.IsStable(selector);

                var subjects = new List<CompoundSelector>();
                SelectorSiblingStability.CollectBoxCreatingSubjects(selector, subjects);

                return stable && subjects.Count == 0 ? Plain : new RuleSharing(stable, subjects.Count > 0 ? subjects.ToArray() : null);
            }
        }

        /// <summary>
        /// One level of an <c>@container</c> nesting chain a rule is indexed under: the rule's own
        /// declared <c>&lt;container-name&gt;</c> (null when unnamed) and its condition, either a plain
        /// size-feature <see cref="MediaList"/> (reusing <c>@media</c>'s grammar - see
        /// <see cref="ContainerRule"/>) or a <c>style()</c> query tree. Exactly one of
        /// <see cref="SizeCondition"/>/<see cref="StyleCondition"/> is non-null. Carried forward the same
        /// way <see cref="MediaList"/>[] EnclosingMedia is - deferred, per-candidate-box evaluation
        /// (<see cref="ContainerQueryMatcher"/>) rather than resolved once at index-build time, since
        /// (unlike <c>@supports</c>) neither form is a static, argument-free fact.
        /// </summary>
        internal readonly record struct ContainerCondition(string? Name, MediaList? SizeCondition, IStyleQueryCondition? StyleCondition);

        // A style rule paired with its resolved @layer rank, for the cascade's author passes (which
        // need the rank both to reverse layer order for !important and to band rules by layer for
        // revert-layer). Unlayered = UnlayeredRank.
        internal readonly record struct LayeredStyleRule(IStyleRule Rule, int LayerRank);

        private const int UnlayeredRank = int.MaxValue;

        private Dictionary<string, List<IndexedRule>>? _tagIndex;
        private Dictionary<string, List<IndexedRule>>? _classIndex;
        private Dictionary<string, List<IndexedRule>>? _idIndex;
        private List<IndexedRule>? _universalRules;

        // The rules of the indexes above that declare a property other than the purely visual ones (see AnyConsequentialRuleMatches),
        // which are few: a stylesheet's rules mostly set sizes, fonts and spacing.
        private Dictionary<string, List<ConsequentialRule>>? _consequentialTagIndex;
        private Dictionary<string, List<ConsequentialRule>>? _consequentialClassIndex;
        private Dictionary<string, List<ConsequentialRule>>? _consequentialIdIndex;
        private List<ConsequentialRule>? _consequentialUniversalRules;

        // Final @layer precedence ranks (qualified layer name -> rank; earlier/lower rank = lower
        // priority for normal declarations), computed once after the layer tree is fully walked.
        private Dictionary<string, int>? _layerRanks;

        /// <summary>True when the document declares any <c>@layer</c> cascade layer.</summary>
        internal bool HasCascadeLayers { get; private set; }

        /// <summary>
        /// True when the document declares any rule whose selector could possibly match via
        /// <see cref="MatchesAsFirstLineSelector"/> - i.e. a deliberate superset computed once, up
        /// front, by <see cref="CouldMatchAsFirstLineSelector"/> (same selector-shape switch, minus the
        /// per-box <see cref="DoesSelectorMatch(ISelector, ICssDomNode?)"/> conjunct
        /// <see cref="MatchesAsFirstLineSelector"/> applies in its <see cref="CompoundSelector"/> case).
        /// False here proves no box can have any <c>::first-line</c> rules, so
        /// <see cref="GetFirstLineStyleRules"/> would always return empty; true does not prove the
        /// converse. See <c>DomParser.ResolveFirstLineStyle</c>, which uses this to skip its two
        /// candidate-gathering passes entirely on the (overwhelmingly common) documents with no
        /// <c>::first-line</c> rule at all.
        /// </summary>
        internal bool HasFirstLineRules { get; private set; }

        private int RankOf(string? layerName) =>
            layerName is not null && _layerRanks!.TryGetValue(layerName, out var rank) ? rank : UnlayeredRank;

        private void EnsureIndex()
        {
            if (_universalRules is not null) return;

            // Ordinal, not InvariantCulture. These three indexes are hashed once per
            // candidate tag/class/id on every box, and a culture-aware comparer routes every one of
            // those hashes through ICU. HTML/CSS identifiers are
            // ASCII and the spec matches them ASCII case-insensitively, so ordinal is both faster and
            // closer to the rule than Unicode case folding is.
            var tagIndex = new Dictionary<string, List<IndexedRule>>(StringComparer.OrdinalIgnoreCase);
            var classIndex = new Dictionary<string, List<IndexedRule>>(StringComparer.OrdinalIgnoreCase);
            var idIndex = new Dictionary<string, List<IndexedRule>>(StringComparer.OrdinalIgnoreCase);
            var universal = new List<IndexedRule>();
            var keys = new List<(SelectorBucketKind Kind, string Key)>();
            var layerRegistry = new LayerRegistry();
            var order = 0;
            var hasFirstLineRules = false;

            foreach (var stylesheet in Stylesheets)
            {
                IndexRules(stylesheet.Rules, stylesheet.IsUserAgent, null, null, null, tagIndex, classIndex, idIndex, universal, keys, layerRegistry, ref order, ref hasFirstLineRules);
            }

            _layerRanks = layerRegistry.ComputeRanks();
            HasCascadeLayers = _layerRanks.Count > 0;
            HasFirstLineRules = hasFirstLineRules;
            _tagIndex = tagIndex;
            _classIndex = classIndex;
            _idIndex = idIndex;
            _universalRules = universal;
        }

        /// <summary>
        /// Discards the lazily-built selector index so it is rebuilt from the full
        /// <see cref="Stylesheets"/> list on the next query. Call after appending a stylesheet to an
        /// already-queried <see cref="CssData"/> (e.g. <c>PeachPdfCssContent.AddStyleSheet</c>).
        /// <see cref="EnsureIndex"/> guards on <see cref="_universalRules"/>, so clearing it is enough
        /// to force a full rebuild of every index (<see cref="_tagIndex"/>/<see cref="_classIndex"/>/
        /// <see cref="_idIndex"/>/<see cref="_universalRules"/>/<see cref="_layerRanks"/>).
        /// </summary>
        internal void InvalidateIndex()
        {
            _universalRules = null;
            _consequentialUniversalRules = null;
        }

        /// <summary>
        /// Builds the <c>@layer</c> tree while stylesheets are walked and, once complete, assigns each
        /// layer a precedence rank that respects nesting (CSS Cascade 5 §6.4.2): a parent layer's whole
        /// subtree is contiguous and its sub-layers are ordered by first appearance <em>within that
        /// parent</em>, so <c>@layer a.b; @layer c; @layer a.d;</c> ranks as <c>a &lt; a.b &lt; a.d &lt; c</c>
        /// rather than interleaving <c>c</c> between <c>a</c>'s sub-layers. A layer's own (direct) rules
        /// carry the layer's own name and sort <em>before</em> its nested sub-layers (a shorter path is a
        /// prefix of a longer one, and the prefix ranks lower/earlier).
        /// </summary>
        private sealed class LayerRegistry
        {
            // Qualified layer name -> its path of first-appearance indices from the root down (one entry
            // per name segment). Lexicographic comparison of these paths yields the nested layer order.
            private readonly Dictionary<string, List<int>> _pathKeys = new(StringComparer.Ordinal);
            // Parent qualified name ("" = top level) -> next child index to hand out (first-appearance order).
            private readonly Dictionary<string, int> _childCount = new(StringComparer.Ordinal);
            private int _anonCounter;

            /// <summary>Registers an anonymous <c>@layer { }</c> as a distinct node and returns its unique key.</summary>
            public string RegisterAnonymous(string? parent)
            {
                // A leading space can't occur in a real custom-ident layer name, so this never collides.
                var qualified = QualifyLayer(parent, " anon" + _anonCounter++);
                Register(qualified);
                return qualified;
            }

            /// <summary>
            /// Registers a (possibly dotted) qualified layer name, first establishing each ancestor
            /// prefix - <c>@layer a.b</c> implicitly declares <c>a</c> too (CSS Cascade 5 §6.2).
            /// </summary>
            public void Register(string qualified)
            {
                var segments = qualified.Split('.');
                var parent = string.Empty;
                var prefix = string.Empty;
                List<int> path = [];

                for (var i = 0; i < segments.Length; i++)
                {
                    prefix = i == 0 ? segments[0] : prefix + "." + segments[i];
                    if (_pathKeys.TryGetValue(prefix, out var existing))
                    {
                        path = existing;
                    }
                    else
                    {
                        var idx = _childCount.TryGetValue(parent, out var count) ? count : 0;
                        _childCount[parent] = idx + 1;
                        path = [.. path, idx];
                        _pathKeys[prefix] = path;
                    }
                    parent = prefix;
                }
            }

            public Dictionary<string, int> ComputeRanks()
            {
                var names = _pathKeys.Keys.ToList();
                names.Sort((a, b) => ComparePath(_pathKeys[a], _pathKeys[b]));
                var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
                for (var i = 0; i < names.Count; i++)
                    ranks[names[i]] = i;
                return ranks;
            }

            private static int ComparePath(List<int> a, List<int> b)
            {
                var shared = Math.Min(a.Count, b.Count);
                for (var i = 0; i < shared; i++)
                {
                    if (a[i] != b[i]) return a[i].CompareTo(b[i]);
                }
                // A prefix (a parent layer's own direct rules) sorts before its longer extensions (its
                // nested sub-layers). This resolves sibling contiguity correctly (the #237 gap); the exact
                // CSS Cascade 5 §6.2 treatment of a parent's direct rules interleaved with its own
                // sub-layers is a documented simplification (see docs / the tracked follow-up).
                return a.Count.CompareTo(b.Count);
            }
        }

        /// <summary>
        /// Walks a rule list in true document order, recursing into <c>@media</c>/<c>@container</c>
        /// blocks (any nesting depth) in place, and buckets every style rule found - assigning each an
        /// ever-increasing <see cref="IndexedRule.DocumentOrder"/> and recording the chain of
        /// enclosing media/container conditions it's nested under, if any.
        /// </summary>
        private void IndexRules(
            IEnumerable<IRule> rules,
            bool isUserAgent,
            MediaList[]? enclosingMedia,
            ContainerCondition[]? enclosingContainers,
            string? currentLayer,
            Dictionary<string, List<IndexedRule>> tagIndex,
            Dictionary<string, List<IndexedRule>> classIndex,
            Dictionary<string, List<IndexedRule>> idIndex,
            List<IndexedRule> universal,
            List<(SelectorBucketKind Kind, string Key)> keys,
            LayerRegistry layerRegistry,
            ref int order,
            ref bool hasFirstLineRules)
        {
            foreach (var rule in rules)
            {
                switch (rule)
                {
                    case IStyleRule styleRule:
                        var indexedRule = new IndexedRule(styleRule, isUserAgent, order++, enclosingMedia, enclosingContainers, currentLayer, RuleSharing.Of(styleRule.Selector));

                        if (!hasFirstLineRules && CouldMatchAsFirstLineSelector(styleRule.Selector))
                            hasFirstLineRules = true;

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

                        // CSS Nesting: a nested rule's selector is already resolved to an absolute
                        // selector against its parent, so index it like any other rule - in the SAME
                        // media/@layer context and right after the parent in document order (so it wins
                        // ties by source order, matching its textual position after the parent).
                        if (styleRule.NestedRules.Count > 0)
                            IndexRules(styleRule.NestedRules, isUserAgent, enclosingMedia, enclosingContainers, currentLayer, tagIndex, classIndex, idIndex, universal, keys, layerRegistry, ref order, ref hasFirstLineRules);
                        break;

                    case IMediaRule mediaRule:
                        var nestedMedia = enclosingMedia is null
                            ? [mediaRule.Media]
                            : (MediaList[])[.. enclosingMedia, mediaRule.Media];
                        IndexRules(mediaRule.Rules, isUserAgent, nestedMedia, enclosingContainers, currentLayer, tagIndex, classIndex, idIndex, universal, keys, layerRegistry, ref order, ref hasFirstLineRules);
                        break;

                    // @layer statement (`@layer a, b, c;`) only declares layer order — register each
                    // name so later block-form rules inherit the declared first-appearance order.
                    case LayerStatementRule layerStatement:
                        foreach (var name in layerStatement.Names)
                            layerRegistry.Register(QualifyLayer(currentLayer, name));
                        break;

                    // @layer block (`@layer name { rules }`) — its rules belong to that layer.
                    case ILayerRule layerRule:
                        var qualified = string.IsNullOrEmpty(layerRule.Name)
                            // Each anonymous layer is its own distinct layer; give it a unique key.
                            ? layerRegistry.RegisterAnonymous(currentLayer)
                            : QualifyLayer(currentLayer, layerRule.Name);
                        if (!string.IsNullOrEmpty(layerRule.Name))
                            layerRegistry.Register(qualified);
                        IndexRules(layerRule.Rules, isUserAgent, enclosingMedia, enclosingContainers, qualified, tagIndex, classIndex, idIndex, universal, keys, layerRegistry, ref order, ref hasFirstLineRules);
                        break;

                    // @supports: the condition is invariant (IConditionFunction.Check() takes only the render context - unlike @media, which is re-evaluated per query since media features
                    // can vary per call, see MediaQueryMatcher.Matches below), so it's resolved once,
                    // right here, rather than carried forward as per-query state the way EnclosingMedia
                    // is. A true condition indexes the nested rules normally (inheriting the surrounding
                    // media/@layer context, exactly like an IMediaRule with no conditions); a false one
                    // contributes nothing - the same "block is a no-op" outcome as before this was wired
                    // up, just decided by the real condition now instead of unconditionally.
                    case ISupportsRule supportsRule:
                        if (supportsRule.Condition.Check(_renderContext))
                            IndexRules(supportsRule.Rules, isUserAgent, enclosingMedia, enclosingContainers, currentLayer, tagIndex, classIndex, idIndex, universal, keys, layerRegistry, ref order, ref hasFirstLineRules);
                        break;

                    // @container: truthiness depends on the nearest matched element's own container
                    // size/style, a per-candidate-box fact - unconditionally descend and carry the
                    // condition forward as deferred state, exactly like @media's EnclosingMedia chain
                    // (see ContainerQueryMatcher.Matches, consulted lazily in GatherMatchedRules.Collect).
                    case IContainerRule containerRule:
                        var containerName = string.IsNullOrEmpty(containerRule.Name) ? null : containerRule.Name;
                        // ContainerRule.Media is a non-null (but empty) MediaList even on the style()
                        // branch - AppendChild(new MediaList(...)) runs unconditionally in its constructor
                        // - so SizeCondition must be nulled out explicitly here to keep this record's own
                        // documented "exactly one of SizeCondition/StyleCondition is set" invariant true.
                        var condition = new ContainerCondition(
                            containerName,
                            containerRule.StyleCondition is null ? containerRule.Media : null,
                            containerRule.StyleCondition);
                        var nestedContainers = enclosingContainers is null
                            ? [condition]
                            : (ContainerCondition[])[.. enclosingContainers, condition];
                        IndexRules(containerRule.Rules, isUserAgent, enclosingMedia, nestedContainers, currentLayer, tagIndex, classIndex, idIndex, universal, keys, layerRegistry, ref order, ref hasFirstLineRules);
                        break;
                }
            }
        }

        private static string QualifyLayer(string? parent, string name) =>
            string.IsNullOrEmpty(parent) ? name : parent + "." + name;

        /// <summary>
        /// Enumerates every rule in every stylesheet, descending into the grouping at-rules whose
        /// contents participate directly in the document (<c>@layer</c>/<c>@media</c>/<c>@container</c>/a
        /// true-condition <c>@supports</c>) so an <c>@font-face</c>/<c>@property</c>/<c>@page</c>
        /// nested inside one is still found — including one guarded by <c>@supports</c>, per CSS
        /// Conditional Rules 3/4 §3 (any at-rule may appear inside a supports-rule's block).
        /// <c>@media</c>/<c>@container</c> are descended unconditionally, same as <c>@layer</c> - their
        /// per-candidate-box truthiness has no bearing on whether a font/registration nested inside is
        /// collected (see the matching <c>IMediaRule</c> case's identical unconditional descent). Source
        /// order is preserved (a grouping rule's children are yielded at the grouping rule's own
        /// position), so last-declared-wins collection stays correct.
        /// </summary>
        /// <param name="media">When given, an <c>@media</c> block is descended only if its query matches this context, for a caller whose
        /// rules apply document-wide (<c>@keyframes</c>) and so have no candidate box to evaluate the query against later.</param>
        internal IEnumerable<IRule> EnumerateRulesRecursive(MediaQueryContext? media = null)
        {
            foreach (var stylesheet in Stylesheets)
            {
                foreach (var rule in FlattenRules(stylesheet.Rules, media))
                    yield return rule;
            }
        }

        internal IEnumerable<IRule> FlattenRules(IEnumerable<IRule> rules, MediaQueryContext? media = null)
        {
            foreach (var rule in rules)
            {
                yield return rule;

                switch (rule)
                {
                    case ILayerRule layerRule:
                        foreach (var child in FlattenRules(layerRule.Rules, media))
                            yield return child;
                        break;
                    case IMediaRule mediaRule:
                        if (media is not { } mediaContext || MediaQueryMatcher.Matches([mediaRule.Media], mediaContext))
                            foreach (var child in FlattenRules(mediaRule.Rules, media))
                                yield return child;
                        break;
                    case IContainerRule containerRule:
                        foreach (var child in FlattenRules(containerRule.Rules, media))
                            yield return child;
                        break;
                    case ISupportsRule supportsRule:
                        if (supportsRule.Condition.Check(_renderContext))
                            foreach (var child in FlattenRules(supportsRule.Rules, media))
                                yield return child;
                        break;
                }
            }
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
                    var last = complexSelector.Subject;
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
        /// <param name="renderContext">Platform adapter</param>
        /// <param name="stylesheet">the stylesheet source to parse</param>
        /// <param name="combineWithDefault">true - combine the parsed css data with default css data, false - return only the parsed css data</param>
        /// <returns>the parsed css data</returns>
        public static async Task<CssData> Parse(RenderContext renderContext, string stylesheet, bool combineWithDefault = true)
        {
            var parser = new CssParser(renderContext, null);
            return await parser.ParseStyleSheet(stylesheet, combineWithDefault);
        }

        /// <summary>
        /// Genuinely zero-copy counterpart of <see cref="Parse(RenderContext, string, bool)"/> - see
        /// the parser's own remarks for why
        /// <see cref="ReadOnlyMemory{T}"/>, not <see cref="ReadOnlySpan{T}"/>, is what makes this possible.
        /// </summary>
        /// <param name="renderContext">Platform adapter</param>
        /// <param name="stylesheet">the stylesheet source to parse</param>
        /// <param name="combineWithDefault">true - combine the parsed css data with default css data, false - return only the parsed css data</param>
        /// <returns>the parsed css data</returns>
        public static async Task<CssData> Parse(RenderContext renderContext, ReadOnlyMemory<char> stylesheet, bool combineWithDefault = true)
        {
            var parser = new CssParser(renderContext, null);
            return await parser.ParseStyleSheet(stylesheet, combineWithDefault);
        }

        internal IEnumerable<IStyleRule> GetStyleRules(MediaQueryContext media, ICssDomNode node, ContainerQuerySizes? containerSizes = null) =>
            GetStyleRulesByOrigin(media, node, userAgentOnly: null, containerSizes: containerSizes);

        internal List<IStyleRule> GetUserAgentStyleRules(MediaQueryContext media, ICssDomNode node, ContainerQuerySizes? containerSizes = null) =>
            GetStyleRulesByOrigin(media, node, userAgentOnly: true, containerSizes: containerSizes);

        internal IEnumerable<IStyleRule> GetAuthorStyleRules(MediaQueryContext media, ICssDomNode node, ContainerQuerySizes? containerSizes = null) =>
            GetStyleRulesByOrigin(media, node, userAgentOnly: false, containerSizes: containerSizes);

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
        internal IEnumerable<IStyleRule> GetFirstLineStyleRules(MediaQueryContext media, CssBox box, bool userAgentOnly, ContainerQuerySizes? containerSizes = null) =>
            GetStyleRulesByOrigin(media, box, userAgentOnly, firstLineOnly: true, containerSizes: containerSizes);

        /// <summary>
        /// Author style rules for the HTML cascade, in BOTH the normal-declaration order and the
        /// <c>!important</c> order, each rule paired with its resolved <c>@layer</c> rank. Candidate
        /// matching runs once; the two orderings differ only in how layer rank participates:
        /// <list type="bullet">
        /// <item>Normal: ascending rank (unlayered = <see cref="UnlayeredRank"/> last, so it wins).</item>
        /// <item>Important: descending rank (unlayered first/loses, earliest layer last/wins) — the
        /// CSS Cascade 5 §6.4.2 layer-order reversal for <c>!important</c>. Specificity and source order
        /// within a layer are NOT reversed.</item>
        /// </list>
        /// When the document declares no cascade layers the two lists are identical (byte-for-byte the
        /// pre-<c>@layer</c> ordering). The per-rule rank also lets the cascade band rules by layer for
        /// <c>revert-layer</c>. Only the HTML cascade (<c>DomParser.CascadeApplyStyles</c>) needs this;
        /// UA/first-line/SVG paths keep <see cref="GetStyleRulesByOrigin"/>.
        /// </summary>
        internal (List<LayeredStyleRule> Normal, List<LayeredStyleRule> Important) GetAuthorStyleRulesForCascade(MediaQueryContext media, ICssDomNode node, ContainerQuerySizes? containerSizes = null)
        {
            var matched = GatherMatchedRules(media, node, userAgentOnly: false, firstLineOnly: false, containerSizes);

            List<LayeredStyleRule> Sort(bool importantOrder) => SortForCascade(matched, importantOrder, node);

            var normal = Sort(importantOrder: false);
            // With no layers every rank is UnlayeredRank, so the important order is identical - reuse the
            // same list to skip the second sort (and keep the no-@layer path allocation-for-allocation
            // the same as before this feature).
            var important = HasCascadeLayers ? Sort(importantOrder: true) : normal;
            return (normal, important);
        }

        private struct CascadeKey
        {
            public IStyleRule Rule;
            public int Rank;
            public Priority Specificity;
            public int DocumentOrder;
        }

        [ThreadStatic]
        private static CascadeKey[]? _cascadeKeys;

        /// <summary>
        /// Orders the matched rules for the cascade: layer rank (reversed for <c>!important</c>), then specificity, then document
        /// order. Plain keyed insertion sort over a reused buffer - a box usually matches a handful of rules, and the LINQ
        /// <c>OrderBy/ThenBy/Select/ToList</c> chain this replaces allocated roughly a dozen objects per box. The key is total
        /// (document order is unique per rule), so the result is the same as the stable sort it replaces.
        /// </summary>
        private List<LayeredStyleRule> SortForCascade(List<IndexedRule> matched, bool importantOrder, ICssDomNode node)
        {
            var count = matched.Count;
            var result = new List<LayeredStyleRule>(count);
            if (count == 0) return result;

            var keys = SortKeys(matched, importantOrder, node, firstLineOnly: false);

            for (var i = 0; i < count; i++)
            {
                result.Add(new LayeredStyleRule(keys[i].Rule, keys[i].Rank));
                keys[i].Rule = null!;
            }

            return result;
        }

        /// <summary>
        /// The same ordering for the user-agent, <c>::first-line</c> and plain-author queries, which want just the rules.
        /// <paramref name="firstLineOnly"/> orders by each selector's own specificity instead of the matched alternative's.
        /// </summary>
        private List<IStyleRule> SortRules(List<IndexedRule> matched, ICssDomNode node, bool firstLineOnly)
        {
            var count = matched.Count;
            var result = new List<IStyleRule>(count);
            if (count == 0) return result;

            var keys = SortKeys(matched, importantOrder: false, node, firstLineOnly);

            for (var i = 0; i < count; i++)
            {
                result.Add(keys[i].Rule);
                keys[i].Rule = null!;
            }

            return result;
        }

        private CascadeKey[] SortKeys(List<IndexedRule> matched, bool importantOrder, ICssDomNode node, bool firstLineOnly)
        {
            var count = matched.Count;
            var keys = _cascadeKeys;
            if (keys is null || keys.Length < count) _cascadeKeys = keys = new CascadeKey[Math.Max(count, 16)];

            for (var i = 0; i < count; i++)
            {
                var indexed = matched[i];
                var key = new CascadeKey
                {
                    Rule = indexed.Rule,
                    Rank = RankOf(indexed.LayerName),
                    Specificity = firstLineOnly
                        ? indexed.Rule.Selector.Specificity
                        : count == 1 ? Priority.Zero : GetMatchedSpecificity(indexed.Rule.Selector, node),
                    DocumentOrder = indexed.DocumentOrder,
                };

                var at = i;
                while (at > 0 && Precedes(in key, in keys[at - 1], importantOrder))
                {
                    keys[at] = keys[at - 1];
                    at--;
                }

                keys[at] = key;
            }

            return keys;
        }

        private static bool Precedes(in CascadeKey a, in CascadeKey b, bool importantOrder)
        {
            if (a.Rank != b.Rank) return importantOrder ? a.Rank > b.Rank : a.Rank < b.Rank;

            var bySpecificity = a.Specificity.CompareTo(b.Specificity);
            if (bySpecificity != 0) return bySpecificity < 0;

            return a.DocumentOrder < b.DocumentOrder;
        }

        private List<IStyleRule> GetStyleRulesByOrigin(MediaQueryContext media, ICssDomNode node, bool? userAgentOnly, bool firstLineOnly = false, ContainerQuerySizes? containerSizes = null)
        {
            var matched = GatherMatchedRules(media, node, userAgentOnly, firstLineOnly, containerSizes);

            // Stable sort by specificity, tie-broken by DocumentOrder (true source order, including
            // across the plain-rule/@media boundary, assigned once up front by IndexRules) - equal-
            // specificity rules keep true document order for correct source-order tiebreaking.
            // GetMatchedSpecificity's own ListSelector-alternative resolution calls the ordinary
            // DoesSelectorMatch, which (like the main Collect filter above) can't correctly identify
            // which alternative of a mixed list actually matched via ::first-line - firstLineOnly uses
            // the selector's own overall specificity instead, a documented simplification for the rare
            // case of multiple ::first-line rules of equal origin/importance whose relative order
            // depends on a mixed-list specificity nuance.
            // @layer cascade-layer precedence (CSS Cascade 5 §6.4.2) sorts ahead of specificity for
            // normal declarations: a rule in a lower-ranked layer loses to any rule in a higher-ranked
            // layer (or an unlayered rule) regardless of specificity. Within one layer, specificity
            // then true document order break ties. (The !important layer-order reversal is handled by
            // GetAuthorStyleRulesForCascade for the author passes; UA/first-line have no layers.)
            return SortRules(matched, node, firstLineOnly);
        }

        /// <summary>
        /// Gathers the candidate rules matching <paramref name="node"/> (via the tag/class/id/universal
        /// index buckets), filtered by origin and enclosing <c>@media</c>, deduped. Unsorted — callers
        /// apply their own cascade ordering. Extracted so the normal and <c>!important</c> author
        /// orderings can share one matching pass (see <see cref="GetAuthorStyleRulesForCascade"/>).
        /// </summary>
        private List<IndexedRule> GatherMatchedRules(MediaQueryContext media, ICssDomNode node, bool? userAgentOnly, bool firstLineOnly, ContainerQuerySizes? containerSizes = null)
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
                if (!MediaQueryMatcher.Matches(indexed.EnclosingMedia, media)) return;
                // @container is HTML-only (no query-container concept for SVG) - an SVG node (never a
                // CssBox) permissively skips the check the same way a rule with no @container nesting
                // does, since ContainerQueryMatcher.Matches needs a CssBox to walk ParentBox from.
                if (indexed.EnclosingContainers is not null && node is CssBox containerCandidate
                    && !ContainerQueryMatcher.Matches(indexed.EnclosingContainers, containerCandidate, containerSizes))
                    return;
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
                    foreach (var className in new SpaceSeparatedTokens(classAttr))
                    {
                        if (TryGetByToken(_classIndex!, className, out var classRules))
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

            return matched;
        }

        /// <summary>
        /// Whether an element with the same parent, tag and attributes as <paramref name="box"/> takes the same computed style from
        /// the cascade as <paramref name="box"/> does, so that one can adopt the other's instead of matching and applying the rules
        /// again. It does when no rule that could reach the element depends on anything else about it: its place among its siblings
        /// or what it contains (<see cref="SelectorSiblingStability"/>), whether matching it adds a box to the element or marks it, or
        /// an <c>@container</c> query, which looks at the element's surroundings in a way this does not follow.
        /// </summary>
        /// <remarks>
        /// The rules asked are the candidates the index offers for the element (the universal ones and those under its tag, classes
        /// and id), whatever their media: a superset of the rules that match, which can only answer no where yes was possible.
        /// </remarks>
        internal bool CanShareStyleAmongSiblings(CssBox box)
        {
            EnsureIndex();

            var universal = _universalRules!;
            for (var i = 0; i < universal.Count; i++)
            {
                if (!AllowsSharedStyle(universal[i], box)) return false;
            }

            ICssDomNode node = box;
            if (node.TagName is not { } tagName) return false;

            if (_tagIndex!.TryGetValue(tagName, out var tagRules))
            {
                for (var i = 0; i < tagRules.Count; i++)
                {
                    if (!AllowsSharedStyle(tagRules[i], box)) return false;
                }
            }

            var classAttr = node.GetAttribute("class");
            if (!string.IsNullOrEmpty(classAttr))
            {
                foreach (var className in new SpaceSeparatedTokens(classAttr))
                {
                    if (!TryGetByToken(_classIndex!, className, out var classRules)) continue;

                    for (var i = 0; i < classRules.Count; i++)
                    {
                        if (!AllowsSharedStyle(classRules[i], box)) return false;
                    }
                }
            }

            var idAttr = node.GetAttribute("id");
            if (!string.IsNullOrEmpty(idAttr) && _idIndex!.TryGetValue(idAttr, out var idRules))
            {
                for (var i = 0; i < idRules.Count; i++)
                {
                    if (!AllowsSharedStyle(idRules[i], box)) return false;
                }
            }

            return true;
        }

        private static bool AllowsSharedStyle(in IndexedRule indexed, CssBox box)
        {
            if (indexed.EnclosingContainers is not null) return false;

            var sharing = indexed.Sharing;
            if (!sharing.Stable) return false;

            if (sharing.BoxCreatingSubjects is { } subjects)
            {
                for (var i = 0; i < subjects.Length; i++)
                {
                    if (SubjectWithoutPseudoElementMatches(subjects[i], box)) return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Whether any rule applies to <paramref name="node"/> that declares a property other than a purely visual one of its own
        /// box (fonts, spacing, borders, backgrounds, sizes: see <see cref="DeclaresAConsequentialProperty"/>). Asked of the elements
        /// of an inline <c>&lt;math&gt;</c> by the cascade (see <c>CssBoxMath.SettleSource</c>): such an element is never laid out or
        /// painted as a box, so a rule that only styles one does nothing, while a colour, generated content, a counter, a named
        /// string, a bookmark and the like are read from it by the rest of the pipeline. It errs towards yes: a rule for
        /// <c>::before</c>, <c>::after</c> or <c>::marker</c> counts for its element without its other conditions being tested.
        /// It reads only the few rules that declare such a property, not every rule an element could match.
        /// </summary>
        internal bool AnyConsequentialRuleMatches(MediaQueryContext media, ICssDomNode node, ContainerQuerySizes? containerSizes = null)
        {
            EnsureConsequentialIndex();

            bool Applies(ConsequentialRule rule)
            {
                var indexed = rule.Indexed;
                if (!MediaQueryMatcher.Matches(indexed.EnclosingMedia, media)) return false;
                if (indexed.EnclosingContainers is not null && node is CssBox container
                    && !ContainerQueryMatcher.Matches(indexed.EnclosingContainers, container, containerSizes))
                    return false;

                if (rule.GeneratesOnAnyElement) return true;

                if (rule.GeneratedContentSubjects is { } subjects)
                {
                    for (var i = 0; i < subjects.Length; i++)
                    {
                        if (SubjectWithoutPseudoElementMatches(subjects[i], node)) return true;
                    }
                }

                return !rule.EndsWithPseudoElement && DoesSelectorMatch(indexed.Rule.Selector, node);
            }

            var universal = _consequentialUniversalRules!;
            for (var i = 0; i < universal.Count; i++)
            {
                if (Applies(universal[i])) return true;
            }

            if (node.TagName is not { } tagName) return false;

            if (_consequentialTagIndex!.TryGetValue(tagName, out var tagRules))
            {
                for (var i = 0; i < tagRules.Count; i++)
                {
                    if (Applies(tagRules[i])) return true;
                }
            }

            var classAttr = node.GetAttribute("class");
            if (!string.IsNullOrEmpty(classAttr))
            {
                foreach (var className in new SpaceSeparatedTokens(classAttr))
                {
                    if (TryGetByToken(_consequentialClassIndex!, className, out var classRules))
                    {
                        for (var i = 0; i < classRules.Count; i++)
                        {
                            if (Applies(classRules[i])) return true;
                        }
                    }
                }
            }

            var idAttr = node.GetAttribute("id");
            if (!string.IsNullOrEmpty(idAttr) && _consequentialIdIndex!.TryGetValue(idAttr, out var idRules))
            {
                for (var i = 0; i < idRules.Count; i++)
                {
                    if (Applies(idRules[i])) return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A rule of the index of rules <see cref="AnyConsequentialRuleMatches"/> reads, with what testing it against an element needs
        /// worked out once: whether it is aimed at generated content (<c>::before</c>, <c>::after</c>, <c>::marker</c>) and, if so,
        /// the compound selectors that end in that pseudo-element. The other pseudo-elements (the footnote ones among them) add
        /// nothing to an element's own box.
        /// </summary>
        private sealed class ConsequentialRule
        {
            internal ConsequentialRule(IndexedRule indexed)
            {
                Indexed = indexed;
                EndsWithPseudoElement = CssData.EndsWithPseudoElement(indexed.Rule.Selector);

                var subjects = new List<CompoundSelector>();
                var any = false;
                CollectGeneratedContentSubjects(indexed.Rule.Selector, subjects, ref any);

                GeneratesOnAnyElement = any;
                GeneratedContentSubjects = subjects.Count > 0 ? subjects.ToArray() : null;
            }

            internal IndexedRule Indexed { get; }

            internal bool EndsWithPseudoElement { get; }

            /// <summary>Whether the selector is a bare generated-content pseudo-element, which any element can have.</summary>
            internal bool GeneratesOnAnyElement { get; }

            internal CompoundSelector[]? GeneratedContentSubjects { get; }

            private static void CollectGeneratedContentSubjects(ISelector selector, List<CompoundSelector> subjects, ref bool any)
            {
                switch (selector)
                {
                    case ListSelector list:
                        foreach (var alternative in list)
                            CollectGeneratedContentSubjects(alternative, subjects, ref any);
                        break;

                    case ComplexSelector complex:
                        if (complex.LastOrDefault().Selector is { } subject)
                            CollectGeneratedContentSubjects(subject, subjects, ref any);
                        break;

                    case PseudoElementSelector pseudo:
                        if (IsGeneratedContentPseudoElement(pseudo)) any = true;
                        break;

                    case CompoundSelector compound:
                        if (compound.LastOrDefault() is PseudoElementSelector last && IsGeneratedContentPseudoElement(last))
                            subjects.Add(compound);
                        break;
                }
            }
        }

        /// <summary>
        /// Whether <paramref name="compound"/>, a subject ending in a generated-content pseudo-element, matches the element without it.
        /// The conditions further out in a complex selector are not tested, so it can answer yes where the rule would not apply,
        /// never the reverse. A pure test: matching a pseudo-element compound against a box creates the pseudo-element box.
        /// </summary>
        private static bool SubjectWithoutPseudoElementMatches(CompoundSelector compound, ICssDomNode node)
        {
            for (var i = 0; i < compound.Length; i++)
            {
                var member = compound[i];
                if (member is not PseudoElementSelector && !DoesSelectorMatch(member, node)) return false;
            }

            return true;
        }

        // ::first-letter is not generated content but it changes the boxes the same way: it splits the first letter off an element's text.
        private static bool IsGeneratedContentPseudoElement(PseudoElementSelector pseudo) =>
            pseudo.Name is PseudoElementNames.Before or PseudoElementNames.After or PseudoElementNames.Marker or PseudoElementNames.FirstLetter;

        private void EnsureConsequentialIndex()
        {
            EnsureIndex();
            if (_consequentialUniversalRules is not null) return;

            static List<ConsequentialRule> Consequential(List<IndexedRule> rules)
            {
                var result = new List<ConsequentialRule>();
                foreach (var indexed in rules)
                {
                    if (DeclaresAConsequentialProperty(indexed.Rule)) result.Add(new ConsequentialRule(indexed));
                }

                return result;
            }

            static Dictionary<string, List<ConsequentialRule>> ConsequentialByKey(Dictionary<string, List<IndexedRule>> index)
            {
                var result = new Dictionary<string, List<ConsequentialRule>>(StringComparer.OrdinalIgnoreCase);
                foreach (var (key, rules) in index)
                {
                    var consequential = Consequential(rules);
                    if (consequential.Count > 0) result[key] = consequential;
                }

                return result;
            }

            _consequentialTagIndex = ConsequentialByKey(_tagIndex!);
            _consequentialClassIndex = ConsequentialByKey(_classIndex!);
            _consequentialIdIndex = ConsequentialByKey(_idIndex!);
            _consequentialUniversalRules = Consequential(_universalRules!);
        }

        /// <summary>
        /// Whether the rule declares anything but properties that only change how the element's own box looks or is sized. The
        /// allowlist is of the families authors write for MathML tokens and for resets: font, text, spacing, border, background,
        /// outline, size and custom properties. <c>color</c> is deliberately not among them, nor is <c>display</c>, <c>content</c>,
        /// the counter, string and bookmark properties, <c>all</c>, or an unfamiliar name.
        /// </summary>
        private static bool DeclaresAConsequentialProperty(IStyleRule rule)
        {
            foreach (var property in rule.Style)
            {
                var name = property.Name;
                if (name.StartsWith("--", StringComparison.Ordinal)) continue;

                if (name.StartsWith("font", StringComparison.Ordinal) || name.StartsWith("text-", StringComparison.Ordinal)
                    || name.StartsWith("margin", StringComparison.Ordinal) || name.StartsWith("padding", StringComparison.Ordinal)
                    || name.StartsWith("border", StringComparison.Ordinal) || name.StartsWith("background", StringComparison.Ordinal)
                    || name.StartsWith("outline", StringComparison.Ordinal) || name.StartsWith("min-", StringComparison.Ordinal)
                    || name.StartsWith("max-", StringComparison.Ordinal))
                    continue;

                if (name is "line-height" or "letter-spacing" or "word-spacing" or "box-sizing" or "box-shadow" or "width" or "height"
                    or "vertical-align" or "white-space" or "opacity" or "cursor")
                    continue;

                return true;
            }

            return false;
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
                // Indexed rather than Where().Select().DefaultIfEmpty().Max(): Selectors is
                // IEnumerable-only, so that chain boxes an enumerator and allocates per call. Every
                // matching alternative's specificity has to be examined (not just the first), so this
                // doesn't use MatchOrder's cost-first short-circuiting benefit - it's here purely to
                // remove the allocation. Priority.Zero is already the minimum possible specificity (its
                // components are non-negative counts), so seeding the running max with it and folding in
                // only matched alternatives reproduces Where(...).DefaultIfEmpty(Priority.Zero).Max()
                // exactly, including the "no alternative matched" case.
                var max = Priority.Zero;
                for (var i = 0; i < list.Length; i++)
                {
                    if (!DoesSelectorMatch(list[i], node)) continue;
                    var specificity = list[i].Specificity;
                    if (specificity > max) max = specificity;
                }
                return max;
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
                //
                // It must also never match a bare text box, another anonymous box, or a generated
                // pseudo-element box (Selectors 4 §5.2: "*" is any *element*; Cascade 4 §1.1: text nodes
                // "cannot be targeted by selectors", their values come from inheritance). Matching text made
                // a "* { font-family }" rule land on the text directly, so no more specific rule for the
                // parent element could reach it; matching a ::before box made a "* { margin: 0 }" reset
                // apply to generated content, which "*::before" alone is meant to reach. All of these have no
                // TagName (the IsRoot wrapper too, so the explicit check is belt and braces).
                AllSelector => node is not null && !node.IsRoot && IsElementNode(node),
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
                LangSelector langSelector => DoesSelectorMatch(langSelector, node),
                _ => false
            };
        }

        /// <summary>
        /// Whether <paramref name="node"/> is an element (has a <see cref="ICssDomNode.TagName"/>) - the only
        /// thing a selector not tied to an element name, class, id or attribute (<c>*</c>, <c>:not()</c>,
        /// <c>:lang()</c>) may match as its subject (Selectors 4 §5.2, §4.3, §7.2). A bare text or anonymous box
        /// is not an element and only ever inherits (Cascade 4 §1.1/§7.2), and neither is a generated
        /// pseudo-element box: it is reached only through its own pseudo-element selector, which matches the
        /// rest of the compound against the originating element (see the <c>referenceBox</c> logic in
        /// <see cref="DoesSelectorMatch(CompoundSelector, ICssDomNode?)"/>).
        /// </summary>
        private static bool IsElementNode(ICssDomNode node) => node.TagName is not null;

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

        /// <summary>
        /// The document language for <paramref name="node"/> per CSS 2.1 §5.11.4/the HTML language
        /// determination algorithm: the "lang" attribute of the nearest element that has one, checking
        /// the node itself before any ancestor - or null if none is set anywhere up the chain. Only the
        /// HTML "lang" attribute is consulted; "xml:lang" is out of scope (an inline SVG's own HTML-style
        /// "lang" attribute still matches, for free, since <see cref="ICssDomNode.GetAttribute"/> is the
        /// same generic accessor for every node kind).
        /// </summary>
        private static string? GetElementLanguage(ICssDomNode node)
        {
            var current = (ICssDomNode?)node;
            while (current is not null)
            {
                var lang = current.GetAttribute("lang");
                if (!string.IsNullOrEmpty(lang)) return lang;
                current = current.Parent;
            }
            return null;
        }

        internal static bool DoesSelectorMatch(ListSelector listSelector, ICssDomNode? node)
        {
            // Indexed via MatchOrder (cheapest alternative first, see SelectorMatchCost) rather than
            // LINQ's Any(): Selectors only implements IEnumerable<ISelector>, so Any() boxes an
            // enumerator and allocates a closure per call on what is the hottest path in the whole
            // cascade (once per box per candidate rule).
            var order = listSelector.MatchOrder;
            for (var i = 0; i < order.Length; i++)
            {
                if (DoesSelectorMatch(listSelector[order[i]], node)) return true;
            }
            return false;
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
                {
                    // Indexed via MatchOrder rather than LINQ's Any() - see the DoesSelectorMatch
                    // overloads above for why (Selectors is IEnumerable-only, so Any() allocates).
                    var order = list.MatchOrder;
                    for (var i = 0; i < order.Length; i++)
                    {
                        if (MatchesAsFirstLineSelector(list[order[i]], box)) return true;
                    }
                    return false;
                }

                case ComplexSelector complex:
                    var lastSegmentSelector = complex.Subject;
                    return lastSegmentSelector is not null && HasFirstLineSubject(lastSegmentSelector);

                case CompoundSelector compound:
                {
                    if (!HasFirstLineSubject(compound)) return false;

                    // Indexed via MatchOrder rather than Where().All() - see the plain branch of
                    // DoesSelectorMatch(CompoundSelector, ...) above for the same rewrite and why.
                    var order = compound.MatchOrder;
                    for (var i = 0; i < order.Length; i++)
                    {
                        var s = compound[order[i]];
                        if (s is PseudoElementSelector) continue;
                        if (!DoesSelectorMatch(s, box)) return false;
                    }
                    return true;
                }

                default:
                    return false;
            }
        }

        /// <summary>
        /// A document-level, box-independent superset of <see cref="MatchesAsFirstLineSelector"/>: same
        /// selector-shape switch, but the <see cref="CompoundSelector"/> case omits the per-box
        /// <c>compound.Where(...).All(s => DoesSelectorMatch(s, box))</c> conjunct, since no box exists
        /// yet when this runs (once, in <see cref="EnsureIndex"/>). Can therefore return true for a
        /// selector that ends up matching no box, but can never return false for one that would have
        /// matched some box - so a false result from every rule in the document proves
        /// <see cref="HasFirstLineRules"/> can safely stay false, and <see cref="GetFirstLineStyleRules"/>
        /// would always return empty.
        /// </summary>
        private static bool CouldMatchAsFirstLineSelector(ISelector selector) => selector switch
        {
            ListSelector list => AnyCouldMatchAsFirstLineSelector(list),
            ComplexSelector complex => complex.LastOrDefault().Selector is { } last && HasFirstLineSubject(last),
            CompoundSelector compound => HasFirstLineSubject(compound),
            _ => false
        };

        // Indexed rather than list.Any(CouldMatchAsFirstLineSelector) - see the DoesSelectorMatch
        // overloads above for why (Selectors is IEnumerable-only, so Any() allocates). This method runs
        // once per rule at EnsureIndex time rather than per box, so the allocation it used to cause was
        // never actually hot - fixed anyway for consistency now that MatchOrder exists.
        private static bool AnyCouldMatchAsFirstLineSelector(ListSelector list)
        {
            var order = list.MatchOrder;
            for (var i = 0; i < order.Length; i++)
            {
                if (CouldMatchAsFirstLineSelector(list[order[i]])) return true;
            }
            return false;
        }

        private static bool HasFirstLineSubject(ISelector selector) => selector switch
        {
            // Indexed rather than LINQ's LastOrDefault() - see the DoesSelectorMatch overloads above
            // for why (Selectors is IEnumerable-only, so LastOrDefault() allocates).
            CompoundSelector { Length: > 0 } compound =>
                compound[compound.Length - 1] is PseudoElementSelector { Name: PseudoElementNames.FirstLine },
            PseudoElementSelector pseudo => pseudo.Name == PseudoElementNames.FirstLine,
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

        internal static bool DoesSelectorMatch(CompoundSelector compoundSelector, ICssDomNode? node)
        {
            if (node is null)
            {
                return false;
            }

            var lastSelector = compoundSelector[compoundSelector.Length - 1];

            // Structural pseudo-classes (ChildSelector subtypes, OnlyChildSelector, OnlyOfTypeSelector)
            // are matched against `node` itself here, same as every other compound member - their own
            // DoesSelectorMatch overload re-derives sibling scope from `node` as needed, so no special
            // handling is required for them beyond the plain path below.
            if (lastSelector is not PseudoElementSelector)
            {
                // Indexed via MatchOrder (cheapest selector first, see SelectorMatchCost) rather than
                // LINQ's All(): Selectors only implements IEnumerable<ISelector>, so All() boxes an
                // enumerator and allocates a closure per call on what is the hottest path in the whole
                // cascade (once per box per candidate rule).
                var order = compoundSelector.MatchOrder;
                for (var i = 0; i < order.Length; i++)
                {
                    if (!DoesSelectorMatch(compoundSelector[order[i]], node)) return false;
                }
                return true;
            }

            // Pseudo-elements (::before/::after/::marker/::first-letter) exist only in the HTML box tree
            // and the synthesis below mutates it; an SVG node never has one, so a pseudo-element compound
            // simply never matches an SVG node.
            if (node is not CssBox box)
                return false;

            if (lastSelector is PseudoElementSelector pseudoElementSelector)
            {
                var referenceBox = box.IsFirstLetterPseudoElement ? box.FirstLetterOriginatingBox
                    : box.IsFootnoteCallPseudoElement || box.IsFootnoteMarkerPseudoElement ? box.FootnoteSourceBox
                    : box.IsPseudoElement ? box.ParentBox : box;

                // Same indexed-order approach as the plain branch above, skipping the pseudo-element
                // itself (still pinned/handled separately below) instead of LINQ's Where().All().
                var isMatchWithoutPseudoElement = true;
                var order = compoundSelector.MatchOrder;
                for (var i = 0; i < order.Length; i++)
                {
                    var selector = compoundSelector[order[i]];
                    if (selector is PseudoElementSelector) continue;
                    if (!DoesSelectorMatch(selector, referenceBox))
                    {
                        isMatchWithoutPseudoElement = false;
                        break;
                    }
                }

                if (!isMatchWithoutPseudoElement) return false;

                if (box.IsPseudoElement)
                {
                    return DoesSelectorMatch(pseudoElementSelector, box);
                }

                // A pseudo-element is generated on an *element* (Selectors 4 §3.3: "pseudo-elements
                // represent abstractions about the document tree beyond those specified by the document
                // language"; every one of them is defined as a child of its originating element). A box
                // with no HtmlTag at this point in the pipeline is an anonymous text box - the box a raw
                // text node became - which is not an element and cannot originate one. The universal
                // selector (and :not()/:lang()) no longer match such a box, so a blanket "*::before" -
                // Charts.css's own ".charts-css *::before { box-sizing: border-box }" is the case that
                // found this - normally never gets this far; the guard stays as the last line of defence
                // for any other compound member that accepts a box with no element behind it, since a
                // blanket rule would otherwise hang an empty ::before/::after box off every text box.
                //
                // That is not cosmetic: it leaves a box holding BOTH its own words and child boxes, and
                // CssLayoutEngine.FlowBox flows a box's own words only when it has no child boxes
                // (`if (boxes.Count is 0 && box.Words.Count > 0) boxes = [box]`), so the text was never
                // positioned by any inline flow again. It kept whatever coordinates an earlier pass had
                // left on it and stayed flagged CssRect.AwaitsTheNextFragmentainer, which made
                // FragmentEmitter drop the word entirely - the text simply disappeared from the PDF.
                if (box.HtmlTag is null) return false;

                switch (pseudoElementSelector.Name)
                {
                    case PseudoElementNames.Before when !box.Boxes.Any(b => b.IsBeforePseudoElement):
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
                    case PseudoElementNames.After when !box.Boxes.Any(b => b.IsAfterPseudoElement):
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
                    case PseudoElementNames.Marker when !box.Boxes.Any(b => b.IsMarkerPseudoElement):
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
                    case PseudoElementNames.FirstLetter:
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

            var comparison = node.NameComparison;
            foreach (var className in new SpaceSeparatedTokens(classNames))
            {
                if (className.Equals(classSelector.Class, comparison)) return true;
            }

            return false;
        }

        /// <summary>
        /// Looks a class token up in an index keyed with <see cref="StringComparer.OrdinalIgnoreCase"/> without making a string
        /// of it, where the runtime can (a span alternate lookup from .NET 9).
        /// </summary>
        private static bool TryGetByToken<T>(Dictionary<string, List<T>> index, ReadOnlySpan<char> token,
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out List<T>? rules)
        {
#if NET9_0_OR_GREATER
            return index.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(token, out rules);
#else
            return index.TryGetValue(token.ToString(), out rules);
#endif
        }

        private static bool DoesSelectorMatch(IdSelector idSelector, ICssDomNode? node)
        {
            if (node is null) return false;
            var id = node.GetAttribute("id");
            return id is not null && id.Equals(idSelector.Id, node.NameComparison);
        }

        // The HTML Standard's "Case-sensitivity of selectors" (§4.16.2) limits ASCII case-insensitive
        // attribute-*value* matching to this fixed list of legacy attributes, on HTML elements in HTML
        // documents only - every other attribute (data-*, href, title, id-like attributes, ...) is
        // case-sensitive by default. See issue #1384.
        private static readonly FrozenSet<string> HtmlCaseInsensitiveAttributeValues = new[]
        {
            "accept", "accept-charset", "align", "alink", "axis", "bgcolor", "charset", "checked",
            "clear", "codetype", "color", "compact", "declare", "defer", "dir", "direction",
            "disabled", "enctype", "face", "frame", "hreflang", "http-equiv", "lang", "language",
            "link", "media", "method", "multiple", "nohref", "noresize", "noshade", "nowrap",
            "readonly", "rel", "rev", "rules", "scope", "scrolling", "selected", "shape", "target",
            "text", "type", "valign", "valuetype", "vlink"
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        // Attribute selectors read the value once via node.GetAttribute (case-sensitivity of the
        // attribute-*name* lookup is the node's own - case-insensitive for a plain HTML CssBox,
        // case-sensitive for SVG/MathML foreign content, see CssBox.IsWithinForeignContent and
        // SvgCssBoxDomNode.GetAttribute), then compare the *value* with AttributeValueComparison: an
        // explicit `i`/`s` modifier (Selectors 4 §6.3) wins; otherwise, for a node whose NameComparison is
        // OrdinalIgnoreCase (an HTML element - foreign content always reports Ordinal), the default
        // follows the fixed legacy-attribute list above - Ordinal (case-sensitive) for everything else.
        private static StringComparison AttributeValueComparison(IAttrSelector s, ICssDomNode node)
        {
            return s.CaseSensitivity switch
            {
                AttrCaseSensitivity.Insensitive => StringComparison.OrdinalIgnoreCase,
                AttrCaseSensitivity.Sensitive => StringComparison.Ordinal,
                _ => node.NameComparison == StringComparison.OrdinalIgnoreCase
                        && HtmlCaseInsensitiveAttributeValues.Contains(s.Attribute)
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal
            };
        }

        private static bool DoesSelectorMatch(AttrAvailableSelector s, ICssDomNode? node)
        {
            return node?.GetAttribute(s.Attribute) is not null;
        }

        private static bool DoesSelectorMatch(AttrMatchSelector s, ICssDomNode? node)
        {
            if (node is null) return false;
            var value = node.GetAttribute(s.Attribute);
            return value is not null && value.Equals(s.Value, AttributeValueComparison(s, node));
        }

        private static bool DoesSelectorMatch(AttrListSelector s, ICssDomNode? node)
        {
            if (node is null) return false;
            var value = node.GetAttribute(s.Attribute);
            if (value is null) return false;
            var comparison = AttributeValueComparison(s, node);
            foreach (var token in new SpaceSeparatedTokens(value))
            {
                if (token.Equals(s.Value, comparison)) return true;
            }

            return false;
        }

        private static bool DoesSelectorMatch(AttrContainsSelector s, ICssDomNode? node)
        {
            if (node is null) return false;
            var value = node.GetAttribute(s.Attribute);
            return value is not null && value.Contains(s.Value, AttributeValueComparison(s, node));
        }

        private static bool DoesSelectorMatch(AttrBeginsSelector s, ICssDomNode? node)
        {
            if (node is null) return false;
            var value = node.GetAttribute(s.Attribute);
            return value is not null && value.StartsWith(s.Value, AttributeValueComparison(s, node));
        }

        private static bool DoesSelectorMatch(AttrEndsSelector s, ICssDomNode? node)
        {
            if (node is null) return false;
            var value = node.GetAttribute(s.Attribute);
            return value is not null && value.EndsWith(s.Value, AttributeValueComparison(s, node));
        }

        private static bool DoesSelectorMatch(AttrHyphenSelector s, ICssDomNode? node)
        {
            if (node is null) return false;
            var value = node.GetAttribute(s.Attribute);
            if (value is null) return false;
            var comparison = AttributeValueComparison(s, node);
            return value.Equals(s.Value, comparison) || value.StartsWith(s.Value + "-", comparison);
        }

        private static bool DoesSelectorMatch(PseudoClassSelector pseudoClassSelector, ICssDomNode? node)
        {
            var name = pseudoClassSelector.Class;

            // :scope with no scoping root in play is :root (Selectors 4 §6.6) - PeachPDF never matches
            // against a scoping element, so the two always give the same answer.
            if (name == PseudoClassNames.Root || name == PseudoClassNames.Scope)
            {
                // :root is the document's root element - <html> for HTML, or the outermost element of a
                // (standalone/inline) SVG fragment, which is any element node with no parent element.
                if (node?.TagName is null) return false;
                if (GetNearestParentElement(node) is not null) return false;
                return node is not CssBox
                    || node.TagName.Equals("html", StringComparison.OrdinalIgnoreCase);
            }

            // :empty (Selectors 4 §9.5) is a pure document-tree question, so every node kind answers it -
            // HTML boxes and SVG elements alike. A non-element node is never :empty.
            if (name == PseudoClassNames.Empty)
            {
                return node is { TagName: not null } && node.IsEmpty;
            }

            // :any-link (Selectors 4 §7.1) is the union of :link and :visited. :visited never matches -
            // a static PDF has no browsing history - so it answers exactly where :link does. Neither
            // matches an SVG node; IsClickable is CssBox-only.
            if (name == PseudoClassNames.Link || name == PseudoClassNames.AnyLink)
            {
                return node is CssBox { IsClickable: true };
            }

            // :placeholder-shown is the one form-control state a generated PDF can answer: it is
            // fixed by the source attributes at generation time. It deliberately does not try to
            // model the live state after a reader edits the AcroForm field; PDF has no selector or
            // appearance-state equivalent for that transition.
            if (name == PseudoClassNames.PlaceholderShown)
            {
                return node is CssBox box && FormFieldMapper.IsPlaceholderShown(box);
            }

            return false;
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
                case PseudoElementNames.Before when box.IsBeforePseudoElement:
                case PseudoElementNames.After when box.IsAfterPseudoElement:
                case PseudoElementNames.Marker when box.IsMarkerPseudoElement:
                case PseudoElementNames.Placeholder when box.IsPlaceholderPseudoElement:
                case PseudoElementNames.FirstLetter when box.IsFirstLetterPseudoElement:
                case PseudoElementNames.FootnoteCall when box.IsFootnoteCallPseudoElement:
                case PseudoElementNames.FootnoteMarker when box.IsFootnoteMarkerPseudoElement:
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

            var sameTypeOnly = childSelector is FirstTypeSelector or LastTypeSelector;
            var fromEndPosition = childSelector is LastChildSelector or LastTypeSelector;

            // Answered from the parent's sibling index when it has one (see SiblingIndex): without it every
            // row of a long table scans and copies all its siblings. An "of S" clause filters the siblings, so
            // that rarer form keeps the walk below.
            if (childSelector.Kind is AllSelector
                && SiblingIndex.For(parent.Children, node.NameComparison) is { } siblingIndex
                && siblingIndex.IndexOf(node) is var at and >= 0)
            {
                int indexedPosition;
                if (sameTypeOnly)
                {
                    var (typePosition, typeCount) = siblingIndex.TypePositionOf(node);
                    indexedPosition = fromEndPosition ? typeCount - typePosition : typePosition + 1;
                }
                else
                {
                    indexedPosition = fromEndPosition ? siblingIndex.Elements.Count - at : at + 1;
                }

                return MatchesAnPlusB(indexedPosition, childSelector.Step, childSelector.Offset);
            }

            IEnumerable<ICssDomNode> siblingNodes = parent.Children.Where(b => b.TagName is not null);

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
            return node is not null && IsElementNode(node) && !DoesSelectorMatch(notSelector.Inner, node);
        }

        private static bool DoesSelectorMatch(MatchesSelector matchesSelector, ICssDomNode? node)
        {
            return DoesSelectorMatch(matchesSelector.Inner, node);
        }

        private static bool DoesSelectorMatch(HasSelector hasSelector, ICssDomNode? node)
        {
            return node is not null && HasRelativeMatch(node, hasSelector.Inner);
        }

        /// <summary>
        /// CSS 2.1 §5.11.4: matches if the element's language (<see cref="GetElementLanguage"/> - the
        /// nearest ancestor-or-self "lang" attribute) is exactly the given range, or the range is an
        /// ASCII-case-insensitive prefix of it immediately followed by a hyphen (e.g. ":lang(en)" matches
        /// lang="en" and lang="en-US", but not lang="english"). Case-folding is always ASCII/BCP47, not
        /// <paramref name="node"/>'s own <see cref="ICssDomNode.NameComparison"/> (which differs between
        /// HTML and SVG nodes for unrelated reasons - element/attribute name casing, not language tags).
        /// </summary>
        private static bool DoesSelectorMatch(LangSelector langSelector, ICssDomNode? node)
        {
            if (node is null || !IsElementNode(node)) return false;
            var language = GetElementLanguage(node);
            if (language is null) return false;

            return language.Equals(langSelector.LanguageRange, StringComparison.OrdinalIgnoreCase)
                || language.StartsWith(langSelector.LanguageRange + "-", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Evaluates :has()'s (possibly comma-separated) relative-selector-list argument against node,
        /// treating node as the implicit :scope. A <see cref="RelativeSelector"/> alternative walks its
        /// complex-selector chain forward from node via <see cref="MatchesRelativeChain"/>, anchoring
        /// the chain's FIRST compound to node via the leading combinator (CSS Selectors 4 §4.5); anything
        /// else (a plain selector - the default descendant case) falls back to
        /// <see cref="HasDescendantMatch"/>. See SelectorConstructor.GetRelativeResult() for how
        /// <see cref="HasSelector.Inner"/> gets built.
        /// </summary>
        private static bool HasRelativeMatch(ICssDomNode node, ISelector inner)
        {
            // Indexed via MatchOrder rather than LINQ's Any() - see the DoesSelectorMatch overloads
            // above for why (Selectors is IEnumerable-only, so Any() allocates).
            if (inner is ListSelector list)
            {
                var order = list.MatchOrder;
                for (var i = 0; i < order.Length; i++)
                {
                    if (HasRelativeMatch(node, list[order[i]])) return true;
                }
                return false;
            }

            if (inner is RelativeSelector relative)
                return MatchesRelativeChain(node, relative.Combinator, relative.Selector);

            return HasDescendantMatch(node, inner);
        }

        /// <summary>
        /// Walks a :has() relative selector's complex-selector chain forward from anchor: the chain's
        /// FIRST compound is related to anchor via combinator (the relative selector's leading
        /// combinator), and each subsequent compound is related to the PREVIOUS compound's match via
        /// that compound's own internal combinator (its <see cref="ComplexSelector"/> entry's
        /// Delimiter) - e.g. for ":has(&gt; .a .b)", ".a" must be a direct child of :scope, and ".b" a
        /// descendant of THAT ".a" match, not of :scope directly. This is a forward search (existence of
        /// a valid subject reachable from anchor), unlike <see cref="DoesSelectorMatch(ComplexSelector, ICssDomNode?)"/>'s
        /// backward walk from a fixed subject up through its ancestors - anchoring the WHOLE chain
        /// against a single one-hop candidate (as if testing "does this direct child match '.a .b' as
        /// its own subject") would wrongly require the anchor's child itself to satisfy the chain's LAST
        /// compound instead of its first.
        /// </summary>
        private static bool MatchesRelativeChain(ICssDomNode anchor, Combinator combinator, ISelector selector)
        {
            if (selector is ComplexSelector complex)
                return MatchesChainStep(anchor, combinator, complex.Entries, 0);

            return RelativeCandidates(anchor, combinator).Any(candidate => DoesSelectorMatch(selector, candidate));
        }

        private static bool MatchesChainStep(ICssDomNode anchor, Combinator combinator,
            IReadOnlyList<CombinatorSelector> entries, int index)
        {
            var compound = entries[index].Selector;
            if (compound is null) return false;

            foreach (var candidate in RelativeCandidates(anchor, combinator))
            {
                if (!DoesSelectorMatch(compound, candidate)) continue;
                if (index == entries.Count - 1) return true;
                if (MatchesChainStep(candidate, DelimiterToCombinator(entries[index].Delimiter), entries, index + 1))
                    return true;
            }

            return false;
        }

        /// <summary>Element nodes related to anchor via combinator - direct children, the immediate next sibling, following siblings, or (default/anything unrecognized) any-depth descendants - each filtered to real elements (TagName is not null), skipping anonymous/text nodes.</summary>
        private static IEnumerable<ICssDomNode> RelativeCandidates(ICssDomNode anchor, Combinator combinator)
        {
            if (combinator == Combinator.Child)
                return anchor.Children.Where(c => c.TagName is not null);

            if (combinator == Combinator.AdjacentSibling)
            {
                var next = GetNextSiblingElement(anchor);
                return next is null ? [] : [next];
            }

            if (combinator == Combinator.Sibling) return FollowingSiblingElements(anchor);

            return AllDescendants(anchor);
        }

        private static Combinator DelimiterToCombinator(string? delimiter) => delimiter switch
        {
            Combinators.Child => Combinator.Child,
            Combinators.Adjacent => Combinator.AdjacentSibling,
            Combinators.Sibling => Combinator.Sibling,
            _ => Combinator.Descendent
        };

        /// <summary>The next element sibling (skipping anonymous/text nodes) after node, in document order - the forward analogue of the preceding-sibling walk in DoesSelectorMatch(ComplexSelector, node)'s "+"/"~" cases, used for :has(+ S)/:has(~ S).</summary>
        private static ICssDomNode? GetNextSiblingElement(ICssDomNode node)
        {
            var parent = node.Parent;
            if (parent is null) return null;
            if (SiblingIndex.For(parent.Children, node.NameComparison) is { } index && index.IndexOf(node) is var at and >= 0)
            {
                return at + 1 < index.Elements.Count ? index.Elements[at + 1] : null;
            }

            var siblings = parent.Children.Where(b => b.TagName is not null).ToList();
            var idx = siblings.IndexOf(node);
            return idx >= 0 && idx + 1 < siblings.Count ? siblings[idx + 1] : null;
        }

        /// <summary>Every element sibling following node, in document order - materializes the filtered sibling list once rather than re-walking parent.Children on each step (unlike a GetNextSiblingElement chain, which is O(n^2) over a long run of siblings).</summary>
        private static IEnumerable<ICssDomNode> FollowingSiblingElements(ICssDomNode node)
        {
            var parent = node.Parent;
            if (parent is null) yield break;

            IReadOnlyList<ICssDomNode> siblings;
            int idx;
            if (SiblingIndex.For(parent.Children, node.NameComparison) is { } index && index.IndexOf(node) is var at and >= 0)
            {
                siblings = index.Elements;
                idx = at;
            }
            else
            {
                var scanned = parent.Children.Where(b => b.TagName is not null).ToList();
                siblings = scanned;
                idx = scanned.IndexOf(node);
            }

            if (idx < 0) yield break;

            for (var i = idx + 1; i < siblings.Count; i++) yield return siblings[i];
        }

        private static IEnumerable<ICssDomNode> AllDescendants(ICssDomNode node)
        {
            foreach (var child in node.Children)
            {
                yield return child;
                foreach (var descendant in AllDescendants(child)) yield return descendant;
            }
        }

        /// <summary>
        /// Recursively searches box's descendants (at any depth) for one matching inner, short-
        /// circuiting on the first hit - mirrors the shape of DomUtils.GetBoxById/GetBoxByTagName.
        /// Backs :has()'s default (descendant) relative-selector form.
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

        /// <summary>
        /// The nearest preceding sibling of <paramref name="node"/> that is an element
        /// (<c>TagName is not null</c>), or null when there is none — including when
        /// <paramref name="node"/> is not itself an element, or is absent from
        /// <paramref name="children"/>. Both of those are the cases the
        /// <c>Where(...).ToList().IndexOf(...)</c> this replaces folded into <c>idx &lt;= 0</c>.
        ///
        /// Comparison is the list's own equality, NOT <c>ReferenceEquals</c>: an SVG DOM node
        /// (<c>SvgCssBoxDomNode</c>, <c>SvgXmlDomNode</c>) is a fresh wrapper object per access
        /// that defines value equality over the box it wraps, so reference identity would silently
        /// stop matching sibling combinators inside inline SVG.
        /// </summary>
        private static ICssDomNode? PrecedingElementSibling(IReadOnlyList<ICssDomNode> children, ICssDomNode node)
        {
            if (node.TagName is null) return null;

            if (SiblingIndex.For(children, node.NameComparison) is { } index && index.IndexOf(node) is var at and >= 0)
            {
                return at > 0 ? index.Elements[at - 1] : null;
            }

            ICssDomNode? previous = null;
            for (var i = 0; i < children.Count; i++)
            {
                var sibling = children[i];
                if (sibling.Equals(node)) return previous;
                if (sibling.TagName is not null) previous = sibling;
            }

            return null;
        }

        /// <summary>
        /// <paramref name="node"/>'s position in <paramref name="children"/>, or -1 when it is not
        /// an element or is absent. See <see cref="PrecedingElementSibling"/> on the equality.
        /// </summary>
        private static int ChildIndexOf(IReadOnlyList<ICssDomNode> children, ICssDomNode node)
        {
            if (node.TagName is null) return -1;

            for (var i = 0; i < children.Count; i++)
            {
                if (children[i].Equals(node)) return i;
            }

            return -1;
        }

        private static bool DoesSelectorMatch(ComplexSelector complexSelector, ICssDomNode? node)
        {
            var isLowestItem = true;
            ICssDomNode? currentRef = node;

            for (var selectorIndex = complexSelector.Length - 1; selectorIndex >= 0; selectorIndex--)
            {
                var selector = complexSelector[selectorIndex];
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

                        var previous = PrecedingElementSibling(parent.Children, currentRef);
                        if (previous is null) return false;
                        currentRef = previous;
                        if (!DoesSelectorMatch(selector.Selector, currentRef)) return false;
                        break;
                    }

                    case "~":
                    {
                        var parent = currentRef.Parent;
                        if (parent is null) return false;

                        IReadOnlyList<ICssDomNode> children = parent.Children;
                        int at;
                        if (SiblingIndex.For(children, currentRef.NameComparison) is { } index
                            && index.IndexOf(currentRef) is var indexedAt and >= 0)
                        {
                            children = index.Elements;
                            at = indexedAt;
                        }
                        else
                        {
                            at = ChildIndexOf(children, currentRef);
                        }

                        if (at <= 0) return false;

                        ICssDomNode? match = null;
                        for (var i = at - 1; i >= 0; i--)
                        {
                            var sibling = children[i];
                            if (sibling.TagName is null) continue;
                            if (!DoesSelectorMatch(selector.Selector, sibling)) continue;
                            match = sibling;
                            break;
                        }
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
            CssData cssData = new(_renderContext);
            cssData.Stylesheets.AddRange(Stylesheets);
            return cssData;
        }
    }
}
