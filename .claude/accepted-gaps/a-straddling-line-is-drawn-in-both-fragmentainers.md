# A straddling line is drawn in both fragmentainers instead of overflowing one

Tracking issue: [#484](https://github.com/jhaygood86/PeachPDF/issues/484).

CSS Fragmentation Level 3 [§4.1](https://www.w3.org/TR/css-break-3/#possible-breaks) makes a line box
monolithic — there is no possible break point inside one — so a line that does not fit its fragmentainer must
**overflow** it. PeachPDF instead claims such a line for *both* fragmentainers (`FragmentEmitter.ClaimsWord`'s
`FallsPast` arm), so it is drawn twice under two different page clips and reads as one line cut in half across
the page break.

**This is deliberate, and removing it is measurably worse.** It was removed once, by
[#446](https://github.com/jhaygood86/PeachPDF/issues/446)/PR #472's tie-break, and the readable remainder went
with it: 45 words, one line at each of a four-page flex document's three breaks, surviving only as a clipped
sliver at the foot of the page above. [#477](https://github.com/jhaygood86/PeachPDF/issues/477)/PR #480
restored it. Losing content is strictly worse than slicing it, so the second claim stays until the *cause* is
fixed rather than the symptom.

Two paths produce a straddling line, and neither is the emitter's to fix:

- **flex/grid item content** — laid out under `HtmlContainerInt.SuppressWordPageBreaks`, which gates
  `CssLayoutEngine.FlowBox`'s straddle check, and the engines' later `AssignLocations` translation never
  re-runs it. The real fix is to ask the straddle question again once the item is at its final position, which
  is #390 stage 4 / #400 territory: today an item's content is laid out at a provisional origin.
- **`MonolithicContent.FitsNoFragmentainer`** — a line taller than the band stays put by design, because
  breaking to a fresh fragmentainer would repeat the problem forever. Here §4.1 agrees the line cannot break;
  only the "continue it in the next fragmentainer rather than clip it" part deviates, and that *is* a
  `FragmentEmitter` change.

Do not "fix" this by narrowing `ClaimsWord` — that is exactly the change #477 had to undo. Note also that
"every word claimed exactly once" **passes** while the content is being lost, and the showcase corpus contains
no straddle-beyond-tolerance case, so neither of this repo's usual guards can see a regression here; see
[the membership invariant](../invariants/fragmentation-one-membership-question-is-asked-with-one-tolerance.md).

The reader-facing half is in `docs/html-css-support.md` under the flex/grid break rules. The same shape one
level over — the *decoration* rectangle of a straddling line — is
[#471](https://github.com/jhaygood86/PeachPDF/issues/471).
