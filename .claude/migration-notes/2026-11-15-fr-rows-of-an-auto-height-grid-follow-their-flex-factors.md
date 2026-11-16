# `fr` rows of a grid with no definite height are in the ratio of their flex factors

`grid-template-rows: 1fr 2fr` in a grid with an auto height used to give each row just its own content (equal content gave two equal rows). The rows now
share by the flex fraction the content asks for (CSS Grid 1 §12.7.1): the same content gives a 1:2 pair, and a row holding more than its ratio allows
sets the fraction for the others, so a grid with `1fr 2fr` rows holding 60pt and 30pt is 60pt and 120pt. Rows are never smaller than their content, and
grids with a definite height, `auto` rows and vertical grids are unchanged. An item spanning flexible rows no longer grows the last of them; it sets
the fraction for the rows it crosses.
