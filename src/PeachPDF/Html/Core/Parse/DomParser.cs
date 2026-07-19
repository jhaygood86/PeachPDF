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

using PeachPDF;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Parse
{
    /// <summary>
    /// Handle css DOM tree generation from raw html and stylesheet.
    /// </summary>
    internal sealed class DomParser
    {
        /// <summary>
        /// Parser for CSS
        /// </summary>
        private readonly CssParser _cssParser;

        /// <summary>
        /// Init.
        /// </summary>
        public DomParser(CssParser cssParser)
        {
            ArgumentNullException.ThrowIfNull(cssParser);

            _cssParser = cssParser;
        }

        /// <summary>
        /// Generate css tree by parsing the given html and applying the given css style data on it.
        /// </summary>
        /// <param name="html">the html to parse</param>
        /// <param name="htmlContainer">the html container to use for reference resolve</param>
        /// <param name="cssData">the css data to use</param>
        /// <returns>the root of the generated tree</returns>
        public async Task<(CssBox cssBox, CssData cssData, HtmlDocumentMetadata metadata)> GenerateCssTree(string html, HtmlContainerInt htmlContainer, CssData cssData)
        {
            CssBox.ClearCounter();
            var root = HtmlParser.ParseDocument(html);
            root.IsRoot = true;
            root.HtmlContainer = htmlContainer;

            // Must happen before CorrectTextBoxes (below) parses every text box's words - hyphens:auto
            // needs to know the document's language (see CssBox.ParseToWords) at that point, not after
            // this whole method returns. Root is a synthetic wrapper box, not the document's actual
            // <html> element, so it must be located by tag name.
            var htmlBox = DomUtils.GetBoxByTagName(root, "html");
            var lang = htmlBox?.HtmlTag?.TryGetAttribute("lang", "");
            htmlContainer.DocumentLanguage = string.IsNullOrEmpty(lang) ? null : lang;

            var metadata = ExtractMetadata(root);

            const bool cssDataChanged = false;

            (cssData, _) = await CascadeParseStyles(root, htmlContainer, cssData, cssDataChanged);

            //var media = htmlContainer.GetCssMediaType(cssData.MediaBlocks.Keys);
            var media = "print"; // TODO: fix this

            var cssValueParser = new CssValueParser(htmlContainer.Adapter);

            await CascadeApplyStyleFonts(cssData, htmlContainer.Adapter);

            CascadeApplyPageStyles(htmlContainer, root, cssData);
            htmlContainer.PageRules = cssData.Stylesheets
                .SelectMany(s => s.Rules.OfType<PageRule>())
                .ToList();

            CascadeApplyStyles(cssValueParser, root, cssData, media);

            EnsureListItemMarkers(cssValueParser, root, cssData, media);

            ApplyFirstLetterPseudoElements(cssValueParser, root, cssData, media);

            CorrectTextBoxes(root);

            CorrectReplacedElementBoxes(root);

            CorrectLineBreaksBlocks(root);

            CorrectInlineBoxesParent(root);

            CorrectAbsolutelyPositionedInlineElements(root);

            CorrectBlockInsideInline(root);

            CorrectInlineBoxesParent(root);

            CorrectAnonymousTables(root);

            return (root, cssData, metadata);
        }


        #region Private methods

        private static async Task CascadeApplyStyleFonts(CssData cssData, RAdapter adapter)
        {
            foreach (var stylesheet in cssData.Stylesheets)
            {
                foreach (var fontRule in stylesheet.FontfaceSetRules)
                {
                    var fontFamilyName = CssValueParser.GetFontFaceFamilyName(fontRule.Family);
                    var fontFaceCandidates = CssValueParser.GetFontFacePropertyValue(fontRule.Source);

                    // The @font-face rule's own font-weight/font-style/font-stretch descriptors are
                    // authoritative for how THIS specific resource participates in matching, independent
                    // of what the file's own internal tables say - resolve them once per rule and apply
                    // to every src candidate it declares.
                    var weightOverride = FontFaceDescriptorResolver.ResolveWeight(fontRule.Weight);
                    var isItalicOverride = FontFaceDescriptorResolver.ResolveIsItalic(fontRule.Style);
                    var stretchOverride = FontFaceDescriptorResolver.ResolveStretch(fontRule.Stretch);

                    // src is itself a comma-separated fallback list (e.g. woff2, then woff, then a local()
                    // match) - try each candidate in declaration order, local() before url() within a
                    // candidate exactly as before, and stop at the first one that actually loads.
                    foreach (var fontFaceDefinition in fontFaceCandidates)
                    {
                        var isLoaded = false;

                        if (fontFaceDefinition.Local is not null)
                        {
                            isLoaded = await adapter.AddLocalFontFamily(fontFamilyName, fontFaceDefinition.Local, weightOverride, isItalicOverride, stretchOverride);
                        }

                        if (!isLoaded && fontFaceDefinition.Url is not null)
                        {
                            isLoaded = await adapter.AddFontFamilyFromUrl(fontFamilyName, fontFaceDefinition.Url, fontFaceDefinition.Format, stylesheet.BaseUri, weightOverride, isItalicOverride, stretchOverride);
                        }

                        if (isLoaded) break;
                    }
                }
            }
        }

        /// <summary>
        /// Read styles defined inside the dom structure in links and style elements.<br/>
        /// If the html tag is "style" tag parse it content and add to the css data for all future tags parsing.<br/>
        /// If the html tag is "link" that point to style data parse it content and add to the css data for all future tags parsing.<br/>
        /// </summary>
        /// <param name="box">the box to parse style data in</param>
        /// <param name="htmlContainer">the html container to use for reference resolve</param>
        /// <param name="cssData">the style data to fill with found styles</param>
        /// <param name="cssDataChanged">check if the css data has been modified by the handled html not to change the base css data</param>
        private async Task<(CssData cssData, bool cssDataChanged)> CascadeParseStyles(CssBox box, HtmlContainerInt htmlContainer, CssData cssData, bool cssDataChanged)
        {
            if (box.HtmlTag != null)
            {
                // Check for the <link rel=stylesheet> tag. Per HTML4/5, `rel` is a space-separated set
                // of link types (e.g. `rel="appendix stylesheet"` is still a stylesheet link), so this
                // must check for the "stylesheet" token rather than requiring an exact match.
                if (box.HtmlTag.Name.Equals("link", StringComparison.CurrentCultureIgnoreCase) &&
                   box.GetAttribute("rel", string.Empty)
                       .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                       .Any(token => token.Equals("stylesheet", StringComparison.CurrentCultureIgnoreCase)))
                {
                    CloneCssData(ref cssData, ref cssDataChanged);
                    var (stylesheet, resolvedUri) = await StylesheetLoadHandler.LoadStylesheet(htmlContainer, box.GetAttribute("href", string.Empty));
                    if (stylesheet != null)
                        await _cssParser.ParseStyleSheet(cssData, stylesheet, resolvedUri);
                }

                // Check for the <style> tag
                if (box.HtmlTag.Name.Equals("style", StringComparison.CurrentCultureIgnoreCase) && box.Boxes.Count > 0)
                {
                    CloneCssData(ref cssData, ref cssDataChanged);
                    foreach (var child in box.Boxes)
                        await _cssParser.ParseStyleSheet(cssData, child.Text!);
                }
            }

            foreach (var childBox in box.Boxes)
            {
                (cssData, cssDataChanged) = await CascadeParseStyles(childBox, htmlContainer, cssData, cssDataChanged);
            }

            return (cssData, cssDataChanged);
        }

        private static void CascadeApplyPageStyles(HtmlContainerInt htmlContainer, CssBox root, CssData cssData)
        {
            foreach (var style in cssData.Stylesheets)
            {
                foreach (var pageRuleInterface in style.PageRules)
                {
                    if (pageRuleInterface is not PageRule pageRule)
                        continue;

                    // Only base @page rules (no selector) affect global margins and size
                    if (pageRule.Selector != null)
                        continue;

                    if (pageRule.Style.MarginLeft.Length > 0)
                    {
                        htmlContainer.MarginLeft = CssValueParser.ParseLength(pageRule.Style.MarginLeft, htmlContainer.PageSize.Width, root);
                    }

                    if (pageRule.Style.MarginTop.Length > 0)
                    {
                        htmlContainer.MarginTop = CssValueParser.ParseLength(pageRule.Style.MarginTop, htmlContainer.PageSize.Width, root);
                    }

                    if (pageRule.Style.MarginBottom.Length > 0)
                    {
                        htmlContainer.MarginBottom = CssValueParser.ParseLength(pageRule.Style.MarginBottom, htmlContainer.PageSize.Width, root);
                    }

                    if (pageRule.Style.MarginRight.Length > 0)
                    {
                        htmlContainer.MarginRight = CssValueParser.ParseLength(pageRule.Style.MarginRight, htmlContainer.PageSize.Width, root);
                    }

                    if (pageRule.Style.Size.Length > 0)
                    {
                        htmlContainer.CssPageSize = ParsePageSizeToPdfPoints(pageRule.Style.Size);
                    }
                }
            }
        }

        private static readonly FrozenDictionary<string, XSize> NamedPageSizes = new Dictionary<string, XSize>(StringComparer.OrdinalIgnoreCase)
        {
            { "a0",      new XSize(2383.94, 3370.39) },
            { "a1",      new XSize(1683.78, 2383.94) },
            { "a2",      new XSize(1190.55, 1683.78) },
            { "a3",      new XSize(841.89,  1190.55) },
            { "a4",      new XSize(595.28,   841.89) },
            { "a5",      new XSize(419.53,   595.28) },
            { "a6",      new XSize(297.64,   419.53) },
            { "b4",      new XSize(708.66,  1000.63) },
            { "b5",      new XSize(498.90,   708.66) },
            { "letter",  new XSize(612,       792)   },
            { "legal",   new XSize(612,      1008)   },
            { "ledger",  new XSize(1224,      792)   },
            { "tabloid", new XSize(792,      1224)   },
        }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        private static XSize? ParsePageSizeToPdfPoints(string sizeValue)
        {
            var parts = sizeValue.Trim().Split(Array.Empty<char>(), StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return null;

            XSize? namedSize = null;
            bool? landscape = null;

            foreach (var part in parts)
            {
                if (part.Equals("portrait", StringComparison.OrdinalIgnoreCase))
                {
                    landscape = false;
                }
                else if (part.Equals("landscape", StringComparison.OrdinalIgnoreCase))
                {
                    landscape = true;
                }
                else if (NamedPageSizes.TryGetValue(part, out var known))
                {
                    namedSize = known;
                }
            }

            if (namedSize.HasValue)
            {
                var size = namedSize.Value;
                if (landscape == true && size.Width < size.Height)
                    size = new XSize(size.Height, size.Width);
                else if (landscape == false && size.Width > size.Height)
                    size = new XSize(size.Height, size.Width);
                return size;
            }

            // landscape/portrait keyword alone — caller will handle orientation after we return null
            if (landscape.HasValue)
                return null;

            // Try parsing explicit length values
            double? width = null, height = null;
            foreach (var part in parts)
            {
                var pt = ParseLengthToPdfPoints(part);
                if (pt.HasValue)
                {
                    if (width == null) width = pt;
                    else if (height == null) { height = pt; break; }
                }
            }

            if (width.HasValue)
                return new XSize(width.Value, height ?? width.Value);

            return null;
        }

        internal static double? ParseLengthToPdfPoints(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            // Find where the unit starts
            var i = value.Length - 1;
            while (i >= 0 && (char.IsLetter(value[i]) || value[i] == '%'))
                i--;

            var unitStr = value.Substring(i + 1).ToLowerInvariant();
            if (!double.TryParse(value.Substring(0, i + 1), NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
                return null;

            return unitStr switch
            {
                "pt"          => number,
                "px"          => number * (72.0 / 96.0),
                "in"          => number * 72.0,
                "cm"          => number * (72.0 / 2.54),
                "mm"          => number * (72.0 / 25.4),
                "pc"          => number * 12.0,
                _             => (double?)null,
            };
        }

        /// <summary>
        /// Applies style to all boxes in the tree.<br/>
        /// If the html tag has style defined for each apply that style to the css box of the tag.<br/>
        /// If the html tag has "class" attribute and the class name has style defined apply that style on the tag css box.<br/>
        /// If the html tag has "style" attribute parse it and apply the parsed style on the tag css box.<br/>
        /// </summary>
        /// <param name="valueParser">the css value parser to use</param>
        /// <param name="box">the box to apply the style to</param>
        /// <param name="cssData">the style data for the html</param>
        /// <param name="media">The media type to apply styles to</param>
        private static void CascadeApplyStyles(CssValueParser valueParser, CssBox box, CssData cssData, string media)
        {
            // 1. Set CSS spec initial values
            foreach (var style in CssDefaults.InitialValues)
                CssUtils.SetPropertyValue(valueParser, box, style.Key, style.Value);

            // 2. Inherit inheritable properties from parent
            box.InheritStyle();

            // Regular property declarations whose value contains var(...) are deferred here and resolved
            // once, after the whole cascade (all phases below) has finished, so var() sees this box's
            // FINAL custom property values regardless of which phase declared them.
            var pendingVarProperties = new Dictionary<string, string>();

            // Matched rules are already sorted by CssData (specificity ascending, then true source
            // order) - materialize each origin once so both the normal and important passes below
            // reuse the same matched/sorted list instead of re-querying CssData twice per origin.
            var uaRules = cssData.GetUserAgentStyleRules(media, box).ToList();
            var authorRules = cssData.GetAuthorStyleRules(media, box).ToList();

            // Inline style is parsed up front - parsing is pure text -> rule with no cascade side
            // effects, so hoisting it here (ahead of TranslateAttributes, which still runs at its
            // original point below) doesn't change behavior, and lets RulesUseRevertKeyword below
            // see it too.
            IStyleRule? inlineRule = null;
            if (box.HtmlTag != null && box.HtmlTag.HasAttribute("style"))
            {
                var styleAttributeText = box.HtmlTag.TryGetAttribute("style");
                var block = CssParser.ParseStyleSheet("* { " + styleAttributeText + " }");
                inlineRule = block.StyleRules.Single();
            }

            // The relatively expensive property/custom-property snapshots below are only ever read
            // back when a later phase's declaration is literally revert/revert-layer (see
            // AssignCssBlock/AssignCustomPropertyDeclaration), which is rare - so each is only
            // captured when something that will actually consult it uses one of those keywords.
            var authorUsesRevert = RulesUseRevertKeyword(authorRules);
            var uaUsesRevert = RulesUseRevertKeyword(uaRules);
            var inlineUsesRevert = inlineRule is not null && RulesUseRevertKeyword([inlineRule]);

            // Cascade precedence (lowest to highest, i.e. applied in this order so "last write wins"
            // naturally produces the correct winner): UA-normal, Author-normal (inline-normal wins
            // ties within this tier), Author-!important (inline-!important wins ties within this
            // tier), UA-!important (per spec, origin order REVERSES for !important, so it's applied -
            // and wins - last). Each phase's revert/revert-layer target is a snapshot of the box's
            // state immediately before that phase ran - generalizing the pre-existing (and already
            // tested) "revert = state right before this phase" model uniformly across all six phases,
            // rather than a stricter, more formally origin-pure model.

            // 3. UA normal
            AssignCssBlocks(valueParser, box, uaRules, importantPass: false, null, null, pendingVarProperties);
            var needsUaSnapshot = authorUsesRevert;
            var uaSnapshot = needsUaSnapshot ? CssUtils.SnapshotProperties(box) : null;
            var uaCustomSnapshot = needsUaSnapshot ? CssUtils.SnapshotCustomProperties(box) : null;

            // 4. Author normal
            AssignCssBlocks(valueParser, box, authorRules, importantPass: false, uaSnapshot, uaCustomSnapshot, pendingVarProperties);
            var needsAuthorNormalSnapshot = inlineUsesRevert || authorUsesRevert;
            var authorNormalSnapshot = needsAuthorNormalSnapshot ? CssUtils.SnapshotProperties(box) : null;
            var authorNormalCustomSnapshot = needsAuthorNormalSnapshot ? CssUtils.SnapshotCustomProperties(box) : null;

            if (box.HtmlTag != null)
            {
                TranslateAttributes(box.HtmlTag, box);
            }

            // 5. Inline normal; revert target is the author-normal-applied state (unchanged from
            // before this restructure)
            var inlineNormalSnapshot = authorNormalSnapshot;
            var inlineNormalCustomSnapshot = authorNormalCustomSnapshot;
            if (inlineRule is not null)
            {
                AssignCssBlock(valueParser, box, inlineRule, importantPass: false, authorNormalSnapshot, authorNormalCustomSnapshot, pendingVarProperties);
                var needsInlineNormalSnapshot = authorUsesRevert;
                inlineNormalSnapshot = needsInlineNormalSnapshot ? CssUtils.SnapshotProperties(box) : null;
                inlineNormalCustomSnapshot = needsInlineNormalSnapshot ? CssUtils.SnapshotCustomProperties(box) : null;
            }

            // 6. Author !important. Note: this means an author-!important "revert" can roll back to
            // a value inline *normal* style just set, not all the way back to the UA snapshot - a
            // deliberate choice for mechanical consistency with the rest of this "snapshot = state
            // right before this phase" model, rather than a stricter per-origin reading of the spec.
            // The built-in UA stylesheet has no !important rules, so this combination (revert inside
            // an author-!important declaration, interacting with a preceding inline-normal value) is
            // untested territory in real usage; documenting it here rather than resolving it silently.
            AssignCssBlocks(valueParser, box, authorRules, importantPass: true, inlineNormalSnapshot, inlineNormalCustomSnapshot, pendingVarProperties);
            var needsAuthorImportantSnapshot = inlineUsesRevert || uaUsesRevert;
            var authorImportantSnapshot = needsAuthorImportantSnapshot ? CssUtils.SnapshotProperties(box) : null;
            var authorImportantCustomSnapshot = needsAuthorImportantSnapshot ? CssUtils.SnapshotCustomProperties(box) : null;

            // 7. Inline !important
            var afterInlineImportantSnapshot = authorImportantSnapshot;
            var afterInlineImportantCustomSnapshot = authorImportantCustomSnapshot;
            if (inlineRule is not null)
            {
                AssignCssBlock(valueParser, box, inlineRule, importantPass: true, authorImportantSnapshot, authorImportantCustomSnapshot, pendingVarProperties);
                var needsAfterInlineImportantSnapshot = uaUsesRevert;
                afterInlineImportantSnapshot = needsAfterInlineImportantSnapshot ? CssUtils.SnapshotProperties(box) : null;
                afterInlineImportantCustomSnapshot = needsAfterInlineImportantSnapshot ? CssUtils.SnapshotCustomProperties(box) : null;
            }

            // 8. UA !important - applied globally last so it wins over everything else, per spec's
            // origin reversal for the !important tier.
            AssignCssBlocks(valueParser, box, uaRules, importantPass: true, afterInlineImportantSnapshot, afterInlineImportantCustomSnapshot, pendingVarProperties);

            // 9. Resolve var() references now that every custom property's final cascaded value is known
            ResolveDeferredVarProperties(valueParser, box, pendingVarProperties);

            // Correct current color
            CssUtils.ApplyCurrentColor(box, valueParser);

            // cascade text decoration only to boxes that actually have text so it will be handled correctly.
            if (box.TextDecoration != string.Empty && box.Text == null)
            {
                foreach (var childBox in box.Boxes)
                {
                    childBox.TextDecoration = box.TextDecoration;
                    childBox.TextDecorationLine = box.TextDecorationLine;
                    childBox.TextDecorationStyle = box.TextDecorationStyle;
                    childBox.TextDecorationColor = box.TextDecorationColor;
                }

                box.TextDecoration = string.Empty;
                box.TextDecorationLine = string.Empty;
                box.TextDecorationStyle = string.Empty;
                box.TextDecorationColor = string.Empty;
            }

            if (!box.FirstLineProcessed)
            {
                box.FirstLineProcessed = true;
                ResolveFirstLineStyle(valueParser, box, cssData, media);
            }

            foreach (var childBox in box.Boxes)
            {
                CascadeApplyStyles(valueParser, childBox, cssData, media);
            }
        }

        /// <summary>
        /// Resolves <see cref="CssBox.ResolvedFirstLineStyle"/> for <paramref name="box"/>: gathers
        /// whichever stylesheet rules actually apply via a <c>*::first-line</c> selector (see
        /// <see cref="CssData.GetFirstLineStyleRules"/> - deliberately NOT the same <c>uaRules</c>/
        /// <c>authorRules</c> already computed above for <paramref name="box"/>'s own normal cascade,
        /// since the ordinary matcher those rely on always excludes first-line-suffixed selectors, to
        /// avoid ever applying first-line-only declarations directly to the real box), and - if any do -
        /// applies just those declarations, in the same UA-normal / author-normal / author-important /
        /// UA-important order <see cref="CascadeApplyStyles"/> itself uses, to a throwaway shadow
        /// <see cref="CssBox"/> seeded from <paramref name="box"/>'s own already-resolved style via
        /// <see cref="CssBox.InheritStyle(CssBox?, bool)"/>. Unlike the real cascade, <c>revert</c>/
        /// <c>revert-layer</c> targets aren't tracked here (accepted as an unlikely-to-matter
        /// simplification for what's already a narrow combination); everything else reuses the exact
        /// same declaration-application machinery as the real cascade. No inline-style handling either -
        /// inline style can never carry a <c>::first-line</c> suffix, so it plays no part here.
        /// </summary>
        private static void ResolveFirstLineStyle(CssValueParser valueParser, CssBox box, CssData cssData, string media)
        {
            var firstLineUaRules = cssData.GetFirstLineStyleRules(media, box, userAgentOnly: true).ToList();
            var firstLineAuthorRules = cssData.GetFirstLineStyleRules(media, box, userAgentOnly: false).ToList();

            if (firstLineUaRules.Count == 0 && firstLineAuthorRules.Count == 0) return;

            var shadowBox = new CssBox(box, null);
            box.Boxes.Remove(shadowBox); // this is a detached resolution helper, never a real tree member
            shadowBox.InheritStyle(box);

            var pendingVarProperties = new Dictionary<string, string>();
            AssignCssBlocks(valueParser, shadowBox, firstLineUaRules, importantPass: false, null, null, pendingVarProperties);
            AssignCssBlocks(valueParser, shadowBox, firstLineAuthorRules, importantPass: false, null, null, pendingVarProperties);
            AssignCssBlocks(valueParser, shadowBox, firstLineAuthorRules, importantPass: true, null, null, pendingVarProperties);
            AssignCssBlocks(valueParser, shadowBox, firstLineUaRules, importantPass: true, null, null, pendingVarProperties);
            ResolveDeferredVarProperties(valueParser, shadowBox, pendingVarProperties);
            CssUtils.ApplyCurrentColor(shadowBox, valueParser);

            box.ResolvedFirstLineStyle = shadowBox;
        }

        /// <summary>
        /// Ensures every box whose <c>Display</c> resolves to <c>list-item</c> has a synthesized
        /// <c>::marker</c> child, per CSS2.1 12.5.1 / CSS Lists Level 3 - marker generation is driven
        /// by the *computed* <c>Display</c> value, not by any particular selector or tag. The common
        /// <c>&lt;li&gt;</c> case already gets one during <see cref="CascadeApplyStyles"/> above (via
        /// the UA stylesheet's <c>li::marker</c> rule's selector-match-time synthesis in
        /// <see cref="CssData.DoesSelectorMatch(CSS.CompoundSelector, CssBox?)"/>) - this only needs to
        /// cover elements that reach <c>Display: list-item</c> WITHOUT that selector matching (e.g.
        /// <c>div { display: list-item }</c>), since selector matching can't key off a computed
        /// <c>Display</c> value (it isn't resolved yet at match time within a cascade pass). Must run
        /// after <see cref="CascadeApplyStyles"/> (so <c>Display</c> is resolved) and before
        /// <see cref="CorrectTextBoxes"/> (so the new box's content gets resolved by that same pass,
        /// same as every other marker box).
        /// </summary>
        private static void EnsureListItemMarkers(CssValueParser valueParser, CssBox box, CssData cssData, string media)
        {
            if (box.Display == CssConstants.ListItem && !box.Boxes.Any(b => b.IsMarkerPseudoElement))
            {
                var markerBox = new CssBoxMarker(box);
                box.Boxes.Remove(markerBox);
                box.Boxes.Insert(0, markerBox);
                markerBox.InheritStyle(box);
                CascadeApplyStyles(valueParser, markerBox, cssData, media);
            }

            foreach (var childBox in box.Boxes.ToArray())
            {
                if (!childBox.IsMarkerPseudoElement)
                {
                    EnsureListItemMarkers(valueParser, childBox, cssData, media);
                }
            }
        }

        /// <summary>
        /// Synthesizes a <c>::first-letter</c> pseudo-element for every box flagged by
        /// <see cref="CssBox.MatchesFirstLetterSelector"/> during <see cref="CascadeApplyStyles"/>
        /// above, by splitting the first letter (CSS1 §1.2: including any immediately-preceding
        /// punctuation) off the box's first real text-bearing descendant. Must run after
        /// <see cref="CascadeApplyStyles"/> (so descendant <c>Display</c> values are resolved - needed
        /// to correctly stop at block-level boundaries) and before <see cref="CorrectTextBoxes"/> (so
        /// the new box's content gets word-parsed by that same pass, like every other pseudo-element).
        /// Unlike <c>::before</c>/<c>::after</c>/<c>::marker</c> (synthesized as a new child of the
        /// matched element itself, inline within <c>CascadeApplyStyles</c>' own per-box processing),
        /// the split point here is a descendant text box possibly several inline levels below the
        /// matched element - see <see cref="CssBox.MatchesFirstLetterSelector"/>'s doc comment for why
        /// that forces this into a separate, later pass instead.
        /// </summary>
        private static void ApplyFirstLetterPseudoElements(CssValueParser valueParser, CssBox box, CssData cssData, string media)
        {
            if (box.MatchesFirstLetterSelector && !box.FirstLetterProcessed)
            {
                box.FirstLetterProcessed = true;

                var textBox = FindFirstLetterTargetTextBox(box);
                if (textBox != null)
                {
                    SplitFirstLetter(valueParser, cssData, media, box, textBox);
                }
            }

            foreach (var childBox in box.Boxes.ToArray())
            {
                if (!childBox.IsFirstLetterPseudoElement)
                {
                    ApplyFirstLetterPseudoElements(valueParser, childBox, cssData, media);
                }
            }
        }

        /// <summary>
        /// Depth-first, first-child-first search for the first descendant of <paramref name="box"/>
        /// carrying real, non-whitespace element text - the target for a <c>::first-letter</c> split.
        /// Stops at (does not descend into) any descendant that starts its own independent formatting
        /// context (block-level, table-parts, or an atomic inline-level box like inline-block) - CSS1's
        /// "first formatted line"/first-letter concept is scoped to <paramref name="box"/>'s own inline
        /// content, not a nested one. Also skips <c>::before</c>-generated content: first-letter targets
        /// the element's own real text only (a documented narrowing versus full CSS2.1, which does allow
        /// targeting generated content in some cases).
        /// </summary>
        private static CssBox? FindFirstLetterTargetTextBox(CssBox box)
        {
            foreach (var child in box.Boxes)
            {
                if (child.IsBeforePseudoElement || child.IsMarkerPseudoElement)
                    continue;

                if (child.Text != null)
                {
                    if (!string.IsNullOrWhiteSpace(child.Text))
                        return child;
                    continue;
                }

                if (IsFirstLetterScopeBoundary(child))
                    continue;

                var found = FindFirstLetterTargetTextBox(child);
                if (found != null) return found;
            }

            return null;
        }

        private static bool IsFirstLetterScopeBoundary(CssBox box) =>
            box.Display is CssConstants.Block or CssConstants.Table or CssConstants.TableRow
                or CssConstants.TableRowGroup or CssConstants.TableCell or CssConstants.ListItem
                or CssConstants.Flex or CssConstants.InlineBlock or CssConstants.InlineTable
                or CssConstants.InlineFlex;

        /// <summary>
        /// Splits <paramref name="textBox"/>'s text at the CSS1 §1.2 "first letter" boundary (skipping
        /// leading whitespace, then any leading run of Unicode punctuation categories Ps/Pe/Pi/Pf/Po
        /// immediately followed by one more character), inserting a new box holding that first-letter
        /// substring as <paramref name="textBox"/>'s preceding sibling and truncating
        /// <paramref name="textBox"/> to the remainder. The new box's <see cref="CssBox.ParentBox"/> is
        /// <paramref name="textBox"/>'s own real structural parent (so it inherits normally, e.g. bold
        /// from a nested <c>&lt;b&gt;</c>), while <see cref="CssBox.FirstLetterOriginatingBox"/> is set
        /// to <paramref name="originatingBox"/> (<c>E</c>, the element the <c>::first-letter</c>
        /// selector actually matched) purely for selector re-matching. Gives the new box a full,
        /// independent <see cref="CascadeApplyStyles"/> pass of its own - not just
        /// <see cref="CssBox.InheritStyle(CssBox?, bool)"/> - so author <c>E::first-letter</c>
        /// declarations actually apply on top of that inherited baseline, the same as
        /// <see cref="EnsureListItemMarkers"/> does for its own synthesized marker box.
        /// </summary>
        private static void SplitFirstLetter(CssValueParser valueParser, CssData cssData, string media, CssBox originatingBox, CssBox textBox)
        {
            var text = textBox.Text!;
            var idx = 0;

            while (idx < text.Length && char.IsWhiteSpace(text[idx]))
                idx++;

            if (idx >= text.Length) return;

            var letterStart = idx;

            while (idx < text.Length && IsFirstLetterPunctuation(text[idx]))
                idx++;

            if (idx < text.Length)
                idx++;

            var firstLetterText = text[letterStart..idx];
            var remainder = text[idx..];

            var parentBox = textBox.ParentBox!;
            var insertIndex = parentBox.Boxes.IndexOf(textBox);

            var firstLetterBox = new CssBox(parentBox, null)
            {
                IsFirstLetterPseudoElement = true,
                FirstLetterOriginatingBox = originatingBox,
                Text = firstLetterText
            };

            parentBox.Boxes.Remove(firstLetterBox);
            parentBox.Boxes.Insert(insertIndex, firstLetterBox);

            textBox.Text = remainder;

            firstLetterBox.InheritStyle(parentBox);
            CascadeApplyStyles(valueParser, firstLetterBox, cssData, media);
        }

        /// <summary>
        /// CSS1 §1.2's first-letter punctuation categories: Ps/Pe/Pi/Pf/Po (open/close/initial-quote/
        /// final-quote/other punctuation) - deliberately excludes Pd (dash) and Pc (connector).
        /// </summary>
        private static bool IsFirstLetterPunctuation(char c)
        {
            var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            return category is System.Globalization.UnicodeCategory.OpenPunctuation
                or System.Globalization.UnicodeCategory.ClosePunctuation
                or System.Globalization.UnicodeCategory.InitialQuotePunctuation
                or System.Globalization.UnicodeCategory.FinalQuotePunctuation
                or System.Globalization.UnicodeCategory.OtherPunctuation;
        }

        /// <summary>
        /// Assigns the given css style rules to the given css box, applying only the declarations
        /// whose <c>!important</c> flag matches <paramref name="importantPass"/>.
        /// </summary>
        private static void AssignCssBlocks(
            CssValueParser valueParser,
            CssBox box,
            IEnumerable<IStyleRule> rules,
            bool importantPass,
            IReadOnlyDictionary<string, string?>? revertTarget,
            IReadOnlyDictionary<string, string>? customPropertyRevertTarget,
            Dictionary<string, string> pendingVarProperties)
        {
            foreach (var rule in rules)
                AssignCssBlock(valueParser, box, rule, importantPass, revertTarget, customPropertyRevertTarget, pendingVarProperties);
        }

        /// <summary>
        /// Checks whether any declaration in the given rules literally uses the revert/revert-layer keyword,
        /// so the (relatively expensive) property snapshot used as their revert target only needs to be
        /// captured when it can actually be consulted.
        /// </summary>
        private static bool RulesUseRevertKeyword(IEnumerable<IStyleRule> rules)
        {
            foreach (var rule in rules)
            {
                foreach (var prop in rule.Style)
                {
                    if (prop.Value is CssConstants.Revert or CssConstants.RevertLayer)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Assigns the given css style block properties to the given css box, applying only the
        /// declarations whose <c>!important</c> flag matches <paramref name="importantPass"/> (the
        /// caller is expected to call this once per origin per pass - see <see cref="CascadeApplyStyles"/>
        /// - so that within a properly-ordered sequence of calls, "last write wins" alone produces the
        /// spec-correct result without needing to track which property names were already locked by an
        /// earlier !important declaration).
        /// Handles all five CSS global keywords: inherit, initial, unset, revert, revert-layer.
        /// Custom property declarations (--foo) are routed to <see cref="AssignCustomPropertyDeclaration"/> instead,
        /// since those keywords mean something different for an open-ended, case-sensitive property store.
        /// Regular declarations whose value contains var(...) are deferred into <paramref name="pendingVarProperties"/>
        /// rather than applied immediately — see <see cref="ResolveDeferredVarProperties"/> for why.
        /// </summary>
        /// <param name="valueParser">the css value parser to use</param>
        /// <param name="box">the css box to assign css to</param>
        /// <param name="stylesheetRule">the stylesheet rule to assign</param>
        /// <param name="importantPass">true to apply only <c>!important</c> declarations, false to apply only normal ones</param>
        /// <param name="revertTarget">Property snapshot representing the prior cascade origin, used for revert/revert-layer</param>
        /// <param name="customPropertyRevertTarget">Case-sensitive custom-property snapshot for revert/revert-layer</param>
        /// <param name="pendingVarProperties">Accumulates regular declarations whose value contains var(...), keyed by property name</param>
        private static void AssignCssBlock(
            CssValueParser valueParser,
            CssBox box,
            IStyleRule stylesheetRule,
            bool importantPass,
            IReadOnlyDictionary<string, string?>? revertTarget,
            IReadOnlyDictionary<string, string>? customPropertyRevertTarget,
            Dictionary<string, string> pendingVarProperties)
        {
            foreach (var prop in stylesheetRule.Style)
            {
                if (prop.IsImportant != importantPass)
                    continue;

                if (PropertyFactory.IsCustomPropertyName(prop.Name))
                {
                    AssignCustomPropertyDeclaration(box, prop, customPropertyRevertTarget);
                    continue;
                }

                var value = prop.Value switch
                {
                    CssConstants.Inherit when box.ParentBox != null
                        => CssUtils.GetPropertyValue(box.ParentBox, prop.Name),
                    CssConstants.Inherit
                        => CssDefaults.GetInitialValue(prop.Name),
                    CssConstants.Initial
                        => CssDefaults.GetInitialValue(prop.Name),
                    CssConstants.Unset when CssDefaults.InheritedProperties.Contains(prop.Name) && box.ParentBox != null
                        => CssUtils.GetPropertyValue(box.ParentBox, prop.Name),
                    CssConstants.Unset
                        => CssDefaults.GetInitialValue(prop.Name),
                    CssConstants.Revert or CssConstants.RevertLayer
                        => revertTarget is not null && revertTarget.TryGetValue(prop.Name, out var rv)
                            ? rv
                            : CssDefaults.GetInitialValue(prop.Name),
                    _ => prop.Value
                };

                if (value is null) continue;

                if (value.Contains("var(", StringComparison.OrdinalIgnoreCase))
                {
                    // Overwrites any earlier pending entry for this name (last write wins).
                    pendingVarProperties[prop.Name] = value;
                }
                else
                {
                    // A later plain value supersedes an earlier deferred var() value for the same property.
                    pendingVarProperties.Remove(prop.Name);
                    if (IsStyleOnElementAllowed(box, prop.Name, value))
                        CssUtils.SetPropertyValue(valueParser, box, prop.Name, value);
                }
            }
        }

        /// <summary>
        /// Assigns a single custom property (--foo) declaration to the box's custom-property store.
        /// Unlike regular properties, custom properties are always inherited, their names are case-sensitive,
        /// and their global keywords resolve against the custom-property dictionary rather than the fixed,
        /// known-property switch used by <see cref="AssignCssBlock"/>. The stored value is left unresolved
        /// (it may itself contain var(...)) — resolution happens once, later, in <see cref="ResolveDeferredVarProperties"/>,
        /// which is what makes multi-hop var() graph resolution correct regardless of declaration order.
        /// </summary>
        private static void AssignCustomPropertyDeclaration(
            CssBox box,
            IProperty prop,
            IReadOnlyDictionary<string, string>? customPropertyRevertTarget)
        {
            var rawValue = prop.Value switch
            {
                CssConstants.Inherit or CssConstants.Unset // custom properties are always inherited
                    => box.ParentBox?.CustomProperties != null &&
                       box.ParentBox.CustomProperties.TryGetValue(prop.Name, out var pv)
                        ? pv
                        : null,
                CssConstants.Initial
                    => null, // guaranteed-invalid value => property becomes absent
                CssConstants.Revert or CssConstants.RevertLayer
                    => customPropertyRevertTarget != null &&
                       customPropertyRevertTarget.TryGetValue(prop.Name, out var rv)
                        ? rv
                        : null,
                _ => prop.Value
            };

            box.CustomProperties ??= new Dictionary<string, string>();
            if (rawValue is not null)
                box.CustomProperties[prop.Name] = rawValue;
            else
                box.CustomProperties.Remove(prop.Name);
        }

        /// <summary>
        /// Result of attempting to resolve every var() reference in a declaration's value. Success is false
        /// only when a reference is "guaranteed-invalid" (no matching custom property, no fallback, or a
        /// cyclic reference) — per spec this invalidates the whole value, not just the failing substring.
        /// </summary>
        private readonly record struct VarResolution(bool Success, string? Value);

        /// <summary>
        /// Resolves every regular property deferred during the cascade's three phases (see
        /// <see cref="AssignCssBlock"/>) now that this box's custom properties reflect their FINAL cascaded
        /// (though not yet var()-resolved) values. Resolution is graph-based and memoized per box via a shared
        /// `resolvedCache`/`resolving`/`cyclic` triple, so multi-hop cyclic references (e.g. --a: var(--b);
        /// --b: var(--c); --c: var(--a);) are detected correctly regardless of which pending property triggers
        /// the lookup first.
        /// </summary>
        private static void ResolveDeferredVarProperties(CssValueParser valueParser, CssBox box, Dictionary<string, string> pendingVarProperties)
        {
            if (pendingVarProperties.Count == 0) return;

            var resolvedCache = new Dictionary<string, string>();
            var resolving = new HashSet<string>();
            var cyclic = new HashSet<string>();

            foreach (var (name, rawValue) in pendingVarProperties)
            {
                var result = SubstituteVarReferences(box, rawValue, resolvedCache, resolving, cyclic);
                var finalValue = result.Success ? result.Value : GetGuaranteedInvalidFallback(box, name);

                if (finalValue is not null)
                    ApplyResolvedPropertyValue(valueParser, box, name, finalValue);
            }
        }

        /// <summary>
        /// Applies a fully var()-resolved property value to the box. Shorthand properties (e.g. "background")
        /// are re-parsed through the real CSS-OM shorthand converter and expanded into longhands here, because
        /// unlike margin/padding/border/font/flex/list-style, not every shorthand has a "whole string" case in
        /// <see cref="CssUtils.SetPropertyValue"/> — that switch normally only ever receives longhands, since
        /// shorthands are expanded into them at parse time (<c>StyleDeclaration.SetShorthand</c>), a step
        /// var()-containing shorthands deliberately skip (see <c>StylesheetComposer.FillDeclarations</c>)
        /// because they can't be split into per-longhand slices until their var() references are resolved,
        /// which has only just happened here.
        /// </summary>
        private static void ApplyResolvedPropertyValue(CssValueParser valueParser, CssBox box, string name, string value)
        {
            // Re-parse through the real Layer A converter for every var()-resolved declaration, not just
            // shorthands, so calc()-family (and any other) expressions are validated and canonicalized the
            // same way a literal declaration would be, instead of being handed to Layer B unchecked.
            var reparsed = StylesheetParser.Default.ParseDeclaration($"{name}: {value}");

            if (reparsed is ShorthandProperty { HasValue: true } shorthand)
            {
                var longhands = PropertyFactory.Instance.CreateLonghandsFor(name);
                shorthand.Export(longhands);

                foreach (var longhand in longhands)
                {
                    // A sub-property the shorthand text didn't mention must still reset to its CSS-spec
                    // initial value (e.g. a prior "font-style: italic" must not survive a later
                    // "font: bold var(--sz) Arial") - matching what the non-var() shorthand path
                    // (StyleDeclaration.SetShorthand/ShorthandProperty.Export) does. Export now gives an
                    // omitted longhand HasValue:true with the literal "initial" sentinel (rather than
                    // HasValue:false) so it can win the normal cascade against an earlier rule's real
                    // value - AssignCssBlock's switch resolves that sentinel for the non-var() path, but
                    // this var()-resolution path calls SetPropertyValue directly, bypassing that switch,
                    // so it must resolve the sentinel itself here (same as an explicit HasValue:false).
                    var longhandValue = longhand.HasValue && longhand.Value != CssConstants.Initial
                        ? longhand.Value
                        : CssDefaults.GetInitialValue(longhand.Name);
                    if (longhandValue is not null && IsStyleOnElementAllowed(box, longhand.Name, longhandValue))
                        CssUtils.SetPropertyValue(valueParser, box, longhand.Name, longhandValue);
                }

                return;
            }

            if (reparsed is { HasValue: true } property && IsStyleOnElementAllowed(box, name, property.Value))
                CssUtils.SetPropertyValue(valueParser, box, name, property.Value);
        }

        /// <summary>
        /// Resolves every var(...) occurrence in <paramref name="value"/> to plain text. Quote-aware (so
        /// `content: "var(--x)"` is left untouched) and paren-depth-aware (so a fallback containing nested
        /// commas, e.g. `var(--a, var(--b, red))` or a fallback with a comma-taking function, splits correctly).
        /// </summary>
        private static VarResolution SubstituteVarReferences(CssBox box, string value, Dictionary<string, string> resolvedCache, HashSet<string> resolving, HashSet<string> cyclic)
        {
            if (value.IndexOf("var(", StringComparison.OrdinalIgnoreCase) < 0)
                return new VarResolution(true, value);

            var sb = new StringBuilder();
            var i = 0;
            while (i < value.Length)
            {
                if (TryMatchVarCall(value, i, out var argsStart, out var callEnd))
                {
                    var (name, fallback) = SplitFirstTopLevelComma(value, argsStart, callEnd - 1);

                    if (TryResolveCustomProperty(box, name, resolvedCache, resolving, cyclic, out var found))
                    {
                        sb.Append(found);
                    }
                    else if (fallback != null)
                    {
                        var fallbackResult = SubstituteVarReferences(box, fallback, resolvedCache, resolving, cyclic);
                        if (!fallbackResult.Success) return new VarResolution(false, null);
                        sb.Append(fallbackResult.Value);
                    }
                    else
                    {
                        return new VarResolution(false, null); // guaranteed-invalid
                    }

                    i = callEnd;
                }
                else if (value[i] is '"' or '\'')
                {
                    var quoteEnd = SkipQuotedString(value, i);
                    sb.Append(value, i, quoteEnd - i);
                    i = quoteEnd;
                }
                else
                {
                    sb.Append(value[i]);
                    i++;
                }
            }

            return new VarResolution(true, sb.ToString());
        }

        /// <summary>
        /// Recursive + memoized: resolves box.CustomProperties[name]'s own var() references first (so a custom
        /// property may reference another custom property, in any declaration order), using `resolving` as a
        /// visited-set for cycle detection across the whole reference graph for this box.
        /// <paramref name="cyclic"/> permanently marks a property that was found to (directly or transitively)
        /// reference itself. This is distinct from a plain "not found" — per the CSS spec, a property that
        /// references itself is guaranteed-invalid regardless of any fallback written inside that same
        /// reference (e.g. `--self: var(--self, red);` must NOT resolve to "red": writing var(--self, ...)
        /// inside --self's own definition is a self-reference, full stop, matching real browsers). Without
        /// this permanent marker, the fallback used to locally satisfy the mid-cycle lookup would get cached
        /// as if it were --self's legitimately resolved value.
        /// </summary>
        private static bool TryResolveCustomProperty(CssBox box, string name, Dictionary<string, string> resolvedCache, HashSet<string> resolving, HashSet<string> cyclic, out string? value)
        {
            if (cyclic.Contains(name))
            {
                value = null;
                return false;
            }

            if (resolvedCache.TryGetValue(name, out value)) return true;

            if (box.CustomProperties == null || !box.CustomProperties.TryGetValue(name, out var rawValue))
            {
                value = null;
                return false;
            }

            if (!resolving.Add(name))
            {
                cyclic.Add(name); // name is referenced while already being resolved — a cycle
                value = null;
                return false;
            }

            var result = SubstituteVarReferences(box, rawValue, resolvedCache, resolving, cyclic);
            resolving.Remove(name);

            if (cyclic.Contains(name) || !result.Success)
            {
                cyclic.Add(name);
                value = null;
                return false;
            }

            resolvedCache[name] = value = result.Value!;
            return true;
        }

        /// <summary>
        /// Matches a case-insensitive "var(" starting at <paramref name="start"/> and scans forward for the
        /// matching close paren. On success, <paramref name="argsStart"/> is the index of the first argument
        /// character and <paramref name="callEnd"/> is the index just past the closing paren.
        /// </summary>
        private static bool TryMatchVarCall(string value, int start, out int argsStart, out int callEnd)
        {
            argsStart = 0;
            callEnd = 0;

            if (start + 4 > value.Length) return false;
            if (string.Compare(value, start, "var(", 0, 4, StringComparison.OrdinalIgnoreCase) != 0) return false;

            var depth = 1;
            var i = start + 4;
            while (i < value.Length)
            {
                var c = value[i];
                if (c is '"' or '\'')
                {
                    i = SkipQuotedString(value, i);
                    continue;
                }

                if (c == '(') depth++;
                else if (c == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        argsStart = start + 4;
                        callEnd = i + 1;
                        return true;
                    }
                }

                i++;
            }

            return false;
        }

        /// <summary>
        /// Splits a var() argument list at the first top-level (paren-depth 0, outside quotes) comma.
        /// The text before it is the custom property name (trimmed); everything after — commas and all —
        /// is the fallback, per spec.
        /// </summary>
        private static (string Name, string? Fallback) SplitFirstTopLevelComma(string value, int start, int end)
        {
            var depth = 0;
            var i = start;
            while (i < end)
            {
                var c = value[i];
                if (c is '"' or '\'')
                {
                    i = SkipQuotedString(value, i);
                    continue;
                }

                if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (c == ',' && depth == 0)
                {
                    var name = value[start..i].Trim();
                    var fallback = value[(i + 1)..end].Trim();
                    return (name, fallback.Length > 0 ? fallback : null);
                }

                i++;
            }

            return (value[start..end].Trim(), null);
        }

        /// <summary>
        /// Advances past a quoted string literal starting at <paramref name="start"/> (which must point at the
        /// opening quote), honoring backslash escapes so an escaped quote doesn't end the string early.
        /// Returns the index just past the closing quote (or the string's end, if unterminated).
        /// </summary>
        private static int SkipQuotedString(string value, int start)
        {
            var quote = value[start];
            var i = start + 1;
            while (i < value.Length)
            {
                if (value[i] == '\\' && i + 1 < value.Length)
                {
                    i += 2;
                    continue;
                }

                if (value[i] == quote)
                    return i + 1;

                i++;
            }

            return i;
        }

        /// <summary>
        /// The value a property falls back to when a var() reference in it is guaranteed-invalid — reuses
        /// the exact same expression as the `unset` keyword arm in <see cref="AssignCssBlock"/>, for consistency.
        /// </summary>
        private static string? GetGuaranteedInvalidFallback(CssBox box, string propName)
        {
            return CssDefaults.InheritedProperties.Contains(propName) && box.ParentBox != null
                ? CssUtils.GetPropertyValue(box.ParentBox, propName)
                : CssDefaults.GetInitialValue(propName);
        }

        /// <summary>
        /// Check if the given style is allowed to be set on the given css box.<br/>
        /// Used to prevent invalid CssBoxes creation like table with inline display style.
        /// </summary>
        /// <param name="box">the css box to assign css to</param>
        /// <param name="key">the style key to check</param>
        /// <param name="value">the style value to check</param>
        /// <returns>true - style allowed, false - not allowed</returns>
        private static bool IsStyleOnElementAllowed(CssBox box, string key, string value)
        {
            if (box.HtmlTag == null || key != HtmlConstants.Display) return true;

            if (value is CssConstants.None)
            {
                return true;
            }

            return box.HtmlTag.Name.ToLowerInvariant() switch
            {
                HtmlConstants.Table => value == CssConstants.Table,
                HtmlConstants.Tr => value == CssConstants.TableRow,
                HtmlConstants.Tbody => value == CssConstants.TableRowGroup,
                HtmlConstants.Thead => value == CssConstants.TableHeaderGroup,
                HtmlConstants.Tfoot => value == CssConstants.TableFooterGroup,
                HtmlConstants.Col => value == CssConstants.TableColumn,
                HtmlConstants.Colgroup => value == CssConstants.TableColumnGroup,
                HtmlConstants.Td or HtmlConstants.Th => value == CssConstants.TableCell,
                HtmlConstants.Caption => value == CssConstants.TableCaption,
                _ => true
            };
        }

        /// <summary>
        /// Clone css data if it has not already been cloned.<br/>
        /// Used to preserve the base css data used when changed by style inside html.
        /// </summary>
        private static void CloneCssData(ref CssData cssData, ref bool cssDataChanged)
        {
            if (cssDataChanged) return;

            cssDataChanged = true;
            cssData = cssData.Clone();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tag"></param>
        /// <param name="box"></param>
        private static void TranslateAttributes(HtmlTag tag, CssBox box)
        {
            if (!tag.HasAttributes()) return;

            foreach (var attKey in tag.Attributes!.Keys)
            {
                var value = tag.Attributes[attKey];
                var att = attKey.ToLowerInvariant();

                switch (att)
                {
                    case HtmlConstants.Align:
                        if (tag.Name.Equals(HtmlConstants.Img, StringComparison.OrdinalIgnoreCase))
                        {
                            switch (value)
                            {
                                case HtmlConstants.Left:
                                    box.VerticalAlign = CssConstants.Top;
                                    box.Float = CssConstants.Left;
                                    break;
                                case HtmlConstants.Right:
                                    box.VerticalAlign = CssConstants.Top;
                                    box.Float = CssConstants.Right;
                                    break;
                                case HtmlConstants.Bottom:
                                    box.VerticalAlign = CssConstants.Baseline;
                                    break;
                                case HtmlConstants.Middle:
                                    box.VerticalAlign = CssConstants.PeachBaselineMiddle;
                                    break;
                                case HtmlConstants.Top:
                                    box.VerticalAlign = CssConstants.Top;
                                    break;
                            }
                        }
                        else
                        {
                            if (value is HtmlConstants.Left or HtmlConstants.Center or HtmlConstants.Right or HtmlConstants.Justify)
                                box.TextAlign = value.ToLower();
                            else
                                box.VerticalAlign = value.ToLower();

                        }

                        break;
                    case HtmlConstants.Background:
                        box.BackgroundImages = [new CssImage.Url(value.ToLower())];
                        break;
                    case HtmlConstants.Bgcolor:
                        box.BackgroundColor = value.ToLower();
                        break;
                    case HtmlConstants.Border:
                        if (!string.IsNullOrEmpty(value) && value != "0")
                            box.BorderLeftStyle = box.BorderTopStyle = box.BorderRightStyle = box.BorderBottomStyle = CssConstants.Solid;
                        box.BorderLeftWidth = box.BorderTopWidth = box.BorderRightWidth = box.BorderBottomWidth = TranslateLength(value);

                        if (tag.Name.Equals(HtmlConstants.Table, StringComparison.OrdinalIgnoreCase))
                        {
                            if (value != "0")
                                ApplyTableBorder(box, "1px");
                        }
                        else
                        {
                            box.BorderTopStyle = box.BorderLeftStyle = box.BorderRightStyle = box.BorderBottomStyle = CssConstants.Solid;
                        }
                        break;
                    case HtmlConstants.Bordercolor:
                        box.BorderLeftColor = box.BorderTopColor = box.BorderRightColor = box.BorderBottomColor = value.ToLower();
                        break;
                    case HtmlConstants.Cellspacing:
                        box.BorderSpacing = TranslateLength(value);
                        break;
                    case HtmlConstants.Cellpadding:
                        ApplyTablePadding(box, value);
                        break;
                    case HtmlConstants.Color:
                        box.Color = value.ToLower();
                        break;
                    case HtmlConstants.Dir:
                        box.Direction = value.ToLower();
                        break;
                    case HtmlConstants.Face:
                        //box.FontFamily = _cssParser.ParseFontFamily(value);
                        throw new NotImplementedException();
                    case HtmlConstants.Height:
                        box.Height = TranslateLength(value);
                        break;
                    case HtmlConstants.Hspace:
                        box.MarginRight = box.MarginLeft = TranslateLength(value);
                        break;
                    case HtmlConstants.Nowrap:
                        box.WhiteSpace = CssConstants.NoWrap;
                        break;
                    case HtmlConstants.Size:
                        if (tag.Name.Equals(HtmlConstants.Hr, StringComparison.OrdinalIgnoreCase))
                            box.Height = TranslateLength(value);
                        else if (tag.Name.Equals(HtmlConstants.Font, StringComparison.OrdinalIgnoreCase))
                            box.FontSize = value;
                        break;
                    case HtmlConstants.Valign:
                        box.VerticalAlign = value.ToLower();
                        break;
                    case HtmlConstants.Vspace:
                        box.MarginTop = box.MarginBottom = TranslateLength(value);
                        break;
                    case HtmlConstants.Width:
                        box.Width = TranslateLength(value);
                        break;
                }
            }
        }

        /// <summary>
        /// Converts an HTML length into a Css length
        /// </summary>
        /// <param name="htmlLength"></param>
        /// <returns></returns>
        private static string TranslateLength(string htmlLength)
        {
            var len = new CssLength(htmlLength);

            return len.HasError ? string.Format(NumberFormatInfo.InvariantInfo, "{0}px", htmlLength) : htmlLength;
        }

        /// <summary>
        /// Cascades to the TD's the border specified in the TABLE tag.
        /// </summary>
        /// <param name="table"></param>
        /// <param name="border"></param>
        private static void ApplyTableBorder(CssBox table, string border)
        {
            SetForAllCells(table, cell =>
            {
                cell.BorderLeftStyle = cell.BorderTopStyle = cell.BorderRightStyle = cell.BorderBottomStyle = CssConstants.Solid;
                cell.BorderLeftWidth = cell.BorderTopWidth = cell.BorderRightWidth = cell.BorderBottomWidth = border;
            });
        }

        /// <summary>
        /// Cascades to the TD's the border specified in the TABLE tag.
        /// </summary>
        /// <param name="table"></param>
        /// <param name="padding"></param>
        private static void ApplyTablePadding(CssBox table, string padding)
        {
            var length = TranslateLength(padding);
            SetForAllCells(table, cell => cell.PaddingLeft = cell.PaddingTop = cell.PaddingRight = cell.PaddingBottom = length);
        }

        /// <summary>
        /// Execute action on all the "td" cells of the table.<br/>
        /// Handle if there is "theader" or "tbody" exists.
        /// </summary>
        /// <param name="table">the table element</param>
        /// <param name="action">the action to execute</param>
        private static void SetForAllCells(CssBox table, Action<CssBox> action)
        {
            foreach (var l1 in table.Boxes)
            {
                foreach (var l2 in l1.Boxes)
                {
                    if (l2.HtmlTag is { Name: "td" })
                    {
                        action(l2);
                    }
                    else
                    {
                        foreach (var l3 in l2.Boxes)
                        {
                            action(l3);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Go over all the text boxes (boxes that have some text that will be rendered) and
        /// remove all boxes that have only white-spaces but are not 'preformatted' so they do not effect
        /// the rendered html.
        /// </summary>
        /// <param name="box">the current box to correct its sub-tree</param>
        private static void CorrectTextBoxes(CssBox box)
        {
            for (var i = box.Boxes.Count - 1; i >= 0; i--)
            {
                var childBox = box.Boxes[i];

                CssContentEngine.ApplyContent(childBox);

                if (childBox is CssBoxMarker markerBox)
                {
                    markerBox.ResolveDefaultContent();
                }

                // Per CSS2.1 §12.1/CSS Content Level 3, "normal" computes to "none" for ::before/::after
                // specifically - no box is generated at all, not merely an empty one. Without this, a
                // ::before/::after matched only by a rule that never sets `content` (e.g. PeachPDF's own
                // default UA stylesheet's blanket ":before, :after { white-space: pre-line }" in
                // CssDefaults.cs, which matches every element) leaves a real, empty, Display:inline
                // CssBox on every element in every document - defeating any "this box has no real
                // content" check elsewhere that inspects Boxes (e.g. CssBox.ActsAsInline's guard against
                // misclassifying a genuinely empty block box as an inline-only wrapper, which broke
                // Acid2's own "#eyes-c" - a plain empty block meant to paint bottom-most per Appendix E -
                // into painting in the wrong stacking pass entirely).
                if ((childBox.IsBeforePseudoElement || childBox.IsAfterPseudoElement)
                    && childBox.Content is CssConstants.None or CssConstants.Normal
                    && childBox.ContentImage is null)
                {
                    box.Boxes.RemoveAt(i);
                    continue;
                }

                if (childBox.Text != null)
                {
                    // is the box has text - a non-breaking space (U+00A0) is significant CSS content,
                    // never meaningless inter-tag whitespace, even though .NET's IsNullOrWhiteSpace
                    // treats it the same as an ordinary collapsible space.
                    var keepBox = !HtmlUtils.IsNullOrCollapsibleWhitespace(childBox.Text);

                    // A ::before/::after box's presence is governed entirely by whether its selector
                    // matched (CssData.DoesSelectorMatch synthesizes it as a match side effect) and by
                    // its own `content` value - never by these whitespace-collapse heuristics, which
                    // exist for ordinary anonymous DOM text nodes. Without this, a pseudo-element box
                    // whose `content` resolves to the empty string (a real, common pattern for a
                    // border/background-only generated box, e.g. Acid2's
                    // ".nose div div:before { content: ''; ...border/background... }") looks exactly
                    // like meaningless inter-tag whitespace to every check below and gets deleted before
                    // it's ever laid out or painted.
                    keepBox = keepBox || childBox.IsBeforePseudoElement || childBox.IsAfterPseudoElement;

                    // if the box is a br
                    keepBox = keepBox || childBox.IsBrElement;

                    // is the box is pre-formatted
                    keepBox = keepBox || childBox.WhiteSpace == CssConstants.Pre || childBox.WhiteSpace == CssConstants.PreWrap;

                    // is the box is only one in the parent
                    keepBox = keepBox || box.Boxes.Count == 1;

                    // is it a whitespace between two inline boxes
                    keepBox = keepBox || (i > 0 && i < box.Boxes.Count - 1 && box.Boxes[i - 1].IsInline && box.Boxes[i + 1].IsInline);

                    // is first/last box where is in inline box and it's next/previous box is inline
                    keepBox = keepBox || (i == 0 && box.Boxes.Count > 1 && box.Boxes[1].IsInline && box.IsInline) || (i == box.Boxes.Count - 1 && box.Boxes.Count > 1 && box.Boxes[i - 1].IsInline && box.IsInline);

                    if (keepBox)
                    {
                        // valid text box, parse it to words
                        childBox.ParseToWords();
                    }
                    else
                    {
                        // remove text box that has no 
                        childBox.ParentBox!.Boxes.RemoveAt(i);
                    }
                }
                else
                {
                    // recursive
                    CorrectTextBoxes(childBox);
                }
            }
        }

        /// <summary>
        /// Go over all word-based replaced-element boxes (&lt;img&gt;, inline &lt;svg&gt;) and if
        /// their display style is set to block, put them inside another block but set them back to
        /// inline - both box types represent themselves as a single atomic word in the normal inline
        /// layout algorithm, which can't itself be a block-level box directly.
        /// </summary>
        /// <param name="box">the current box to correct its sub-tree</param>
        private static void CorrectReplacedElementBoxes(CssBox box)
        {
            for (int i = box.Boxes.Count - 1; i >= 0; i--)
            {
                var childBox = box.Boxes[i];
                if (childBox is CssBoxImage or CssBoxSvg && childBox.Display == CssConstants.Block)
                {
                    var block = CssBox.CreateBlock(childBox.ParentBox!, null, childBox);
                    childBox.ParentBox = block;
                    childBox.Display = CssConstants.Inline;
                }
                else
                {
                    // recursive
                    CorrectReplacedElementBoxes(childBox);
                }
            }
        }

        /// <summary>
        /// Correct the DOM tree recursively by replacing  "br" html boxes with anonymous blocks that respect br spec.<br/>
        /// If the "br" tag is after inline box then the anon block will have zero height only acting as newline,
        /// but if it is after block box then it will have min-height of the font size so it will create empty line.
        /// </summary>
        /// <param name="box">the current box to correct its sub-tree</param>
        private static void CorrectLineBreaksBlocks(CssBox box)
        {
            foreach (var childBox in box.Boxes)
            {
                CorrectLineBreaksBlocks(childBox);
            }

            if (!box.IsBrElement)
            {
                return;
            }

            var previousSibling = DomUtils.GetPreviousSibling(box);

            if (previousSibling is null or { IsBlock: true })
            {
                var nextSibling = DomUtils.GetFollowingSiblings(box, b => b is { IsInline: true, IsBrElement: false }, true).FirstOrDefault();

                if (nextSibling is null)
                {
                    box.Text = "\n";
                    box.ParseToWords();
                }
            }
        }

        /// <summary>
        /// Correct DOM tree if there is block boxes that are inside inline blocks.<br/>
        /// Need to rearrange the tree so block box will be only the child of other block box.
        /// </summary>
        /// <param name="box">the current box to correct its sub-tree</param>
        private static void CorrectBlockInsideInline(CssBox box)
        {
            try
            {
                if (DomUtils.ContainsInlinesOnly(box) && !ContainsInlinesOnlyDeep(box))
                {
                    var tempRightBox = CorrectBlockInsideInlineImp(box);
                    while (tempRightBox != null)
                    {
                        // loop on the created temp right box for the fixed box until no more need (optimization remove recursion)
                        CssBox? newTempRightBox = null;
                        if (DomUtils.ContainsInlinesOnly(tempRightBox) && !ContainsInlinesOnlyDeep(tempRightBox))
                            newTempRightBox = CorrectBlockInsideInlineImp(tempRightBox);

                        tempRightBox.ParentBox!.SetAllBoxes(tempRightBox);
                        tempRightBox.ParentBox = null;
                        tempRightBox = newTempRightBox;
                    }
                }

                if (DomUtils.ContainsInlinesOnly(box)) return;

                foreach (var childBox in box.Boxes)
                {
                    CorrectBlockInsideInline(childBox);
                }
            }
            catch (Exception ex)
            {
                box.HtmlContainer?.ReportError(HtmlRenderErrorType.HtmlParsing, "Failed in block inside inline box correction", ex);
            }
        }

        /// <summary>
        /// Rearrange the DOM of the box to have block box with boxes before the inner block box and after.
        /// </summary>
        /// <param name="box">the box that has the problem</param>
        private static CssBox? CorrectBlockInsideInlineImp(CssBox box)
        {
            if (box.Display == CssConstants.Inline)
                box.Display = CssConstants.Block;

            if (box.Boxes.Count > 1 || box.Boxes[0].Boxes.Count > 1)
            {
                var leftBlock = CssBox.CreateBlock(box);

                while (ContainsInlinesOnlyDeep(box.Boxes[0]))
                    box.Boxes[0].ParentBox = leftBlock;
                leftBlock.SetBeforeBox(box.Boxes[0]);

                var splitBox = box.Boxes[1];
                splitBox.ParentBox = null;

                CorrectBlockSplitBadBox(box, splitBox, leftBlock);

                // remove block that did not get any inner elements
                if (leftBlock.Boxes.Count < 1)
                    leftBlock.ParentBox = null;

                int minBoxes = leftBlock.ParentBox != null ? 2 : 1;
                if (box.Boxes.Count <= minBoxes) return null;
                // create temp box to handle the tail elements and then get them back so no deep hierarchy is created
                var tempRightBox = CssBox.CreateBox(box, null, box.Boxes[minBoxes]);
                while (box.Boxes.Count > minBoxes + 1)
                    box.Boxes[minBoxes + 1].ParentBox = tempRightBox;

                return tempRightBox;
            }
            else if (box.Boxes[0].Display == CssConstants.Inline)
            {
                box.Boxes[0].Display = CssConstants.Block;
            }

            return null;
        }

        /// <summary>
        /// Split bad box that has inline and block boxes into two parts, the left - before the block box
        /// and right - after the block box.
        /// </summary>
        /// <param name="parentBox">the parent box that has the problem</param>
        /// <param name="badBox">the box to split into different boxes</param>
        /// <param name="leftBlock">the left block box that is created for the split</param>
        private static void CorrectBlockSplitBadBox(CssBox parentBox, CssBox badBox, CssBox leftBlock)
        {
            CssBox? leftbox = null;
            while (badBox.Boxes[0].IsInline && ContainsInlinesOnlyDeep(badBox.Boxes[0]))
            {
                if (leftbox == null)
                {
                    // if there is no elements in the left box there is no reason to keep it
                    leftbox = CssBox.CreateBox(leftBlock, badBox.HtmlTag);
                    leftbox.InheritStyle(badBox, true);
                }
                badBox.Boxes[0].ParentBox = leftbox;
            }

            var splitBox = badBox.Boxes[0];
            if (!ContainsInlinesOnlyDeep(splitBox))
            {
                CorrectBlockSplitBadBox(parentBox, splitBox, leftBlock);
                splitBox.ParentBox = null;
            }
            else
            {
                splitBox.ParentBox = parentBox;
            }

            if (badBox.Boxes.Count > 0)
            {
                CssBox rightBox;
                if (splitBox.ParentBox != null || parentBox.Boxes.Count < 3)
                {
                    rightBox = CssBox.CreateBox(parentBox, badBox.HtmlTag);
                    rightBox.InheritStyle(badBox, true);

                    if (parentBox.Boxes.Count > 2)
                        rightBox.SetBeforeBox(parentBox.Boxes[1]);

                    if (splitBox.ParentBox != null)
                        splitBox.SetBeforeBox(rightBox);
                }
                else
                {
                    rightBox = parentBox.Boxes[2];
                }

                rightBox.SetAllBoxes(badBox);
            }
            else if (splitBox.ParentBox != null && parentBox.Boxes.Count > 1)
            {
                splitBox.SetBeforeBox(parentBox.Boxes[1]);
                if (splitBox.HtmlTag is { Name: "br" } && (leftbox != null || leftBlock.Boxes.Count > 1))
                    splitBox.Display = CssConstants.Inline;
            }
        }

        /// <summary>
        /// Makes block boxes be among only block boxes and all inline boxes have block parent box.<br/>
        /// Inline boxes should live in a pool of Inline boxes only so they will define a single block.<br/>
        /// At the end of this process a block box will have only block siblings and inline box will have
        /// only inline siblings.
        /// </summary>
        /// <param name="box">the current box to correct its sub-tree</param>
        private static void CorrectInlineBoxesParent(CssBox box)
        {
            if (ContainsVariantBoxes(box))
            {
                for (int i = 0; i < box.Boxes.Count; i++)
                {
                    if (box.Boxes[i].IsInline)
                    {
                        var newbox = CssBox.CreateBlock(box, null, box.Boxes[i++]);
                        while (i < box.Boxes.Count && box.Boxes[i].IsInline)
                        {
                            box.Boxes[i].ParentBox = newbox;
                        }
                    }
                }
            }

            if (!DomUtils.ContainsInlinesOnly(box))
            {
                foreach (var childBox in box.Boxes)
                {
                    CorrectInlineBoxesParent(childBox);
                }
            }
        }


        private static void CorrectAbsolutelyPositionedInlineElements(CssBox box)
        {
            if (box is { Display: CssConstants.Inline, Position: CssConstants.Absolute })
            {
                var blockBox = new CssBox(box.ParentBox, null);
                blockBox.Display = CssConstants.Block;
                blockBox.Position = CssConstants.Absolute;
                blockBox.Left = box.Left;
                blockBox.Top = box.Top;
                blockBox.Bottom = box.Bottom;
                blockBox.Right = box.Right;
                blockBox.Width = box.Width;
                blockBox.Height = box.Height;
                blockBox.TextAlign = box.TextAlign;

                box.Position = CssConstants.Static;
                box.ParentBox = blockBox;
            }

            foreach (var childBox in box.Boxes.ToArray())
            {
                CorrectAbsolutelyPositionedInlineElements(childBox);
            }
        }

        /// <summary>
        /// Corrects the missing elements in tables per https://www.w3.org/TR/CSS2/tables.html#anonymous-boxes
        /// </summary>
        /// <param name="box"></param>
        private static void CorrectAnonymousTables(CssBox box)
        {
            // 1. Remove irrelevant boxes
            CorrectAnonymousTablesRemoveIrrelevantBoxes(box);

            foreach (var childBox in box.Boxes.ToArray())
            {
                CorrectAnonymousTablesRemoveIrrelevantBoxes(childBox);
            }


            // 2. Generate missing child wrappers
            CorrectAnonymousTablesGenerateMissingChildWrappers(box);

            foreach (var childBox in box.Boxes.ToArray())
            {
                CorrectAnonymousTablesGenerateMissingChildWrappers(childBox);
            }

            // 3. Generate Missing Parents
            CorrectAnonymousTablesGenerateMissingParents(box);

            foreach (var childBox in box.Boxes.ToArray())
            {
                CorrectAnonymousTablesGenerateMissingParents(childBox);
            }

            foreach (var childBox in box.Boxes.ToArray())
            {
                CorrectAnonymousTables(childBox);
            }
        }

        private static void CorrectAnonymousTablesRemoveIrrelevantBoxes(CssBox box)
        {
            // 1.1 All child boxes of a 'table-column' parent are treated as if they had 'display: none'
            if (box.Display is CssConstants.TableColumn)
            {
                foreach (var childBox in box.Boxes)
                {
#if DEBUG
                    Console.WriteLine($"dom: set child box {childBox.Id} of table-column parent {box.Id} to display: none");
#endif

                    childBox.Display = CssConstants.None;
                }
            }

            // 1.2 If a child C of a 'table-column-group' parent is not a 'table-column' box, then it is treated as if it had 'display: none'.
            if (box.ParentBox?.Display is CssConstants.TableColumnGroup && box.Display is not CssConstants.TableColumn)
            {
#if DEBUG
                Console.WriteLine($"dom: set child box {box.Id} to display:none if parent is table-column-group and child is not table-column");
#endif

                box.Display = CssConstants.None;
            }

            // 1.3 This is handled via CorrectTextBoxes above
            // 1.4 This is handled via CorrectTextBoxes above
        }

        private static void CorrectAnonymousTablesGenerateMissingChildWrappers(CssBox box)
        {
            // 2.1 If a child C of a 'table' or 'inline-table' box is not a proper table child, then generate an anonymous 'table-row' box around C and all consecutive siblings of C that are not proper table children.
            if (box.ParentBox?.Display is CssConstants.Table)
            {
                if (!DomUtils.IsProperTableChild(box))
                {
#if DEBUG
                    Console.WriteLine($"dom: if box {box.Id} is not a proper table child and parent is a table, then generate table around element");
#endif

                    var followingMatchingSiblings =
                        DomUtils.GetFollowingSiblings(box, sibling => !DomUtils.IsProperTableChild(sibling), true)
                            .ToList();

                    // SetBeforeBox positions the new wrapper at C's original index in the grandparent
                    // (the constructor above only appends it at the end) - required so the wrapper
                    // takes C's place in document/column order instead of drifting to the end once C
                    // itself is reparented into it below.
                    var tableRowBox = new CssBox(box.ParentBox, null);
                    tableRowBox.Display = CssConstants.TableRow;
                    tableRowBox.SetBeforeBox(box);
                    box.ParentBox = tableRowBox;

                    followingMatchingSiblings.ForEach(sib => sib.ParentBox = tableRowBox);
                }
            }

            // 2.2 If a child C of a row group box is not a 'table-row' box, then generate an anonymous 'table-row' box around C and all consecutive siblings of C that are not 'table-row' boxes.
            if (box.ParentBox?.IsTableRowGroupBox ?? false)
            {
                if (box.Display is not CssConstants.TableRow)
                {
#if DEBUG
                    Console.WriteLine($"dom: if box {box.Id} is not a table row and parent is a table row group box, then generate table-row around element");
#endif

                    var followingMatchingSiblings =
                        DomUtils.GetFollowingSiblings(box, sibling => sibling.Display is not CssConstants.TableRow, true)
                            .ToList();

                    var tableRowBox = new CssBox(box.ParentBox, null);
                    tableRowBox.Display = CssConstants.TableRow;
                    tableRowBox.SetBeforeBox(box);
                    box.ParentBox = tableRowBox;

                    followingMatchingSiblings.ForEach(sib => sib.ParentBox = tableRowBox);
                }
            }

            // 2.3 If a child C of a 'table-row' box is not a 'table-cell', then generate an anonymous 'table-cell' box around C and all consecutive siblings of C that are not 'table-cell' boxes.
            if (box.ParentBox?.Display is CssConstants.TableRow)
            {
                if (box.Display is not CssConstants.TableCell)
                {

#if DEBUG
                    Console.WriteLine($"dom: if box {box.Id} is not a table cell and parent is a table row, then generate table-row around element and following  elements");
#endif

                    var followingMatchingSiblings =
                        DomUtils.GetFollowingSiblings(box, sibling => sibling.Display is not CssConstants.TableCell, true)
                            .ToList();

                    var tableCellBox = new CssBox(box.ParentBox, null);
                    tableCellBox.Display = CssConstants.TableCell;
                    tableCellBox.SetBeforeBox(box);
                    box.ParentBox = tableCellBox;

                    followingMatchingSiblings.ForEach(sib => sib.ParentBox = tableCellBox);
                }
            }
        }

        private static void CorrectAnonymousTablesGenerateMissingParents(CssBox box)
        {
            // 3.1 For each 'table-cell' box C in a sequence of consecutive internal table and 'table-caption' siblings, if C's parent is not a 'table-row' then generate an anonymous 'table-row' box around C and all consecutive siblings of C that are 'table-cell' boxes.
            if (box.Display is CssConstants.TableCell)
            {
                if (box.ParentBox?.Display is not CssConstants.TableRow)
                {
                    var followingMatchingSiblings =
                        DomUtils.GetFollowingSiblings(box, sibling => sibling.Display is CssConstants.TableCell, true)
                            .ToList();

                    var tableRowBox = new CssBox(box.ParentBox, null);
                    tableRowBox.Display = CssConstants.TableRow;
                    tableRowBox.SetBeforeBox(box);
                    box.ParentBox = tableRowBox;

                    followingMatchingSiblings.ForEach(sib => sib.ParentBox = tableRowBox);
                }
            }

            // 3.2 For each proper table child C in a sequence of consecutive proper table children, if C is misparented then generate an anonymous 'table' or 'inline-table' box T around C and all consecutive siblings of C that are proper table children. (If C's parent is an 'inline' box, then T must be an 'inline-table' box; otherwise it must be a 'table' box.)
            // - A 'table-row' is misparented if its parent is neither a row group box nor a 'table' or 'inline-table' box.
            // - A 'table-column' box is misparented if its parent is neither a 'table-column-group' box nor a 'table' or 'inline-table' box.
            // - A row group box, 'table-column-group' box, or 'table-caption' box is misparented if its parent is neither a 'table' box nor an 'inline-table' box.

            if (DomUtils.IsProperTableChild(box))
            {
                var isMissingParent = box.ParentBox is null;
                var isParentNotTable = box.ParentBox?.Display is not CssConstants.Table;
                var isParentNotInlineTable = box.ParentBox?.Display is not CssConstants.InlineTable;

                var isMisparented = isMissingParent && isParentNotTable && isParentNotInlineTable;

                if (isMisparented)
                {
                    var parentDisplay = box.ParentBox is null || box.ParentBox.IsBlock ? CssConstants.Table : CssConstants.InlineTable;

                    var followingMatchingSiblings =
                        DomUtils.GetFollowingSiblings(box, DomUtils.IsProperTableChild, true)
                            .ToList();

                    var tableBox = new CssBox(box.ParentBox, null);
                    tableBox.Display = parentDisplay;
                    box.ParentBox = tableBox;

                    followingMatchingSiblings.ForEach(sib => sib.ParentBox = tableBox);
                }
            }

        }

        /// <summary>
        /// Check if the given box contains only inline child boxes in all subtree.
        /// </summary>
        /// <param name="box">the box to check</param>
        /// <returns>true - only inline child boxes, false - otherwise</returns>
        private static bool ContainsInlinesOnlyDeep(CssBox box)
        {
            foreach (var childBox in box.Boxes)
            {
                if (!childBox.IsInline || !ContainsInlinesOnlyDeep(childBox))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Check if the given box contains inline and block child boxes.
        /// </summary>
        /// <param name="box">the box to check</param>
        /// <returns>true - has variant child boxes, false - otherwise</returns>
        private static bool ContainsVariantBoxes(CssBox box)
        {
            bool hasBlock = false;
            bool hasInline = false;
            for (int i = 0; i < box.Boxes.Count && (!hasBlock || !hasInline); i++)
            {
                var isBlock = !box.Boxes[i].IsInline;
                hasBlock = hasBlock || isBlock;
                hasInline = hasInline || !isBlock;
            }

            return hasBlock && hasInline;
        }

        private static HtmlDocumentMetadata ExtractMetadata(CssBox root)
        {
            string? title = null;
            string? author = null;
            string? subject = null;
            string? keywords = null;
            DateTime? date = null;
            string? generator = null;

            var titleBox = DomUtils.GetBoxByTagName(root, "title");
            if (titleBox != null)
            {
                var raw = string.Concat(titleBox.Boxes.Select(b => b.Text ?? string.Empty)).Trim();
                if (!string.IsNullOrEmpty(raw)) title = raw;
            }

            var metaBoxes = new List<CssBox>();
            CollectBoxesByTagName(root, "meta", metaBoxes);

            foreach (var meta in metaBoxes)
            {
                var name = meta.HtmlTag?.TryGetAttribute("name")?.ToLowerInvariant();
                var content = meta.HtmlTag?.TryGetAttribute("content");
                if (name is null || content is null) continue;

                switch (name)
                {
                    case "author":    author    = content; break;
                    case "subject":   subject   = content; break;
                    case "keywords":  keywords  = content; break;
                    case "generator": generator = content; break;
                    case "date":
                        if (DateTime.TryParse(content, out var dt)) date = dt;
                        break;
                }
            }

            return new HtmlDocumentMetadata(title, author, subject, keywords, date, generator);
        }

        private static void CollectBoxesByTagName(CssBox box, string tagName, List<CssBox> results)
        {
            if (box.HtmlTag?.Name.Equals(tagName, StringComparison.OrdinalIgnoreCase) == true)
                results.Add(box);
            foreach (var child in box.Boxes)
                CollectBoxesByTagName(child, tagName, results);
        }

        #endregion
    }
}