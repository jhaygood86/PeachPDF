# A converter type's static field must not read `Converters` while `Converters` may still be initializing

`Converters` (a static class of `static readonly` fields) constructs value converters as it initializes,
some of which are types with static fields of their own that read `Converters.*` (for example
`GradientConverter.StopColorConverter = ColorConverter.WithCurrentColor()`). Which of the two types is
touched first decides whether the second sees a finished `Converters` or a half-initialized one (a field
not yet assigned reads as `null`). CoreCLR on Windows happened to touch `GradientConverter` first; Mono on
Android touched `Converters` first, so `ColorConverter` was still `null`, `WithCurrentColor()` built an
`OrValueConverter` with a null operand, and parsing any `linear-gradient(...)` threw a
`NullReferenceException` inside `OrValueConverter.Convert` - only on the emulator.

Rule: a static field in a converter type that reads another `Converters` field is built on first use
(`s_x ??= ...` in a property), never in the type's own static initializer. Symptom to recognize: a
`NullReferenceException` in a CSS converter's `Convert` that appears on one runtime and not another.
`GradientConverter.StopColorConverter` is the one known instance; others may exist and will show up the
same way.
