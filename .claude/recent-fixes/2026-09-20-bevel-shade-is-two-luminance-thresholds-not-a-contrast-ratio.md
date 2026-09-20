# `BorderBevelColors.Shade` is two luminance thresholds, not a contrast ratio

Closes jhaygood86/PeachPDF#1224, which asked for the near-white half only. Porting the whole of
Blink's `CalculateInsetOutsetColor` turned out to be the smaller change and fixed a second divergence
the issue did not know about.

## The load-bearing idea

The issue proposed keeping the existing near-black fallback and adding the near-white one. Reading the
actual function first showed the two ends are one rule, and that the near-black end was also wrong:

```cpp
// third_party/blink/renderer/core/paint/box_border_painter.cc
Color CalculateInsetOutsetColor(bool is_darken, const Color& color) {
  if (RuntimeEnabledFeatures::TableDefaultBorderColorCurrentColorEnabled()) {
    constexpr float kBaseDarkColorLuminance = 0.014443844f;  // Luminance of rgb(32, 32, 32)
    constexpr float kBaseLightColorLuminance = 0.83077f;     // Luminance of rgb(235, 235, 235)
    float luminance = color_utils::GetRelativeLuminance4f(color.toSkColor4f());
    if (luminance <= kBaseDarkColorLuminance)
      return is_darken ? color.Light() : color.Light().Light();
    if (is_darken)
      return color.Dark();
    return luminance > kBaseLightColorLuminance ? color : color.Light();
  }
  // ...the pre-M149 branch, which is where the 1.75 contrast ratio lives
}
```

PeachPDF's near-black fallback was `ContrastRatio(color, Dark(color)) < 1.3`, derived by sampling
Chrome at the gray boundary (32 lightens, 33 darkens). That reproduces the boundary *on grays* exactly,
which is why it survived, but it is not the rule. The contrast ratio in the real code is 1.75 and it
belongs to the **old** branch, where it decided the *lit* face rather than the dark one — a different
question entirely.

`Shade` is now the four-line port, and `ContrastRatio` is deleted.

## Found by running it, not by reading it

An exhaustive sweep of all 16,777,216 sRGB colors (`python3` + numpy, both rules evaluated per color)
says the two differ on 223,626 of them:

- **200,298** at the near-white end — the miss the issue filed, lit face only.
- **23,328** very dark chromatic colors where the old contrast-ratio test lightened both faces and
  Blink darkens. One-directional: there is no color where Blink lightens and the old rule darkened.
  `#001e4c` is representative — luminance 0.014503, just above the threshold, but contrast ratio 1.290
  against its own darkened form, just below 1.3.

That second bucket is invisible to grays and to uniformly-random color sampling (the issue's 60-sample
measurement found 58/60, all near-white misses), which is why it needed the exhaustive sweep rather
than more samples.

## Threshold boundaries, verified rather than assumed

Both constants are knife-edges and worth pinning:

| gray | luminance | vs constant | face |
| --- | --- | --- | --- |
| 32 | 0.014443843596 | `<= 0.014443844` | both lighten |
| 33 | 0.015208514423 | above | darkens |
| 235 | 0.830769876775 | **not** `> 0.83077` | still lightens |
| 236 | 0.838799011741 | `> 0.83077` | declared color |

Gray 235 clears the constant by 1.4e-7, so the `double` literals this file uses and the `float`
constants Blink uses agree on it only because Blink's comparison is strict and the constant *is*
lum(235). Do not "tidy" either literal to fewer digits, and do not relax `>` to `>=`.

## Deliberately not done

- The pre-M149 branch is not implemented behind any switch. PeachPDF targets current Chrome, and the
  feature has been stable since M151.
- Alpha still plays no part in the luminance test — Blink thresholds the declared color, not the
  composite. The issue's `rgba(74,144,217,.5)` row matches under this rule already.
- `Dark`/`Light` themselves are untouched; only the selection between them changed.

## Evidence

- Full suite: 12,608 passed, 1 failed, 9 skipped. The failure
  (`CssLayoutEngineTableTests.TableLayout_AsymmetricWrappableHeaders_InterpolateBetweenColumnMinAndMax_Issue1157`)
  reproduces unchanged on `main` with this branch stashed — pre-existing, unrelated to shading.
- Diff coverage on `BorderBevelColors.cs`: every changed coverable line hit, none missed.
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings, 0 errors.
- The `border_style` showcase grew a "colors too light to lighten" row, rasterized through **both**
  PDFium and MuPDF per this repo's paint-verification convention. Both agree, and sampling the PDFium
  raster gives lit `rgb(240,240,240)` / dark `rgb(156,156,156)` — Chrome's exact bytes for
  `border: 16px outset #f0f0f0`.

## Traps

- A bevel sample has to be read as a *pair*; a single face can be matched to the wrong rule. This is
  what produced the wrong conclusion recorded in
  [2026-09-19-bevel-lit-face-lightening-matches-current-chrome.md](2026-09-19-bevel-lit-face-lightening-matches-current-chrome.md).
- Tests that assert `BorderBevelColors.Shade(...)` on both sides of the comparison cannot fail when the
  rule changes — most of `BorderStylePaintIntegrationTests`/`OutlineStylePaintIntegrationTests` is
  written that way, and the whole suite passed on the first run of the new rule. The tests that
  actually guard this are the ones pinning literal `RColor`s.
- `docs/html-css-support.md` describes the bevel fallbacks in prose. It has now been wrong-then-fixed
  twice; change it in the same commit as `Shade`.
- **`<hr>` is not affected by any of this, and looks like it should be.** Review of this change flagged
  the UA default `hr { border: 1px inset }` + `border-bottom-color: #EEEEEE` (`CssDefaults.cs:92`/`:156`)
  as the most commonly hit instance of the near-white rule, since `#EEEEEE` is above the threshold.
  It is not hit at all: `<hr>` has its own content painter (`FragmentContentPainters.For`'s
  `CssBoxHr => HrPainter`), and `HrFragmentPainter` draws each side with a solid brush of the raw
  declared color through `BordersDrawHandler.DrawBorder` - a legacy shim that never reads
  `BorderTopStyle`. So a rule discards `border-style` entirely and never reaches `Shade`. Rendering the
  same fixture before and after this change gives byte-identical `<hr>` output. Filed as
  jhaygood86/PeachPDF#1225 (paint path) and #1226 (the UA colors, which are hardcoded per-side greys
  where Blink uses `color: gray` + `currentColor`). If you are reasoning about which elements a change
  to `BorderBevelColors` moves, check the painter, not just the UA sheet.
