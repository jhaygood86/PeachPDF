# A left float with a negative `margin-top` no longer hangs layout (#1512)

**Symptom.** Layout never returned for a `float: left` with a negative `margin-top` that did not fit
beside an earlier left float and had to drop below it (134pt + 144pt in 260pt).

**Cause.** `FloatBoxLeft` drops the float to `MaxBottom + margin-top`, i.e. it puts the *margin edge*
on the blocker's bottom. `Top` is the border-box top, so a negative margin leaves it above the
blocker's bottom, and `IsFloatIntersecting` (which compared the border top with `ActualBottom`) found
the same blocker again on every iteration: same `Top`, same drop, forever. `FloatBoxRight` never adds
the margin, which is why the right-float variant never hung.

**Fix.** `CssFloatCoordinates` carries the float's `MarginTop` and exposes `OuterTop`
(`Top + max(0, -MarginTop)`); the vertical-conflict test reads that, the margin-edge top CSS 2.1
§9.5.1 rule 5 talks about. A positive or zero margin gives `OuterTop == Top`, so every other float
is unchanged. Only `FloatBoxLeft` sets `MarginTop`; `FloatBoxRight` still compares its border top.

**Not done.** The first placement (before any collision) still ignores a negative `margin-top`
(the float starts at the current Y rather than that much higher), as before. `FloatBoxRight` was not
touched since it does not hang.

**Evidence.** `FloatLayoutRegressionTests` (three margin shapes fail by never finishing without the
fix, plus a control where the floats fit side by side).
