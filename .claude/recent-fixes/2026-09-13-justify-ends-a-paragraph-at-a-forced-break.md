# `text-align: justify` ends a paragraph at a forced break, and `text-align-last` decides how (#1021)

Closes the accepted gap `justify-still-justifies-the-line-before-a-forced-break.md`, filed during the
#1013 review. User-visible consequences are in
[../migration-notes/2026-09-13-text-align-last-and-the-line-before-a-forced-break.md](../migration-notes/2026-09-13-text-align-last-and-the-line-before-a-forced-break.md);
this is the reasoning. What was deliberately *not* built is in
[../accepted-gaps/text-align-is-not-a-shorthand-of-text-align-all-and-last.md](../accepted-gaps/text-align-is-not-a-shorthand-of-text-align-all-and-last.md).
The rule a future change must not re-derive is in
[../invariants/inline-a-lines-relationship-to-a-forced-break-is-stated-when-the-break-closes-it.md](../invariants/inline-a-lines-relationship-to-a-forced-break-is-stated-when-the-break-closes-it.md).

## The load-bearing idea: state the fact at the break, don't look for it later

The gap file predicted two hard problems, and **one statement solves both**. The flow already records
`CssLineBox.FollowsForcedBreak` on the line a `<br>` *opens*; the change records the mirror,
`PrecedesForcedBreak`, on the line the same `<br>` *closes*, at the same moment, in the same two places
(`FlowBox`'s wrap branch and `CreateVerticalLineBoxes`'s `word.IsLineBreak` branch).

- **The "position problem"** — the break marker lives on the following line — disappears because nothing
  is derived from the following line. No lookahead, no line index threaded down from
  `FinalizeLineBoxes`, no `IndexOf` per line (which would have made finalization quadratic).
- **The "fragmentation problem"** — a break landing on a fragmentainer boundary has no successor line
  when the pass finalizes — disappears for the same reason. The pass that stops at the break discards
  the line the `<br>` opened (`blockBox.LineBoxes.Remove(coordinates.Line)`) and finalizes the rest with
  `blockFinished: false`; the line the break *closed* was flagged before any of that happened, and it is
  still flagged when the resumed pass rebuilds its successor. `AForcedBreakInAPaginatedBlock_…` is the
  test that would fail if this were ever re-derived from the list.

Assigned (`= word.IsLineBreak`), never or-ed: a line is closed exactly once, and a re-run layout pass
over the same box tree must re-derive the bit rather than compound it — the same discipline
`CssRect.PrecededByWordSeparator` follows for the same reason. The invariant file linked above records
the traversal that confirmed no `CssLineBox` instance survives a flow (`CreateLineBoxes` either clears
or discards from `completedLines`; `CreateVerticalLineBoxes` clears unconditionally), which is what
makes "assign" sufficient.

## Why this needed `text-align-last` and not just a second exemption

§6.1 does not say "the last line before a forced break is exempt", it says it "is start-aligned **unless
otherwise specified by `text-align-last`**". Bolting a second `if` onto `ApplyJustifyAlignment` would
have hard-coded start alignment in a third place (the block's last line and §6.4.3's unexpandable line
being the first two) and left no way for an author to ask for anything else.

So the decision moved **up**, out of the two justify routines and into one helper both dispatchers call:

```
ResolveUsedAlignment(line, endsAParagraph, textAlign, towardStart, towardEnd)
    → (alignment, opportunities)
```

`ApplyJustifyAlignment`/`ApplyVerticalJustifyAlignment` now only stretch; they no longer decide *which*
lines to stretch, lost `blockFinished`/`isLastColumn` and both early returns, and take the opportunity
count as a parameter rather than re-walking the line for it.

`ResolveUsedAlignment` is where the three rules meet, and the order matters:

1. §6.1/§6.3 — a line that ends a paragraph is aligned by `text-align-last`, not by `text-align`.
2. §6.4.3 — a line whose contents "cannot be stretched to the full width of the line box" (here, a
   line still resolving to `justify` with **no** opportunity) "must be aligned as specified by the
   `text-align-last` property", so it too is handed to `text-align-last`…
3. …and when `text-align-last` is itself `justify`, the **start edge** stands. This is a deliberate
   deviation: §6.4.3's parenthetical says "(If `text-align-last` is `justify`, then they must be
   aligned as for `center`.)" and no browser implements it — Chromium, Gecko and WebKit all
   start-align. Recorded in
   [../accepted-gaps/unexpandable-justified-line-starts-rather-than-centres.md](../accepted-gaps/unexpandable-justified-line-starts-rather-than-centres.md).

Step 3 is not decorative. Without it, `text-align-last: justify` on an RTL paragraph whose closing line
holds one word left that word against the physical *left* edge — worse than omitting the property, since
`auto` and `center` both place it correctly. The same shape in the vertical engine was worse still: the
early return also skipped the inline-start **anchoring** every other vertical arm performs, so an
auto-height `direction: rtl` vertical box (which lays words out against a placeholder far edge, issue
#797) left the column far outside its real bottom.

Two more consequences worth knowing:

- The opportunity walk is O(words) and now runs **once per line, only for a line being justified** —
  `ResolveUsedAlignment` returns early for any non-justify alignment, and hands its count to the justify
  routine rather than letting it count again.
- `start`/`end` resolution is shared: `ResolveLogicalAlignment` takes the physical pair as parameters
  (the box's `direction` horizontally, `WritingModeFrame.InlineStartIsBottom` vertically) instead of each
  dispatcher rolling its own `switch`, and `text-align-last`'s own `start`/`end` resolve against the
  identical pair by construction.

One §6.1 gap was closed while the method was open: an **overflowing** justified line is start-aligned
and spills past the *end* edge, which under RTL means flush right spilling left. `leftover <= 0` used to
`return` unconditionally, which is correct for LTR (the flow already left the line at the start edge) and
wrong for RTL; it now calls `ApplyRightAlignment`, whose deliberate negative-`diff` branch exists for
exactly this. The vertical counterpart's own `leftover <= 0` return was left alone — it is untouched by
this change and belongs with #797's auto-height anchoring story.

`text-align-last` itself cost **one object in `css-properties.json`** — inherited, initial `auto`,
`enum-keyword` over the `TextAlignLast` enum and `Map.TextAlignmentsLast` that the CSS-OM layer already
had and the render layer had never consumed. The generator emitted the `TextArea` field, the initial
value, the cascade entry and the getter; no hand-written storage, no `customSetter`.

## The one thing found by running it rather than reading it

`PropertyFlags.Inherited` was added to `TextAlignLastProperty` (CSS-OM) as an apparent oversight and
**reverted**: it broke `TextPropertyTests.TextAlignLastNoneIllegal`, because `Property.IsInherited` is
`Inherited && IsInitial`, so an *illegal* declaration of an inherited property reads as inherited. The
flag turned out to be dead metadata — its only consumers are `Property.CanBeInherited` and
`StyleDeclaration.UpdateDeclarations`, and **nothing calls `UpdateDeclarations`**. Render-side
inheritance comes from the JSON's own `inherited: true`, which is what actually runs.
`WordBreakProperty` is unflagged for the same non-reason. Don't re-add it without also deciding what
the CSS-OM flag is for.

## Evidence

- New `TextAlignLastTests` (23 tests: 15 facts + an 8-case theory). Verified to fail against the
  unfixed behaviour by reverting only the two `PrecedesForcedBreak` reads in the dispatchers: **5 of the
  forced-break tests failed**, including the paginated and vertical ones. The `text-align-last` keyword
  tests could not have passed before at all — the property did not exist.
- `AssertCentred` deliberately requires a **real** gutter, not just equal ones: a stretched line has two
  zero gutters and satisfied the equality on its own, which is how the first draft of
  `TextAlignLastCenter_…` passed against the unfixed engine. Same trap defused in
  `AnRtlTextAlignLastJustify_…`: its first fixture put three words on the closing line, where
  `text-align-last: justify` genuinely stretches it to the measure and the edge assertion passes either
  way — it now uses `A<span>B</span>`, two words the source has no space between, so the line provably
  cannot be stretched.
- One existing test's comment was corrected rather than its assertions:
  `JustificationOpportunityTests.LineWithNoJustificationOpportunityAtAll_IsLeftStartAligned` uses a
  `<br>`, so its line is now exempt *twice over* — §6.4.3 on its own is covered in the new file, on a
  line no forced break touches (two contiguous words followed by a token no measure can hold).
- Full suite `--framework net8.0`: **11 058 passed / 0 failed / 9 skipped**.
- `diff-cover` against `origin/main`: **100%** on all 46 changed lines. (An earlier run was 91% — the
  `start`/`end`/`left` arms of `ResolveLastLineAlignment`, which the theory now covers.)
- `dotnet build PeachPDF.slnx -t:Rebuild -c Release`: 0 warnings, 0 errors.
- All 115 showcases re-rendered and pixel-diffed (PDFium, threshold 16/255) against a baseline built by
  stashing only the three `src/PeachPDF` files: **exactly one changed, the new `text_align_last` one**.
  Nothing else in the gallery pairs `justify` with a `<br>` or with `direction: rtl`, so the collateral
  surface really is zero. The new showcase was then rasterized through **both** PDFium and MuPDF, which
  agree. Its own before/after is the clearest statement of the bug — the `<br>`-separated address block
  rendered with `Peach   State   Technologies` stretched across the full measure, line by line.

## Not done

`text-align-all`, `justify-all` and `match-parent` — see the accepted-gap file linked above — and
§6.4.3's centre-the-unexpandable-line parenthetical, which has its own gap file. Also untouched: the leading space visible at the start of a line following a `<br>` in the new showcase. It
is present identically in the before render, so it predates this change and belongs to whatever handles
the synthetic `"\n"` word's own advance.
