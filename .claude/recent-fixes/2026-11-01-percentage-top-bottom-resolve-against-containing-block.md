# Percentage top/bottom on a positioned box (#2002)

**Symptom:** `top: 10%` / `bottom: 10%` on an absolute box and `top: 10%` on a relative one were placed as if 0
(`calc(10% + 5px)` kept only the 5px).

**Cause:** two different wrong bases. Absolute placement read the positioned ancestor's `ActualHeight`, which is still the
content-driven value while its children are placed (0 for a fixed `height: 200px`, whose height is applied afterwards).
Relative placement resolved against the *box's own* `ActualWidth`/`ActualHeight`, so `left: 10%` was wrong too.

**Fix (`CssBox.cs`):** `AbsoluteContainingBlockHeight` reads the ancestor's declared height via `CssLayoutEngine.GetBoxHeight`
(minus its borders: the padding box) - the same thing `ResolvePositionedAutoBlockMargins` already did, which now calls it. Relative
offsets resolve left/right against `ContainingBlock.AvailableWidth` and top/bottom against `RelativeContainingBlockHeight`
(`ResolveDefiniteHeightValue` of the containing block, minus its borders and padding).

**Spec-silent case, checked in engine source:** a relative box's top/bottom percentage with a containing block of indefinite
height. Gecko (`ReflowInput::ComputeRelativeOffsets`), WebKit (`RenderBoxModelObject::relativePositionOffset`) and Blink
(`ComputeRelativeInsets`) all treat it as `auto`; then the usual near/far rule applies (an `auto` top with a set bottom moves the
box the other way). `ResolveNearFarOffset` now takes a nullable basis and does exactly that.

**Not done:** an absolute box whose positioned ancestor has `height: auto` still sees that ancestor's provisional height on the
first pass (CSS Sizing 3 makes it definite, `ApplyParentHeight` re-runs it later); only the declared-height case is covered by tests.

**Evidence:** `PercentageInsetLayoutTests` (13 cases; 10 fail without the change); engine 16958/16958, `PeachPDF.Tests` green.
