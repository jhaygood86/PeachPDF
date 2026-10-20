# Multi-column containers holding only text now columnize

**Before:** A container with `columns` or `column-count` whose content was only text, with no block-level child of its own, was laid out as one ordinary inline flow across the container's full width. The v0.9.22 support matrix documented this as a limitation. Text beside a float in such a container was not divided either: the float shortened its lines, and the columns were unaffected.

**Now:** Text placed directly in a multi-column container is divided into columns, like any other column content. Text beside a float is divided too, and the float sits in the column where it appears. Text under a vertical writing mode keeps its previous layout and stays in single-column flow. A multi-column scroll container (`overflow` other than `visible`, with a `height` or `max-height`) that holds only text keeps its previous layout: one inline flow across the full width.

**Why:** The parser wraps an inline-only run in an anonymous block when its parent is multi-column. That gives the columns engine the block-level shape it fragments; widening the dispatch instead drops words. A float joins the wrapped run, as it does in any block, so the inline flow places it beside the text.  A bounded scroll container is left out because the columns engine skips words when it fills columns of a bounded height.
