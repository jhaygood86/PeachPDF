# WinUI: the RangeValue pattern

`HtmlNodeAutomationPeer` implements `IRangeValueProvider` for the range roles (`HtmlViewLogic.IsRangeRole`), reading the node's `Value`, `ValueMin` and `ValueMax` from the engine through `CurrentNode`, so a value changed after load is read from the current tree and not the one the peer was made from.

- **Explicit interface implementation.** A text field's `IValueProvider` already has a public `Value` (a string) and `IsReadOnly`; `IRangeValueProvider` has members with the same names and other types, so the range ones are explicit. The compiler would otherwise have been satisfied by the wrong one for one of the two interfaces.
- **Steps.** UI Automation wants a small and a large change; the document has no `step`, so they are a hundredth and a tenth of the extent (`RangeSteps`). A `step` attribute would be the better source and is not read.
- **Only a slider moves.** A progress bar or meter is read-only (`IsReadOnly` true and `SetValue` does nothing); a slider goes through the same `SetFieldValue` the Value pattern uses, which sets the range input and keeps it within `min` and `max`.
- **Checked in a window:** a progress bar and a range input read (value, least, most, read-only, the steps), then set (the progress bar unchanged, the slider moved).
