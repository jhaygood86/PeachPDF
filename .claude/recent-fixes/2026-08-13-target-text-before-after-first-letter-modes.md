# target-text() before/after/first-letter modes wired up (#719)

`target-text(<target>, before | after | first-letter)` (css-content-3 §5) parsed but only the default
`content` mode ever resolved to anything - the other three silently produced nothing, same as an
unresolved target. See the closed accepted-gap this fix retires:
`target-text-mode-argument-not-supported.md` (deleted in this change).

## The fix

`CssContentEngine.ResolveTargetText` already re-derived `mode` from the raw `FunctionToken` arguments
correctly - the only missing piece was dispatching on it. `ExtractContentValue` (the `content()`
function's own mode switch, used by the `content` property and `bookmark-label` - not `string-set`,
which has its own separate `EvaluateContentList` in `CssNamedStringEngine`) already had exactly this
dispatch logic (`ExtractText`/`ExtractPseudoElementContent`/`ExtractFirstLetter`), so `ResolveTargetText`
now calls the same three helpers, just against the resolved `targetBox` instead of `cssBox`. No new
plumbing needed - this really was the small, self-contained follow-up the issue predicted.

`ExtractPseudoElementContent`/`ExtractFirstLetter` both take a plain `CssBox` and internally resolve
pseudo-element/parent lookups themselves, so no signature changes were needed to call them from
`ResolveTargetText` with an arbitrary target box (not necessarily `cssBox` or its ancestor).

## What was found by running it, not by reading it

The TestHarness showcase (`target_counter_toc`, "Table of Contents & Cross-References") originally had
`target-text()` in its *section comment* but never actually called the function anywhere in the HTML -
only `target-counter()`/`leader()` were demonstrated. Added a real cross-reference section exercising
all four modes. First render used `content: " ★"` for the `::after` demo mode - rasterized as a tofu/
missing-glyph box under the showcase's serif font (a font-coverage gap, unrelated to target-text itself).
Swapped to plain text (`" (revised)"`) rather than chasing font coverage for an unrelated glyph.

## Evidence

- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0` - full suite green (8754 passed).
- `dotnet build PeachPDF.slnx -t:Rebuild` - zero warnings.
- Showcase PDF rasterized (PyMuPDF) and visually confirmed: cross-reference list shows the target's own
  text (content), a generated `::after` suffix (after), the heading's first letter (first-letter), and a
  generated `::before` badge (before) - and the badges/suffix also show correctly on the actual chapter
  heading pages they're generated on.
