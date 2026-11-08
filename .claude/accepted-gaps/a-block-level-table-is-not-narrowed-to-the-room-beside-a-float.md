# A block-level table is not narrowed to the room beside a float

A table in normal flow is moved beside a float when its width fits there, and below the floats when it does not (`CssLayoutEngineTable.PlaceBesideFloats`, CSS 2.1 section 9.5). An auto-width table is not given a narrower width that would fit beside the float: its shrink-to-fit width is decided against the full containing block, so a table whose content could wrap into the room beside the float goes below it instead (CSS 2.1 section 10.3.3 would narrow it first). Tracking issue #2187.
