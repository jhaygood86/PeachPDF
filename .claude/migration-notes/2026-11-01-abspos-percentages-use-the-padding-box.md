# Percentage offsets and sizes on absolute boxes use the padding box

Before: on a positioned ancestor with padding and/or border, a percentage `left`/`right`/`width` on an
absolute child resolved against the ancestor's border box (placement) or content box (auto-size fill,
percentage width); percentage `height`/auto-height fill used the content or border box.

Now: all of them resolve against the ancestor's padding box, as CSS 2.1 §10.1 requires, so a
`left: 10%; right: 10%` child of a padded, bordered box ends exactly 10% of the padding box from each
edge. Documents using length insets, or ancestors without padding/border, render as before.
