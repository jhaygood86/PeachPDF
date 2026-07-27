# A flush line *begins* its fragmentainer, so that is the slot a directional break vacates

_CSS Fragmentation Level 3 §3.1/§4.4. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

`SlotStartingAt` resolves a top edge flush on a boundary to the **later** slot, so a line whose top sits on a
slot's content top does not merely *reach* that fragmentainer — it **begins** it. Two questions in
`LineRelocation.DeltaFor` depend on that being one fact rather than two:

- §4.4 says a forced break at a point that already *is* a boundary is satisfied — but a **directional** value
  asks for more than a boundary. It asks that the content *begin* on a page of the named side, so a flush
  line satisfies it only where `BreakValues.SlotIsOn(slot, side)` holds. Clearing the value unconditionally
  downgrades `recto` to `page` for any line that happens to land flush.
- Where the side is *not* satisfied, the slot the parity walk must reserve is **that** slot, not the next
  one. Starting the walk at `slot + 1` finds parity already satisfied there, reserves nothing (and retracts
  any earlier reservation), the vacated slot holds no printable content, CSS Paged Media 3 §3.2 drops it,
  and the line prints on exactly the page it was moved off.

The measured symptom, on a 200pt page with 160pt of filler so the line is flush on slot 1, and
`break-before: recto`: **block flow gives 3 pages** (blank page 2, content on page 3, a right page) and flex
gave **2**, with the content on a left page — `recto` byte-for-byte indistinguishable from `page`, which is
the defect the parity walk was added to fix.

**A test on the slot index does not catch it.** The slot was already correct; the page was lost downstream,
when the emitter dropped an unreserved empty slot. Any test for a directional break has to assert
`FragmentTree.Fragmentainers.Count` as well as where the box landed.
