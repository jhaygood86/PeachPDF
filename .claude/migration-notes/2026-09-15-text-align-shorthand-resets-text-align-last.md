# `text-align` now resets an element's own `text-align-last` to `auto`

`text-align` used to be an independent longhand: declaring `text-align` on an element never touched
that element's own `text-align-last`, so a `text-align-last` inherited from an ancestor kept applying
regardless of what `text-align` value a descendant declared for itself.

`text-align` is now a real shorthand over `text-align-all`/`text-align-last`
(css-text-3 §6.1). Declaring a plain value (`left`/`right`/`center`/`justify`/`start`/`end`) now also
**resets that same element's own `text-align-last` to `auto`** — matching the CSS specification,
though not matching any current browser engine (none of them implements this reset either; see the
"Notes" section of the tracking issue for why PeachPDF now follows the spec's letter here anyway).

## Before / after

```html
<div style="text-align-last: justify">
  <p style="text-align: center">…</p>
</div>
```

- **Before**: `p`'s last line was justified — the inherited `text-align-last: justify` from `div`
  was never reset by `p`'s own `text-align: center`.
- **After**: `p`'s last line is centred — `p`'s own `text-align: center` resets `p`'s own
  `text-align-last` to `auto`, which (per css-text-3 §6.3) defers to `text-align-all` under
  non-`justify` values, i.e. `center`.

An author relying on the old behavior can restore it by declaring `text-align-last` explicitly on
the same element as `text-align`, rather than relying on inheritance from an ancestor.

## Also new in this change

- `text-align-all` is now a real, independently settable longhand (css-text-3 §6.2).
- `justify-all` (sets `text-align-all`/`text-align-last` both to `justify`) and `match-parent`
  (resolves `start`/`end` against the *parent's* own `direction`, recursing through a chain of
  `match-parent` ancestors) are both recognized, on `text-align` and directly on
  `text-align-all`/`text-align-last`.
