# The frame's own child loop places a block child before laying it out

Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320). Closes
[#550](https://github.com/jhaygood86/PeachPDF/issues/550) (`#515.4`), and with it
[#515](https://github.com/jhaygood86/PeachPDF/issues/515) (`#390`'s stage 2) and the
[#390](https://github.com/jhaygood86/PeachPDF/issues/390) epic. Behaviour-neutral by construction.

## The load-bearing idea

After `#539`/`#540` the *arithmetic* of placing a block child was already the frame's
(`ResolveBlockChildOffset`/`CommitBlockChildOffset`), but the **call** was still the child's:
`CssBox.LayoutContents` reached back out to `(ParentBox ?? this)` in the middle of the child's own layout to
ask where it went and to have itself committed there. Control flowed child → parent → child, which is the
one shape `#390` names in its title.

The pass is now **entered at the frame**. `CssBox.LayoutBlockChild(g, child, framePlacesChild)` is what a
child loop calls per child; `DriveBlockChildPass` runs the three phases the pass is made of, in the order
their dependencies force:

1. `child.BeginBlockPass(g)` — the resumption record plus the once-per-layout prologue.
2. `PlaceAndSizeBlockChild(g, child)` on the **frame** — resolve the offset, have the child size itself
   against the page that offset lands on, commit.
3. `child.LayoutPassContents(g, resume, placed)` — `LayoutContents`, the marker, the break publish, the
   epilogue, and the one-shot re-place target the epilogue can hand back.

`LayoutBlockChildren`, the columns engine's two child loops and `LayoutOutOfFlowChildren` drive their
children this way. `CssBox.PerformLayout` stays as the **adapter** for every other caller — a layout engine
measuring an item, the root — naming `ParentBox ?? this` on the box's behalf. `PlaceBlockBox` and the
`PositionAssignedByEngine` placement gate are gone; `PlaceAsBlockChild`/`PlaceBlockChild` stay for
`CssBoxHr`, which runs a pass of its own.

**Which children a frame positions became the frame's question too.** `ItemContentCommit` now lays an
already-positioned flex/grid item out through `CssBox.LayoutContentAtItsAssignedPosition`
(`framePlacesChild: false`) instead of setting a flag the box checks about itself. That is the literal thing
`#550` asked for — "an engine-controlled child is simply a child the loop doesn't call those on."
`PositionAssignedByEngine` itself stays, for the `PerformLayoutEpilogue` movers, which run after the box is
complete and so cannot be covered by a decision taken when the pass was entered.

## What was found by running it, not by reading it

**The first design silently killed nine tests, and the way it did it is the finding.** `PerformLayoutImp` is
`protected virtual` and three box kinds override it (`CssBoxHr`, `CssBoxMarker`, `CssProxyBox`), each
replacing the whole pass rather than having phases to drive. The obvious answer — a
`RunsALayoutPassOfItsOwn` predicate the frame checks, calling the override only for those three and the
phases for everyone else — compiled, and the suite reported **9 failures across `RenderErrorReportingTests`
and `NoProgressBackstopTests`**. Both files subclass `CssBox` and override `PerformLayoutImp` to state a
condition no markup produces (a box whose layout throws; a box that hands back the same break record every
pass). Under that design their overrides were simply never called. Six more such subclasses across three
table test files would have gone the same way had they not been caught first.

So `PerformLayoutImp` stays the one virtual "run this box's pass" seam, and gains the frame and the
placement flag as parameters: `PerformLayoutImp(g, frame, framePlacesChild)`, whose base implementation is
`frame.DriveBlockChildPass(g, this, framePlacesChild)`. Nine test subclasses across five files were updated
to the new signature. The general rule is now recorded as an invariant: **a frame must call the override for
every child**, because a hook this repo's tests use to state unreachable conditions fails silently rather
than loudly when it stops being called.

**The columns engine's "fill path" needed no separate treatment.** `#550` names it alongside
`LayoutBlockChildren`; tracing it shows `FillColumns` → `CssBox.FillFragmentainerWithBlockChildren` →
`LayoutBlockChildren`, so converting the loop converted the fill path with it. The two loops the engine
*does* drive itself — the degenerate single-column path and phase 1's measurement pass — were converted
too, for uniformity; both are literal `foreach` over `columnsBox.Boxes`, so `columnsBox.LayoutBlockChild(g,
child)` is the same call `child.PerformLayout(g)` already made.

**One `catch` arm was uncovered before and after, and covering it took a detached box.** The `}` of
`LayoutBlockChild`'s handler is reached only when the failing child has no `HtmlContainer` to build a
`RenderError` with — 40 exceptions pass through the handler across the suite and every one of them has a
container. Same shape as the handler it moved from, so this cost diff coverage without being new (see
`.claude/invariants/testing-touching-a-line-inside-an-untested-catch-costs-diff-coverage.md`). Pinned with a
pair of detached `CssBox`es rather than left at 97%.

## The retraction-mechanism honesty check

`#390`'s own text claims the five mechanisms that retract work "would collapse into 'do not append that
fragment.'" **That does not happen here, and it is worth stating plainly rather than repeating the epic's
framing.** It already happened once, for `ResetChildrenForRefill` (`#516`), and for its own reason — it was a
two-line wrapper over the shared `PassRewind.RollBackTo`. Nothing further collapses in this PR, and the
implementation is the evidence rather than an assumption:

- **`FragmentEmitter.InvalidateFrom`** (reached through `HtmlContainerInt.InvalidateEmittedFragmentsFor`,
  which `#550` calls `InvalidateFor`) retracts an already-*emitted*, frozen slot, because a box only reaches
  its epilogue on the pass that completes it and §4.3's movers can therefore relocate content out of a
  fragmentainer already emitted. That is a cross-pass lifecycle fact about *when* a box finishes; it is
  independent of which frame assigns `Location`, and moving the call site changed nothing about it.
- **`PassRewind.RollBackTo`** retracts *prologue* state — `_prologueDone`, per-line rectangles, line boxes —
  so a re-entered pass can lay a box out from the start. Position is not what it rolls back.
- **`DiscardLineBoxesFrom`** is inline layout, out of scope for every stage of the epic.

What actually retires a retraction is per-fragment geometry: a `CssBox` still carries one mutable `Location`
and one set of rectangles, so "discard the fragment" is not yet an expressible operation. Inverting who
*calls* the placement does not change that.

## What was deliberately not done

- **Coordinates stay document-absolute.** `#550` scopes fragmentainer-local coordinates out and this PR
  honoured that; the frame commits the offset before the child lays out its content, but nothing is
  converted. Confirmed rather than assumed: `FragmentEmitter`'s entire input contract is document space,
  `ItemContentCommit` commits all four engines to "final position first, then content", and a grep over the
  suite finds **974** `Location.Y` reads in test code, **529** of them inside an `Assert`. Drafted as a follow-up issue (see the PR description)
  rather than attempted.
- **`PlaceAsBlockChild`/`PlaceBlockChild` were kept**, and `CssBoxHr` still calls them. Folding the rule into
  the phased pass would give it the base prologue it deliberately does not run today — word measurement,
  `string-set`, named-page registration, and a forced break it currently never takes — which is an output
  change, and this PR may not make one. `CssBoxMarker` and `CssProxyBox` are the same story with less to say.
- **The flex/grid/table engines' own item loops still call `PerformLayout`.** Those are measurement layouts
  the engine translates into place afterwards, not block-flow child loops; converting them would state
  something about them that is not true. The adapter exists precisely so they read honestly.
- **`PositionAssignedByEngine` was not retired**, only demoted. Its three epilogue reads are about movers
  that run after the box is complete, which no decision taken at pass entry can cover.

## Evidence

Full `net8.0` suite green (**7125 passing**, 9 skipped, 0 failed — 7119 baseline plus 6 new) and CLI suite
green (96 passing). Zero-warning `dotnet build PeachPDF.slnx -t:Rebuild`. **98% diff coverage** against
`origin/main` (68 lines, 1 missing — the keep-with-next first-line retry's own call, confirmed dead twice
over by `#538` and `#539` and left unconverted for the same reason). **Byte-identical across the full
77-showcase corpus** vs `origin/main`, normalized for `/CreationDate`, `/ID`, font subset tags, annotation
`/NM`/`/M` and PDFsharp's plaintext creation-date/time header lines — which is the real proof this is the
neutral inversion it is meant to be, given that it moves the entry point of every block box's layout.

New `TheFrameDrivesItsChildsPassTests` (6) observes the call sequence rather than inferring it, through a
recording `CssBox` subclass: the frame a pass is driven with is the box's own parent on every pass; an
ordinary block child is placed by its frame on every pass; **the frame's position is already final when its
child's pass begins** (the assertion a child that placed itself mid-layout could not pass); a flex item's
commit pass is driven with the frame *not* placing it while its measurement passes are; and both arms of the
frame's error handling.
