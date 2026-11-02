# `accent-color` colours the mark of a checked check box or radio button

Before: `accent-color` was ignored; the check mark and radio dot used the control's text colour.

Now: the mark is drawn in `accent-color` when it is not `auto`, in the painted page and in the PDF form
widget appearances. Documents that do not set it are unchanged.
