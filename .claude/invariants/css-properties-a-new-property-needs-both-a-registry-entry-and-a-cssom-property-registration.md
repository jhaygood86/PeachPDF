A brand-new CSS property (one with no pre-existing entry anywhere) needs **two** independent
registrations before a declaration using it actually reaches `CssBox`, not just the one in
`css-properties.json` `CLAUDE.md`'s own "Adding a property" section documents:

1. `css-properties.json` + the source generator, which produces `CssPropertyRegistry`'s
   `Set_<Prop>`/`Validate_<Prop>`/`Get_<Prop>` — this is necessary, but not sufficient.
2. A CSS-OM `Property` subclass (`src/PeachPDF/CSS/StyleProperties/**`) registered via
   `PropertyFactory.AddLonghand`/`AddShorthand` (`src/PeachPDF/CSS/Factories/PropertyFactory.cs`),
   with an `IValueConverter` (`src/PeachPDF/CSS/Model/Converters.cs`) that at least validates the
   property's grammar.

Without (2), `PropertyFactory` doesn't recognize the property name at all, and the stylesheet/inline-
`style=""`-attribute parsing pipeline that builds `StyleDeclaration` silently drops the whole
declaration before `CssPropertyRegistry.TrySet` (or the generated `Set_<Prop>` it dispatches to) is
ever called — **for every value**, keyword or otherwise. This is easy to miss during manual testing:
a bare keyword value (e.g. `auto`) happens to equal the property's own initial/fallback value in a
naive resolver, so a keyword-only smoke test can pass even though the whole declaration was dropped
and nothing was actually applied. A non-keyword value (a length, a color, anything the fallback
doesn't already equal) fails loudly and is what actually surfaces the gap — write that test first,
not last, when adding a property that has both a keyword and a value form.

Measured symptom: adding `text-decoration-thickness` (`keyword-or-value` cssDataType, `auto | from-
font | <length-percentage>`) with only the `css-properties.json` entry compiled and even *appeared*
to work for `auto`/`from-font` (both resolved to the correct-looking output), but `3pt`/`10%` silently
resolved to the `auto` fallback in every case — the declaration was never reaching `CssBox` at all;
`auto`'s test happened to pass by coincidence, not because the property was applied.
