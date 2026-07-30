# A directional break inside a multicol reaches its side but drops the page it skipped

_CSS Fragmentation Level 3 §3.1. Tracked as [#545](https://github.com/jhaygood86/PeachPDF/issues/545)._

`break-before: right`/`left`/`recto`/`verso` inside a multi-column container escapes the container and lands
the content on a page of the requested side — the *step* is taken correctly. What is lost is the page it
stepped over: outside a container that slot is reserved and emitted as a real blank page, and inside one the
reservation is dropped, so the document comes out a page shorter than the identical break produces elsewhere.
[§3.1](https://www.w3.org/TR/css-break-3/#break-between)'s "one *or two* page breaks" means that skipped page
is a real fragmentainer, so dropping it is a genuine deviation and not a rendering nicety.

**The cause is a lifetime mismatch, not the columns engine.** `CssBox`'s `_escapedForcedBreakPending` and
`_escapedForcedBreakBlankSlot` carry what the escaping break settled from the pass that *decides* it to the pass
that *places* it, because the placing pass takes the resumed-target branch and an enclosing engine re-opens the
box's prologue in between (`PassRewind.RollBackTo`), retracting both. Both fields are cleared in
`BeginLayoutPass` when the **layout generation** changes; the record that carries the break is not, since the
driver re-feeds it on the reflow layout that follows. So the same escaping break is resumed in both generations
and only the first re-asserts anything — and the second is the generation that produces the document.

**Do not "fix" this by re-deriving the reservation on the resumed branch.** That was tried in
[#539](https://github.com/jhaygood86/PeachPDF/issues/539) and it is why this file exists. Two things were
measured there and both cost real time:

- Gating on the box (`!PlacedByForcedBreak && ForcedBreakTopFor(child) is not null`) is too broad. A box inside
  a multicol is resumed at the next column's top for reasons unrelated to its own `break-before`, so "carries a
  forced break and is being resumed at a target" is not the same statement as "this target is that break's".
  Only `BlockBreakToken.EscapesNestedFragmentainer` distinguishes them.
- Sourcing it from the record instead **still** changes output, and that is the actual finding: anything
  re-derived answers on *both* generations, re-reserving a page the first generation's own prologue had already
  retracted.

Either way `DirectionalPageBreakIntegrationTests.DirectionalBreakInsideMulticol_DegradesToAColumnBreak_KnownBoundary`
goes from two pages to three with the middle one blank. That is very likely the correct output — which is
exactly why closing this is an output change and belongs on `#545`, not in a refactor. Note that test's own
comment is inaccurate as written ("the parity step and its reservation are therefore not taken"): the step *is*
taken and does land on the requested side. Closing `#545` means revisiting that test, this file, and the
paragraph in `docs/html-css-support.md` under "A forced page break escapes the container" together.
