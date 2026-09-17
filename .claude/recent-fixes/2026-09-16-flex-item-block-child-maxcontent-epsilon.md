# Flex item with a block child sized a hair too narrow for its own text (#1154)

`CssLayoutEngineFlex.cs`'s `MeasureItem` has two sibling branches computing an auto-width,
non-flex-grow item's max-content width on a physical-X main axis: one for items whose own
children are inline text directly (`DomUtils.ContainsInlinesOnly`), one for items whose children
include a block box. The inline-only branch already carries a `+ 0.01` epsilon (with its own
comment) guarding against IEEE-754 addition-order differences between its intrinsic sum
(`LineContentWidth`) and the real line-layout fit-check's own incremental summation. The
block-child branch — which is what #1154's `<div><h1>...</h1></div>` shape hits, since a block
child makes `ContainsInlinesOnly` false — computed the analogous sum via
`CssLayoutEngine.GetMaxContentWidth` → `CssBox.GetMinMaxWidth` → `GetMinMaxSumWords` with no such
guard.

`GetMinMaxSumWords` sums the same cached per-word widths `LineContentWidth` does, but via a
recursive walk that resets and re-accumulates its running sum at every block-level descendant
(`CssBox.cs`, `GetMinMaxSumWords`, ~lines 7413-7501) — a different floating-point addition
order/grouping than `LineContentWidth`'s flat single loop. Non-associative FP addition means the
two sums are not always bit-identical, and the resulting value is written back as the item's real
CSS width and re-laid-out for real; the real wrap decision (`CssLayoutEngine.cs`'s `FlowBox`, ~line
3520) uses a strict `>` with zero tolerance, so any shortfall — even a few ULPs — wraps text that
fits.

## The fix

Added the same `+ 0.01` epsilon, at the same call site (`MeasureItem`'s block-child branch),
directly on the `GetMaxContentWidth` result, before it's clamped against `naturalMain`/`minContent`.
Did not touch `GetMinContentWidth`/`minContent` (a pure lower bound driven by a single unbreakable
run, not a multi-word sum — no evidence it needs the same guard) or `GetMaxContentWidth` itself
(would also affect grid track sizing, block/abs-pos shrink-to-fit, and vertical-writing-mode
sizing — all currently correct, no reason to risk shifting their pinned test expectations). This
mirrors an idiom already established elsewhere in this codebase for the identical class of
strict-`>` wrap-boundary comparison (`CssLayoutEngine.cs`'s own local `+ 0.01` guards on
`GetMaxContentWidth(...) > available` and `outerRight > actualLimitRight`), rather than a novel
one-off constant.

## Verified

Added `FlexItem_WithBlockChildHeading_DoesNotSpuriouslyWrap`
(`FlexboxIntegrationTests.cs`), a `[Theory]` sweeping the exact font sizes #1154 reported
(28/30/31/32/33/34/36/48px) against the issue's own repro markup, asserting the heading lays out
on exactly one line at every size. Before the fix, 4 of the 8 sizes (30, 33, 36, 48px) failed with
the heading wrapping to two lines; after the fix all 8 pass. Ran the full Flexbox suite
(`dotnet test ... --filter "FullyQualifiedName~Flexbox"`, 349 tests) with no regressions,
particularly in `SpaceBetween_ItemWithBlockChildren_IsSizedByItsContent_NotAnEvenSplit` and
`Item_WhoseMinContentExceedsTheRow_KeepsItsMinContent`, which exercise the same branch. Ran the
full net8.0 suite (12211 passed, 9 pre-existing platform-specific skips) with no regressions. Full
solution rebuild (`dotnet build PeachPDF.slnx -t:Rebuild`) stayed at 0 warnings/0 errors across
net8.0/net10.0/net11.0. Diff coverage on the two changed lines in `CssLayoutEngineFlex.cs` came
back at 100% via `diff-cover` against `origin/main`.
