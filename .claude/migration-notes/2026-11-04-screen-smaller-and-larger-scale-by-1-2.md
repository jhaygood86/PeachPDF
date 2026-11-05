# `font-size: smaller` and `larger` scale by 1.2 on screen

**Before.** Both stepped the parent's size by 2pt on every medium, which on a screen document (12pt by default) is a 17% step and not the browsers' ratio.

**Now.** On the screen medium `larger` is the parent times 1.2 and `smaller` the parent divided by 1.2, as in browsers. A paged document keeps the 2pt step.

Not verified against `v0.9.26-preview1` (the screen medium's defaults arrived after it).
