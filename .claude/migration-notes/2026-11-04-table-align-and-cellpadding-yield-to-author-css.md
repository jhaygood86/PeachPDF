# `<table align>` places the table, and `cellpadding`/`border` yield to a cell's own CSS

In an HTML document:

- `<table align="center">` now centers the table (auto side margins), and `align="left"`/`"right"` float it, where it set `text-align` on the table and left the table at the start edge. The text of its cells is no longer centered by it (cells inherited the `text-align`). A table that is floated now takes its place in the flow of the content around it, so a following table in normal flow is laid over it (as it already was for a CSS `float`). The value must match exactly, ASCII case-insensitively: `align=" center "` does nothing, as in a browser.
- A `padding` or `border` declared on a cell (its `style` attribute, or any stylesheet rule, `!important` or not) now beats the table's `cellpadding` / `border`. `<table cellpadding="0">` with `<td style="padding: 30px 25px">` kept 0 before and keeps 30px 25px now. A longhand wins only for its own side (`padding-left` leaves the other three at the `cellpadding`).
