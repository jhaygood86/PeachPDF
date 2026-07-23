using PeachPDF.CSS;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Parse;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// The single implementation of the <c>@page</c> rule cascade (which rules apply to a given page,
    /// in what precedence order) shared by paint-time consumers (<c>PdfGenerator</c>'s margin-box and
    /// page-style selection) and layout-time geometry (<c>PageGeometryTable</c>'s per-page margin
    /// resolution). The cascade itself is a pure function of the page number and the active named
    /// page; the two <c>ActiveNameAt*</c> helpers are the two documented attribution policies for
    /// deriving that name from the registered <see cref="NamedPageElement"/>s.
    /// </summary>
    internal static class PageRuleResolver
    {
        /// <summary>
        /// The named page in effect for a page judged by its END: the CSS <c>page</c> property
        /// propagates forward through the normal flow until a later element sets a different one — so
        /// the name in effect is whichever assignment most recently took effect at or before this
        /// page's end (the highest Y still &lt; <paramref name="pageY"/> + <paramref name="pageHeight"/>),
        /// not just an assignment whose own Y falls inside this page's range. Used by the paint-time
        /// margin-box/page-style selection, preserving the established mid-page-switch semantics. The
        /// epsilon guards against an element's Y and the page boundary being computed via independent
        /// accumulation paths that differ by floating-point noise.
        /// </summary>
        internal static string? ActiveNameAtPageEnd(
            IReadOnlyList<NamedPageElement> namedPageElements, double pageY, double pageHeight)
        {
            return namedPageElements
                .Where(e => e.Y < pageY + pageHeight - HtmlContainerInt.PageBoundaryEpsilon)
                .OrderByDescending(e => e.Y)
                .Select(e => e.Name)
                .FirstOrDefault();
        }

        /// <summary>
        /// The named page in effect at a slot's START: the most recent assignment strictly before
        /// <paramref name="slotTop"/> (with the boundary epsilon, so an element registered exactly at
        /// the slot top counts as this slot's name). Used by the per-page geometry computation, where
        /// a slot's band height must not depend on registrations *inside* the slot — legal because a
        /// box whose explicit <c>page</c> differs from the active name always forces a break onto a
        /// fresh page (see <c>CssBox.PerformLayoutImp</c>'s named-page forced break), and its
        /// registration Y is snapped to that page's slot top (<c>CssBox.NamedPageRegistrationY</c> —
        /// the box itself sits its preserved post-break margin below the top); a name can therefore
        /// never genuinely change mid-slot.
        /// </summary>
        internal static string? ActiveNameAtSlotStart(
            IReadOnlyList<NamedPageElement> namedPageElements, double slotTop)
        {
            return namedPageElements
                .Where(e => e.Y < slotTop + HtmlContainerInt.PageBoundaryEpsilon)
                .OrderByDescending(e => e.Y)
                .Select(e => e.Name)
                .FirstOrDefault();
        }

        /// <summary>
        /// Selects the most specific @page rule for the given page.
        /// Priority (last wins): base → named page → :right/:left → :first.
        /// </summary>
        internal static PageRule? SelectPageRule(
            IReadOnlyList<PageRule> rules, int pageNumber, string? activeNamedPage)
        {
            var ordered = GetOrderedApplicableRules(rules, pageNumber, activeNamedPage);
            return ordered.Count > 0 ? ordered[^1] : null;
        }

        /// <summary>
        /// Resolves the effective set of margin-box declarations (<c>@top-left</c>, <c>@bottom-right</c>,
        /// etc.) for the given page — the CSS cascade for <c>@page</c> rules is per-declaration, not
        /// per-rule, so a page can (and, in css4.pub's real dictionary CSS, does) simultaneously match a
        /// low-specificity base named-page rule that defines <c>@top-left/@top-center/@top-right</c> AND
        /// a higher-specificity compound <c>name:left</c>/<c>name:right</c> rule that only defines
        /// <c>@bottom-left</c>/<c>@bottom-right</c>/<c>@right-top</c> — both sets of margin boxes must
        /// render together (merged by box name, with a more specific rule's own definition of a given
        /// box name winning over a less specific rule's), not just whichever single rule
        /// <see cref="SelectPageRule"/> would pick as "the" applicable one for page-level properties like
        /// <c>margin</c>/<c>size</c>.
        /// </summary>
        internal static IReadOnlyList<MarginStyleRule> SelectApplicableMarginRules(
            IReadOnlyList<PageRule> rules, int pageNumber, string? activeNamedPage)
        {
            var ordered = GetOrderedApplicableRules(rules, pageNumber, activeNamedPage);
            var merged = new Dictionary<string, MarginStyleRule>(StringComparer.OrdinalIgnoreCase);

            foreach (var rule in ordered)
            {
                foreach (var margin in rule.Margins)
                {
                    var name = margin.Selector?.Text?.Trim().ToLowerInvariant();
                    if (string.IsNullOrEmpty(name)) continue;

                    if (!merged.TryGetValue(name, out var mergedRule))
                    {
                        mergedRule = new MarginStyleRule(margin.Parser) { Selector = margin.Selector };
                        merged[name] = mergedRule;
                    }

                    // Per-declaration merge, not whole-rule replacement: a later (higher-precedence)
                    // rule's own properties win, but properties it doesn't redeclare survive from an
                    // earlier, less-specific rule for the same box name - matching real CSS cascade
                    // (and Prince), which resolves @page per-declaration like any other stylesheet rule.
                    MergeDeclarationsInto(mergedRule.Style, margin.Style);
                }
            }

            return merged.Values.ToList();
        }

        /// <summary>
        /// Resolves the effective, per-declaration-merged page-context style (the properties declared
        /// directly on matching <c>@page</c> rules themselves, not inside any margin-box block) for the
        /// given page. Per CSS Paged Media, margin boxes inherit these when they don't declare a property
        /// themselves - see <c>MarginBoxRenderer.Render</c>'s <c>pageStyle</c> parameter. Uses the
        /// same ascending-precedence merge as <see cref="SelectApplicableMarginRules"/>, independently of
        /// <see cref="SelectPageRule"/>'s single-winner selection (still used, unchanged, for page-level
        /// <c>margin</c>/<c>size</c> via <see cref="ResolvePageMargins"/>).
        /// </summary>
        internal static StyleDeclaration? SelectApplicablePageStyle(
            IReadOnlyList<PageRule> rules, int pageNumber, string? activeNamedPage)
        {
            var ordered = GetOrderedApplicableRules(rules, pageNumber, activeNamedPage);
            StyleDeclaration? merged = null;

            foreach (var rule in ordered)
            {
                if (rule.Style is null) continue;

                merged ??= new StyleDeclaration(rule.Parser);
                MergeDeclarationsInto(merged, rule.Style);
            }

            return merged;
        }

        /// <summary>
        /// Resolves the four page margins for the winning rule, in true PDF points, falling back
        /// per-side to the base margins for sides the rule doesn't declare (or declares with a value
        /// the page-geometry layer can't resolve: viewport-relative/ch units, or any relative unit
        /// when no <paramref name="context"/> was captured).
        /// </summary>
        internal static (double L, double T, double R, double B) ResolvePageMargins(
            PageRule? rule, double baseL, double baseT, double baseR, double baseB,
            PageLengthContext? context = null)
        {
            if (rule == null) return (baseL, baseT, baseR, baseB);
            var s = rule.Style;

            // Re-base the em/ex length basis on the winning page rule's own font-size when it sets one
            // (css-page-3 §7.1 / issue #162); otherwise keep the captured context, whose EmPt already carries
            // the base @page font (or the root font). rem and 100% are unaffected.
            var ctx = context;
            if (context is { } c0 && s is { } fs && fs.FontSize.Length > 0)
                ctx = c0 with { EmPt = MarginBoxRenderer.ResolveFontSizePt(fs.FontSize) };

            return (
                Resolve(s.MarginLeft)   ?? baseL,
                Resolve(s.MarginTop)    ?? baseT,
                Resolve(s.MarginRight)  ?? baseR,
                Resolve(s.MarginBottom) ?? baseB
            );

            double? Resolve(string value) => ctx is { } c
                ? DomParser.ParseLengthToPdfPoints(value, c)
                : DomParser.ParseLengthToPdfPoints(value);
        }

        /// <summary>
        /// Copies every declared property from <paramref name="source"/> into <paramref name="target"/>,
        /// overwriting same-named properties already present - the shared per-declaration merge step for
        /// <see cref="SelectApplicableMarginRules"/> and <see cref="SelectApplicablePageStyle"/>.
        /// </summary>
        private static void MergeDeclarationsInto(StyleDeclaration target, StyleDeclaration source)
        {
            foreach (var property in source.Declarations)
            {
                target.SetProperty(property);
            }
        }

        /// <summary>
        /// Every <c>@page</c> rule that applies to this page, in ascending cascade precedence (base rule
        /// first if present, then named/pseudo matches from lowest to highest specificity score —
        /// preserving declaration order among equal scores, so a later-declared rule still wins ties —
        /// then <c>:first</c> last, since it always outranks everything else per spec). Shared by
        /// <see cref="SelectPageRule"/> (single-winner page-level properties) and
        /// <see cref="SelectApplicableMarginRules"/> (per-margin-box-name cascade merge).
        /// </summary>
        private static List<PageRule> GetOrderedApplicableRules(
            IReadOnlyList<PageRule> rules, int pageNumber, string? activeNamedPage)
        {
            var result = new List<PageRule>();
            if (rules.Count == 0)
                return result;

            PageRule? baseRule = null;
            PageRule? firstRule = null;
            var matches = new List<(PageRule Rule, int Score)>();

            foreach (var rule in rules)
            {
                var entries = (rule.Selector as PageSelector)?.Entries;

                if (entries is not { Count: > 0 })
                {
                    baseRule = rule;
                    continue;
                }

                foreach (var entry in entries)
                {
                    // Page names are case-sensitive CSS custom-idents; pseudo-class keywords
                    // (first/left/right) are matched case-insensitively.
                    var nameMatches = entry.Name is null || entry.Name == activeNamedPage;
                    var pseudo = entry.Pseudo?.ToLowerInvariant();
                    var isFirst = pseudo == "first";

                    // ":first" (optionally combined with a matching name) always outranks every other
                    // selector shape, regardless of declaration order — per the CSS Paged Media spec,
                    // this is a special case, not part of the additive name/pseudo specificity score
                    // below (a compound "chapter1:first" still requires the name to match; a bare
                    // ":first" applies unconditionally on page 1).
                    if (isFirst)
                    {
                        if (nameMatches && pageNumber == 1)
                            firstRule = rule;
                        continue;
                    }

                    var pseudoMatches = pseudo switch
                    {
                        null => true,
                        "left" => pageNumber % 2 == 0,
                        "right" => pageNumber % 2 != 0,
                        _ => false
                    };

                    if (!nameMatches || !pseudoMatches) continue;

                    // Specificity: name+pseudo(left/right) > name-alone > pseudo(left/right)-alone.
                    var score = (entry.Name != null ? 2 : 0) + (entry.Pseudo != null ? 1 : 0);
                    matches.Add((rule, score));
                }
            }

            if (baseRule != null) result.Add(baseRule);
            // OrderBy is a stable sort — equal-score matches keep their original (declaration) order, so
            // the later-declared one still ends up last (highest precedence), matching the prior single-
            // winner behavior's ">=" tie-break.
            result.AddRange(matches.OrderBy(m => m.Score).Select(m => m.Rule));
            if (firstRule != null) result.Add(firstRule);

            return result;
        }
    }
}
