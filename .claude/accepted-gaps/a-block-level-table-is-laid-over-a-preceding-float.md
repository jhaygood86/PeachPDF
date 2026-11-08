# A block-level table that does not fit beside a preceding float is not moved below it

A table in normal flow after a float is moved clear of a float on its left when it fits beside it (`CssLayoutEngineTable.PlaceBesideFloats`, CSS 2.1 section 9.5). One too wide for the room beside the float stays at the container's edge over the float, where Chrome moves it below the float; a float on the right is not avoided either, and the table's width is not narrowed to the room beside the float (tracking issue #2187).
