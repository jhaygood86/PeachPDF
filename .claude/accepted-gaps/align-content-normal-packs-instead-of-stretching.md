# `align-content: normal` packs the lines instead of stretching them

_Tracked as [#461](https://github.com/jhaygood86/PeachPDF/issues/461). Confirmed while reviewing
[#458](https://github.com/jhaygood86/PeachPDF/issues/458)._

`align-content`'s initial value is `normal`, which behaves as `stretch` on a flex container
([css-align-3 §5.1](https://www.w3.org/TR/css-align-3/#propdef-align-content),
[css-flexbox-1 §8.4](https://www.w3.org/TR/css-flexbox-1/#align-content-property)): the lines grow
equally to absorb the free cross space. `CssLayoutEngineFlex.DistributeCrossSpace` has a `stretch`
arm that does exactly that, but `normal` reaches the `default:` arm instead and packs at cross-start.
Since `CssBoxProperties.AlignContent` defaults to `"normal"`, this is the **default** rendering of
every multi-line flex container with free cross space, not an opt-in path.

Measured on a `flex-direction: column; flex-wrap: wrap-reverse; width: 200pt; height: 100pt` container
with lines of cross size 20 / 50 / 30: unset gives `x` 180 / 130 / 100 with 100pt of the container
unused; `align-content: stretch` spelt out gives 146.67 / 63.33 / 0 with each line 33.33pt wider.

**Why it is not taken here.** The one-line version — adding `"normal"` to the `stretch` arm — moves the
default rendering of every such container, so the showcases and the existing `align-content` fixtures
have to be re-read rather than re-baselined. It also sits next to
[§9.4 step 8](https://www.w3.org/TR/css-flexbox-1/#algo-cross-line), which this engine applies only
when `flex-wrap: nowrap`, so a *single*-line `wrap`/`wrap-reverse` container leaves its line at the
content's cross size rather than the container's; the two are better taken together.

**Consequence for tests.** A fixture that means "packed at cross-start" must say
`align-content: flex-start` rather than leaving the property unset, or it silently pins this deviation
and will need rewriting when #461 closes. `WrapReverse_Column_UnequalLineCrossSizes_StackRightToLeft…`
in `FlexboxIntegrationTests` states it for that reason.
