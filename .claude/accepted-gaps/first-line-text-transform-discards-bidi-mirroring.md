# `::first-line` with a differing `text-transform` paints unmirrored bidi text

Tracking issue: [#553](https://github.com/jhaygood86/PeachPDF/issues/553).

`CssBox.ApplyFirstLineStyleOverride` derives a word's `FirstLineText` from `OriginalText` (the
pre-text-transform source, which bidi mirroring deliberately never touches — see
`CssRectWord.PreMirrorText`) when a `::first-line` rule's own `text-transform` differs from the
box's own. `FragmentPainter.Text.cs`'s `PaintWords` then prefers `word.FirstLineText ?? word.Text`.
On a word that also underwent bidi L2/L4 reordering+mirroring, this paints the *unmirrored,
logical-order* `FirstLineText` instead of the reordered/mirrored `Text`, discarding the bidi work for
any word on the target's first formatted line.

`<style>p::first-line { text-transform: uppercase; }</style><p><bdo dir="rtl">hello</bdo></p>` -
`word.Text` is correctly `'olleh'`, but the painted text is `'HELLO'` (uppercased from the original,
un-reversed `'hello'`) instead of the required `'OLLEH'`.

Related, same area: `CssBox.RemeasureWordsTail` and `ApplyFirstLineStyleOverride` both re-measure
`boxWord.Text`, which after mirroring has run is the mirrored/reversed string — with GSUB ligature
shaping active, reversing a run can change which ligatures form, so the re-measured width can differ
from the width the rest of layout was built on.

**Deliberately out of scope for now.** A narrow combination (`::first-line` + a differing
`text-transform` + bidi-reordered content specifically on the first line) that needs its own
mirrored-and-transformed text derivation rather than reusing either existing field as-is.
