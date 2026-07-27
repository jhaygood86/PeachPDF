# An outside `::marker` is positioned by the pass that places its item, and by no other

_CSS Fragmentation Level 3 / CSS 2.1 §12.5.1. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

An *outside* `::marker` (the CSS default) is reached by nothing generic: `CssLayoutEngine.FlowBox`
skips it, `CssBox.LayoutBlockChildren` skips it, and `DomUtils.GetPreviousSibling` steps over it —
all three ask `CssBox.IsOutsideMarker`, which is the one place that grammar lives. Exactly one call
positions it, `CssBox.LayoutOutsideMarker`, and **which pass makes that call decides which
fragmentainer claims the marker**, because a fragment is claimed by the geometry a box carries at the
moment the slot is frozen.

**One rule, not two: the pass that *places* the item.** The marker is positioned against the item's
own border box (beside its first line box), so the item's height and how much of it fits here are not
inputs — only where the item was put. `CssBox.MarkerBelongsToTheFragmentainerBeingFilled` therefore
asks `resume is null`, and every other pass leaves the marker exactly where that one put it. Both
halves of that cost a measured defect:

- **Not the pass that *completes* the item.** Positioned from `CssBox.PerformLayoutEpilogue`, which
  runs only there, a straddling item's marker got its coordinates after the slot those coordinates
  fall in had been frozen, and nothing re-opened that slot:
  `HtmlContainerInt.InvalidateEmittedFragmentsFor` is a no-op for a box no frozen fragmentainer
  holds, which a never-emitted marker is. Measured symptom: the bullet or number painted on **no page
  at all** ([#444](https://github.com/jhaygood86/PeachPDF/issues/444)).
- **And not again on a pass that *resumes* it.** Inside a column a box that does not finish is laid
  out again at the next column's inline position (`CssBox.ResumeInTheNextFragmentainer`), so a marker
  re-positioned there moves to a column its item did not begin in — while the earlier column's
  `BoxGeometrySnapshot` still holds the origin the first pass gave it, so the word is claimed twice.
  Measured on a 660-document `column-count: 2|3` sweep (both `column-fill` values, 4–14 items, page
  heights 120–300pt): **2350** markers in a later fragment than their item's first, and **134**
  documents with a duplicate word claim ([#468](https://github.com/jhaygood86/PeachPDF/issues/468)).
  Nothing further is needed to keep the marker out of the later column — a column's snapshot records
  word origins of its own, and `FragmentEmitter`'s `FragmentRegion` tests the inline axis, so a marker
  sitting in a neighbouring column's span is simply not claimed there.

**A pass can place the item and then keep none of it, and that is not a placing pass.** The decision
that drops it is the *parent's* — §3.1's break-before propagation, §5.4's orphans floor, a column's
own overflow arm — and is not made until the item's own layout has returned, by which point the
marker has been positioned. The fill then drops the item from that fragmentainer's captured geometry
altogether, and the marker left behind is claimed by nothing and painted nowhere, which is #444's
symptom reached from the other direction. `CssBox.TakeBackTheMarkerOfAnItemThisPassKeptNothingOf`
hands it back by re-`AwaitPlacement`-ing it, and `CssBox.OutsideMarkerAwaitsPlacement` is what lets a
resumed pass pick it up: the marker's own word flag is the entire bookkeeping channel, so there is no
second one to keep in step. Same sweep, 9 markers — every one of them `column-fill: balance`, where
the extra fill attempts make a placement that keeps nothing ordinary rather than exotic.

**And it must run last within its pass.** A block opening its inline flow declares that this layout
has placed none of its subtree's words yet (`CssBox.AwaitPlacement`, from
`CssLayoutEngine.CreateLineBoxes`), and that walk reaches the marker even though the flow never visits
it. Positioning the marker before `LayoutContents` therefore has the declaration take it straight
back, and the marker is claimed by nothing again — being positioned is what clears the flag
(`CssRect.Top`'s setter). Anything that moves this call earlier "for clarity" reintroduces the whole
defect with no visible sign in the diff.

**What is safe by construction and what is not.** A mover that relocates the item through
`CssBox.OffsetTop`/`OffsetLeft` takes the marker with it, because both recurse through `Boxes` — that
covers the §4.3 movers, the keep-with-next pulls, and the flex/grid engines translating a placed item.
A *column* does not relocate that way: `CssLayoutEngineColumns` lays the child out again at a new
inline position rather than offsetting it, which is exactly why the marker must not be re-positioned
there.
