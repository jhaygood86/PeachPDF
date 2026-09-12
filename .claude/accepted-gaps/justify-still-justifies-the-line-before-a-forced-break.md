# `text-align: justify` still justifies the last line before a forced break

> **Tracking issue not yet filed** — the `gh` CLI was not available on the machine this was found on.
> File it with the title *"text-align: justify justifies the last line before a forced break"* and the
> body below, then replace this block with the usual `Tracked as **#NNN**.` line.

Found while reading css-text-3 in full for issue #1013 (see
[../recent-fixes/2026-09-12-justify-expands-at-opportunities-not-word-boundaries.md](../recent-fixes/2026-09-12-justify-expands-at-opportunities-not-word-boundaries.md));
pre-existing, and deliberately left alone by that change, which was about *where* a justified line's
expansion goes, not about *which* lines get justified at all.

[css-text-3 §6.1](https://www.w3.org/TR/css-text-3/#text-align-property) defines `justify` as
"Unless otherwise specified by `text-align-last`, the last line **before a forced line break** is
start-aligned." PeachPDF exempts only the last line of the *block*
(`CssLayoutEngine.ApplyJustifyAlignment`'s `blockFinished && lineBox.Equals(...LineBoxes[^1])`
check), so in

```html
<div style="text-align: justify; width: 200pt">AA BB CC DD EE FF GG<br>next paragraph</div>
```

the line ending at the `<br>` is stretched to the full measure, where a browser leaves it ragged.
`<br>`-separated address blocks and verse are the shapes that show it.

Two things make this more than a one-line fix, which is why it was not folded into #1013:

- **The forced-break marker lives on the *following* line, not the one that needs exempting.**
  `FlowBox` wraps *before* placing the `"\n"` word, so `CssLineBox.FollowsForcedBreak` is set on the
  line the break starts. Answering "is this line the one before a forced break" means looking ahead
  one line — cheap only if the index is threaded down from `FinalizeLineBoxes` (which already
  iterates by index); an `IndexOf` per line would make finalization quadratic in a long block.
- **A forced break landing exactly on a fragmentainer boundary cannot see its successor at all.** The
  pass that stops at the break finalizes its lines with `blockFinished: false` and the next line does
  not exist yet; the resumed pass creates it, by which time the previous line has already been
  aligned. That is the same shape the `blockFinished` parameter exists to handle, and it would need
  the same kind of carried-over signal (`InlineBreakToken.FollowsForcedBreak` already carries the
  mirror-image of it for `text-indent: each-line`).

Related and equally absent: `text-align-last` and `text-align-all` are not implemented at all, so
there is no way for an author to ask for the justified-last-line behaviour either. Closing this gap
properly probably means implementing `text-align-last` and routing both the block's last line and the
last line before a forced break through it, rather than hard-coding start alignment in two places.
