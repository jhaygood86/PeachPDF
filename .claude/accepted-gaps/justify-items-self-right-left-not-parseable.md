# `justify-items`/`justify-self` cannot parse `right` or `left`

Tracking issue: [#605](https://github.com/jhaygood86/PeachPDF/issues/605).

Per [CSS Box Alignment Module Level 3 §5](https://www.w3.org/TR/css-align-3/#justify-items-property),
`justify-items`/`justify-self` accept `left`/`right` in addition to the `<self-position>` keyword set
they share with `align-items`/`align-self` — `left`/`right` only make sense on the inline (justify)
axis, so `align-items`/`align-self` don't accept them.

`CssLayoutEngineGrid.cs`'s `AlignmentOffset` already has real behavior for `right` (and would honor
`left` the same way `start` currently falls through), but the value can never reach it:
`Converters.JustifyItemsConverter`/`JustifySelfConverter` (`src/PeachPDF/CSS/Model/Converters.cs`)
alias `AlignItemsConverter`/`AlignSelfConverter` wholesale, and the align-axis converter has no
`left`/`right` case. A `justify-items: right` (or `left`) declaration is rejected by Layer A's own
CSS-OM parser before it ever reaches cascade dispatch — `css-properties.json`'s `justify-items`/
`justify-self` entries deliberately exclude both keywords from `supportedValues` to match this,
rather than claiming a support level real dispatch can't deliver (an earlier version of this
migration's JSON briefly claimed `right` was supported, based on the layout code alone, without
checking whether Layer A's parser could actually produce the value in the first place).

**Deliberately out of scope.** Fixing this means giving `justify-items`/`justify-self` their own
converter (or parameterizing the shared one by axis) that additionally accepts `left`/`right` — a
change to shared converter infrastructure `align-items`/`align-self` also depend on, not a JSON/docs
accuracy fix.
