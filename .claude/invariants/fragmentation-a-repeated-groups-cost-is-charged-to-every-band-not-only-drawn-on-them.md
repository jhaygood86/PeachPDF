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
header's worth of nothing.

**It takes two assertions, not one, and they are asked of opposite edges.** A header's room comes off the
band's head, so it is stated as "where does the flow start"; a footer's comes off its foot, so it is
"where does the flow stop". `TableRepeatedGroupConditionsTests` has one of each
(`ATallHeaderThatDoesNotRepeat_…` / `ATallFooterThatDoesNotRepeat_…`), and with only the header's,
reverting the footer's gate left the **entire suite green** — while four of the five gated subtractions
are the footer's.

**And it takes two fixture shapes.** A table whose single row runs out of room *inside a cell* never
enters `TakeBreakBeforeRow`, which is where a later band's proxies are written; a table of many short
rows that breaks *between two rows* never exercises the continuation path. They are disjoint halves of
this engine, and the repeated-group work has twice reached first for the mid-cell one alone. Reverting
both of `TakeBreakBeforeRow`'s gates was also invisible to a full green suite until the many-row fixture
existed — and that is the shape an ordinary document has.

The specific trap the four raw `- _footerHeight` subtractions set: they were correct only because
`_footerHeight` was zero for exactly the tables that draw no repeated footer. A group that is *measured
and then declined* breaks that coincidence, so the height has to be asked for through something that
knows the decision (`RepeatedFooterHeight`) rather than read off the field.

And keep "is this group detached" separate from "does this group repeat". A declined group is still
detached, still measured, still drawn once — at the table's top, or under its last row. Three sites are
keyed to detachment for three different reasons, and the sharpest is the **headerless** whole-table
pre-check: it is gated on the *absence* of both groups, so reading repetition there sends a declined
table down a relocation path a table with a `<thead>` never takes.


## And "drawn once" is not "claimed by one fragmentainer"

For a **footer**, those two come apart. A group §6.2 declines has no room reserved for it — that is the
point — so where the last row ends flush with the band's foot the footer straddles the boundary and is
legitimately claimed by two fragmentainers while having been placed exactly once
([#518](https://github.com/jhaygood86/PeachPDF/issues/518)). Whether it does depends on font metrics:
Windows CI reported `[2, 3]` for a fixture Linux reported `[3]` for, with the engine doing the same thing
on both.

So state "laid out once" as **one `CssProxyBox` of the group's display type**, not as a slot list. A
header can be asked either way — it is placed at a band's top and cannot straddle for this reason — but a
footer cannot.
