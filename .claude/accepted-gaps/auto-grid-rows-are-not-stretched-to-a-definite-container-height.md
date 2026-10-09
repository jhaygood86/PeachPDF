# An `auto` grid row is not stretched to fill a definite container height

css-grid-1 §12.8 stretches the `auto` tracks to absorb the container's free space when `align-content` (rows)
or `justify-content` (columns) is `normal` or `stretch`. PeachPDF does it for the columns only
(`CssLayoutEngineGrid` passes `stretchColumns` to `SizeColumnTracks`); the rows are sized to their tallest item
and only positioned against `align-content`, so an `auto` row in a grid with a definite `height` stays
content-sized, with or without an explicit `align-content: stretch`.

Measured against headless Chrome (pt; `grid-template-columns:100pt; height:200pt; width:300pt`, one item):

| item | Chrome | PeachPDF |
|---|---|---|
| `<div>` | 100 x 200 | 100 x 15 |
| `<img>` 96x48px, `align-self: center` | 72 x 36 at y=82 | 72 x 38.8 at y=0 |
| `<img>` 96x48px, `align-self: stretch` | 400 x 200 | 77.6 x 38.8 |

The 38.8pt (rather than 36pt) and 77.6pt (rather than 72pt) are not part of this gap: they are the taller
replaced-item row of [issue #1666](https://github.com/jhaygood86/PeachPDF/issues/1666) (the 2.8pt line-box
strut), and the stretched width follows from that height through the 2:1 ratio. Closing this gap will not make
them 36 and 72; closing #1666 will.

Every item is placed as if the row were its content height, so `align-self` has no free space to work in and a
stretched item stays short. This was already the behaviour on `main` before the replaced grid item work, which
is why it was recorded rather than fixed there: it changes the used height of every `auto` row in a
fixed-height grid and wants its own sweep against Chrome.

Filed as [issue #1681](https://github.com/jhaygood86/PeachPDF/issues/1681).
