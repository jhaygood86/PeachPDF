# The UA sheet should not clip table cells

`td, th { overflow: hidden }` in the default stylesheet is not from any spec. The HTML Standard's
rendering section sets `display`, `padding` and `font-weight` on those elements and nothing else,
and no browser clips a cell.

## What measuring it turned up

- **It is invisible under automatic table layout**, which is why it survived. A narrow `td` simply
  widens to fit its content, so the clip never bites — the first repro I wrote reported identical
  ink before and after. It needs `table-layout: fixed`, where the column cannot grow.
- **The observable is ink, not text.** A clipped glyph is still in the content stream, so
  `pdftotext` finds it either way and reports nothing wrong — the same property that makes
  clipping undetectable from the finished PDF. Rasterising is the only way to see it. Measured with
  MuPDF (which honours clipping) at 200dpi, counting dark pixels to the right of a 40pt cell:
  Chrome 2021, before 1315, after 2542. `pypdfium2` is not installed here, so this is one renderer
  rather than the two the testing conventions ask for — but MuPDF is specifically the one that
  honours the clip, which is the property under test.
- **Three existing fixtures depended on the UA rule.** `OverflowClipIntegrationTests`'
  rounded-box-in-a-table-cell tests obtained a clipping cell from the default sheet, and one of them
  asserted `Overflow.Hidden` on the `td` outright. Their real subject is the padding-box clip bound
  under a non-uniform `border-radius`, not where the cell's overflow came from, so each fixture now
  declares `overflow: hidden` on the cell itself. The assertions are unchanged and still pass.

## Evidence

`TableCellOverflowTests`: a cell's computed overflow is `Visible`, a cell pushes no clip of its own
(asserted through `FragmentPaintHarness` + `RecordingGraphics.PushedClips`, per the convention for a
change that alters what reaches the graphics layer), and an author-declared `overflow: hidden` still
clips. The first two fail against `main`. Full suite green on net8.0 (10,348), 0 new build warnings.
