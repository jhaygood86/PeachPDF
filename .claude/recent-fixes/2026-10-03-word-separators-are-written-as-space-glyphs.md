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
  two fonts can differ), flush against the word on the side of its logical predecessor: start edge for an
  even `BidiLevel`, end edge for an odd one. It runs *before* the word, so the stream reads `word␠word` in
  logical order. Sideways vertical words draw it inside their own `SidewaysRotation`, at `x = -advance` of the
  natural rect; upright runs draw it `advance` above (or, RTL, below) the column cell, centred across it.
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
  box's first word. The extracted text, `before inside after`, is what a browser gives; the glyph is inkless,
  so the clip does not matter.

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

- New `WordSeparatorGlyphTests` (25 cases) plus one declarative-API case: horizontal, inline-box-only space,
  adjacent spans, wrapped line, vertical wrapped column (mixed and upright, also asserted on
  `PrecededByWordSeparator` after layout), `pre`, collapsible-before-preserved, justify, RTL (`bdo`), sideways
  vertical (mapped through the recorded transform), upright vertical (LTR, RTL, centring), `text-overflow`,
  font without a space glyph, glyph width equal to the gap for each separator source and for a sideways
  column, baseline alignment across font sizes, synthesized superscript. Every case that expects a space was
  red before the change. Mutation probes - drop the RTL side, the upright offset, the upright RTL branch, the
  upright centring, the glyph check, the baseline term, the sub/superscript shift, the text-overflow flag, the
  vertical `wordOpensTheLine`, and each of the four `WordSeparatorStyle` sources - each turn a matching case red.
- Six existing tests counted `DrawString` calls as words and now filter `" "` out explicitly;
  `InlineBlockOverflowClipPaintTests` counts seven `Tj` (four runs, three separators).
- End to end through `PeachPDF.Cli`, MuPDF with synthesized spaces turned off: before `PremiumWidget`,
  `BlocksatzmitmehrerenWoerterndie`, `Vertikalseitlich`; after `Premium Widget`, `Blocksatz mit mehreren
  Woertern die`, `Vertikal seitlich`, Hebrew and upright vertical with their spaces.
- An independent adversarial review of the first version (glyph in the following word's font; 50 + 27 hand-written cases rendered with base, head and Chrome; a
  1500-word and an 89-page document) found no over-drawing anywhere - no leading or doubled spaces, spaces +
  lines = words exactly - and no visible raster change; raster diffs were sub-0.01pt anti-aliasing from `Td`
  rounding (see the migration note). PDF/A-2a output passed for every case that passes on `main`. File size
  +4% on the 89-page document; layout time unchanged within noise. Its one major finding - the font - is
  what `WordSeparatorStyle` fixes; the end-to-end extraction above was re-run on the final version.
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings. `PeachPDF.Tests` (net10.0): all pass except
  `AnonymousBoxDefaultingTests.AListItemCostsLittleMoreThanAPlainBlock`, an allocation-ratio bound that fails
  identically on unmodified `main` on this machine. Only net10.0 was run locally (no .NET 8 runtime
  installed); net8.0 is left to CI. `PeachPDF.Cli.Tests`: all pass.
