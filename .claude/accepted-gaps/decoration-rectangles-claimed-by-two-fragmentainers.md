# A decoration rectangle straddling a band boundary is still claimed by both fragmentainers

Tracking issue: [#471](https://github.com/jhaygood86/PeachPDF/issues/471).

[#446](https://github.com/jhaygood86/PeachPDF/issues/446) settled **word** membership — a word belongs to the
fragmentainer its own top starts in (`FragmentEmitter.ClaimsWord`), *provided layout could have moved it*
(see [#477](https://github.com/jhaygood86/PeachPDF/issues/477), which corrected that rule after it was
applied unconditionally and deleted content). The `Lines` arm of `BuildDraft` was deliberately left on
`FragmentRegion.Contains`'s raw overlap rule, so a *decoration* rectangle crossing a band boundary is still
added to both slots' drafts, and the later slot's copy paints near that page's top — a stray slice of an
inline's border/background. It is on `main` independently of #446.

**Applying #446's rule to it is wrong, and was measured wrong twice.** A decoration rectangle is a line *box*:
it carries the line's leading, so it is taller than the content that decided where the line went, and it can
straddle by far more than `PageBoundaryEpsilon`. In `box_decoration_break.pdf`'s section 5, band 4 ends at
3827.32 and the inline's rectangle at `3823.32..3842.32` has its top inside band 4 while its words are on
page 5 — "the band its top starts in" draws the strip at the bottom of the page the content is not on, which
is exactly the artifact an early draft of #446 produced (visible in the showcase diff, invisible to the
~6,760-test suite). #477 is the same lesson arriving from the other side: for anything that straddles beyond
the tolerance, *both* fragmentainers are the answer, and a rule that picks one loses what the other showed.

What it actually needs is the line→fragmentainer association layout already holds, rather than a re-derivation
from coordinates — #400's move, one level down. It is not a tolerance problem, so it is not closed by picking
a better epsilon. See also
[fragmentation-which-drafts-exist-decides-whether-a-frozen-slot-is-emitted-again.md](../invariants/fragmentation-which-drafts-exist-decides-whether-a-frozen-slot-is-emitted-again.md)
for why a change here cannot be judged by the suite alone.
