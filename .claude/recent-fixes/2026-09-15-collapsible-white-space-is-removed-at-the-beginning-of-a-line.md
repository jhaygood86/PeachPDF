# A line's beginning is a real position now — three defects that shared one wrong question (issue #1087)

[css-text-3 phase II](https://www.w3.org/TR/css-text-3/#white-space-phase-2) removes a sequence of
collapsible spaces at the *beginning* of a line. `CssLayoutEngine.FlowBox` had no notion of where a
line began, so the source newline between a `<br>` and the element after it indented the whole line
the `<br>` opened by one space (3.013pt at the default 12pt font). Formatting the source across lines
changed the rendered output; minifying the same markup laid out correctly.

Fixing that turned up two more defects with the same root, both confirmed by rendering before being
touched. All three are fixed here.

## The load-bearing idea

**`Line.Words.Count == 0` is the test that looks right and is wrong.** A forced-break word is placed
on the line it *opens*, not the one it closes: `FlowBox`'s wrap branch closes the current line,
creates the new `CssLineBox`, stamps its `FollowsForcedBreak` from that same word, and only then
falls through to the shared tail that positions it and calls `ReportExistanceOf` — on the **new**
line. So after a `<br>` the line already holds one word, and every "is this line empty?" question
answers "no" for exactly the line those questions are usually being asked about.

`IsAtLineStart` is that question asked correctly: no word on the line that isn't a zero-width break
marker, and no `Line.Rectangles` entry (an atomic inline-level box contributes one and no word at
all). Recorded as
[.claude/invariants/inline-a-forced-break-word-sits-on-the-line-it-opens.md](../invariants/inline-a-forced-break-word-sits-on-the-line-it-opens.md).

## The three defects

**1. The collapsed space was rendered at the line start.** A collapsed space is never a word here —
`CssBox.AppendWordsFromText` emits one only under `pre`/`pre-wrap`. Everywhere else it survives as a
cursor advance of `box.ActualWordSpacing`, added at two points in `FlowBox`: the whitespace-only-box
tail (`<br>`↵`<div>`) and the `DomUtils.IsBoxHasWhitespace` branch (`<br><span> b</span>`, the space
inside the following inline). Both shapes reproduced; fixing only the first leaves the second
indented. The white space reaching either point is always collapsible — a preserved one is a real
`IsSpaces` word, which leaves `Words.Count` non-zero and never reaches the whitespace-only branch —
so no `white-space` test is needed alongside the line-start one.

**2. A forced break was a justification opportunity.** `CssRectWord.IsSpaces` is "every character is
whitespace" and the marker's text is a newline, so **`IsSpaces` and `IsLineBreak` are both true of
it**. `IsJustificationOpportunity`'s `previous.IsSpaces` arm — written for *preserved* white space —
counted it, so `text-align: justify` spent an expansion share at the head of every line a `<br>`
opened. Measured: `first line here<br>alpha …` at `width: 300pt` put `alpha` at 23.453pt while the
lines above and below started at 20.0pt — **with no white space in the source at all**, so it
survived defect 1's fix entirely and needed its own.

That is also why suppressing the flow's own `PendingWordSeparator` was not enough for the
`<br>`↵`text` shape: `word.HasSpaceBefore` is a fact about the *source text*, true of `alpha` in
`…<br>\nalpha…` whichever line it lands on. `PrecededByWordSeparator` is now `!wordOpensTheLine &&
(…)`, read before `ReportExistanceOf` puts the word on the line — the only moment the question is
answerable. **The test caught this, not the reading**: the first round passed its own field assertion
and still rendered the indent.

**3. An overlong word wrapped off the line the break opened, blanking it.** `HasOrdinaryWrapOpportunityBefore`
also took the `IsSpaces` arm on the marker, reporting a soft wrap opportunity at the very *start* of
a line — which [css-text-3 §5](https://www.w3.org/TR/css-text-3/#line-breaking) does not allow. With
`<p style="width:70pt">AA<br>Supercalifragilisticexpialidocious</p>` the long word landed at y=51.5
(two line-heights below `AA`), the `<br>`'s own line blank; the identical content with a space
instead put it at one line-height. Under `overflow-wrap: break-word` it was worse — the emergency
split was blocked and the line wasted entirely. Fixed at the source (`previous.IsLineBreak` is never
a wrap opportunity) plus the `emergencyBoundaryWrap`/`SuppressLeadingWrap` guards, which now ask
`IsAtLineStart` instead of counting words. After: y=35.75, exactly the spaced control.

## Deliberately not done

- **`childHasLeadingWhitespace` is not itself cleared**, only its advance suppressed — it also tells
  `HasOrdinaryWrapOpportunityBefore` a wrap opportunity exists before `b`'s first word, which the
  space offers whether or not it is rendered.
- **A float is not counted as line content**, and Chromium agrees: a line beside one begins at the
  float's edge with its leading white space removed (53.328px vs PeachPDF's 53.333px).
- **`TryApplyLineClamp`'s own `Words.Count == 0` guard is untouched.** It reads as the same question
  but is load-bearing differently: `IsAtLineStart` returns false for a line holding only an atomic
  inline, whose `Words` list *is* empty, so swapping it in would walk `Words[^1]` off the end.
- **The vertical (`CreateVerticalLineBoxes`) path is untouched** — it never adds this advance and it
  does not place the break word on the following line, so none of the three defects exist there.

## Evidence

**Measured against Chromium through the repo's own Playwright dependency**, with the bundled Source
Code Pro embedded via `@font-face` for *both* engines so neither is reading host fonts (#956's trap).
Deltas in CSS px on `getBoundingClientRect().left`:

| fixture | Chromium | PeachPDF | delta |
|---|---|---|---|
| issue sample, multiline — labels / values | 0 / 450 | 0 / 450 | **0.000** |
| issue sample, minified — labels / values | 0 / 450 | 0 / 450 | **0.000** |
| `<br>`↵`<span>` | 0 | 0 | 0.000 |
| `<br><span>` `b</span>` | 0 | 0 | 0.000 |
| leading white space at block start | 0 | 0 | 0.000 |
| `<span>AA</span> <span>BB</span>` (mid-line) | 28.812 | 28.800 | 0.012 |
| space after an atomic inline | 28.812 | 28.800 | 0.012 |
| space after a left float | 53.328 | 53.333 | 0.005 |
| justified line opened by `<br>` | 0 | 0 | 0.000 |
| `pre-wrap`, preserved spaces after `<br>` | 28.812 | 28.800 | 0.012 |

The multiline and minified rows being *identical in both engines* is the issue's own acceptance
criterion. The 0.012px residuals are Chromium's sub-pixel rounding of one space advance, present on
the unchanged rows too; those rows are the over-reach guards — a space mid-line, after an atomic
inline, or preserved under `pre-wrap` must still render, and does.

- Full suite **11 671 passed / 0 failed** (net8.0); `dotnet build PeachPDF.slnx -t:Rebuild` **0 warnings**.
- **100% line and branch coverage on all 19 changed executable lines.**
- 9 new tests (6 in `WhiteSpaceLayoutIntegrationTests`, 3 in `OverflowWrapIntegrationTests`). The
  behavioral ones were confirmed failing against unmodified `main`; the rest are over-reach guards
  that must pass either way.

## Found along the way, left out of scope

**An empty inline carrying padding/border is placed wrongly, independent of white space.** With
`<span style="padding-left:20pt;border-left:1pt"></span> <span>BB</span>`, Chromium puts `BB` at
27.656px and PeachPDF at 65.600px — a 37.9px gap, far more than the 9.6px space in question.
Confirmed **identical on unmodified `main`**, so it is pre-existing and this change neither causes
nor worsens it. Not filed here; it needs its own investigation of the "hack for actual width
handling" tail rather than a white-space fix.
