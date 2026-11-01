# A positioned box is no longer clipped through an opacity/blend-mode wrapper

Before: an absolutely positioned box inside a non-positioned `opacity`, `mix-blend-mode` (or `z-index`
flex/grid item) wrapper inside an `overflow: hidden` box was clipped to that box, although its containing
block is outside it.

Now: it is drawn unclipped, as in browsers. Content whose containing-block chain does pass through the
`overflow: hidden` box is clipped as before.
