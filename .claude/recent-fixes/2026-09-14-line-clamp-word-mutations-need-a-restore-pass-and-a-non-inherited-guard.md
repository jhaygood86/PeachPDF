# line-clamp: `CssBox.Words` mutations need a restore pass, and a non-inherited property needs a guard against whole-area inheritance

Implements CSS Overflow 4 `line-clamp`: `CssLayoutEngine.CreateLineBoxes`'s wrap loop stops opening a
new line once a block has produced as many lines as its declared limit, appends a generated ellipsis
to the last visible line, and reports the stop via a new `CssLineBoxCoordinates.ClampedStop` flag kept
deliberately separate from the pagination `Break` token (a clamped block's content is done for good,
not paused for a later fragmentainer pass).

## Load-bearing idea: `CssBox.Words` mutations must be undone before a fresh pass, exactly like `overflow-wrap`'s emergency splits

`TryApplyLineClamp` pops trailing words from a line's owner box(es) to make room for the ellipsis and
appends the ellipsis word to whichever real word survives as the line's own last word. Both mutate
`CssBox.Words` in place. That list is never rebuilt from scratch between layout passes over the same
box tree - `CreateLineBoxes` can run more than once for one block within a single document layout (an
unrestricted-width measure pass, a variable-page-width reflow, a multi-column fill attempt, ...), and a
first draft of this feature left those mutations permanent: a second pass saw the already-shortened
word list and popped/appended again on top of it, compounding without bound. A two-column document with
`line-clamp` reproduced this immediately - two stacked ellipses and silently deleted words, found only
by actually running layout twice over the same tree
(`LayoutHarness.LayoutRepeatedlyAsync`), not by reading the code.

Fixed the same way `RestoreOverflowWrapSplits` already handles the analogous emergency-split case:
`CssBox` gained `LineClampPoppedWords`/`LineClampEllipsisWord` fields recording exactly what
`TryApplyLineClamp` did (owner, index, word), and a new `RestoreLineClampMutations`, called from the
same two places `RestoreOverflowWrapSplits` is (the top of a fresh, non-resumed `CreateLineBoxes`/
`CreateVerticalLineBoxes` pass), undoes it before the pass re-derives the clamp fresh. Popped words are
reinserted in reverse removal order (LIFO), which is what lands each one back at the index it actually
occupied.

## A structural trap: the ellipsis's owner box cannot be the clamped block itself

An earlier draft of this fix used the clamped block's own box as the ellipsis's owner (reasoning: CSS
Overflow 4 wants it "a direct child of the block container", not of whichever inline happened to be
last). This is wrong whenever the block has any child boxes of its own - which is the common case,
since a block's bare text is wrapped in an anonymous child box, not held directly on the block - per
the `dom-a-box-must-never-hold-both-its-own-words-and-child-boxes` invariant, `CssLayoutEngine.FlowBox`
silently skips a box's own `Words` whenever it also has `Boxes`. Measured concretely: every real word
vanished from the fragment tree the moment the ellipsis moved to `blockBox.Words`, because
`FlowBox`/`FragmentEmitter` simply never looked at that list again. The ellipsis is instead added to
whichever real word survives as the line's own last word after popping - not fully what the spec wants
(an ellipsis after a specially-styled trailing span can still visually inherit that span's own
decorations), which is recorded as a remaining gap rather than worked around with a synthetic anonymous
inline this change does not add.

## What was deliberately not done

- No clamping in vertical writing modes at all (`CreateVerticalLineBoxes` never calls
  `TryApplyLineClamp`) - real, separate work, not a small follow-up.
- The two-value `<integer> <block-ellipsis>` grammar (a custom marker string, or `auto`/`none`) is not
  accepted - `line-clamp`'s value shape has no room for a second component without a real
  `block-ellipsis` longhand or a compound value type, both larger than this change.
- `max-lines` counts one block's own line boxes, not the whole block formatting context (an edge case
  only when a `line-clamp`ed block's direct inline content is itself interrupted by a nested formatting
  context).
- The ellipsis does not honor a `::first-line` font override.

All four are recorded in the accepted-gap file, along with the pre-existing RTL-ellipsis-placement and
no-`-webkit-box`-alias gaps this change's file replaces (word-granularity truncation, previously
recorded there as a simplification, was found on closer spec reading to be the *specified*
§block-ellipsis placement rule, not a gap, and was removed rather than carried forward).

## Evidence

15/15 new tests pass (parsing/storage, line-count cutoff, exact clamped height, non-inheritance,
floor-guarded/verified popping, and two dedicated multi-pass regression tests - one for the ellipsis,
one for a popped word - each proving a repeated layout pass over the same tree comes out identical, not
compounding). Full suite: 11428/11428 (net8.0), 0 failures. Diff coverage: 98% (one pre-existing,
already-accepted uncovered line, `CssLayoutEngine.cs:3448`, gated on `box-decoration-break: clone`
co-occurring with a pagination break on a clamped line - not exercised by any fixture in this change or
its predecessor). Full solution rebuild: 0 warnings. Showcase re-rasterized through both PDFium and
MuPDF; both agree.
