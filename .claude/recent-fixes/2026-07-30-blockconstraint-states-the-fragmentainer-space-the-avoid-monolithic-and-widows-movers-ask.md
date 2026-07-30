# BlockConstraint states the fragmentainer space the avoid/monolithic and widows movers ask

Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320). Part of [#515](https://github.com/jhaygood86/PeachPDF/issues/515)
(`#390`'s stage 2). Closes [#538](https://github.com/jhaygood86/PeachPDF/issues/538) (`#515.1`).

## The load-bearing idea

A new `BlockConstraint` (`Fragmentation/BlockConstraint.cs`) packages "which fragmentainer, and how far
below its content edge" into one value, replacing the `PageIndexOf`/`PageTopOf`/`PageBandHeightOf`
triads `CssBox.PerformLayoutEpilogue`'s two page-context movers (the `break-inside: avoid`/monolithic
relocation, and the `widows` whole-box push) and `FitsInFragmentainer` each spelled out inline. It is
constructed fresh at a specific slot — via `BlockConstraint.For(box)` (the slot the box's own top
already falls in) or `BlockConstraint.AtSlot(container, contextRoot, slot, offset)` (an explicit
candidate) — rather than reading the ambient `HtmlContainerInt.CurrentFragmentainer`: these movers ask
about a box's own already-settled position or a specific candidate slot, never about which
fragmentainer the live pass's cursor is currently naming, so this stays exactly as behaviour-neutral as
the raw calls it replaces (conflating "the box's own slot" with "the pass's cursor" is the mistake
`#435` spent two PRs unwinding — see the two `2026-07-30` notes preceding this one).

This is a narrower reading of the plan than the issue's own sketch (`FragmentainerContext? Fragmentainer,
double BlockOffset, double AvailableInlineSize, double ClonedBlockEnd` threaded as a `PerformLayout`
parameter through every layout entry point). Two things changed on contact with the actual code:

- **No consumer of this PR needs `AvailableInlineSize`.** None of the three movers touch inline size at
  all. Adding it now would be an unused field carried for a future PR's sake — left for whichever of
  `#539`/`#540` actually resolves inline size against the constraint.
- **Computed on demand instead of threaded as a parameter.** The three movers all run in
  `PerformLayoutEpilogue`, after the box's own `Location`/`ActualBottom` are already settled — there is
  no "child hasn't been positioned yet" moment here for a parameter to reach across, unlike `#539`'s
  forced-break target (which genuinely is resolved before the child lays out, and where a parameter
  really does carry information a computed-on-demand call could not reach). Threading a parameter through
  `PerformLayout`/`PerformLayoutImp`'s ~30 call sites (3 overrides, 4 engines' own static entry points, a
  shared delegate type, 8 test-double overrides) for three read-only call sites that only need the box's
  own current state would have been a large, high-risk mechanical change for no behavioural gain over a
  static factory reading that same state fresh each call.

## What was found by running it, not by reading it

**A different check was silently reused for two arithmetically distinct questions on the first attempt,
caught by the full suite rather than the corpus.** The `avoid`/monolithic mover asks "does the box
straddle *from its current offset*" (`Straddles`, which subtracts `BlockOffset`); the `widows` whole-box
push asks "would the box's own extent alone fit a *fresh* band" (no offset — the push lands the box at
that band's own content top). The first draft used `constraint.Straddles(...)` for both, which is wrong
for the second: `Widows4_CannotBeSatisfiedWithoutBreakingOrphans_PushesTheWholeBox` caught it immediately
(expected `Location.Y == 100`, got `38` — the push never fired). Fixed by comparing directly against
`constraint.NextBandHeight` for the widows case instead of routing it through `Straddles`.

**The keep-with-next first-line retry (`PerformLayoutEpilogue`'s *other* page-context mover, at the top
of the method) turned out to be untested dead code in the existing suite, discovered only once diff
coverage was checked against the refactored lines.** Converting it the same way as the other two left 3
lines with zero hits under `dotnet test --collect:"XPlat Code Coverage"`, and checking the *original*
lines at the same position against `origin/main` showed they were already at zero hits before this PR —
not a regression this PR introduced, but a pre-existing gap this PR was the first to touch in a way that
surfaced it. Measured directly: across the full suite, `firstLinePage > ownPage` (line 3598) is true 244
times, but `keepWithNextRun.Count > 0` (the very next line) is true on **none** of them — the specific
"pull the preceding run forward" branch this block exists for has apparently not been exercised by any
current fixture, including the one whose own name and doc comment claim to
(`KeepWithNextIntegrationTests.ParagraphFirstLinePushedByWordFlow_PullsAvoidChainedHeadingAlong`, whose
assertions evidently pass via some other mechanism reaching the same visual result).

## What was deliberately not done

- **The keep-with-next first-line retry was reverted to its original, unconverted form** rather than
  forcing a contrived fixture into existence just to satisfy the coverage gate on dead code. `#539`
  already scopes touching keep-with-next substantially (the forced-break target becoming the frame's own)
  — that is the right place to both convert this block and build the fixture that actually exercises it,
  since by then there will be a real behavioural reason to. Noted on `#539` directly so it is not lost.
- **`EarlyBreak.Discover`'s own internal `ownPageTop`/`destinationBand` reads were not converted.** They
  read two different boxes' coordinates (`subject`, not always the box that discovered the decision), which
  doesn't reduce cleanly to one box's own `BlockConstraint` the way the three single-box movers do; forcing
  it through the same shape for consistency's sake wasn't worth the indirection over two already-clean
  one-line reads.
- **No `#if DEBUG` cross-check was added.** The plan's own suggestion (`constraint.AbsoluteTop ==
  Location.Y`) proves a *threaded parameter* reached its destination; this design doesn't thread one — a
  constraint computed fresh from `Location.Y` is tautologically consistent with it, so the check would
  assert nothing.

## Evidence

Full `net8.0` suite green (7091 passing, 9 skipped, 0 failed) and CLI suite green (96 passing). Zero-warning
solution rebuild. 100% diff coverage against `origin/main`. Byte-identical across the full 77-showcase
corpus (one new showcase, `gsub_ligatures`, landed on `main` via `#536` since this branch was last based on
it). New `BlockConstraintTests.cs` pins the arithmetic in isolation against `HtmlContainerInt`'s own
`PageTopOf`/`PageBandHeightOf`/`PageBottomOf`.
