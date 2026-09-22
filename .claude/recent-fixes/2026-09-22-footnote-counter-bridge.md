# Bridging the footnote counter: an ambient context, not a counter engine

`counter-reset`/`counter-increment: footnote` and `content: counter(footnote)` now work. The thing to
know before touching this again is **why the number is not resolved by `CssCounterEngine`**, because
that is the obvious-looking approach and it cannot work: that engine resolves counters purely from
DOM position, and a footnote's number depends on which *page* its call landed on - information that
only exists part-way through the footnote convergence loop.

## The shape that does work was already in the codebase

There is no single "counter(page) engine" to copy; there are three sibling mechanisms, all sitting
*alongside* `CssCounterEngine` rather than inside it. The right one to model this on is
`RunningElementPageContext`: an ambient `int?` on the container, set in a try/finally around the one
moment a number is applied, and consulted by an early arm in `CssContentEngine.AppendCounter` before
it falls through to the document-counter lookup. Outside that window the field is null and
`counter(footnote)` on an ordinary element resolves exactly as it always did, which is what makes
this additive rather than a behaviour change for anything else.

## Reset and step are the cascade, not a special case

This looked like it needed a "was the property declared?" signal and does not. `counter-reset` is one
property, so an author declaration on an applicable `@page` replaces the UA sheet's own
`counter-reset: footnote` wholesale. That single rule produces every behaviour asked for: nothing
declared keeps the per-page reset; `counter-reset: none` gives continuous numbering; a
`counter-reset` naming only some *other* counter also gives continuous numbering (same reason); and
`counter-reset: footnote 10` restarts each page at 10. The third of those is the regression risk for
existing documents - see the migration note.

## Two traps that throw, both found by running rather than reading

- **The numbering walk must visit slots in ascending order, and must visit slots with no footnotes.**
  The old loop was `foreach (var (slot, calls) in bySlot)` over a `Dictionary`, whose insertion order
  is document order of *calls* - not slot order once a `footnote-policy` forced break has moved one.
  Harmless while every page restarted at 1; load-bearing the moment a counter runs across pages. And
  a `counter-reset` on a page carrying no note of its own still has to take effect there.
- **Re-parsing a renumbered box needs a bidi re-resolve first, on *both* paths.** The plan expected
  this only on the new author-`content` path; running it showed the *default* path throws too, which
  is why the re-resolve lives at the single point that re-parses (`ReparseFootnoteText`) rather than
  inside the content re-application. This is the fourth site to hit that trap, so it is now recorded
  in `.claude/invariants/text-re-resolved-after-parse-must-re-resolve-its-bidi-levels-first.md`
  rather than as another expiring fix note - see
  [2026-09-08-a-page-counter-that-gains-a-digit-restales-its-bidi-levels.md](2026-09-08-a-page-counter-that-gains-a-digit-restales-its-bidi-levels.md)
  for the previous one, which has the same "the fixtures all stopped below ten" root cause.

Also confirmed by running rather than reading: `ResolveFootnotesForThisAttempt` called
`ParseToWords()` unguarded where `DomParser.DetachOneFootnoteBody` guards the identical call, so
`::footnote-call { content: url(...) }` or `content: none` threw a `NullReferenceException` on every
pass after the first. Both probes were written as failing tests first, and both failed as predicted.

## Convergence

The loop's change signal gained a numbering signature (number plus the exact call/marker text, in
document order), in the same spirit as the `target-counter` loop's own text signature. A wider number
changes the *call's* inline width - which moves the flow around it, and so which page the call lands
on - without necessarily changing any note area's reserved height, so the pre-existing height
comparison alone could let the loop exit a pass early with stale numbers. There is a test asserting
the word following a two-digit call sits further right than after a one-digit one; a text-only
assertion would have passed even with that feedback broken.

## Deliberately done, though not strictly required

`counters()` was implemented in the `content` property. Leaving `counters(footnote, …)` working while
general `counters()` did not would have been an odd asymmetry, and it is real CSS.

Doing it turned up a second, unrelated defect that no test would ever have found from the engine
side: `Converters.CounterConverter` put the `.Or(counters(...))` **inside** `counter()`'s own argument
converter - one closing paren too far right - so the grammar read as "a `counter()` whose arguments
are either `<name> <style>` or a nested `counters(...)`". A top-level `counters()` therefore never
validated, and the declaration was dropped before any engine saw it. That is why `counters()`
appeared to exist only in `CssNamedStringEngine`: `string-set` has its own grammar. Both now share
one implementation, which also reads the counter-style argument the `string-set` copy silently
dropped.

## Evidence

Full suite green (13,233 passed / 9 skipped, net8.0). 13 new tests in
`FootnoteCounterIntegrationTests`, plus the two NRE probes in `FootnoteIntegrationTests`.
`.claude/accepted-gaps/footnote-counter-not-bridged-into-csscounterengine.md` deleted (issue #754
closed); the stale-slot gap gained a note that continuous numbering makes its symptom visible as a
wrong *number* rather than only a misplaced area.
