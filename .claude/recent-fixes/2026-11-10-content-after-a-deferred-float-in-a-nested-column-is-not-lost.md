# Content after a float a column deferred is not treated as unplaced, and columns keep a width (#2318)

Documents 2 and 3 of #2318 (document 1 is [the container that goes below a float](2026-11-10-a-multi-column-container-with-no-room-beside-a-float-goes-below-it.md)).

**Document 2: a word marked "awaiting" that nothing laid out again.** An inner `columns: 3` after a paragraph, then a `float: right` taller than what is left of its outer column, then a paragraph. The fill defers the float to the next column and goes on placing the paragraph after it (`CssBox.LayoutBlockChildren`, `deferredFloats`), so by the end of the fill every in-flow child is placed and "the record names the float alone" (`DeferredFloatsOnly`). Four places read that record as "the float and everything after it in the source is still owed", and each treated the paragraph after the float as unplaced:

- `PlacedBelow` and `AddBoxesBeyond` counted it as below the break, leaving it out of the column's snapshot (and tripping a Debug assert, "a captured root is also excluded", in `BoxGeometrySnapshot.Capture`);
- `FillColumns` started the continuation's column at the first child after the float, capturing that paragraph a second time with its earlier geometry;
- `PassRewind.RollBackTo` reset every child from the float on (`ResetForRefill`, which marks all of a subtree's words `AwaitsTheNextFragmentainer`), and a continuation that only lays out the float never positions them again, so the emitter skipped the paragraph's word for ever.

**Change.** `BlockBreakToken.OwesOnlyDeferredFloats` carries the fact on the record (set where `LayoutBlockChildren` creates it, kept by `RestatedInTheNextColumn`'s `with`), and the four places above ask the token. A first version read a flag on the box (`DeferredFloatsOnly`) instead; it was already cleared when the continuation's rollback ran, which is why the first three fixes alone did not clear the document. Found by logging, in turn: the fill's children, each column capture's `beyond`/`captured` lists, the emitter's visit of the word (`awaits=True`), and the stack of whoever set `AwaitsTheNextFragmentainer`.

**Document 3: a column with no width claims nothing.** An outer `columns: 4` narrowed beside a 60% float is 27pt wide, and the inner `columns: 4` inside one of its columns has 12pt gaps: `(27 - 3 x 12) / 4` is negative, clamped to 0, and a zero-width region claims none of the words laid out in it (`w5071_161`, then `w5071_185` and `w5071_186`).

A first version took fewer columns when the gaps left under one pixel. Asking Chrome (the document served over a local HTTP server, read with `getBoundingClientRect`) showed it does not: it keeps all four columns at zero width (`p161` is `w=0` at x=16, `p185` `w=0` at x=48, a 16px pitch of gap plus nothing), the text overflows them and is painted, and the inner `float: right` lies at x = -66.8px, left of the viewport, as ours does. So the emitter follows that instead (`FragmentEmitter`):

- `FragmentRegion.ContainsInlineAxis`: a region with no width contains a word that starts at its position (no width means no overlap with any word, so the start decides);
- `StartsInAnotherInstance`: a zero-width column holds the words that start at its position, and when two instances have such a column at the same place (the same container's fill under two outer columns) the first draws the word. Without this the open-right last column (`44..inf`, which a word of 60pt starting at 32 overlaps) and the zero-width one (`32..32`) both emitted `w5071_185`.

**Evidence.** Documents 2 and 3 draw every word once. Release engine suite 19,384 (1 skipped: #2320), `PeachPDF.Tests` 1,807, zero warnings.

**Not done.** #2320 case 1 (a word drawn twice, which `main` does as well).
