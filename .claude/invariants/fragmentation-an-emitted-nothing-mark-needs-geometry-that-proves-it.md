# An "emitted nothing" mark needs geometry that proves the box is finished

`FragmentEmitter` prunes a subtree marked "emitted nothing from slot N on" (`CssBox.RecordEmittedNothingAt`)
for every later slot, so a wrong mark silently drops content from the page that holds it. A mark may only be
written when the box's own laid-out content provably ends at or above where the emitter has walked:

- Measure a box through `FragmentEmitter.SettledBottomOf`, never `ActualBottom` directly. A plain inline
  box's `Location` and `ActualBottom` are not placed by the inline flow at all; only its line rectangles and
  words are.
- A box on the outgoing break chain has not finished, however empty the range looked: its height is only
  applied on the pass that completes it, so its `ActualBottom` is its top.
- A box empty throughout a pass's range may still have content below that range; the end-of-pass commit
  requires the content to end within it.

**Measured symptom:** whole headings, captions or paragraphs missing from a page while the layout places
them; on `main` the SVG showcase's fourth page lost every heading and caption. Check any change here against
the same build with pruning's two reads turned off (the `ownPrunable` check in `BuildDraft` and the
`LiveChildStartFor` guard in `ChildrenOf`): the unpruned walk only ever adds words, so any word it draws and
the pruned build does not is a wrong mark. `PEACHPDF_VERIFY_FRAGMENT_PRUNING=1` finds divergences too, but also
reports harmless ones (empty sliver fragments), so count words rather than trusting it alone.
