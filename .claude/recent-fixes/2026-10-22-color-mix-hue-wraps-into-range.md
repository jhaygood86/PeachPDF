# color-mix() in hsl/hwb resolved to black (#1769)

`ColorSpaceMath.InterpolateHue` returned `h1 + t * diff` unnormalised. A shorter-arc mix from 0 to 240 degrees gives -60, and
`HslToSrgb` selects its sector with `(int)(h / 60) % 6`, which is -1 for a negative hue, so no sector matched and the
colour came out black. lch/oklch were unaffected only because `PolarToRect` goes through sin/cos. The fix normalises the
interpolated hue into [0, 360) in `InterpolateHue`, the one place every polar mix and gradient passes through.

Found by writing the failing test first (`ColorMix_HslAndHwb_RedBlue_TakesTheShorterHueArc`) and reading the value
`MixComponents` returned; the earlier guess that the hsl/hwb scales disagreed (0-100 vs 0-1) was wrong, the conversions agree.
