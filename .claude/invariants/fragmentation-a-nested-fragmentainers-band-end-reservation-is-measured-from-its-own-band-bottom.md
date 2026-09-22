# A nested fragmentainer's band-end reservation is measured from its own band bottom, not the page's

_CSS Fragmentation Level 3 / css-multicol-1. Tracker: [#756](https://github.com/jhaygood86/PeachPDF/issues/756)._

`FragmentainerContext.ReserveBandEnd` insets from **that context's** `BandBottom`. For a page context
that is the page's content-band bottom; for a column context it is `boxTop + target`, the column's
own bottom. So a reservation that belongs to the *page* must never be seeded onto a column context:
under `column-fill: balance` a column typically ends well above the page bottom, and the inset would
stop content that far above a balanced column bottom which already sits far above the thing being
reserved for. The symptom is content pulled up for no visible reason, on a page whose footnote area
is nowhere near the columns.

The lever for a page-level reservation inside a multi-column container is
`CssLayoutEngineColumns`'s `pageBudget` instead - it is only ever used as a ceiling (`Math.Min`, the
`target >= pageBudget` stop, `EstimateBalancedColumnHeight`'s cap), so subtracting from it is exactly
"this page has less room than it looks" and is a no-op wherever the reservation is zero.

`ReserveBandEnd` on a column context is therefore reserved for a genuinely **column-scoped** area,
whose band bottom really is where it sits. **The two must never both be applied for the same amount** -
they compose, and silently double-count it.

Measured symptom of the bug this closed: with a two-column `column-fill: auto` container on a page
carrying a footnote, column content ran to 369.6 where the reserved strip began at 367 - i.e. it
overlapped the note area rather than stopping above it.
