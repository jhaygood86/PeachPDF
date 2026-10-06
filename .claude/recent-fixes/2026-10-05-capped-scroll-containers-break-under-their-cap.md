# A scroll container with a max-height breaks between its lines under its cap (#1479, #1375)

Before: an auto-height scroll container with a `max-height` was monolithic for every overflow value, so it moved
whole to the next page, or was sliced when taller than a page. Now it breaks like a block while its content is under
the cap, and what lies past the cap is laid out without a break.

Load-bearing idea: **the cap is consumed block size, not a document distance.** `ApplyHeight` clamped at
`Location.Y + cap`, which for a box that broke across pages ignores the unused strip at the foot, so the box's
bottom landed on an already-emitted page and a line under the cap was clipped (found by a sweep over the first
filler height, not by reading: the first experiment, which only dropped `max-height` from `IsMonolithic`, lost
no words and even looked right in a plain text extraction, but clipped one line at certain offsets and drew the
box's following sibling on top of it). `CappedScrollContainer` reads the consumed size back off the lines already
placed (per slot: the lowest line bottom minus that slot's content top), so there is no counter to go stale on a
widows rewind or any other re-pass, and `CapBottom` gives the document y where the cap is reached.

What stops the old failure (a break among clipped lines ends the pass, everything after the box is placed on an
emitted page): no break is taken past the cap. `FlowBox` skips the break for a line whose consumed height is at or
past the cap, `LayoutBlockChild` runs a block child that starts past the cap with the fragmentainer detached (the
way a monolithic box's content is), and the block loop's `StepOverTo` is skipped for such a child so the cursor
does not run ahead of the box's capped end.

Also needed, found by running it: clipped ink past the cap used to make a trailing page. `IsPrintableContentIn`
now ignores the lines of a clipping capped box that start past its cap. It reads the lines through
`CappedScrollContainer`, **not** the container's live bounds: reading `OverflowClipOf` there broke 5 existing card
tests, because a box still continuing has no settled bottom at slot-emission time (invariant
`fragmentation-a-box-continuing-into-the-next-pass-has-no-settled-bottom`).

Not done: a capped box holding a table, flex or grid container stays whole (accepted gap, #1645), because those
engines take breaks the two hooks above cannot see. `overflow: auto`/`scroll` content past the cap is still not
clipped in PDF output (a separate, existing gap), so it draws past the box as it did when the box was monolithic.
The retry-based design (lay out, note a clip, lay the document out again) was not revisited: it kept stale
geometry and cost 6x on 400 clipped cards; the one-pass design measured 4.5s against 4.4s for the same document.

Evidence: `CappedScrollContainerBreakTests` (a 46-position sweep for lost content, overlap, blank pages and visible
lines under and past the cap), the pinned `IsMonolithic` tests updated, full net8.0 suite green apart from the
existing `AListItemCostsLittleMoreThanAPlainBlock` failure, zero-warning rebuild.
