# `height`/`min-height` on `<table>`/`<tr>` now take effect

## What changed

**A `height` or `min-height` declared on a `<table>` or a `<tr>` previously had no effect at all.** The
table/row laid out at its content-driven height regardless of what was declared, exactly as if the
property had never been written. The same declaration on a `<td>` or a plain block element already
worked. A common visible symptom: `vertical-align: middle`/`bottom` on a cell looked like a no-op,
because the row was always exactly one line tall, so `top`/`middle`/`bottom` all landed in the same
place — this is common in report/invoice markup (a fixed-height letterhead row, a signature block, a
banded table whose rows are meant to be a uniform height regardless of content).

```html
<table style="width:400px; height:80px">
  <tr>
    <td style="vertical-align:top">top</td>
    <td style="vertical-align:middle">middle</td>
    <td style="vertical-align:bottom">bottom</td>
  </tr>
</table>
```

Before this change, PeachPDF drew a one-line-tall table with all three labels on the same line
regardless of the `height:80px`. It now draws an 80px-tall table with the three labels at three
different heights, matching real browsers.

**Behavior now follows [CSS 2.1 §17.5.3](https://www.w3.org/TR/CSS21/tables.html#height-layout):** a
table's height is the *maximum* of its specified `height`/`min-height` and the sum of its rows' natural
heights — an explicit value smaller than the content never clips the table (a document that relied on a
too-small `height` to crop a table's visible content, if any existed, will now see the full table
instead). This half is spec-mandated. The other half — *how* any surplus above the natural total is
divided among the rows — is left undefined by CSS 2.1 itself ("CSS 2.1 does not define how extra space
is distributed when the 'height' property causes the table to be taller than it otherwise would be");
PeachPDF distributes it proportionally to each row's own natural height, the same rule this engine
already applies to column-width surplus, which is a reasonable implementation choice rather than a
spec requirement. A `<tr>`'s own `height`/`min-height` works the same way, one level down — a per-row
minimum, never a clip.

## Not covered by this change

- **`writing-mode: vertical-rl`/`vertical-lr` tables** are unaffected — see the accepted-gap note.
- **A table whose own row loop must continue into a separate, later top-level layout pass** (one cell's
  content alone needing a third fragmentainer) does not get height enforcement — see the accepted-gap
  note. Ordinary multi-page tables are unaffected by this and are fully handled.
- **A repeating `<thead>`/`<tfoot>` group's own rows never receive a share of the surplus** — only
  ordinary body rows grow. A header/footer group keeps its natural height even when the table as a whole
  grows, matching how a one-time surplus computed for the table's body wouldn't have a coherent meaning
  for a group that repeats identically on every page.
