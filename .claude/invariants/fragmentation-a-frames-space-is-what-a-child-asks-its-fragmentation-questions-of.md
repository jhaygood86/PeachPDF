# A frame's space is what a child asks its fragmentation questions of, and the frame is handed down

_CSS Fragmentation Level 3. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

A block-level box's layout pass is entered at the **frame**, not at the box: `CssBox.LayoutBlockChild` is
what a child loop calls per child, and `CssBox.DriveBlockChildPass` runs the pass in three phases — the
child opens it (`BeginBlockPass`), the frame places and sizes it (`PlaceAndSizeBlockChild`), the child lays
out its own content (`LayoutContents`). Four things that shape depends on, each of which has already cost
something once:

- **The phase order is forced by dependencies, in one direction only.** Placement reads what the prologue
  settles (`_isForcedBreak`, `_forcedBreakSide`, `_adjoinsForcedBreakPoint`, `UsedPageName`), and the child's
  inline size is resolved against the page the *offset* lands on, so `MeasureWordsSize` has to have run
  before `GetBoxWidth` reads a leftover word. Nothing in the frame's block-flow arithmetic reads the child's
  size — checked line by line for [#540](https://github.com/jhaygood86/PeachPDF/issues/540), and it is what
  makes the split fall at one clean seam. Reversing any pair puts a box on the previous layout generation's
  measure, which is the defect `#540` removed.
- **The frame is a parameter, not a lookup.** `PerformLayoutImp(g, frame, framePlacesChild)` receives it
  from whoever entered the pass; `CssBox.PerformLayout` is the adapter that names `ParentBox ?? this` for a
  caller that is not a child loop (an engine measuring an item, the out-of-flow walk, the root). A box that
  looked its own frame up mid-layout could not be told "you are not being placed at all", which is the next
  point.
- **Which children a frame positions is the frame's question.** An engine-positioned item is one driven with
  `framePlacesChild: false` (`ItemContentCommit` → `CssBox.LayoutContentAtItsAssignedPosition`), not one
  that is placed and then notices from inside its own layout that it should not have been.
  `PositionAssignedByEngine` still exists, but only for the epilogue's own movers — the keep-with-next
  first-line retry, §4.3's `avoid`/monolithic relocation, §5.4's widows push — which run after the box is
  complete. Do not re-introduce it as a placement gate.
- **`PerformLayoutImp` is still the one virtual "run this box's pass" seam, and it must stay reachable for
  every box.** An earlier attempt at this change gave the frame a `RunsALayoutPassOfItsOwn` predicate and
  called the override only for the three box kinds that set it (`CssBoxHr`, `CssBoxMarker`, `CssProxyBox`).
  That silently made every *other* override dead: nine tests across five files subclass `CssBox` and override
  `PerformLayoutImp` to state a condition no markup produces (a box whose layout throws, a cell that stops,
  a box that hands back the same break record every pass), and all nine stopped running rather than failing
  loudly. The frame calls the override for every child; the base implementation is what dispatches back into
  the phases.

Coordinates stay **document-absolute** throughout. The frame commits the offset before the child lays out
its content, but nothing is converted to be fragmentainer-relative — `FragmentEmitter`'s whole input contract
is document space, `ItemContentCommit` commits every engine to "final position first, then content", and
several hundred test assertions read `Location.Y` directly.
