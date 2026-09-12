# A line taller than a page is clipped to its first page, not drawn twice (issue #484)

`FragmentEmitter.ClaimsWord`'s `FallsPast` tie-break (added by PR #480 for #477) let a word claim
every fragmentainer its rectangle geometrically overhangs, not just the one it starts in. It existed
for two cases where layout never had the chance to move a straddling line: a flex/grid item's content
under `SuppressWordPageBreaks` (translated into place without a fresh straddle check), and
`MonolithicContent.FitsNoFragmentainer` — content taller than a whole page, which layout leaves
exactly where it is because moving it would only repeat the same overflow on the next fragmentainer.

The flex/grid half is already closed at the source (`CssLayoutEngineGrid`/`CssLayoutEngineFlex` commit
content live at its final position now, per `StraddlingLineClaimTests.ARowOrLineTheEngineCouldNotFit_ContinuesOnTheNextPageInstead`).
That left `FitsNoFragmentainer` as the only live reason for the second claim — and per #484, it
shouldn't get one either: such content should be **clipped** to the first page it starts on, not
repeated on every later page it overlaps.

## The fix

Narrowed the `FallsPast` disjunct in `ClaimsWord` to additionally require
`!MonolithicContent.FitsNoFragmentainer(rect.Height, 0, 0, container)` — the same page-height
threshold layout itself already asks in `CssRect.WouldStraddleFragmentainer`, with `0, 0` for the
cloned-decoration insets since the emitter's own check was already a deliberately looser,
insets-dropped form of layout's (see the method's own remarks). The arm is not deleted outright: it
still exists to *remove* an over-eager claim rather than to invent one, and a future case could still
reach it.

## What the measurement said

A `<p style='font-size:1800pt;line-height:1'>T</p>` on an A4 page (10pt margins) spans 3+ page bands.
Rendered through the full `PdfGenerator` pipeline and rasterized with both PDFium and MuPDF:

| | page 1 | page 2 |
|---|---|---|
| before | top of the "T" stroke, clipped at the page edge | **the stem's continuation, drawn again at the top of the page** |
| after | identical | blank |

Both renderers agree. This is exactly the kind of defect a token-presence test cannot see — the word
is claimed by page 1 either way, so "every word claimed exactly once" was already satisfied by the
bug; only rendering and looking caught the duplicate draw, per this repo's own painting-verification
convention.

## Test changes

`StraddlingLineClaimTests.AWordTallerThanTheBand_IsClaimedByEveryBandItCovers` asserted the bug
directly (every band the word covers claims it). Renamed to
`AWordTallerThanTheBand_IsClippedToItsFirstBandOnly` and rewritten to assert single-claim by the
first band alone. `ARowOrLineTheEngineCouldNotFit_ContinuesOnTheNextPageInstead` (the flex/grid case)
and `BandMembershipToleranceTests` (the within-tolerance single-claim case) needed no changes and
still pass — proof the fix doesn't touch either path.

## Docs / notes

- Deleted `.claude/accepted-gaps/a-straddling-line-is-drawn-in-both-fragmentainers.md` (gap closed).
- `docs/html-css-support.md`'s flex/grid-break-rules section now states the line is clipped rather
  than repeated.
- `.claude/invariants/fragmentation-one-membership-question-is-asked-with-one-tolerance.md` updated:
  both of the tie-break's historical extra-claim cases are now resolved, so the arm has no live case
  left today, though it stays for the reasons above.
- Left `.claude/accepted-gaps/decoration-rectangles-claimed-by-two-fragmentainers.md` (#471) untouched
  — a related but separate, still-open issue about the *decoration* rectangle of a straddling line,
  not its word content.

## Evidence

- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0`: 10832 passed / 0 failed.
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.
- Both changed lines hit millions of times across the coverage run (net8.0/net10.0/net11.0).
- Rasterized before/after with both PDFium and MuPDF (see table above); both agree.
