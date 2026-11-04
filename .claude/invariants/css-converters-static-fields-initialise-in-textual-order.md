# `Converters.cs`: a converter built from another must be declared below it

`PeachBrackets.Css.Converters` is a static class of `static readonly IValueConverter` fields, and static field initialisers run in
textual order. A field that composes another (`ColorConverter.OrAuto()`, `PureColorConverter.Or(...)`) reads **null** if the field it uses is
declared further down, and nothing fails at that moment: the `OrValueConverter` just holds a null primary. The `NullReferenceException`
surfaces later, inside `OrValueConverter.Convert`, only when something first validates a value for that property - which is how it looked:
`ComputedStyleTests.CascadeDefaultingLoop_OnAFreshBox_OnlyKnownExceptionsAreNotNoOps` crashed in
`CssPropertyRegistry.Validate_<Property>` while the build and the property's own tests were green.

`ColorConverter` is defined late in the file (after the transform and filter converters), so every new property whose grammar builds on it
belongs after it, not beside its siblings. Check with a test that sets the property to a real value through `CssUtils.SetPropertyValue`.
