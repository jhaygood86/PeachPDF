# `background-position` accepts a vertical-first three- or four-value position

`background-position: top 10px left` (and `bottom left 20px`, `top 10px left 20px`), alone or in the `background` shorthand, dropped the whole declaration. CSS Backgrounds 3 §3.6 takes the horizontal and vertical groups in either order, so these are now accepted and place the image from the named edges. A document that had one of them silently lost its `background-position` (the image sat at `0% 0%`) and now sees it positioned.
