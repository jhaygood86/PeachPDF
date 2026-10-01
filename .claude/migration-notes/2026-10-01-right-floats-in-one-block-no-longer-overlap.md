# Two `float: right` boxes in one block no longer overlap

**Before:** a document with several `float: right` siblings and no inline content between them (for example a row of
right-floated buttons or badges directly inside a `div`) drew every one at the container's right edge, on top of each
other. If they did not fit side by side the second still stayed at the first's height.

**Now:** as in a browser, a later right float sits to the left of the earlier one at the same height when it fits, and
drops below the earlier float's margin box when it does not (its own `margin-top` applies after the drop).

Check any layout that used right floats as a workaround for the old placement (negative margins, explicit offsets): it
will now be placed further left than before.
