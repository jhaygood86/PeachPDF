# `background-clip: text` now actually clips to text, and CFF fonts get real vector outlines

## What changed

**`background-clip: text` was previously silently inert.** Any `background-clip` value other than
`border-box`/`padding-box`/`content-box` — including the standard gradient-text idiom's `text` — fell
back to `border-box`, so a declaration like:

```css
h1 {
  background: linear-gradient(to right, #e11, #11e);
  background-clip: text;
  color: transparent;
}
```

painted a solid gradient bar across the whole heading, with the text itself invisible (`color:
transparent` drew nothing, and the background covered the same area) — content loss, not graceful
degradation.

`background-clip: text` now clips every background layer (solid color and/or any number of
`background-image`/gradient layers) to the union of the element's own laid-out glyph outlines,
including nested inline descendants (e.g. a `<span>` with a different font weight). The glyphs
themselves still paint as ordinary, selectable, extractable PDF text via the normal text-show operator
— `color: transparent` only makes them invisible, it does not convert them to vector art — so this is
a genuine improvement over PeachPDF's existing SVG gradient-text support, which loses selectability.

A box falls back to its old `border-box` behavior (rounded corners preserved, if any) when the font
has no decodable glyph outline (a CID-keyed CFF font) or the box is set to a vertical writing mode —
see the accepted-gap notes for both.

## Also in this change

**CFF/OpenType-CFF ("OTTO") fonts now decode real vector glyph outlines**, via a new Type 2 charstring
interpreter (`Type2CharstringInterpreter`/`CffTable`) alongside the existing TrueType `glyf` decoder.
This is what makes `background-clip: text` work for CFF fonts, and as a side effect also fixes two
existing features that silently fell back for any CFF font before now:

- SVG gradient/pattern `fill`/`stroke` and `<textPath>` on `<text>` now honor a CFF font's real glyph
  shape instead of falling back to a solid fill (or, for `<textPath>`, the straight baseline).
- `text-decoration-skip-ink` now finds real ink under a CFF font instead of reporting "no ink known"
  and leaving an unbroken decoration line.

A CID-keyed CFF font (common in CJK "Pro"/Source Han Sans/Noto Sans CJK OpenType-CFF builds) still
falls back for all three features — this narrows what used to be a blanket "every CFF font" gap down
to that specific subset.

**A gradient/pattern fill immediately following a fully-transparent fill (e.g. `color: transparent`
text) is no longer silently invisible.** `PdfGraphicsState.RealizeBrush`'s gradient/pattern branch
never realized its own fill alpha unless the gradient had a semi-transparent stop of its own, so it
silently inherited whatever alpha the *previous* solid-color fill left active — this is not specific
to `background-clip: text`, but that feature's routine pairing with `color: transparent` is what
surfaced it (two adjacent gradient-clipped headings used to show only the first one). See
`.claude/recent-fixes/2026-09-16-gradient-fill-inherits-leftover-fill-alpha.md` for the full root
cause.
