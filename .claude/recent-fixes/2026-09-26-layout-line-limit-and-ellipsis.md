# Line limit and ellipsis in `PeachDrawing.Text.Layout`

`ParagraphStyle.MaxLines`, `Ellipsis`, `TextOverflow` (`Clip`, `Ellipsis`); `LineBox.IsTruncated`; `ParagraphLayout.IsTruncated`.

- **It reuses the generated run of the hyphenation work.** A cut line is a `LineSpec` with `CutAt` (where its drawn text ends); `Assemble` draws to `CutAt` and appends the
  ellipsis as a generated piece (first in an RTL paragraph, last otherwise), and `LineBox.Range` runs on to the end of the text so that the hidden part is "hanging" in the
  sense the class already documents (in `Range`, past `ContentEnd`, not drawn). The last line of a limited paragraph is `LineEnd.Last`, aligned as a last line.
- **Only the allowed lines are worked out.** `Build` stops after `MaxLines` lines; `hidesText` is simply `spec.End < text.Length` at the last allowed line, which is why
  text ending in a newline (an empty last line follows) is not truncated. A limit on a 500,000 character text costs three lines.
- **`FitWithin` is `LargestFit` that is allowed to answer "nothing".** The ellipsis may be wider than the room; then only the ellipsis remains (`ContentEnd == Start`). The
  cut lands on grapheme boundaries only (the test sweeps widths over "e + combining acute" pairs), and spaces are trimmed before the ellipsis (`ContentEnd` of the cut).
- **The ellipsis takes the style of the last character drawn** (or the line's first if none), found by `AtomStyleAt`, so a larger run at the end gives a larger ellipsis. If the face has
  no U+2026 (and no fallback answers), `...` is used; measured with the same `Generate` shaping cache the hyphen uses (renamed from a hyphen-only cache).
- **A line limit is about lines.** A single line that overflows its width (an unbreakable word, `NoWrap`) is not "text left out"; it is cut only when `TextOverflow.Ellipsis` is set.
  The first version of the RTL test forgot this and drew an uncut line, which is what pinned the rule down.

Evidence: `ParagraphEllipsisTests` (21), the other layout tests unchanged.
