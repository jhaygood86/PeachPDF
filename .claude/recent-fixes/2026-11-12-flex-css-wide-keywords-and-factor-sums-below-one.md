# Flex: CSS-wide keywords on the shorthand, and factor sums below one

**#2331.** `FlexConverter` (`CSS/Model/Converters.cs`) tried `WithOrder(grow, shrink, basis).OrGlobalValue()`. The three-part
order ran first, so `initial` was consumed as `flex-grow`'s value and the other two parts took their omitted-value fallbacks
(shrink 1, basis 0). The keyword is now tried before the order, as its own converter, so it reaches all three longhands.
Browsers also keep `0%` as the omitted basis for `flex: <number>` (the spec says 0); that fallback is untouched.

**#2356.** Spec 9.7 step 4c scales the free space by the sum of the unfrozen items' *raw* flex factors when it is below 1, on
the shrink side too (the base-size-scaled shrink factor is only the distribution weight). `CssLayoutEngineFlex.ScaleByFactorSum`
is applied in the plain grow and shrink paths of `DistributeFlex` and in the freezing loops (`GrowAgainstAutoMinimums`,
`ShrinkAgainstAutoMinimums`), against the free space captured on the loop's first pass (Chromium `line_flexer.cc`, Gecko
`ResolveFlexibleLengths` do the same).

**Evidence.** `FlexFactorTests` (190pt grow case, a two-item sum of exactly 1, a shrink case, `flex: initial` against
`flex: 0 1 auto`) and `FlexPropertyTests` (all four keywords on all three longhands) fail without the converter change (6 of
11) and the grow/shrink tests fail without the engine change. Engine suite 19633/19633, PeachPDF.Tests 1893 passed with 9
skipped; `PeachBrackets` rebuilds with zero warnings.

**Not done.** The flex base size, not the hypothetical size, is what the spec uses for the initial free space; the engine uses
the hypothetical size throughout, and that was left alone.
