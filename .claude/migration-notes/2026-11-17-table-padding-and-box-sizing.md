# Table padding and box-sizing

**Before / now**, each a change a document author can see:

- **A `<table>` element's own `padding`** used to be ignored and treated as zero in the default
  (`border-collapse: separate`) model. It is now honored: it grows the table's own border box on
  every side, like any other box's padding. (`border-collapse: collapse` still ignores it, per
  CSS 2.1 §17.6.2 - that part is unchanged.)
- **A real `<table>` element's `box-sizing`** is now `border-box` by default (the HTML Standard's
  own rendering rule), so a `width`/`height`/`max-width`/`max-height`/`min-width` set on a
  `<table>` already covers its own padding and border, as it effectively always did while padding
  was ignored. A `display: table` element with no author `box-sizing` stays `content-box` (the CSS
  Sizing 3 default for every other box), so its `width` and friends no longer include its own
  padding/border - they used to, because `box-sizing` had no table-specific default before this.

See [Tables](../../docs/html-css-support.md#tables).
