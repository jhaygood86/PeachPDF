# An engine-assigned position or size is not the generic frame's to re-derive

_CSS Fragmentation Level 3. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

`CssLayoutEngineFlex`'s commit pass (`CssBox.PositionAssignedByEngine`, added for
[#430](https://github.com/jhaygood86/PeachPDF/issues/430)) re-enters `box.PerformLayout(g)` on a flex
item that already has its final `Location`, `Width` and `Height` — the engine, not the frame, decided
them. Three generic mechanisms independently assume they are the ones deciding, and each corrupted the
item's re-layout when the flag was missing, found only by full-suite and showcase-corpus runs, not by
design review:

- **The frame's own block-flow placement** overwrote the engine's `Location` with a
  block-flow-derived one on every re-layout.
- **`ResolveOwnInlineSize`'s `GetBoxWidth` call** has a `box.Words.Count > 0` branch that sums stale
  word widths left over from the *previous* generation's layout and uses that sum in place of the
  already-pinned `Width` — corrupting word-wrap specifically for nested flex-in-flex content
  (`.row` > `.cell` > `.box` > text). Its `Width`/`Height` are already correct and need no re-deriving
  before the commit pass.

  Both of those are now answered where the pass is *entered* rather than by the box noticing mid-layout:
  the commit pass drives the item through `CssBox.LayoutContentAtItsAssignedPosition`, which is
  `LayoutBlockChild(…, framePlacesChild: false)`, so neither `ResolveBlockChildOffset`/
  `CommitBlockChildOffset` nor `ResolveOwnInlineSize` is called for it at all. Which children a frame
  positions is the frame's question — do not put either of these back behind a flag the box checks about
  itself.
- **`PerformLayoutEpilogue`'s §4.3 correctors** (keep-with-next retry, avoid/monolithic relocation via
  `TakeEarlyBreak`, orphans/widows) re-fired during the commit pass and moved a line
  `RelocateLinesAcrossFragmentainers` had already placed, landing it in the wrong fragmentainer slot.
  Guarded with `&& !PositionAssignedByEngine` on all three blocks — and this is the whole of what the
  flag is still for. These run *after* the box is complete, so no decision at the point the pass was
  entered can cover them.

Any future pass that re-lays-out a box at a position/size an engine (not block flow) already assigned
needs to audit for this same class of mechanism before trusting a "full suite green" result — a defect
in any of the three above is invisible to a test that only checks the pass completes without throwing,
and two of the three only showed up as pixel-level showcase diffs, not a raised exception or a failed
word-count assertion.
