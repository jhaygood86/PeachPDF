# A flex container is read down the page, not down the source

_Landed 2026-07-27. [Issue #448](https://github.com/jhaygood86/PeachPDF/issues/448)._

The pass that moves a flex line or grid row across a fragmentainer boundary walked its lines in list
order and accumulated a running displacement, on the assumption that a later line is lower down the
page. Two ordinary declarations break that, and
[PR #454](2026-07-27-an-avoided-break-between-two-flex-lines-moves-the-earlier-line.md) had just made
the walk rest on it much harder by reaching *back* over lines it had already placed.

**The load-bearing idea is that two different orders were being read off one list.** §3.1 identifies a
break point by **flow** order — the earlier sibling's `break-after` and the later one's `break-before`
name the same point — while *which* line a boundary leaves above it, and which lines follow one that
moves, are questions about **block-axis** geometry. The engines had one list doing both jobs. So
`LineGroup` now carries the break point above a group as an explicit flow-ordered pair
(`Earlier`/`Later`) and the list itself is in block-axis order, which lets the two disagree.

**`flex-wrap: wrap-reverse`.** `DistributeCrossSpace` reverses the cross offsets after assigning them,
so the first line in the source is the *last* one down the page. The engine now emits the groups
reversed, and at the boundary above group *i* the two sides swap roles: the group's **own** items carry
the governing `break-after` and the group **above** it carries the `break-before`. That reading is the
only realizable one — the pair names a boundary, and the only thing a fragmentainer can do at a boundary
is start the content *below* it on a new page. Moving the later-in-flow line instead would move a line
*up* past the one above it.

**A column direction** stacks the lines along the inline axis: they share one block-axis range, so no
boundary falls between two of them. All lines become **one** group, which kills the between-lines break
value (it named nothing) while keeping the geometric half — a line that may not be cut still moves, and
the lines beside it move with it rather than sliding out of alignment. The block-axis break points such
a container really has are between the *items within* a line;
[that is filed and recorded](../accepted-gaps/flex-column-container-has-no-break-points-between-items.md).

## What was found by running it rather than by reading it

**The `flowLines.Count > 1` guard on the column arm was unfalsifiable and is gone.** It read as the
obvious statement "only a container with more than one line has this problem", but for a single line the
one-group arm produces a `LineGroup` *identical* to the one the per-line walk produces — same boxes, same
`Earlier`, same `Later` — so no fixture can distinguish the two. Neutralizing it failed nothing. The
guard is now the plain `!_isRow`, with the equivalence stated in a comment; the single-line column test
is kept as the pin that the arm does not change the ordinary case.

**Neutralizing the wrap-reverse pair swap the *naive* way throws rather than failing.** Taking the two
sides from geometry (`earlier = the group above`) while keeping the reversed order fails exactly the
three forced-break rows, which is the right measurement. Taking them from the *unreversed* list index
instead walks off the front of it — worth knowing before "simplifying" the branch, because
`CssLayoutEngineFlex.PerformLayout` swallows the exception into a `RenderError` and the failure surfaces
as five unrelated-looking test failures rather than an index error.

**The showcase needed 8 filler paragraphs, not 11.** At 11 the container cleared the boundary entirely
and the fixed and unfixed builds produced byte-identical pages — the section demonstrated nothing while
looking correct, which is
[the same trap the last one hit](2026-07-27-an-avoided-break-between-two-flex-lines-moves-the-earlier-line.md)
from the other direction. At 8 the upper line straddles: on the unfixed build the two `source line 1`
cards are drawn 29.7pt too high, overlapping the bottom edge of the cards above them. Verified in PDFium.

## Evidence

Tests: `FlexGridFragmentationIntegrationTests` +9 cases counting theory rows. Verified load-bearing by
neutralizing each part in turn: the whole wrap-reverse arm (order **and** pair) → **5 fail**; the pair
swap alone, with the order kept → **3** (the forced-break rows only, geometry intact); the first group's
"this line is not the first in flow, so nothing above it is a break point" arm → **1**; the column
single-group arm → **3**.

Full net8.0 suite green (6714), CLI green (96), **100% diff coverage**, zero-warning
`dotnet build PeachPDF.slnx -t:Rebuild`.

**68 of 69 showcases identical**; `paged_media_monolithic_content` gained the wrap-reverse section above.
Comparing showcase PDFs byte-for-byte needs three fields normalized first — `/CreationDate`, the `/ID`
array, and the random six-letter **font subset tag** — or all 69 differ on every run and the comparison
says nothing. Annotation `/NM` GUIDs are random too, which is what makes the five showcases carrying link
annotations differ after the first two are scrubbed.
