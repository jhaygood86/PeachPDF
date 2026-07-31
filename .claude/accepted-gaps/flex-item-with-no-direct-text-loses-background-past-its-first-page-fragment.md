# A flex item with no direct text of its own loses its background past its first page fragment

_Tracked as [#569](https://github.com/jhaygood86/PeachPDF/issues/569). Found while diagnosing why the
`invoice` showcase rendered as 2 pages — that pagination bug itself is fixed
(`BlockConstraint.For`/`EndingAt` now refuse a fragmentation question during a detached measurement
pass); this is a distinct, narrower defect noticed in the process, in code that landed the same day
(`CssLayoutEngineFlex`'s column/row commit passes, `ItemContentCommit.CommitLayout`)._

A flex item whose own content is entirely block-level children — no direct text on the item itself,
e.g. a `<footer>` flex item holding only `<div>`/`<p>` children — loses its `background` (and padding)
on every page after the first, if its content genuinely spans more than one page. A plain, non-flex
block with the identical shape (no direct text, only block children, content spanning several pages)
renders its background correctly on every page it spans — confirmed by comparing the two side by side.
The gap is specific to a box going through the flex engine's per-item content commit pass.

`FragmentEmitter.BuildDraft` sets `usesOwnBounds = true` for any box with zero own line-rectangles
(`RectanglesOf(box, snapshot).Count == 0`) — true here, since all of `footer`'s content lives on child
boxes rather than directly on itself. The existing "shell" branch that resolves a rectangle for an
`usesOwnBounds` box only fires when the box holds **no** children in this fragmentainer either
(`children.Count == 0`) — the pure "shell continues a fragment" case. A continuation fragment that
legitimately holds children (some of `footer`'s `<p>`s really do land on the second page) but no line
content of its own falls into neither path, so it never gets a decoration rectangle at all: traced via
`FragmentPainter.PaintBackground`, which is invoked exactly once for such a box, covering only the
first page's own portion.

`FragmentEmitterTests.AMultiPageBlock_KeepsItsDecorationOnEveryPageItSpans` only covers a box with its
own *direct* text spanning pages (`rectangles.Count > 0` takes an entirely different, already-correct
path in `BuildDraft`) — it does not cover a box whose content is exclusively via children. No existing
test covers a flex item with this shape splitting across pages while checking its decoration continues.

Left out of scope here because closing it needs the same kind of deep, first-principles trace into
`FragmentEmitter.BuildDraft`'s draft-materialization logic that the pagination bug above did, and
doing that in the same sitting risked rushing a fix to genuinely tricky fragment-tree code. See
issue #569 for the suggested fix direction (a per-fragment decoration rectangle for a box that has
children in this fragmentainer but no line content of its own, alongside the existing pure-shell case).
