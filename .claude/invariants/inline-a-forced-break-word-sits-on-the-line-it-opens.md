# Inline layout: a forced-break word sits on the line it *opens*, not the one it closes

`CssLayoutEngine.FlowBox`'s wrap branch is entered for the `<br>`'s own word (`word.IsLineBreak`).
It closes the current line, creates the **new** `CssLineBox`, stamps that new line's
`FollowsForcedBreak` from the same word — and only then falls through to the shared placement tail,
which sets `word.Left`/`word.Top` from the post-wrap cursor and calls
`coordinates.Line.ReportExistanceOf(word)` on the **new** line.

So immediately after a forced break, the line that is about to receive the next real content already
holds one word: the zero-width break marker, positioned at that line's own start. (The companion
rule for the line being *closed* is
[inline-a-lines-relationship-to-a-forced-break-is-stated-when-the-break-closes-it.md](inline-a-lines-relationship-to-a-forced-break-is-stated-when-the-break-closes-it.md).)

Two consequences, and both have already been paid for once.

## 1. "Is this line empty?" must skip line-break words

`Line.Words.Count == 0` is the test that looks right and is wrong — it answers "no" for precisely the
line a forced break just opened. `IsAtLineStart` is the one that accounts for it (and for atomic
inline-level boxes, which contribute a `Line.Rectangles` entry and no word at all).

**Measured symptom** (issue #1087): [css-text-3 phase II](https://www.w3.org/TR/css-text-3/#white-space-phase-2)
removes a collapsible space at the beginning of a line, and the removal is guarded on the line being
empty. Written as `Line.Words.Count == 0` the guard never fires after a `<br>`, so the source newline
in

```html
<div ...>Field name:</div><div ...>short</div>
<br/>
<div ...>Long field name:</div><div ...>long</div>
```

indented every post-`<br>` line by one space — 3.013pt at the default 12pt font — while the same
markup minified onto one source line laid out correctly. Verified against Chromium: both forms put
every label at x = 0 and every value at x = 450px, to 0.000px.

**A second measured symptom of the same wrong question:** an overlong word wrapped off the line the
`<br>` had just opened, rendering it blank. `<p style="width:70pt">AA<br>Supercalifragilistic…</p>`
put the word two line-heights below `AA` where a space in place of the `<br>` put it one; under
`overflow-wrap: break-word` the emergency split was blocked and the line wasted entirely. The wrap
decision's own guards (`emergencyBoundaryWrap`, `SuppressLeadingWrap`) now ask `IsAtLineStart`.

`TryApplyLineClamp` still asks `Words.Count == 0`, and that is **not** a site to "fix" by swapping in
`IsAtLineStart`: it guards a `Words[^1]` read, and `IsAtLineStart` returns false for a line holding
only an atomic inline — whose `Words` list is empty — so the swap would index off the end.

## 2. The marker word is white space, so predicates keyed on `IsSpaces` see it

`CssRectWord.IsSpaces` is "every character is whitespace" and the marker's text is a newline, so
**`IsSpaces` and `IsLineBreak` are both true of it**. Any predicate that tests `IsSpaces` without
excluding a line break will fire on a `<br>`.

**Measured symptom** (also #1087): `IsJustificationOpportunity`'s `previous.IsSpaces` arm — written
for *preserved* white space under `pre`/`pre-wrap` — counted the break marker, so `text-align:
justify` spent one expansion share at the head of every line a `<br>` opened. At `width: 300pt`,
`first line here<br>alpha beta …` put `alpha` at x = 23.453pt while the lines above and below it
started at 20.0pt — an indent with **no white space in the source at all**, so it survived the phase
II fix above and needed its own. The arm now reads `previous is { IsSpaces: true, IsLineBreak: false }`.

A word's own coordinates make the placement rule visible directly: the `<br>` word's `Left` equals
the *next* line's start, never the previous line's end.
