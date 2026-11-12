# The `type` attribute on ol, ul and li now sets the marker style

`<ol type="a|A|i|I|1">` used to number every item `1.` and `<ul type="square|circle">` drew a disc. The attribute now maps to `list-style-type`
as a presentational hint, as in a browser: `<ol type="i" start="3">` shows `iii.`. `type` on `<li>` overrides its list for that item. Any author
`list-style-type` (stylesheet or `style`) still wins. Documents that relied on the attribute being ignored will now show the requested style.
