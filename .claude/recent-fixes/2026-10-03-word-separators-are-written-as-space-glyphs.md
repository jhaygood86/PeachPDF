# Word separators are written as space glyphs

User-visible consequences are in
[../migration-notes/2026-10-03-word-separators-are-written-as-space-glyphs.md](../migration-notes/2026-10-03-word-separators-are-written-as-space-glyphs.md);
this is the reasoning.

## The defect

A collapsed space between two words was never a glyph. `CssBox.ParseToWords` splits text into
`CssRectWord`s and records the space only as `HasSpaceBefore`/`HasSpaceAfter`; the flow turns it into an
advance; the painter draws each word with its own `DrawString`. So the content stream had one `Tj` per word
and a bare `Td` jump where the space was. Readers that take text in stream order (PdfPig's `Page.Text`, plain
full-text indexers, DMS imports) got `PremiumWidget`; PDFium and MuPDF hid it by rebuilding gaps from
positions. ISO 32000-1 §14.8.2.5 asks for word breaks to be represented by an actual space character.

## Load-bearing ideas

**Draw on the flow's own record, `CssRect.PrecededByWordSeparator`, not on the word-level space flags.** It is
the one place all three sources of an inter-word advance meet (this word's `HasSpaceBefore`, the previous
word's `HasSpaceAfter`, and a white-space-only inline box between two others, which no word carries), and the
horizontal flow already excludes the word that opens a line. Keying the paint on `HasSpaceAfter` would have
missed `<span>AA</span> <span>BB</span>` and drawn a space at the head of every wrapped line.

**Draw the glyph in the font that measured the gap, which the flow now records next to the flag
(`CssRect.WordSeparatorStyle`).** Each source measures the advance in a different box's font: the previous
word's trailing space in that word's box (its `ActualWordSpacing`), a white-space-only inline box and the
`IsBoxHasWhitespace` child-leading-space path in the enclosing box. The first version used the following
word's own font; the review measured `a <span style='font-size:200px'>B</span>` drawing a 47.5pt space into
a 3.5pt gap, starting off the page and covering `a`. `CssLineBoxCoordinates.PendingWordSeparatorStyle`
carries the source alongside `PendingWordSeparator` in `FlowBox`; `CreateVerticalLineBoxes` keeps a local.

- `FragmentPainter.PaintWordSeparator` draws `" "` in that box's `ActualFont`, `GetWhitespaceWidth` wide, on
  the word's baseline (`rect.Y + box font ascent - separator font ascent + sub/superscript shift`, since the
  two fonts can differ). It runs *before* the word, so the stream reads `word␠word` in logical order.
  Sideways vertical words draw it inside their own `SidewaysRotation`, at `x = -advance` of the natural
  rect; upright runs draw it `advance` above (or, RTL, below) the column cell, centred across it.

**Anchor a horizontal separator at the end of the word before it, not in front of the word after it.**
The first version put the glyph flush against the following word. In a widened gap (justification,
`word-spacing`, `letter-spacing`) the widening then sat *in front of* the space, and pypdf, which rebuilds
a space from a wide `Td` jump and only skips that when the text so far already ends in one, read two: the
review measured 0 → 57/96/99 double spaces for justify/letter-spacing/word-spacing. A browser's text run
has the space right after the word and the widening after the space. `FragmentPainter` remembers the last
word it drew (`PaintedWord`: the word, its index in its line's logical-order `Words`, its rect, transform
and clip; images and leaders count) and `PrecedingWordEdge` anchors at its facing edge when it is this
word's logical predecessor, drawn under the same transform and clip, on the expected side. Otherwise the
old placement stays: flush against the word on its predecessor's side (start edge for an even `BidiLevel`,
end edge for an odd one). The clip condition is not cosmetic: the first word inside an `overflow: hidden`
inline-block has its predecessor outside the box, and anchoring there drew the glyph under the box's clip,
where it is cut away (`InlineBlockOverflowClipPaintTests` caught it).

**Write the separator so the next word's `Td` does not change.** Each separator as its own `Td`+`Tj`
moved nothing in layout, yet changed pixels in both PDFium and MuPDF: readers keep the text line matrix in
single precision, and a different chain of relative `Td`s rounds differently - a glyph at 301.5pt landed at
301.49997, which at 144 dpi is exactly a pixel boundary, and PDFium flipped a whole glyph edge.
`XGraphicsPdfRenderer.TryShowAtPen` therefore shows a run of spaces with a bare `<sp> Tj`, no `Td`, when
it follows the previous text operator (`TextRun`: recorded after every plain and italic branch, `Td` or
`Tm`, and after `DrawPositionedGlyphs`; content unchanged since apart from this call's own text state;
baseline within a quarter em; under or out of the synthetic-italic shear alike, which keeps advances
horizontal; requested x within 0.1em of where layout put that
run's end - measured width plus one `letter-spacing` per glyph). The glyphs land at the pen, not at x.
Deciding by the pen instead let a fallback separator - one the painter anchored in front of the
following word, because the word in between was `visibility: hidden` - continue from the word before the
hidden one, in the wrong gap (the review pass found it). The renderer cannot tell a separator from any
other run of spaces, so preserved `white-space: pre` spaces that start where the previous word ends are
written the same way. Showing text moves the text matrix, not the line matrix the next `Td` is relative to
(ISO 32000-1 §9.4.2), so the word `Td` chain is byte-identical to `main`. Measured alternatives:
- *Space appended inside the previous word's own hex string:* line matrix unchanged too, but PDFium aligns a
  text object's glyphs to the pixel grid differently once it holds one more glyph, and shifted whole words
  by a pixel (plain paragraph: 152 px at 144 dpi, identical glyph origins).
- *`[adj <sp>] TJ` to land exactly on the requested x:* fine for the few-thousandths `/W` rounding, but a
  separator after a word drawn glyph by glyph (GPOS) needs an adjustment of a whole `letter-spacing` per
  glyph (see below), and pypdf reads a `TJ` number of 95% of a space as a word break of its own: doubles
  again. The glyph is inkless, so it now simply lands at the pen.
- *Fixing the reader-side drift instead* (`AdjustTdOffset` storing the rounded position): makes positions
  exact but changes `main`'s own `Td` chain, i.e. pixels in 90 of 202 showcases on its own. Not this
  change's business.

**One glyph, one ToUnicode destination: U+0020 wins.** Arial and the bundled Source Sans 3 draw U+00A0
with the space glyph. `CMapInfo.AddShapedText` recorded the last source drawn for a glyph, so one `&nbsp;`
drawn after the last ordinary space turned every word break of the document into U+00A0 (before this
change the space glyph was rarely drawn, so the collision rarely mattered). `CMapInfo.MapGlyphToText` now
never lets anything replace `" "` and always lets `" "` replace. The no-break space then extracts as a
space, as it does from Chrome's PDFs in pypdf and PDFium (MuPDF still reads U+00A0 from Chrome's).
Per-occurrence `/ActualText` would keep it exact:
[a-no-break-space-drawn-with-the-space-glyph-extracts-as-a-space.md](../accepted-gaps/a-no-break-space-drawn-with-the-space-glyph-extracts-as-a-space.md).
- Both `DrawWordGlyphs` callers pass the flag: `PaintWordSequence` and the `text-overflow` kept-part path.
- A font without U+0020 gets no glyph. Drawing it would show `.notdef` on the page and throw under PDF/A
  (`RequireNoMissingGlyphsForPdfA`).

## Found by running it, not by reading it

- **The vertical flow flagged the word that opens a column.** `CreateVerticalLineBoxes` assigned
  `pendingWordSeparator || HasSpaceBefore` without FlowBox's `wordOpensTheLine` guard, so the separator
  pending from the previous column landed on the next column's first word. Invisible until something painted
  the flag; now guarded the same way (`line.Words.TrueForAll(w => w.IsLineBreak)`, the vertical twin of
  `IsAtLineStart`). The review found vertical `text-align: justify` output unchanged by it.
- **An `IsSpaces` guard looked right and was wrong.** The first version skipped preserved-white-space words,
  reasoning they paint their own glyphs. A mutation probe showed no test needed it; the probe that did,
  `<span>AA </span><span style="white-space:pre">  BB</span>`, showed it *dropped* the collapsible space in
  front of the preserved ones (`AA  BB` for a line that holds three spaces). The flag is set exactly where the
  flow added an advance, so it needs no second filter. `white-space: pre` on its own never sets the flag.
- **A child's own leading space is not the `IsBoxHasWhitespace` path.** `<span>a</span><span> b</span>`
  measures its gap in the child's font through `b`'s `HasSpaceBefore`; the path that advances by the
  *enclosing* box's word space is reached only by the declarative API's text spans, which is where its test
  lives (`DeclarativeApiIntegrationTests.TextSpans_LeadingSpaceOfALaterSpan_…`).
- **The inline-block case.** `before <span style="display:inline-block">inside</span> after` writes a space
  in front of `inside`, inside the inline-block's own padding (and clip) - the flag carries into the atomic
  box's first word. The extracted text, `before inside after`, is what a browser gives. The glyph is
  inkless, but it must stay inside the clip, which is why the predecessor anchor also compares clips.
- **`::first-line` and the enclosing-box sources.** A white-space-only inline box and a child's leading
  space record the real box as `WordSeparatorStyle`, not its first-line counterpart (the gap is measured
  with that box's `ActualWordSpacing`, so its font is right). Taking the separator's colour from it drew a
  black space on a green first line (`FirstLinePseudoElementIntegrationTests` caught it); colour and
  `letter-spacing` now come from `word.FirstLineStyle ?? WordSeparatorStyle`, the style the previous word was
  drawn in, which also keeps the writer from switching `rg`/`Tc` for the space.
- **A word drawn glyph by glyph ignores `letter-spacing`.** `DrawPositionedGlyphs` (any word with a GPOS
  delta, i.e. most kerned words) advances its pen by advance + GPOS delta only, while `Tc` is applied after
  each glyph's own `Tj` and so never accumulates; layout counted one `letter-spacing` per glyph. Such a word
  is drawn narrower than laid out - `AUTE` in `first_line`'s letter-spaced first line by 2.25pt - with the
  shortfall as extra gap after it. Pre-existing and visible on `main`; left alone here, but it is why
  `TryShowAtPen` compares the requested x with the laid-out end rather than with the pen, and shows the
  glyph at the pen. Worth its own fix.

## What was deliberately not done

- Placement at a direction change:
  [a-word-separator-at-a-direction-change-is-written-at-the-far-edge-of-its-word.md](../accepted-gaps/a-word-separator-at-a-direction-change-is-written-at-the-far-edge-of-its-word.md).
- Separators the flow does not record (inline-flex content, replaced elements, fonts without a space):
  [no-word-separator-glyph-is-written-where-the-flow-records-none.md](../accepted-gaps/no-word-separator-glyph-is-written-where-the-flow-records-none.md).
- No change to `MarginBoxRenderer`: margin-box `content` is drawn as one string and already contains its
  spaces (`Seite 1 von 3` extracted correctly before and after).
- No space after the last word of a line. Extractors already break lines by position, and css-text-3 hangs or
  removes that space anyway.
- No clamp for a negative `word-spacing`: the gap is then narrower than the glyph's advance and the glyph
  overlaps the previous word by the difference (2.2pt measured at `word-spacing:-3px`).
- Mixed font sizes on one line: PDFium's positional extraction still breaks `a <span style="font-size:75pt">B</span> c`
  into three lines, before and after this change, although the glyph sequence and the space positions now
  match Chrome's own PDF of the same markup (which PDFium extracts as `a B c`). The large word's baseline sits
  0.1pt off the small words' in PeachPDF's output and on the same baseline in Chrome's; whether that is what
  PDFium keys on was not measured.

## Evidence

All measured on fresh Release builds of `main` (f0009add) and this branch, rendered through the
TestHarness (all 202 showcases) and through `PeachPDF.Cli` (a 17-document corpus: the review's NBSP
documents, plain/justify/`letter-spacing`/`word-spacing` paragraphs, inline mixes, italic synthesis, Hebrew,
Arabic, mixed bidi, a table, a list, underline, inline-block and out-of-flow neighbours), with Chrome's
print-to-PDF of the corpus as the oracle.

- **Pixels**, MuPDF and PDFium at 96 and 144 dpi, `main` vs this branch: PDFium identical in 201 of 202
  showcases, MuPDF in 197 or 198. One or two differ only faintly (at most 11/255, MuPDF only; which ones
  changed from build to build of this branch - `declarative_api`, `paged_media_running_elements`,
  `paged_media_monolithic_content`, `table_visibility_collapse`, `writing_mode` - so this is MuPDF's own
  sensitivity to the extra text objects, not glyph movement); three flip glyph-edge pixels (at most
  66/255 raw, 44/255 after a 1.5px Gaussian blur) where the `Td` chain changes -
  `scroll_containers_across_pages` and `tab_size` (preserved `white-space: pre` spaces now continue the pen
  without the `Td` they had, so the next word's `Td` is relative to an earlier point) and `tagged_pdf` (a
  marked-content boundary between the words gives the separator a `Td` of its own). In the
  corpus every document is identical except Arabic (MuPDF, 144 dpi, 67 px): a right-to-left separator is
  not at the pen and is positioned with its own `Td`. Glyph origins (MuPDF `rawdict`): identical within
  0.0002pt in 201 showcases, 0.0046pt in `scroll_containers_across_pages`. The first version's
  "rasterize identically" and 0.0093pt were not what the review measured (0.0258pt); they no longer apply.
- **Extraction** over the 202 showcases (`main` → this branch): pypdf double spaces 125 → 144 in 11
  documents (the review: 18 → 1162 in 23). Seven are a pair of real separators around something that is
  not text - an inline image or inline-block, a form field, a float, an out-of-flow element - where
  Chrome's PDF doubles too (`before  after` around an inline-block, in pypdf); three are next to a
  right-to-left run (`bidi_text`, `list_style_type`, `writing_mode`), whose separators are not at the pen
  and get their own `Td`; one (`print_catalog`) follows a letter-spaced, kerned word. Corpus: 0 doubles in plain, justify,
  `letter-spacing`, `word-spacing`, matching Chrome. pdfminer.six doubles in justified and word-spaced text
  (corpus justify 48, Chrome 35; word-spacing 62, Chrome 61): it adds a space for the widening after the
  real one, as for Chrome's. PDFium unchanged (0 → 0 doubles, same words). Non-whitespace order: identical
  in PDFium; differs in pdfminer in 10 showcases, in both directions (`text_align_last`: `main` put "Ut" out
  of order, this branch does not; `letter_word_spacing`/`multicol`: other groupings) - its layout analysis
  is sensitive to any added glyph; not compared against Chrome showcase by showcase. NBSP: the review's two
  documents in Liberation Sans Narrow (space and NBSP share a glyph) now extract exactly as Chrome's in
  pypdf, PDFium and pdfminer.
- **Cost**: uncompressed page content +9.2% across the showcases (up to +22%, `multicol`; +40% on a
  page of nothing but 12pt prose), compressed files +0.30% in total (up to +2.1%). One `<0003> Tj` per
  separator; no `Td` and no `Tc`/`rg` toggles in the common case. Generation time unchanged within noise
  (TestHarness `--benchmark`, two runs each: 10.18/10.34 s vs 10.44/9.98 s per corpus pass); allocations
  +3.3% (each separator is one more shaped `DrawString`).
- **Tests**: `WordSeparatorGlyphTests` (painter: placement, anchoring incl. justify left to right and
  right to left, `word-spacing`, image, `visibility: hidden`, clipped inline-block, direction change,
  opacity, colour/`letter-spacing`, `::first-line` font, the synthesized superscript in upright and
  sideways vertical text - the two baseline-shift mutations the review found surviving now fail),
  `WordSeparatorPdfOutputTests` (content stream: no `Td` for a separator, justified lines, continuing after
  a kerned word and after a synthetic-italic `Tm`, the background/hidden-word/raised-word fallbacks,
  NBSP/ToUnicode in Source Sans 3, tagged PDF marked content), `CMapInfoSharedSpaceGlyphTests`. Mutating the
  right-to-left anchor, the side check, the clip check, the laid-out-end check or the `Tm` bookkeeping each
  turns one of them red. `TestRecordingGraphics.WordDrawStringCalls` replaces
  the hand-written `Where(c => c.Text != " ")` filters.
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings. `PeachPDF.Tests` (net10.0) all pass except
  `AnonymousBoxDefaultingTests.AListItemCostsLittleMoreThanAPlainBlock`, the allocation-ratio bound that
  fails the same way on unmodified `main` in a Debug build here (the review saw it pass 10/10 in Release).
  net8.0 is left to CI (no .NET 8 runtime on this machine).
