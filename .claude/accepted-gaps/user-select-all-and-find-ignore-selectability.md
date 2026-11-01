# `user-select: all` is read as `text`, and Find searches text a reader cannot select

`user-select: none` keeps a selection from starting on the text (hit tests return `null`) and removes it
from `HtmlDocument.GetText`, but two parts of the property are not implemented:

- `all` should make a click select the whole element as one unit. Selection here is driven by the host
  through `Selection`/`HitTest`, so the element-wide behavior would need an element-level selection
  range; `all` is read as `text`.
- `FindText` matches text whatever its `user-select`, and a selection dragged across `none` text from
  outside still highlights the rectangles of that text (only the characters returned by `GetText` are
  filtered).

Both would need the selection model to know about element boundaries, which it does not today.
