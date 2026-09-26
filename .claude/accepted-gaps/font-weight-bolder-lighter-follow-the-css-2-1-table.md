# `bolder`/`lighter` follow the CSS 2.1 table, not the CSS Fonts 4 one

`FontWeightResolver` steps `font-weight: bolder`/`lighter` from the parent's weight by CSS 2.1 section 15.6's table (bolder: 400 below 400, 700
up to 500, else 900; lighter: 100 up to 500, 400 up to 700, else 700). CSS Fonts 4 section 2.2 puts the breakpoints at 100, 350, 550 and 750
(and leaves a weight under 100 unchanged, and `bolder` at 900). The tables agree at the multiples of 100 that CSS 2.1 tabulates and differ
between them: a weight of 380 or 520 with `bolder`, 520 or 720 with `lighter`. Fractional weights (`font-weight: 520.5`) reach that range as
easily as whole ones. Tracked in [#1446](https://github.com/jhaygood86/PeachPDF/issues/1446).

It was left alone when `font-weight` learned fractions because it is a separate rule with tests pinning the CSS 2.1 values
(`FontWeightResolverTests`, `FontWeightResolutionIntegrationTests`); the fractional tests only assert weights where both tables agree, so
fixing it does not disturb them. Fixing it is replacing the two switch expressions with the Fonts 4 table.
