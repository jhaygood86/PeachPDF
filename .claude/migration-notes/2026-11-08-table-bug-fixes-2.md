# Table fixes that change rendered output (second batch)

- The cells of a table in a `direction: rtl` context run from the right: the first cell of a row is the rightmost.
- A table narrower than its `min-width` is widened to it (and centered at that width when its margins are `auto`).
- A table inside `<center>` or an element with an `align` attribute no longer centers the text in its cells; a `text-align` from a style rule is still inherited.
- A block-level table that does not fit beside a float (left or right) is placed below it instead of on top of it.
- A float that fits no page under a repeated `<thead>` no longer makes pages that hold only the header, and a table with a repeated header inside a flex or grid container, an absolutely positioned box or a multi-column container finishes layout instead of running to the pass cap.
