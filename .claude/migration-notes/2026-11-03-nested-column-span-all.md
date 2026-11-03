# `column-span: all` on a nested descendant now spans

A `column-span: all` box that was a grandchild (or deeper) of a multi-column container, reached only through plain blocks,
used to lay out as an ordinary in-column box. It now spans the container's full width and splits the content around it
into independently-balanced column runs.
