# Class, attribute and preference changes; transitions from any restyle

**Before.** A loaded `HtmlDocument` had no way to change a class or attribute, or the preferences media queries read. A transition started only from a `:hover`/`:active`/`:focus` change that moved nothing: a state rule that
changed `width`, `margin` or the like loaded the document again and the page jumped. `transition-property: margin` (a shorthand) was rejected as invalid, and a transition between a property's unset value (`margin-top`'s
initial bare `0`) and a length did not mix.

**Now.** `AddClassAsync`, `RemoveClassAsync`, `ToggleClassAsync`, `SetAttributeAsync`, `RemoveAttributeAsync`, `GetAttribute` and `SetPreferencesAsync` change a loaded document and style it again (it is loaded from its source
again with the change applied, and the change is kept for later loads). With `AnimationsEnabled`, every restyle that loads the document again starts the transitions the element's `transition-*` names, from the property's
old value to its new one, including layout properties when the host steps the document with `AdvanceAnimationsAsync`. A shorthand in `transition-property` covers its longhands, and a bare `0` mixes with a length or percentage.
