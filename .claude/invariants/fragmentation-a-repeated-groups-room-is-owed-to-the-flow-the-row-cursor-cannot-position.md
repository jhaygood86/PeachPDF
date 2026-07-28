# A repeated group's room is owed to the flow the row cursor cannot position

_CSS Fragmentation Level 3 §2, css-tables-3 §2.1. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

`CssLayoutEngineTable` reserves a repeating `<thead>`'s height by advancing `TableRowCursor.CurrentY`.
That reaches every box the pass **places**: the rows it lays out, and the cells of the resumed row it
enters fresh. It reaches **neither half of a mid-cell continuation**, because a cell continuing an
earlier fragmentainer keeps the one `Location` its first fragment was built from
([the rule](fragmentation-a-continuation-may-not-move-geometry-an-earlier-fragmentainer-emitted.md)),
and its content is positioned by the flow, which starts a resumed block at
`FragmentainerContext.ResumeContentTop`.

So `cursor.CurrentY`-after-the-header and `ResumeContentTop` are **two answers to "where does this
fragmentainer's content begin"**, and anything placed above the resumed content has to be stated to
both. It was stated only to the cursor, and the measured symptom is not a crash or a drift but
**content drawn underneath an opaque box**: six words per page hidden under the header's
`background` in `paged_media_table_row_continuation`, on two of three pages, with the fragment tree
perfectly consistent — every word claimed exactly once, the header on every page.

That is the trap worth carrying: **the standing "every word claimed exactly once" check cannot see
this class of defect at all.** Two fragments may each be claimed by exactly one fragmentainer and
still occupy the same rectangle in it. The check is about *which* fragmentainer, never about *where
inside it*, and only rasterizing the page finds the difference.

`FragmentainerContext.ReserveResumeContent`/`RestoreResumeContent` is the seam, and
[css-tables-3 §6.2](https://www.w3.org/TR/css-tables-3/#repeated-headers) is the normative sentence it
implements: *"When the header rows are being repeated, user agents must **leave room** and if needed
render the table top border."* Anything else that comes to occupy the top of a fragmentainer on a
continuation — a repeating `<tfoot>` hoisted to the top, a running header from paged media — owes the
same statement, and owes it additively and restored, because the amount belongs to one subtree's
arrangement of one fragmentainer rather than to the pass.

**A reservation names its fragmentainer, and that is not bookkeeping.** `SlotIndex` is a *cursor*:
`StepOverTo` moves it on when a forced break is realized by placement, so the pass can leave the
fragmentainer a reservation was made in without the reservation being restored. It has to stop
applying there — the page a forced break opens gets no repeated header, because the table's per-row
header block only runs at a break between two rows. Room held on it is room held for a header that is
not drawn, measured as a **13pt blank strip** at the top of that page on a resumed row whose second
cell carries the break. The general form: **any state scoped to "the fragmentainer being filled" must
record which one, because that is a cursor and not a constant.**

Note what this does *not* buy. Two nested repeating-header tables still overlap each other: the inner
table's own proxy is placed at `Math.Max(startY, PageTopOf(ResumeSlotIndex))`, which for a table that
began on an earlier page is the band top — exactly where the outer header was drawn. The inset
composes; the proxy placement does not.
