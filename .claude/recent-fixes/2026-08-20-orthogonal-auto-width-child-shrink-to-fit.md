# Orthogonal auto-width child shrink-to-fit

_Landed 2026-08-20._

[Issue #777](https://github.com/jhaygood86/PeachPDF/issues/777): `CssBox.LayoutVerticalBlockChildren`
(added for #760) gave an auto-width orthogonal-flow child — one whose own resolved `writing-mode` is
`horizontal-tb`, perpendicular to its `vertical-rl`/`vertical-lr` containing block — ordinary
stretch-to-containing-block sizing, filling the parent's whole available block-axis extent, instead of
the shrink-to-fit sizing [CSS Writing Modes 4 §4.3](https://www.w3.org/TR/css-writing-modes-4/#orthogonal-flows)
requires: `min(max-content, max(min-content, constraint))`.

**The accepted-gap note's premise ("no general min-content/max-content/shrink-to-fit algorithm anywhere
in this codebase") was stale, not accurate — that's what made this a small fix, not the "separate,
larger undertaking" the note anticipated.** `CssLayoutEngine.GetFitContentWidth`/`GetMinContentWidth`/
`GetMaxContentWidth`, built on `CssBox.GetMinMaxWidth`'s recursive word-measurement walk, already
existed and were already proven for exactly this "shrink-to-fit against a constraint" shape in two
places: CSS 2.1 §10.3.7 absolutely-positioned auto-width boxes (`GetBoxWidth`'s abs-positioned branch)
and flex column-direction auto-width cross-sizing (`CssLayoutEngineFlex.ShrinkColumnItemToContentWidth`).
The gap note likely predates one of those two additions, or simply wasn't revisited against them when
#760 scoped this out — either way, checking for existing intrinsic-sizing machinery before believing a
"this needs a new algorithm" note would have caught it sooner.

**The load-bearing idea: the ordinary stretch value the child already had *is* the spec's "constraint",**
so no separate derivation was needed. `ResolveOwnInlineSize`'s existing call resolves an auto-width
child's outer width by stretching to `ContainingBlock.ClientRight - ClientLeft` (minus margins) — the
parent's own available block-axis extent, already min/max-width-clamped. That is precisely §4.3's
"constraint": the inline-axis space available from the containing block's own definite dimension (falling
back up the chain when indefinite, which this codebase's tentative-then-shrink auto-width model already
does transitively, with no extra handling needed). `GetFitContentWidth(g, childBox, childWidth)` reuses
that value directly as its `contentAreaWidth` parameter — since it clamps its own result to
`<= contentAreaWidth`, a child whose content already needs the full constraint is returned unchanged, so
this could be applied unconditionally rather than needing a parent-auto-vs-definite-width special case.

**`GetFitContentWidth` alone only ever narrows — it has no floor of its own**, so two floors are applied
on top of it afterward, closing a post-change review's finding that the first cut only implemented the
`min(max-content, ...)` half of §4.3's formula. First, the *algorithmic* min-content floor
(`GetMinContentWidth` — the longest unbreakable run, the same measurement `GetFitContentWidth`'s own
max-content pass already primed via `MeasureWords`, so this is a second read of already-computed state,
not a second layout pass): without it, a constraint narrower than a long unbreakable word squeezed the
child below that word's own width. Second, the child's own CSS `min-width` — floated back up explicitly,
mirroring `ShrinkColumnItemToContentWidth`'s own clamp after the identical call, since the pre-shrink
stretch value already had it applied (via `GetBoxWidth`'s own tail clamp) and skipping this would
silently drop it. `max-width` needs no re-clamp: the result can only be `<= constraint`, and `constraint`
(`childWidth`) was already max-width-clamped before `GetFitContentWidth` ever saw it — provably a no-op,
so it's omitted rather than duplicated the way flex's version keeps it (that call site's `available`
isn't guaranteed already-clamped the way this one is).

Scoped to a plain auto-width, non-replaced block child (`Words.Count == 0`, excluding a replaced element
whose intrinsic width `GetBoxWidth` never stretched in the first place) that isn't table/flex/grid
(mirroring `ResolveOwnInlineSize`'s own exclusion — those resolve their own inline size internally
regardless of writing-mode). New tests in `VerticalWritingModeLayoutIntegrationTests.cs` cover: a short
orthogonal child shrinking well below a wide wrapper's full extent, a long orthogonal child still filling
the constraint exactly as stretch did before (the non-regression case), `min-width` flooring a
shrunk-below-min-width result — the one branch a straightforward "does it shrink" test doesn't reach
(caught by a first diff-coverage pass showing that clamp at 0 hits) — the algorithmic min-content floor
(a single long word against a much narrower constraint) caught by the post-change review noted above, and
an auto-width wrapper with a single short auto-width orthogonal child (both shrink to the content, proving
the constraint-derivation chain doesn't stick at the wrapper's own large, page-width tentative value). The
existing `VerticalRl_OrthogonalHorizontalChild_LaysOutOwnContentHorizontally_WhilePlacedAsOneAtomicBlock`
test (a *definite*-width orthogonal child) needed no changes — definite widths were never stretched, so
they're outside this fix's gate. Full net8.0 suite (9011 passing) and a zero-warning
`dotnet build -t:Rebuild` both pass.

**A post-change review also surfaced two narrower, pre-existing risks in the shared `GetFitContentWidth`/
`GetLargestChildWidth`/`GetBoxWidth` machinery this fix newly exercises for an ordinary block child**
(rather than that machinery's existing callers — `position: absolute` auto-width and flex column items):
a percentage-width descendant's `GetBoxWidth` call resolves against the not-yet-shrunk parent size
(`GetMinMaxSumWords` explicitly guards against the equivalent circularity in its own word-recursion path,
but `GetLargestChildWidth`'s separate `GetBoxWidth` walk does not), and a CSS `content: url(...)`
generated-content box's phantom image word isn't measured until its own `MeasureWordsSize` pass, which
this fix's `Words.Count == 0` replaced-element exclusion runs before. Both are latent in the shared
machinery itself (already reachable via its existing callers), not novel defects introduced by this fix,
and both require narrow, contrived HTML to hit — left as known limitations rather than fixed here.
