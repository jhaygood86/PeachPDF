# Ink that overflows an inline-block lying above the foot is not drawn again on the next page (#1802)

Before: an inline-block with `line-height: 0` and 26pt type in a narrow grid item (240 x 150 page) has a 1.5pt box above
the page foot, but its glyphs overflow it, 2pt past the foot. `FragmentEmitter.ClaimsLine` lets the next page claim
content that layout could not move ("sliced content": a monolithic box or an inline-block that crosses a boundary is
drawn by every page it overlaps), keyed on the *word's* rectangle straddling the band. The word was in the fragment
tree on both pages (y=102.3 and y=-7.7), and a recording canvas, which does not clip, painted it twice.

What was misread: the issue blamed a stale line box left by an earlier sizing layout. The inline-block has one line
box; the second copy is the slice claim. Found by dumping the line boxes and the fragments' words side by side. Through
`PdfGenerator` the PDF text has the word once, because each page clips to its band; the duplicate showed in the
tree and in a recording-canvas test.

Fix: `IsInSlicedContent` now also requires the sliced ancestor's own bottom to lie below the band's foot. A box that
crosses the foot is sliced as before; ink overflowing a box wholly above it is not part of any box that crosses it.

Evidence: `AnInlineBlockInAnEngineItem_IsDrawnOnceAtItsLine` with the grid item at `60%` fails without the change
(`["z226_38", "z226_38"]`); Engine.Tests 16522 and PeachPDF.Tests 1499 on net8.0 (Debug).
