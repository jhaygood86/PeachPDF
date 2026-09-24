# A column-scoped page float records its column when it is laid out

`HtmlContainerInt.PageFloatColumns` says which column a `float-reference: column` page float was last laid out in. It is
written by `NotePageFloatColumn` from `FragmentainerContext.ColumnKey` at the moment the float is laid out - never
derived afterwards from the float's `Location`.

**Why.** A page float is moved to its resolved edge by `FloatBoxPageArea`, and a container that spans pages lays the
same box out in more than one slot, so `Location` after the fact names a place the float was moved to, not the place it
was anchored. Looking it up in `ColumnFragmentainers` (as a footnote call's anchor is) pinned a float anchored on the
second page to the first page's column - measured as a float at Y 43 on page one whose text was on page two.

**What must hold.**

- The key is recorded *before* the fragmentainer is detached for the float's unbroken layout
  (`CssBox.LayoutBlockChild`): a detached context has no column, and a measurement pass has none either, so both leave
  the last real answer alone rather than erase it.
- A float laid out in page-level flow forgets its column, since the page is then its reference.
- `LayoutOutOfFlowChildrenAgain` skips a float that resolved to one of the container's columns; laying it out again at
  the container's own width would move it out of the column.
