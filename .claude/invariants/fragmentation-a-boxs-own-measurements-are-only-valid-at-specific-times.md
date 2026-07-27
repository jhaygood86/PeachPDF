# A box's own measurements are only valid at specific times

_CSS Fragmentation Level 3. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

Only a box that *positions itself* owns its `ActualBottom` (`CssBox.PlacesItselfAsBlockBox`) — anything else holds its previous sibling's. And a box carrying a **pending record** has not finished at all, so its `ActualBottom` is still its own top: a fit test asked about one silently answers "always fits". A word after a break still holds the measurement pass's position, so the **line boxes the pass kept** are the honest source.
