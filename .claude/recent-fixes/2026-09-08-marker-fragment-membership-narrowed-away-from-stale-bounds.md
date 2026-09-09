# A marker's phantom, empty fragment in an abandoned multi-column attempt (#483, partial)

## What was wrong

`FragmentEmitter.BuildDraft` falls back to a box's own captured `Location`/`ActualBottom` bounds
(`UsesOwnBounds`/`ownBoundsCoverRegion`) to decide slot membership whenever the box has no per-line
`Rectangles` — an outside `::marker`'s own case, since `CssBox.LayoutOutsideMarker` positions it
directly rather than through the ordinary inline flow that assigns `Rectangles`. Those bounds are
captured unconditionally by `Fragments.BoxGeometrySnapshot.CaptureBox`, regardless of whether the
marker's own word is `AwaitsTheNextFragmentainer` in that snapshot. For a block-content list item
(`<li><p>…</p></li>`) inside a multi-column container, a column-fill attempt can position the marker in
one column, then discover — via the item's own one-shot early-break retry, not just the "kept nothing"
take-back path — that the item keeps nothing there after all and resume the whole item in the next
column. The abandoned column's stale bounds still satisfied the membership test, producing a second,
**empty** `BoxFragment` for the marker there alongside its real one in the column it actually settled
in — invisible to a per-word claimed-once check, since the phantom carries zero words to double-count.

## The fix, and what made it small

Narrow `ownBoundsCoverRegion`'s bounds-based fallback with `!(CssBox.IsOutsideMarker(box) &&
box.Words.Count > 0 && box.ParentBox?.DerivedStyle.ActualDisplay == Keywords.ListItem)`. `UsesOwnBounds`
itself is untouched (`RectOf` still needs it to size the marker's real fragment once its word is
genuinely claimed); only the membership question narrows.

Two broader versions were tried and measured to regress `Acid2RegressionTests` before landing on this
one — both are worth recording since either would look like a smaller, more obviously-correct fix at a
glance:

- Excluding **every** word-bearing, rectangle-less box (not just markers) regressed
  `FullFixture_MatchesPrinceXmlPageCount` (2 pages → 1): an ordinary inline box in that same shape (bare
  text `CssLineBox.UpdateRectangle`'s `clonesDecorations`/`IsImage` gate skips) still legitimately needs
  its own bounds when none of its words are claimed in a slot its subtree otherwise belongs to.
- Excluding markers specifically (`IsOutsideMarker && Words.Count > 0`, no item-display check) *still*
  regressed the same two Acid2 tests: Acid2's own fixture retargets `ul li.first-part`/`.second-part`/
  `.third-part` to `display: table-cell`/`table` with no `list-style: none`, and PeachPDF still produces
  a marker box with a real word for them despite CSS 2.1 §12.5.1 generating a marker only for
  `display: list-item` — a pre-existing, out-of-scope quirk. Adding the item's own `ActualDisplay ==
  Keywords.ListItem` check scopes the fix to genuine list items and leaves that quirk exactly as it was.

## What this does not fix

An item spanning three or more fragments across *both* a page boundary and a column boundary in one run
can still land its marker's one surviving (correctly single) fragment in a column other than its own
first one. Traced but not fixed — recorded in
`.claude/accepted-gaps/marker-on-a-block-content-list-item-inside-a-multi-column-container.md`, which
this change rewrote to reflect what's fixed vs. what remains (the file previously described the
duplicate/lost-claim defect as still fully open).

## Evidence

- New test `StraddlingListMarkerTests.ABlockContentItemAColumnFillAttemptAbandons_ClaimsItsMarkerExactlyOnce`,
  confirmed via manual stash/pop of the fix to fail pre-fix (`Assert.Single` finds 2 marker fragments,
  one of them empty) and pass post-fix.
- A 216-combination sweep (`column-fill: auto|balance` × `column-count: 2|3` × `item-count: 3|5|8` ×
  `child-count: 1|2|4` × `page-height: 120|200|300` × plain-text or block-only children) went from 39
  markers claimed zero-or-twice to 0. A separate "claimed once, wrong column" count rose 9→16 — several
  of those 16 are markers that were previously invisible (claimed zero times) and are now claimed once,
  just not always in the item's first column yet; this is the documented remainder above, not a new
  regression.
- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0`: 10394 passed, 0 failed
  (Acid2's two tests included, both green).
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.
- Diff coverage against `main`: 100% (`Html/Core/Fragmentation/FragmentEmitter.cs`, the only file
  touched by the fix itself).
