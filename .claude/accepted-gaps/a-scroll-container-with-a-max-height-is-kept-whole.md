# A scroll container with a max-height is kept whole

_CSS Fragmentation Level 3 §2 (monolithic content). Trackers: [#1375](https://github.com/jhaygood86/PeachPDF/issues/1375)
(the `overflow: hidden` spec deviation), [#1479](https://github.com/jhaygood86/PeachPDF/issues/1479) (breaking
capped boxes as browsers print them)._

`MonolithicContent.HasConstrainedBlockSize` treats every capped scroll container as monolithic, whatever its
`overflow` value: a non-auto `height`, a `max-height`, an `aspect-ratio` or both block insets. Only an
auto-height, uncapped one breaks (#1321). §2 lets a UA keep an `overflow: hidden` box whole only with "a
non-auto logical height (and no specified maximum logical height)", so an `overflow: hidden` box with a
`max-height` or an `aspect-ratio` kept whole is a spec deviation (#1375). For `auto`/`scroll` it is allowed,
but Chrome prints a capped box whose content fits under its cap by breaking it between its lines (#1479).

The trap is a capped box whose content overflows the cap. Its clipped lines lie past the box's end, and a
page break among them ends the pass there: the content after the box is placed back on the page the break
left, which is already emitted. Measured on a 300×200pt page, a `max-height: 60pt` box holding 30 lines lost
all ten lines after it. Whether a box overflows its cap is only known after layout.

The first version of #1413 broke every capped box, noted one whose content overflowed its cap after
`ApplyHeight`, and laid the whole document out again (up to three times) with that box kept whole. Review
removed it for two measured reasons, both recorded with their samples in #1479:

- **The retry kept geometry from the attempt it replaced.** Only the root's size and position were reset
  between attempts. A table row broken across the page on the first attempt came out 38pt taller on the
  retry, because an anonymous inline box in the second cell still carried its first-attempt position, and
  `CssBox.GetMaximumBottom` read it. The box's last line then straddled the page foot. Laid out whole from
  the start (`break-inside: avoid`), the same box was right.
- **It was 7–9x slower** on a document of 400 clipping `max-height` cards, for byte-identical output.

A fix needs to decide whether a capped box overflows before the paginated layout places it (for example by
measuring its content once, unbroken, at its used width), or a retry that restores all layout state. For
`overflow: hidden` it also needs to decide what breaking a box whose content overflows its cap should mean,
which #1375 tracks.
