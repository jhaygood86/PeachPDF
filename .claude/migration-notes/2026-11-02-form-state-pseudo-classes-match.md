# `:checked`, `:disabled`, `:enabled`, `:required`, `:optional`, `:read-only` and `:read-write` match

**Before:** these parsed and never matched, in a PDF as on a screen. `input:checked + label { font-weight: bold }` or `input:disabled { opacity: .5 }` had no effect, and a rule such as `:read-only { ... }` selected nothing.

**Now:** they match the attributes the document has when it is styled, as the HTML standard defines them - a checked check box or radio button, a control with `disabled` (also through a disabled `fieldset` or an option's `optgroup`), a control with or without `required`, and a text-like field that is or is not editable. A document whose style sheet has such a rule now gets that rule's declarations, so a page written for a browser looks the way it does in one.

**Why:** a document author reading the engine's output expects the same selector to mean the same thing; the rule was only unmatched because there was no form state to read, and the attributes are state enough.

**Not yet:** a rule that reads these does not restyle when a host changes the control after load; the document keeps its load-time look until it is loaded again (see the accepted gap).
