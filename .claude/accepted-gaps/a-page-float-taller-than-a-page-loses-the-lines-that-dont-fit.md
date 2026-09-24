# A page float taller than a page loses the lines that don't fit

_CSS Page Floats; CSS Fragmentation Level 3 §4.4. Tracker: [#1332](https://github.com/jhaygood86/PeachPDF/issues/1332)._

A page float (`float: top/bottom/top-bottom/snap`, `CssBox.IsPageFloated`) taller than the page's content
band keeps only the lines that fit on the page it is placed on. The rest are placed on no page.

Measured while working on #1321, with a 200pt page, 20pt margins and 20pt lines: a 12-line `float: bottom`
(240pt against a 160pt band) placed lines only up to `F5`. The same happened with the `overflow: hidden`
block on the float itself or inside a plain page-float wrapper, and on `main` before #1321's change.

#1321 keeps page floats and their descendants monolithic (`MonolithicContent.IsFloat`), so it neither
causes nor fixes this. The loss goes through the page-float placement path (`ResolvePageFloatsForThisAttempt`),
not the ordinary float path of
[the float-pagination gap](a-floats-own-content-taller-than-one-page-overflows.md), though both come down to
a float's content being unable to continue onto the next page.
