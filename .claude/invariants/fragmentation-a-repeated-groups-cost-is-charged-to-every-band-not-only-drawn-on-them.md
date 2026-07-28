# A repeated group's cost is charged to every band, not only drawn on them

_From [#494](https://github.com/jhaygood86/PeachPDF/issues/494) (PR #519)._

A repeating `<thead>`/`<tfoot>` costs a table two different things on every band it spans, and they are
written in different places:

- **the proxy** — `CssLayoutEngineTable`'s first header block, `TakeBreakBeforeRow`'s two, step 5a's
  page footer and step 5's closing footer;
- **the room** — `availableHeight` and `RoomForARowIn` at row granularity, and
  `FragmentainerContext.ReserveResumeContent`/`ReserveBandEnd` inside a cell.

**Anything that decides whether a group repeats has to gate both.** Gating only the proxies satisfies
every "drawn once per page" count, every per-word "claimed exactly once" check, and every raster that
looks for the group where it should not be — and still charges each band the group's height. The
symptom is content starting below a blank strip, which is #439's and #493's defect with the sign
flipped: there, room was drawn on but not reserved.

Measured while applying css-tables-3 §6.2's conditions: with the reservations left in place, the flow on
the second page of a declined-tall-header document begins at **124.7** instead of **20.0** — a full
header's worth of nothing. `TableRepeatedGroupConditionsTests.ATallHeaderThatDoesNotRepeat_LeavesTheLaterBandsToTheRows`
is the assertion that can tell the two apart; nothing else in the suite can.

The specific trap the four raw `- _footerHeight` subtractions set: they were correct only because
`_footerHeight` was zero for exactly the tables that draw no repeated footer. A group that is *measured
and then declined* breaks that coincidence, so the height has to be asked for through something that
knows the decision (`RepeatedFooterHeight`) rather than read off the field.

And keep "is this group detached" separate from "does this group repeat". A declined group is still
detached, still measured, still drawn once — at the table's top, or under its last row. Three sites are
keyed to detachment for three different reasons, and the sharpest is the **headerless** whole-table
pre-check: it is gated on the *absence* of both groups, so reading repetition there sends a declined
table down a relocation path a table with a `<thead>` never takes.
