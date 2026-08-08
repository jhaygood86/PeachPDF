# `@page` `em`/`rem` margins and `em`-based `text-indent`/`word-spacing`/`letter-spacing` under a non-default `PixelsPerInch`

Previously, under a non-default `PdfGenerateConfig.PixelsPerInch` (anything other than the default 72),
or when `ShrinkToFit`/`ScaleToPageSize` moved the effective pixels-per-point away from 1.0 for otherwise
ordinary content, two kinds of `em`/`rem`-relative lengths resolved to the wrong absolute size:

- A base `@page { margin: ... }` (or `margin-top`/`margin-left`/etc.) declared in `em` or `rem` resolved
  against the root font-size divided by pixels-per-point *twice*, instead of once - the produced margin
  was too small by a factor of `PixelsPerInch / 72` squared. This affected the `em` basis only when no
  `@page { font-size }` override was declared on the base rule; the `rem` basis was affected
  unconditionally.
- `text-indent`, `word-spacing`, and `letter-spacing` declared in `em` resolved against the declaring
  element's font-size with the same double-scaling, also producing a value too small by that same
  squared factor.

Both are now correct: an `em`/`rem` `@page` margin, and an `em`-valued `text-indent`/`word-spacing`/
`letter-spacing`, resolve to the same true-CSS-points value regardless of `PixelsPerInch` or
`ShrinkToFit`/`ScaleToPageSize`, matching their behavior at the (unaffected) default `PixelsPerInch` of
72. Documents that only ever used the default `PixelsPerInch` with `ShrinkToFit`/`ScaleToPageSize`
disabled saw no change.
