# New transform, animation and transition features

**Before.** `translate`, `rotate` and `scale` were invalid and ignored; `animation-composition` and `transition-behavior` were unknown; `@starting-style` was dropped as an unknown at-rule;
`-webkit-animation`, `-webkit-transition` and `@-webkit-keyframes` were ignored; an animated `color` did not reach the text inside the box; `visibility: hidden` on a state rule reloaded
the document; `@property` animations never ran live; a transition of `margin` (a shorthand in `transition-property`) was invalid.

**Now.** `translate`, `rotate` and `scale` paint (composed in front of `transform`, in that order) and animate; `animation-composition: add | accumulate` combines an animation with what is underneath;
`transition-behavior: allow-discrete` lets `display` and other stepped properties transition; `@starting-style` gives a transition its first style; the `-webkit-` spellings work; an animated
`color` reaches the text; `visibility` is restyled in place; a registered custom property animates on a screen; a shorthand in `transition-property` covers its longhands; and an interrupted
transition reverses over the part already travelled instead of starting over. A document that sets any of the new properties on a page that did not use them looks the same as before.
