# A paginated table with row spans that cannot advance now finishes

A document whose table (with a repeating `thead`, `rowspan` cells and a float that fits no page) could not place a cell on any page used to run for minutes without producing a PDF. It now finishes in about a second, with the stalled passes as empty pages (header only) followed by the content laid out in one piece, the way the same document without the `rowspan` attributes already did. Documents that paginated normally are unchanged.
