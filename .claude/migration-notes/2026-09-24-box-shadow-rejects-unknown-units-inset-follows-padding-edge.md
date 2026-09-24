# `box-shadow` rejects unknown units, accepts `calc()`; inset shadows follow the padding edge's corners

**Before** (the behaviour on `main` after the raster-backend change):

- `box-shadow: 2foo 2px red` was accepted as valid. The same held for `text-shadow` and the `blur()`/`drop-shadow()`
  filter functions.
- `box-shadow: calc(1px + 2px) 2px red` was rejected, so the declaration (and any `calc()`/`min()`/`max()`/`clamp()`
  in an offset, blur or spread) was dropped and no shadow painted.
- A rounded `inset` shadow was confined to the padding box using the element's full `border-radius`, so with a border
  its corners were rounder than the padding edge actually is.

**Now:**

- An unrecognised unit makes the whole `box-shadow` (or `text-shadow`, `blur()`, `drop-shadow()`) declaration invalid,
  as in browsers, so a stylesheet's earlier valid value wins over it in the cascade.
- `calc()`, `min()`, `max()` and `clamp()` work in any length slot of these values.
- A rounded `inset` shadow is confined to the padding edge, whose corner radius is `border-radius` minus the border
  width (zero when the border is at least as wide as the radius), and its lit inner edge follows that shape too. A
  rounded box with no border, or an outset shadow, is unchanged.
