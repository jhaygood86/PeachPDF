# `hyphenate-limit-last` now actually affects layout

Previously: `hyphenate-limit-last` (`none | always | column | page | spread`) parsed, cascaded, and
inherited correctly, but nothing in layout consulted it — every value behaved identically to the
initial `none`, so a hyphen could always end the last line before a column/page break even under
`always`/`column`/`page`/`spread`.

Now: when the resolved value forbids it, a hyphenated split that would otherwise end the last line
before a column, page, or spread break is undone, and the whole original word moves into the
fragmentainer the break resumes in instead — unless the word doesn't fit un-hyphenated on a fresh,
full-width line either, in which case the hyphen is kept rather than deferred with no real benefit.
`spread` is treated the same as `page`, since this engine has no facing-page (two-page) layout concept
to distinguish them. Any existing document that both hyphenates and declares `hyphenate-limit-last` as
something other than `none` will now see fewer (or, under `always`, no) hyphens immediately before a
page or column break, with that content moving to the following page/column whole instead — tracked as
[#723](https://github.com/jhaygood86/PeachPDF/issues/723).
