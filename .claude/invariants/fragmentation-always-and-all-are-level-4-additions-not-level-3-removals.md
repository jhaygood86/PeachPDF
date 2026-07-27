# `always` and `all` are Level 4 additions, not Level 3 removals

_CSS Fragmentation. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

`break-before: always` used to force a page break in PeachPDF and no longer does
([#299](https://github.com/jhaygood86/PeachPDF/issues/299)). The reason is **not** that
css-break-3 dropped the value: `always` and `all` were never in
[Level 3](https://www.w3.org/TR/css-break-3/#break-between)'s `<break-between>` production at all —
they are [Level 4](https://www.w3.org/TR/css-break-4/) *additions*. Rejecting them is PeachPDF
implementing the Level 3 grammar, not tracking a removal.

#299's own issue text has this backwards, and the first implementation pass propagated the mistake
into the docs and the tests before review caught it. Anyone re-reading that issue will be misled the
same way.

Forward-porting the Level 4 values was considered and **declined**
([#328](https://github.com/jhaygood86/PeachPDF/issues/328), closed not-planned): Level 4 is a
Working Draft, and this repo does not implement draft-only values ahead of the spec settling. The
legacy `page-break-*: always` alias is unaffected and still normalizes to `page`.

**The general rule this produced**, and the reason it is worth keeping: a spec-correctness fix that
changes how a *non-compliant* document renders is not a regression. It is stated for readers in the
docs' own [Forward compatibility](https://github.com/jhaygood86/PeachPDF/blob/main/docs/html-css-support.md#forward-compatibility)
section, which is where any future change of this shape should point, alongside a migration note in
the affected doc section.
