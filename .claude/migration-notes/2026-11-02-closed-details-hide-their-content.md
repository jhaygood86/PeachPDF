# A closed `<details>` hides its content

**Before:** `<details>` rendered as a plain block and its `open` attribute changed nothing: the content was always visible, in a PDF as on a screen.

**Now:** a `<details>` without `open` shows only its `<summary>`; everything else in it (any child that is not a summary) is `display: none` until the element is opened. With `open` the whole element is shown, as before. A page written for a browser that hid a section until a reader opened it now hides it in the PDF too.

**Why:** the HTML standard says so, and a screen reader's `expanded`/`collapsed` state was already read from `open`, so the page said "collapsed" over content that was drawn open.

**To get the old result:** add `open` to the element, or `details > :not(summary) { display: block }` to the style sheet. A second `<summary>` child, which is invalid, is still shown. Text written directly inside a `<details>` (not in an element) is not hidden either.
