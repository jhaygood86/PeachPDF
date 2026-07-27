# A grid track grows toward its limit with the space that is there

_Landed 2026-07-27._

[Issue #414](https://github.com/jhaygood86/PeachPDF/issues/414),
[CSS Grid 1 §12.4/§12.6](https://www.w3.org/TR/css-grid-1/#algo-grow-tracks).

Reported as a grid nested in a multi-column container losing the content that overflowed the column's
inline span. **The multi-column part was incidental, and believing otherwise cost the first hour**: a
plain `display: grid` narrower than its own content overflows *itself* — a 105pt grid holding a 155.9pt
max-content item laid that item out at 155.9 and painted it outside the box. Flex has always been right
here. It was filed as an engine-independence defect (#166/#315 family, "an item's geometry comes from a
measurement layout at the container's content origin and nothing re-flows it") because the nesting made
that reading look obvious. What settled it was measuring the same fixture with no container at all.

**The load-bearing reading is that `auto` is `minmax(auto, auto)`, so its base comes from the *min*
function and its growth limit from the *max* one.** `InitTrack` seeded the base at **max-content** and
called the limit unbounded, which is the same answer whenever the space is available and an overflowing
track when it is not. New `GrowTowardLimits` is §12.6 proper: every track starts at its base and grows
toward its growth limit, sharing the free space and freezing as each reaches its limit — the
redistribution loop rather than one equal share, because a track that freezes early leaves room the
others can still use.

**The ordering is what made the first attempt wrong, and the showcase diff is what caught it.** §12.6
runs *before* both §12.7's flex resolution and §12.8's stretch, so maximization had to be hoisted out of
the no-flex branch. Applied only there, the `fr` branch went on reading `bases[i]` — now min-content
rather than max-content — and every `auto` track beside an `fr` collapsed to its longest word. The full
test suite stayed green through that; only the `css_grid_intrinsic` rasterization showed it.

**`fr` tracks are excluded from the maximize step itself.** Their growth limit is their base, and their
unbounded `limits` entry would otherwise absorb everything §12.7 exists to distribute.

**`minmax()` is deliberately not marked stretchable**, though §12.8 would have it be: only a bare `auto`
sets the flag. That is a narrowing kept to hold the blast radius down, not a claim about the spec.

**Two existing tests were characterizations of the missing step and are promoted, not adjusted.**
`Gap4_CalcInsideMinmax_ResolvesFloor` and `Gap7_MinmaxIntrinsicBreadths_UseMinAndMaxContent` each say so
in their own comments — *"the engine has no maximize-tracks step, so with an fr present the track stays
at its base"*. A `minmax(60pt, 100pt)` beside a `1fr` in a 300pt grid now reads 100/200 rather than
60/240, which is what a browser does. A migration note is in `docs/html-css-support.md`.

Tests: `GridLayoutIntegrationTests` (+4 — the narrow container, the roomy control, two `auto` tracks
sharing a container too small for both, and the redistribution a frozen track leaves behind), plus
`MulticolLayoutIntegrationTests`' grid case **promoted from the characterization filed the same day**.
Full net8.0 suite green (6564); **100% diff coverage**. **66 of 67 showcases byte-identical**;
`css_grid_intrinsic` differs — its `minmax()` track now reaches its limit, and it gained a section
putting the same two `auto` columns in a roomy and a cramped container, where **on the unfixed build the
cramped one's second track paints outside the container's own dashed edge**. Verified in both PDFium and
MuPDF.
