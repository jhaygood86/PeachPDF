# The flow after a restarted float is asked about its own page, not the float's (#1533)

**Symptom.** A float with no room at the foot of a page (three lines, so splitting it 2+1 would break
widows) is restarted at the top of the next page, and the paragraph after it kept a line that crossed the
foot of the page it really sits on: painted there and cut in two by the page clip, its words lost to text
extraction. The smallest document is a right float after a table, then a paragraph; a 300x240pt page loses
five words.

**Cause.** `CssBox.TryRestartAt` steps the pass's fragmentainer cursor to the destination slot
(`FragmentainerContext.StepOverTo`) so the restarted head is measured against the band it moves to. For an
in-flow head that is right: everything after it moves too. A float is out of flow, so nothing after it moves,
and the cursor stayed on the float's page. `CssRect.WouldStraddleFragmentainer` asks the cursor's band
(`HtmlContainerInt.BandBeingFilled`), so every line of the paragraph (laid out on page 1) was asked whether it
crossed page 2's foot. It never did, so no line was broken. `CursorSpills` counted it (`cursor=1 grid=0` for each
word of the paragraph), which is what it exists for, and nothing acted on the count.

It is older than the float continuation work (#1520): the layout is identical before and after it. What #1520
changed is that the line used to be painted a second time on page 2, which hid the loss from text extraction.

**Fix.** `TryRestartAt` remembers the slot the cursor was on when the restarted head is a float, and the loop
that re-enters the head puts the cursor back (`FragmentainerContext.StepBackTo`) once that float has been laid
out; the `finally` of the loop clears the memory so an abandoned pass cannot apply it later. `StepBackTo` is the
one place the cursor moves backwards, and only for a caller that stepped it a moment earlier; it is a no-op for a
column's own band. See
[the invariant on the cursor](../invariants/fragmentation-the-fragmentainer-a-pass-is-filling-is-not-the-one-it-opened.md).

**Not done.**

- The cursor is restored only after a restarted *float*. An in-flow head that moves to a later page leaves the
  cursor where it is, as before.
- What follows the float can be placed past it (a second float on the next page, a paragraph with `clear`). The
  loop re-steps the cursor after each in-flow child, so those are right too, and the fixtures pin it; it was not
  changed.
- An *empty* block with `clear` after the float still does not clear (#1535): it is a separate defect with or
  without pagination.
- Documents that lose words for other reasons change too when the content after a float shifts by a line, and
  can then meet a defect that was not reachable before: a straddling line in an unbreakable `overflow: scroll`
  box (#1528, [the scroll container gap](../accepted-gaps/auto-height-scroll-container-in-a-flex-grid-item-column-or-with-an-absolute-box-stays-unbreakable.md)),
  the last line of an absolute box in a multi-column container (#1537). They were already losing words.
- `PEACHPDF_TRACE_PAINT=1` (Debug builds only) prints a line per word the painter visits, with its page,
  rectangle and how much of it the page clip leaves. It is how this was found: a word painted but cut by the
  clip does not show in text extraction. `HtmlContainerInt.ClipReport` lists only the words that were drawn and
  cut, in every build; the trace also lists the ones skipped and the ones shown whole.

**Evidence.** `FloatRestartedToNextPageIntegrationTests`: six of its nine cases fail without the change (a word
reaching 223 to 225 against a foot at 220); the `clear` paragraph cases and the control pass either way and guard
what the loop must keep doing. Corpora of generated documents on small pages (1,950: floats mixed with tables,
columns, scroll boxes and absolute boxes), each rendered on main and on this change and judged by word counts
and the paint trace: no document that main draws completely changes for the worse. Of the ones that change, by
text extraction 59 lose fewer words and 20 lose more, every one already incomplete. The trace also sees words cut
by the page clip; ten of the changed documents judged with it: six are better or unchanged (text extraction had
rated three of them worse) and three are worse by 3, 8 and 29 words on documents already losing 40 to 350, traced
to the defects in the fourth point above. The tenth has the same words as before, now also painted as a slice at
the top of the next page.
