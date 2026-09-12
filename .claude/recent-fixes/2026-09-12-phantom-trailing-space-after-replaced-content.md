# Replaced content no longer reserves a phantom trailing space (issue #1011)

`CssRect.ActualWordSpacing` had two terms: one for a real trailing space (`HasSpaceAfter`) and a
second, unconditional one for any `IsImage` word. The second is inherited straight from the original
HtmlRenderer code (`git log -S` dates it to the initial commit), and it meant every atomic inline —
`<img>`, inline `<svg>`, `<object>`/`<video>`, `<iframe>`, `CssRectShape`, and anything else
answering `IsImage` — pushed whatever followed it one whole word space to the right. With source
white space present the two terms stacked, so `<img> text` got **two** spaces. The form-field work in
[2026-09-11-form-control-attributes-and-placeholder.md](2026-09-11-form-control-attributes-and-placeholder.md)
(#1010) had already had to carve an exception out of it
(`CssRectFormField.ReservesTrailingSpace => false`), which is what put the term under a name and made
it findable.

The fix deletes the term and the `ReservesTrailingSpace` property it was hiding behind, leaving
`ActualWordSpacing` keyed on `HasSpaceAfter` alone. `CssRectFormField`'s override goes with it — it
now gets the correct behaviour from the base class instead of opting out of a wrong one.

## What the measurement said

Numbers at `font: 16px monospace` (one space = 6.5977pt), gap between the atomic inline's right edge
and the next word's left edge:

| markup | before | after | Chromium |
|---|---|---|---|
| `<img>text` | 6.5977 (1 space) | 0 | 0 |
| `<img> text` | 13.1953 (2 spaces) | 6.5977 | one space |
| `<span>Y</span><span>X</span>` (control) | 0 | 0 | 0 |

Chromium's own numbers came from driving the repo's existing Playwright dependency directly
(`page.Locator(...).BoundingBoxAsync()` on the `<img>` and the following `<span>`): image right edge
13.000, following text at 13.000 with no space and at 21.797 with one. That is the evidence the
target is 0, not "0 looks tighter" — css-text-3 §4.1.1 makes white space the only thing that produces
an advance between two adjacent inline-level boxes.

## The thing that nearly read as a regression

Removing the term also removes it from **inside list markers**, because a `disc`/`circle`/`square`
marker's word is a `CssRectShape` and a `list-style-image` marker's is a `CssRectImage` — both
`IsImage`. The `list_style_image` and `marker_styling` showcases visibly tighten as a result.

This is correct, and worth not re-"fixing": PeachPDF's marker gap is the UA sheet's
`li::marker { margin-right: 5px }` (`CssDefaults.cs`), a deliberate uniform stand-in for the
per-counter-style `suffix` descriptor PeachPDF doesn't implement (see
[.claude/accepted-gaps/marker-box-layout.md](../accepted-gaps/marker-box-layout.md)). The phantom
space was *defeating* that rule for exactly the two marker kinds whose word happened to be `IsImage`,
giving them 5px + one font-scaled space while a text marker got 5px. After the fix all three kinds
measure the same 3.75pt (5px) gap — verified directly on `CssBoxMarker`'s word for
disc/decimal/image × inside/outside. Chromium puts a font-independent 7px after an image marker
(its own `LayoutListMarker` constant, also not a spec number), so 5px is nearer than the 13.8px the
phantom space produced.

## Two things the review pass turned up around it

- **`CssBox.GetMinMaxSumWords`'s trailing `maxSum -= box.Words[^1].ActualWordSpacing` became
  provably dead** — it was guarded on `!HasSpaceAfter`, which now selects exactly the words whose
  spacing is zero, because cancelling the phantom term was its only job. Removed, with a note in its
  place saying why it must *not* simply be inverted to hang a real trailing space: this walk carries
  one running `maxSum` across a whole subtree, so a box's last word is not the line's last word when
  a sibling follows it there (`<span>AB </span><span>CD</span>`). The residual §4.1.2 gap was filed
  as #1014 and has since been closed — see
  [2026-09-12-final-line-trailing-space-hangs.md](2026-09-12-final-line-trailing-space-hangs.md),
  which applies the rule at the two points where a line is known to have ended rather than per box.
- **`text-align: justify` still opens a gap where there is no white space** — it spreads its
  expansion over every word boundary rather than over justification opportunities, so on a justified
  line `A<span>B</span>` measures 6.186pt apart where left alignment renders it flush. Pre-existing
  and untouched, but it is the one place the rule this fix asserts does not hold, so the
  `ActualWordSpacing` remarks now say so and it is filed as
  [.claude/accepted-gaps/justify-expands-at-every-word-boundary.md](../accepted-gaps/justify-expands-at-every-word-boundary.md)
  (#1013).

## Evidence

- Full suite 10 832 passed / 0 failed (net8.0); `dotnet build PeachPDF.slnx -t:Rebuild` 0 warnings.
- 100% coverage on every changed line (the new `ActualWordSpacing` expression, 1.5M hits).
- `AtomicInlineSpacingIntegrationTests` (9 tests) confirmed to fail 8/9 against the unfixed code
  before being kept — the ninth is the all-text control, which must pass either way.
- Probe document rasterized through **both PDFium and MuPDF**, which agree: `[■]` flush with no
  source space, `[■ ]` with one.
- All 113 showcases re-rendered and pixel-diffed against a baseline built from unmodified `main`:
  exactly 4 change — `content_image`, `invoice`, `list_style_image`, `marker_styling` — each by
  exactly the one-space shift the fix intends. **Build the baseline from stashed code, not from
  `docs/showcase/`**: that directory was four days stale here and initially attributed `transform`
  and `interactive_pdf_forms` diffs (really #1010's) to this change.
