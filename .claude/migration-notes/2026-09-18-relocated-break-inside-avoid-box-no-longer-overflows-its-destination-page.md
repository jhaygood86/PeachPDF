# A relocated `break-inside:avoid` box no longer overflows its destination page

A `break-inside:avoid` (or monolithic) box whose first in-flow child is a multi-line heading chained via
the UA default `h1-h6 { break-after: avoid }` rule to a sibling that triggers a keep-with-next pull could
be relocated to its destination page with a visible overflow past that page's own content area — up to a
full line of content bleeding into the page's bottom margin (and, sheet permitting, past the physical
page edge). Confirmed present since at least v0.9.18 (`git show v0.9.18:src/PeachPDF/Html/Core/Dom/CssBox.cs`
already had the affected `TryRestartAt`/`FitsInFragmentainer` machinery).

The cause: `CssBox.CanBeLaidOutAgain`'s check for whether a relocated box fits cleanly at its destination
measured the box's own raw `ActualBottom - Location.Y`, the box's original top to its (already-relocated)
content's bottom. A same-pass keep-with-next restart moves the box's first in-flow child forward to the
destination page without moving the box's own recorded top to match, inflating that raw span by the gap
between the two — a span that does not describe the box's real content height. The inflated span could
make the fit check wrongly conclude the box would not fit even a fresh, full destination page, falling
back to a blind coordinate shift (`TranslateForEarlyBreak`) applied on top of content that had already
moved into place — compounding rather than correcting the offset.

The box's own fit check (`CssBox.EffectiveContentTop`) now measures from wherever its first in-flow child
actually landed when a same-pass restart moved it, rather than always from the box's own possibly-stale
top. A document exercising this exact shape (a `break-inside:avoid` box, or one otherwise excluded from
breaking, whose leading heading wraps and keeps too few lines to satisfy `orphans`/its own `break-after`
chain) will now see that box's content land fully within its destination page rather than spilling past
its bottom edge; every other document is unaffected, since the corrected measurement is identical to the
previous one whenever nothing has relocated the box's first in-flow child mid-pass.

Tracked as issue #1213; see
[.claude/recent-fixes/2026-09-18-canbelaidoutagain-phantom-gap-fixed.md](../recent-fixes/2026-09-18-canbelaidoutagain-phantom-gap-fixed.md)
for the fix's own reasoning and evidence.
