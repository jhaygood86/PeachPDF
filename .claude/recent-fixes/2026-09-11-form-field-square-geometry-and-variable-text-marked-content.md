# Form fields: square checkbox/radio geometry, and the `/Tx` marked-content sequence

Two independent defects in interactive PDF forms, both found by looking at rendered output rather than
by reading code, and neither caught by the ~10 700-test suite (which passed untouched before either fix).

## 1. A checkbox was 9.75pt × 16.75pt, not square

`CssRectFormField` was the only phantom-word type in the engine leaving `CssRect.IsImage` at `false` —
`CssRectImage`, `CssRectSvg` and `CssRectShape` all override it to `true`, and `CssRectShape` carries no
image either. The original comment argued the flag from the name ("a field has no decoded external
content"), but `IsImage` is this engine's **"atomic, non-text word carrying its owner box's whole
replaced geometry"** flag, and `CssLineBox.UpdateRectangle` splits the box-model arithmetic across the
two axes by it:

- horizontal border+padding is added *there* (gated on `IsImage`, or on the box being its own
  first/last hosting line box — which a form-field box never is: its `Location` stays `(0,0)` and
  `FirstHostingLineBox`/`LastHostingLineBox` stay null);
- vertical border+padding is *not*, because `CssLayoutEngine.MeasureIntrinsicSize` already folded it
  into `word.Height` (the `word.Height += ...` line at the end of it).

So a field got the vertical inset **twice** and the horizontal **not at all**. Measured, not deduced:
a UA-default checkbox painted `45.27 746.62 9.75 16.75 re`, and a `width:40pt;height:40pt` one painted
`40 × 47` — the constant `+7` in both being `2 × (padding 1pt + border 0.75pt) × 2`. Text fields were
wrong too, just less visibly: a `width: 160pt` field painted its chrome 160pt wide (the *content* box)
instead of 165.5pt.

Fix: `CssRectFormField.IsImage => true`. Every other `IsImage` site was checked first and is benign or
an improvement for an atomic word (skip text measurement, skip `::first-line` re-measure, skip the
baseline re-anchor in `CssLineBox.SetBaseLine`, skip `DrawString` in `FragmentPainter.PaintWords`).

Squareness needed a second change: the UA sheet's `input, select { padding: 1pt 2pt }` is a *text
field* look, and an asymmetric padding makes a correctly-computed border box non-square on its own.
Added `input[type=checkbox], input[type=radio] { padding: 0; margin: 3px 3px 3px 4px }`, matching what
browsers' own UA sheets do (`padding: initial` there). The margin is the browser-default one, so an
unstyled `<input type=checkbox> Label` no longer sits flush against its label.

## 2. Editing a field left the old value visible behind the new one

Reported from Adobe Reader on Android with a screenshot: the text field showed the newly typed value
**and** the generated one **and** mojibake (`-DQH 'RH`), all overlapping. The mojibake is the tell —
those are the generated string's bytes `<002D0044005100480003002700520048>` (glyph IDs into PeachPDF's
embedded subset, spelling "Jane Doe") re-rendered through WinAnsi: `0x2D`→`-`, `0x44`→`D`, `0x51`→`Q`…

Cause: the baked `/AP /N` appearance stream drew the value with no `/Tx BMC` … `EMC` marked-content
sequence around it. That sequence is the contract ISO 32000-1 §12.7.3.3 defines for variable text, and
it is how a reader knows *which part of the appearance to replace*: keep everything before `BMC` and
after `EMC` (the border and background), regenerate what lies between. Apache PDFBox's
`AppearanceGeneratorHelper.setAppearanceContent` is the canonical implementation and spells out the
fallback exactly — `if (bmcIndex == -1) { writeTokens(tokens); writeTokens(TX, BMC); }`, i.e. **append**
the new value after the existing stream. Appending is what produced the doubling.

Fix: `RGraphics.BeginVariableText()`/`EndVariableText()` down to `XGraphicsPdfRenderer`, emitting
`/Tx BMC` and `EMC`. Two things that matter:

- Both are emitted in **graphic mode** (`BeginGraphicMode()` first), because §14.6 forbids a
  marked-content sequence straddling a `BT`/`ET` boundary and the text drawn between them opens its own.
- They are a **dedicated pair rather than a reuse of `EndMarkedContent`**. That was the first thing
  tried: making `EndMarkedContent` force graphic mode looks like a strict improvement (it would also
  fix tagged PDF's own latent straddling), but `ColorGlyphPainter` calls it from *inside* a text object
  — the legal `BT BDC … EMC ET` nesting — and forcing graphic mode there cuts the invisible-text run
  short. Don't merge them.

A comb field's cell dividers had to move: they were drawn after the characters in `DrawComb`, which
would have put them inside the replaceable region, so a reader regenerating the value would erase the
cells. Split into `DrawCombDividers` (chrome, outside) and `DrawCombCharacters` (value, inside).
The sequence is opened even for an empty value — a field that starts empty and is typed into needs a
region to replace just as much as one that starts with text.

Verified by the reporter on the original device: editing, clearing and re-selecting all behave.

## Evidence

- Full suite 10 777 passed / 0 failed (net8.0); solution rebuild 0 warnings.
- 100% diff coverage on every changed library line.
- The nine `FormFieldGeometryIntegrationTests` were confirmed to fail 9/9 against the unfixed code
  before being kept.
- `interactive_pdf_forms` re-rendered through both PDFium and MuPDF, which agree: squares and circles,
  and no change to any field's drawn content.
