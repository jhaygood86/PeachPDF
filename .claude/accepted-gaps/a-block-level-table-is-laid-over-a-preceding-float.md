# A block-level table is laid over a preceding float instead of beside it

A table in normal flow after a float (`align=left|right` or CSS `float`) is placed at the container's edge on top of the float; Chrome places it beside the float when it fits and below it when not. Text, `div` and `p` siblings wrap correctly; only a table box does not avoid floats (tracking issue #2187). Seen with `<table align=left width=200>` followed by `<table width=300>`.
