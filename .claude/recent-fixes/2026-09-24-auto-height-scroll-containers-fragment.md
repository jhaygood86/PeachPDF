# An auto-height scroll container fragments like any block

Closes #1321.

## What was wrong

`MonolithicContent.IsMonolithic` counted every scroll container (`overflow` other than `visible`/`clip`)
as monolithic. A tall `overflow: hidden` wrapper (the clearfix idiom) therefore fit no fragmentainer and
had its content laid out unbroken since #350, with each page showing one slice. The line straddling each
slice boundary was claimed only by the page its top fell on. That page drew it in its bottom margin,
where the page clip hid it, so one line was lost per page. Whether a line happened to straddle
depended on alignment, which is why the issue's `padding-top` was needed to make it appear.

## The load-bearing idea

The spec never asked for that set. css-break-3 §2 says "UAs **may** consider as monolithic any elements
with overflow set to auto or scroll and any elements with overflow: hidden and a non-auto logical height
(and no specified maximum logical height)". An auto-height scroll container grows with its content, so
it has nothing to clip or scroll in the block axis on paper. `IsMonolithic` now requires
`HasConstrainedBlockSize` as well as `IsScrollContainer`: a non-auto logical height, or a max logical
height other than `none`. It applies that to `auto`/`scroll` as well as `hidden`, since §2 only permits
(never requires) treating those as monolithic, and the issue's table shows `overflow: auto` failing the
same way. A percentage against an indefinite containing block counts as `auto`/`none` (CSS 2.1 §10.5,
§10.7, the same test `CssBox.HasAutoBlockEndHeight` uses). In a vertical writing mode the physical
`width`/`max-width` is checked instead.

The exemption applies only where `BreaksInBlockFlow` holds. The box must be display `block` or
`list-item` as computed (`DerivedStyle.ActualDisplay` blockifies floats only, not abspos boxes, so an abspos box qualifies only when its own `display` is `block`/`list-item`). It must not be floated or
page-floated, its parent must not be a flex or grid container, and every ancestor must be a kind known to
carry a break on (`EveryAncestorCarriesABreak`, an allow-list; see the eighth round below). The first
version exempted every scroll container, and a post-change review found two regressions by rendering
against `main`:

- **An `inline-block`** straddling a boundary then had its content fragmented inside its line, which
  PeachPDF cannot do. The box collapsed to a zero-height clip and drew none of its content. §2 separately
  lets inline-level independent formatting contexts stay monolithic.
- **A flex or grid item's `Height`** is set transiently by its engine while measuring, and pinned for
  good by `ItemContentCommit.CommitLayout`. A block-size test on the item therefore answered "auto" to
  `LineRelocation`'s mover before the commit, and "capped" to `LayoutContents` during it. The mover
  didn't move the line, and the commit then laid the content out unbroken, so one line was lost where
  `main` moved the whole flex line.

Both are pinned by `StraddlingAutoHeightScrollContainerInlineBlock_KeepsItsContent` and
`StraddlingAutoHeightScrollContainerItem_MovesWholeWithItsLine`, which fail without the scope limit. The
grid fixture needs `grid-template-columns:60pt`: a grid item stretches past its own `width`, so without
that it is one line tall and never straddles. A tall auto-height flex/grid item loses its boundary lines
on `main` as well; that isn't changed here.

A second review round tightened the block-size test itself:

- **Percentages resolve against layout's own base.** `CssLayoutEngine.PercentageHeightResolves` is
  extracted from `HasDefiniteHeight` and reused. The first version asked
  `IsHeightDefinite(box.ContainingBlock)`. That was wrong for an absolutely positioned box, whose base is
  its nearest positioned ancestor, and for a fixed one, whose base is the page. An abspos `height: 50%`
  under a `position: relative; height: 500pt` ancestor, with an auto-height wrapper in between, was
  wrongly fragmentable.
- **A cap is a length, never a keyword.** The test is `CssValueParser.IsValidLength`, the gate every
  layout site applies to `height`/`max-height`, instead of comparing against the literal `auto`/`none`.
  `max-height: auto` did not actually reach the old comparison as `"auto"` (its test passes either way),
  so this is hardening rather than a live fix.
- **A percentage inside a flex/grid item is treated as a cap.** The item's own `Height` and
  `AlgorithmicDefiniteHeight` change during its engine's pass, so whether a descendant's percentage
  resolves flipped within the pass. That is the same instability that excludes the items themselves.
  `PercentageInsideAFlexOrGridItem_CapsTheBlockSize` resets the item to its unpinned state after layout
  and fails on the earlier code.

A third round made "capped" mean what layout actually caps:

- **`max-height` is tested against its own base.** `ApplyHeight`'s clamp applies a percentage
  `max-height` only when `IsHeightDefinite(box.ContainingBlock)` holds, for abspos boxes too, whereas
  `height` resolves against `PercentageBase`. The previous round sent both through
  `PercentageHeightResolves`. So an abspos `max-height: 50%` under an auto-height in-flow parent counted
  as a cap that layout never applied, and the box was sliced and lost lines.
- **A size fixed without `height` caps too.** A declared `aspect-ratio` (CSS Box Sizing 4 §5) and an
  abspos box with both block-axis insets (CSS 2.1 §10.6.4) give an auto height a size that doesn't grow
  with content. Both are asked of the declaration, not of `TryGetAspectRatioHeight`, which needs the
  used width and so would answer differently before width layout. An `aspect-ratio` card straddling a
  boundary now moves whole again (`EveryScrollContainerValue_MovesWhole`).

A fourth round excluded **floats**. A float computes to `display: block`, but it is placed through
`LayoutContentAtItsAssignedPosition`, which drops the pending break token
(the float-pagination gap, #1201, since closed by #1348). Made fragmentable, a straddling `overflow: hidden` float lost every line after the boundary
(F4–F12 of 12). On `main` all 12 were placed, with the boundary line drawn past the band. The first
review's claim that floats "got better" came from a fixture that didn't reach this path. Checking the
same fixture with `overflow: visible` showed that any float crossing a boundary loses its later lines,
not only one taller than a page, and the gap file now says so.

A fifth round extended both exclusions to **descendants**. An auto-height clearfix block inside an
`inline-block` or a float is laid out through that ancestor's assigned position too. Made fragmentable,
it lost every line after the boundary: with 5 lines, F1–F3 were placed and F4–F5 appeared on no page.
The parent-only test had missed it. The walk covers every ancestor (now `EveryAncestorCarriesABreak`), and
`AutoHeightScrollContainerInsideAnInlineBlockOrFloat_PlacesEveryLine` pins both shapes. A clearfix block
inside a flex item needs no such walk: it splits and keeps every line.

A sixth round added **page floats** (`float: top/bottom/top-bottom/snap`, `CssBox.IsPageFloated`), which
`IsFloated` does not cover, to both float checks through `IsFloat`. No fixture showed a difference from
`main`: a straddling 5-line page float is moved to a page edge and never breaks. The exclusion is there
by construction: a page float is moved to its edge whole, like a float, so it cannot continue a break
either. (A 12-line `float: bottom` lost its lines past F5 at the time, on `main` too; #1348 fixed that on
`main` while this was in review, #1332.)

A seventh round added **vertical writing-mode ancestors** to the walk (`IsUnresumableOrthogonalFlow`).
Such a block lays its children out through `LayoutVerticalBlockChildren` →
`LayoutContentAtItsAssignedPosition`, which drops the child's break token, and the vertical parent is not
itself `IsMonolithic`, so it doesn't detach the fragmentainer either. The measured result was worse than
the review predicted: a clearfix `writing-mode: horizontal-tb` block inside a `vertical-rl` parent placed
**no** lines at all, where `main` placed all five. A vertical scroll container on its own was checked
too: it keeps every line on both the branch and `main`.

An eighth round replaced the deny-list with an **allow-list**, `EveryAncestorCarriesABreak`. Every
ancestor must be block, list-item, block-level flex/grid, or a block-level table and its row groups, rows
and cells, and not floated, page-floated or `IsUnresumableOrthogonalFlow`. The review named two more
break-dropping ancestors, a table caption (`LayoutCaptionGroup`) and inline-level flex/grid/table. A
probe over nine ancestor shapes confirmed the caption and `inline-table` (F4–F5 lost; `main` placed
all five). `inline-flex`/`inline-grid` placed every line, because their items are measured with
breaking off. Each earlier round had found the next deny-list gap only by measuring lines disappear. The
allow-list inverts the failure mode: an unlisted ancestor keeps `main`'s monolithic behaviour, which can
leave the fix out but never lose content. `AutoHeightScrollContainerUnderAnyAncestor_PlacesEveryLine`
pins all nine shapes, and the caption and `inline-table` rows fail on the deny-list version.

A ninth round, from the PR review, found that the allow-list inspected only **ancestors**. A fragmenting
wrapper also needs its *contents* to carry the break, and several did not:

- A tall float in a clearfix wrapper drew 0 of 30 lines (`main` 30), and a `.row` of two floats 0 of 60.
- An `overflow: hidden` child of a **multi-column container** lost 29 words on a single page with no page
  break at all. This one is a paint-time loss: split across the two columns, its first-column fragment
  got a zero-height clip, so the words were in the fragment tree but never drawn. A test that reads the
  fragment tree passes against the broken code; `WordsPlaced` now paints each page through
  `RecordingGraphics` and counts only strings inside every clip in force.
- An **inline-block** holding block content lost all 14 of its lines (fuzz case 1_72): it is laid out
  through `FlowAtomicBlockContentChild` → `LayoutContentAtItsAssignedPosition`, the #1201 path.
- An absolutely positioned badge in a `position: relative; overflow: hidden` wrapper vanished. That turned
  out to be #1349 (see [its entry](2026-09-24-an-absolute-box-on-an-emitted-page-is-drawn-there.md)),
  fixed here too, so the exclusion is now a safety margin rather than the fix.

`EveryDescendantCarriesABreak` is the matching allow-list over the subtree (block, list-item, inline,
atomic inlines, and tables with their parts; not absolutely positioned, page-floated or multi-column;
`display: none` subtrees skipped), and `EveryAncestorCarriesABreak` now rejects a multi-column ancestor.
Flex/grid descendants are excluded although the flex probe lost nothing: the review reported a grid
holding a `break-inside: avoid` paragraph losing words, which did not reproduce here, and an unlisted
kind costs only the old behaviour.

**Inline-blocks and floats were excluded at first, and are not now.** The inline-block loss above was
#1201, which main closed with #1348 while this was in review: `FlowAtomicBlockContentChild` now lays the
content out unbroken (`CssLayoutEngine.LayoutContentUnbroken`), so an inline-block keeps every line
whatever breaks around it. #1348 did the same for a float placed by the *inline* flow (`FlowFloatChild`).
Floats took two more rounds.

A tenth round, from the next PR review, found three float shapes that lost content `main` keeps
(a 300×200pt page, 12pt lines):

- **Text beside a float inside the wrapper** (`<div overflow:hidden><div float:left>F1…F20</div><p>side</p></div>`):
  `side` was drawn on no page, and with a 45-line float all ten paragraphs after it were lost. A
  *block-level* float is not placed by `FlowFloatChild`: it goes through the block frame, its content
  breaks at the page boundary, and the next pass resumes inside it on page 2. The paragraphs beside it are
  then placed back on page 1, which was already emitted.
- **A float's boundary line clipped instead of moved** (nine paragraphs, then a wrapper around a 10-line
  `float: right`): F2 drawn at the page foot past the band, where `main` moves the float to page 2.
- **A wrapper beside a floated sibling** (media object): the second `overflow: hidden` block, placed beside
  a float that crosses the boundary, lost B1–B3 and drew B4 at y=16.3, above the page margin.

That round first kept every wrapper holding or following a float monolithic. It passed the review repros
but put the customer page that motivated this PR back to `main`'s behaviour: a
`#contentcontainer { overflow: hidden }` holding a floated category menu and a long text column, whose
sentence at the page 1/2 boundary was drawn in page 1's bottom margin and clipped. The eleventh round
replaced it with the fix for the cause: **a block-level float is laid out unbroken**, the same way the
inline flow's float and the absolutely positioned box below are (`CssBox.LayoutBlockChild` →
`LayoutBlockChildUnbroken`). Its break no longer ends the pass, so the content beside it is laid out on the
pages it belongs to. A float that then straddles a page boundary but fits on a page is moved whole to the
next page (`CssBox.MoveWholeOntoTheNextPageIfItFits`, called from both the block frame and
`FlowFloatChild`), which is safe because the float is placed before the in-flow content after it; one
taller than a page is sliced ([its gap](../accepted-gaps/a-float-taller-than-a-page-is-sliced-not-fragmented.md),
#317). A float holding a multi-column container keeps the breaking path: an earlier attempt at laying every
float out unbroken broke those, because the columns engine needs the fragmentainer that detaching removes.
With that, floats are allowed back inside a fragmenting wrapper and the preceding-float rule is gone.

A review of the float change found five more things, each reproduced on a 300×200pt page:

- **A float in a column** was laid out unbroken too, and its lines past the column's foot were drawn
  below the column, outside the page band. A column does not continue a slice the way the next page does,
  so a float in a column (`CurrentFragmentainer.HasOwnBand`) keeps the breaking path.
- **A later float rose above the moved one**: its static position was still on the page before, and
  `FloatBox` placed it there. CSS 2.1 §9.5.1 rule 5 is now enforced against the earlier floats among the
  same containing block's children (`LowestOuterTopOfAnEarlierFloat`). Placing two `float: right` boxes
  side by side is broken on `main` independently of this (#1374), so the test asserts only the top for
  right floats.
- **A moved `inside`/`outside` float kept the old page's side.** After the move the float is placed again
  at the new page top (`FloatPositionBesideTheFloatsAt`, the position-only core split out of
  `FloatBoxLeft`/`FloatBoxRight`, so the box is not relocated twice), and translated there with its content.
- **A relative offset decided the move and was then dropped.** The move is decided on `StaticTop`/
  `StaticBottom` and keeps `RelativeOffsetY`.
- **A forced break inside a float is no longer honoured**, since the fragmentainer is detached.
  css-break-3 §3.1 makes that optional outside the root's flow, so it is documented, not tracked.

Each fix has a test that fails without it (checked by removing the fix): the rule 5 theory, the outside
float, the relative theory, the column float, and a float taller than a page that must not move.

The float change also fixes two `main` bugs found in the previous round: #1339 (a block beside a 30-line
float drew only B27–B30; now all 30) and #1340 (the line after a float moved to the next page was drawn on
both pages; now once). The customer page draws the sentence at the top of page 2. Measured on the 300-document
fuzz below, the float change recovers far more than it loses: net-better documents go from 118 (3,655 words)
to 156 (4,582 words), and net-worse from 14 (150 words) to 31 (253 words). The added losses that were
traced are the same pre-existing classes landing somewhere new (#1328 slice-boundary lines, `break-inside:
avoid` blocks, #1369/#1373); the two checked by hand (seeds 91, 269) were one each. After the review fixes
the numbers are net-better 155 (4,602 words) and net-worse 24 (189 words). The one document newly worse than
before them (seed 164) reduces to a float inside a column, which is back on the breaking path and loses
exactly what `main` loses (commented on #1339).

The same round fixed the reviewer's absolutely positioned case in `CssBox.LayoutBlockChild`: a
`position: absolute` block child is laid out unbroken (`LayoutBlockChildUnbroken`, shared with the column
page float), for the same reason as the block-level float. Its break ended the pass, and the in-flow
content after it, which it does not displace (§9.3.1), was placed back on the page the break left. `main`
lost that content too whenever the box was not its parent's first child; once #1349's
`GetPreviousSibling` fix stopped placing it *below* the first-child box, this branch lost it in that case
as well. Now each page shows the slice of the box that falls in it. The cost, a line cut at each page
boundary inside the box and no §4.3 relocation of its contents, is recorded in
[its gap](../accepted-gaps/a-tall-absolutely-positioned-box-is-sliced-not-fragmented.md).

The same round kept a scroll container monolithic where it, or an ancestor, has `break-inside: avoid`
(`AvoidsBreakInside`): fuzz seeds 238 and 273 lost lines there, and the same documents with plain `div`s
lose the same lines on `main` ([#1369's gap](../accepted-gaps/a-break-inside-avoid-block-taller-than-a-page-can-lose-a-line.md)).

A content-preservation fuzz (random nesting of wrappers, floats, abspos, multicol, grid, flex, tables,
`break-inside: avoid`, inline-blocks; unique numbered words; 160–260pt pages) compared the branch with
`main` at b59e736a. The branch recovers 1,509 words in 38 of 91 documents and loses 314 in 22. Every
loss that was traced came from a pre-existing bug the changed pagination moves content onto, not from a
wrapper that now fragments: the #1328 slice-boundary line landing on a different line (1_4, 1_36, 1_54,
1_57, 1_65, 1_75; `main` loses a neighbouring line of the same wrapper instead), and #1358, fragment
pruning dropping the content after a blank stretch in a multi-column container that holds an absolutely
positioned box (1_56, 1_64). `main` hides #1358 only while the absolutely positioned box is the first child,
because #1349 put the content after it back on page 1; with the box anywhere else in the container `main`
loses the same line. The reducer that minimised them is a render server per build (one process, many
documents): the CLI's ~1.4s JIT start-up made the first reduction take 11 minutes, the server 12 seconds.
Two float-sibling bugs found on the way are tracked as #1339 and #1340 and reproduce on `main` with no
`overflow` at all.

`IsScrollContainer` itself is unchanged, since only `IsMonolithic` calls it. Table cells are unaffected
either way: `LayoutContents` already excluded them from suppression by display.

## What was found by running it

- The 14 failing tests after the change were all fixtures that used a bare `overflow: hidden` card as
  the stock monolithic box. They now add `max-height: 1000pt` (or `10000pt` for the tall ones), which
  caps the block size without changing any geometry, so each still pins what it pinned. Once
  `max-height` stopped counting (below) they became `overflow: auto` with a `height` equal to the card's
  measured natural height (60pt, 462pt, 660pt), which again changes no geometry.
- The new `TallAutoHeightScrollContainer_DrawsEveryLineInsideAPageBand` reproduces the issue exactly
  against the old classifier (`L9`/`L19`/`L28` end 2–11pt past the 180pt band) and passes with the fix.
- Given `max-height: 10000pt`, the same fixture still loses those three lines. That is the capped case,
  left as an [accepted gap](../accepted-gaps/a-capped-scroll-container-taller-than-a-page-loses-its-boundary-lines.md)
  tracked by #1328.

## User-visible side effect

An auto-height `overflow: hidden|auto|scroll` card that straddles a page boundary is now split across it
instead of moving whole to the next page (see the migration note). That is what browsers do when
printing. Authors who want the old result can add `break-inside: avoid`, or a fixed `height`.

## A box capped only by max-height breaks, as Chrome prints it

Comparing the showcase against Chrome after the float round showed its capped box, `overflow: hidden;
max-height: 200pt` with content under the cap, printed across two pages by Chrome and moved whole by
PeachPDF. §2's permission for `hidden` is "a non-auto logical height (and no specified maximum logical
height)", so that box is not in the monolithic set at all; the classifier had deliberately counted
`max-height` for every overflow value. `HasConstrainedBlockSize` now takes §2's own case for hidden (a
`height` and no `max-height`). Switching the showcase box to `overflow: auto; max-height` then showed
Chrome prints that across two pages too, so the same rule now applies to every overflow value (plus
`aspect-ratio` and both insets for `auto`/`scroll`, which §2's "may" allows).

Letting it break exposed the trap the old rule had been hiding: when the content overflows the cap, the
clipped lines lie past the box's end, and a break among them ended the pass, so the content after the box
was placed back on an emitted page (probe: ten lines after a 60pt-capped 30-line box, all lost; three
after a straddling 96pt one, plus an empty page). The box's height clamp also measures `max-height` in
document space, across the page gap. Rather than fragment clipped content, `PerformLayoutEpilogue` now
checks such a box after `ApplyHeight` (`NoteIfAFragmentingScrollContainerClips`: content past its padding
edge) and `PerformLayout` lays the document out again with it monolithic, at most three times. That keeps
the old behaviour exactly where it was safe and is an
[accepted gap](../accepted-gaps/a-scroll-container-that-clips-past-its-max-height-is-kept-whole.md)
(#1375). The showcase's box now has a fixed `height`. Vertical writing modes keep the old rule, since the clip check reads the
physical Y axis.
