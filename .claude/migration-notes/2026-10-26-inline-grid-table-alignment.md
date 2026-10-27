# Atomic inline-flex/grid/table boxes honour `vertical-align` and `text-align`

Before: a text-less `inline-grid`/`inline-table` with `vertical-align: top|middle|bottom` moved its background but left its
items behind; under `text-align: center|right` an `inline-flex`/`inline-grid`/`inline-table` box alone on a line did not
move, and one after text could overshoot the line end.

Now: the box and its content move together, and it is aligned at its margin-box width. Under `justify` the box is neither
moved nor resized, and words are not stretched into it.
