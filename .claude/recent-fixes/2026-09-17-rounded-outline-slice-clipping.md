# Rounded sliced outlines preserve their physical outer edges

Rounded `box-decoration-break: slice` geometry paints backgrounds and borders against the unbroken
strip and normally clips back to each fragment rectangle. Reusing that pairing for outlines made the
outline path and its clip disagree by the outline's outward reach. The page's ordinary content-area
clip could then reduce the surviving physical top/left edge to an antialiased hairline, while the
artificial inline-axis break faces could remain visibly closed.

## The load-bearing distinction

A sliced outline is now built from the fragment rectangle itself, rather than building from the
unbroken strip and trying to recover the correct outward spill with a second clip. Every fragment
keeps its block-axis top/bottom outline bands. Physical-edge flags leave the artificial inline-axis
line-break faces open: only the first fragment has the true start edge and only the last fragment has
the true end edge.

**Superseded in part by issue #1163**: the "leave the inline-axis break faces open" paragraph above no
longer describes current behavior. An outline on a wrapped inline now closes every line into its own
complete ring instead — border and background still leave those same edges open, since only outline's
own paint entries force them closed (`FragmentPainter.cs`). The fragment-local path-building fix this
entry describes (building from each line's own rectangle rather than the unbroken strip) is exactly
what #1163 builds on: it is what let the fix be a one-line "force these two flags true" change at the
paint-entry-collection site, rather than a new geometry algorithm.

The page-level content clip is widened by the maximum outline reach on that page. This lets a box at a
content-area edge paint into the page margin, while the box's own and its ancestors' overflow clips are
still pushed later and constrain it normally. `OutlineDrawHandler` remains the single source of the
`auto` outline's real reach.

## Evidence

`RoundedOutlineOnAWrappingInlineElement_ClosesEachFragmentAsACompleteRoundedRing` (renamed by #1163,
which also updated its assertions for the now-closed edges) verifies the fragment-local paths. The
outline showcase retains the same wrapped inline case for the repository's standard manual PDFium and
MuPDF rasterization verification; the test project deliberately carries no runtime dependency on either
renderer.
