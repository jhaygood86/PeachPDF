# Table columns are not mirrored in a `direction: rtl` container

The table box is placed correctly in an `rtl` container (end-aligned, overflowing to the left when too wide), but its cells are laid out left to right: the first cell is at the left where a browser puts it at the right. The table engine has no rtl column axis (its own comments say `startX` always runs forward) (tracking issue #2196).
