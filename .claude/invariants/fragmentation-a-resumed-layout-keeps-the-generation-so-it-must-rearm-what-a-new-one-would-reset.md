# A resumed layout keeps the layout generation, so it must re-arm what a new one would reset

`HtmlContainerInt.TryResumeLayoutAt` (text edits that resume at a page) continues the layout already there: the same `FragmentEmitter`,
pass entries and `LayoutGeneration`, with `PassRewind.RollBackTo` undoing what the passes from the resume point on produced. A new generation
resets a lot lazily and per box (`CssBox.BeginLayoutPass`); a resumed one does not get that, and three things went wrong until each was handled:

- **Spent once-per-layout corrections.** `_orphansBreakTaken`, `_widowsRewindTaken` and `_avoidRelaxed` on every box, and `_passesRewoundFor` on the
  container, are "once per layout". Left alone, the widows rewind taken in the previous layout is refused in the resumed one and a paragraph that
  must put two lines on the next page puts one (seen as a line at the top of a page where a fresh load has two). `CssBox.RearmBreakDecisions` and
  `_passesRewoundFor.Clear()` do it.
- **Ancestors keep their old bottom.** `ActualBottom` only grows (`MarginBottomCollapse` is `Math.Max(ActualBottom, ...)`), and a box the resumed pass
  continues into never goes through the prologue that starts it at `Location.Y`. After an edit that makes the content shorter, `<body>`/`<html>`/root
  stay at the old, taller bottom - the text runs are identical to a fresh load but the content extent is too big. The ancestors of the changed box are
  reset to their tops before the passes run.
- **`ActualSize` is an accumulated maximum** of every box's epilogue, which a layout that did not lay out the earlier boxes cannot reproduce; it is read
  off the boxes afterwards (`FlowBottom`).

- **Where to resume.** Not "the entry before the one that placed it". A widows rewind records its pass a second time (the superseded record and the
  rebuilt one, same slot), so one entry back could roll the tree back over what the rewind had already cut down (two lines drawn at the same baseline).
  And a record that names a line in the middle of a paragraph (`InlineBreakToken`) can have been settled by widows/orphans/keeps passes the resumed
  layout does not run again (`orphans: 3; widows: 3`, `break-inside: avoid`, wrappers with borders failed a sweep that way). The entry used is the last
  one for a slot before the placing pass whose record is a clean block boundary (`IsCleanBoundary`); none, and the whole document is laid out.
- **Flex and grid** report an extent that includes sizes measured before fragmentation, which reading the boxes afterwards misses (167 pt smaller on
  60 flex rows), so those documents are laid out whole.

The check is differential: `TextEditTests` edit late pages of a 150-paragraph document (several edits in a row, split, join, delete) and compare pages,
text runs, positions and content extent with a fresh load after every step; `ASweepOfEdits_OverPageBreakHeavyDocuments_AlwaysMatchesAFreshLoad` repeats that
over plain, widows/orphans 3, `break-inside: avoid`, `break-after: avoid`, bordered wrapper, blockquote and float documents. Any new per-layout latch or accumulated maximum needs the same treatment here.
