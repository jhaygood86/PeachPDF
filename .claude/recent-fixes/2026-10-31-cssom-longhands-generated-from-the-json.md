# CSS-OM longhand classes are generated from css-properties.json

A longhand used to be declared twice: its `css-properties.json` entry (render layer) and a hand-written class under
`CSS/StyleProperties/` (CSS-OM). They restated inheritance and the initial value and had drifted (21 inherited flags,
`text-underline-offset`, `min-height`; see the migration note of the same date).

**The idea.** An optional `cssom` block on a JSON entry (`converter`, flags) makes `CssomPropertiesEmitter` write the
class and `PropertyFactory.RegisterGeneratedLonghands`. Inheritance comes from the entry's own `inherited`.

**Found by running it, not reading it.** The typed argument of `OrDefault(Length.Zero)` / `OrDefault(Color.Black)` is
thrown away: `IdentifierValueConverter<T>.IdentifierValue` keeps only the keyword text. So the CSS-OM never had a
typed initial value, and nothing needed deriving from `initialValue` - the generated class uses plain `OrDefault()`.
(Several of those arguments were wrong against the spec - `Color.Transparent` for `currentcolor`, `Length.Zero` for
`perspective: none` - which is exactly why they were dead data nobody noticed.)

**Traps.**
- The `inherited` flag in JSON is not enough on its own: the render cascade inherits by adopting whole areas
  (`CssBox.InheritStyle`), so an inherited property in `TextDecorationArea` silently does not inherit. `text-underline-offset`
  moved to `TextArea`.
- `PropertyFactory` keeps two animatable notions (the class flag and the factory list) that disagree for some
  properties (`border-*-color`); both are kept (`cssom.animatable`, `cssom.registryAnimatable`) because collapsing them
  is a behaviour change, pinned by `CssomLonghandBaselineTests`.
- 92 classes stay hand-written because they expose a grammar member the JSON's `cssom-grammar` validator calls, or build
  their converter in a way that is not `<expr>.OrDefault(...)`. Migrating them needs those members hoisted first.

**Evidence.** `CssomLonghandBaselineTests` (snapshot of flags and keyword/var/value behaviour of every longhand, captured
before the migration; the only diff is the 21 intended inherited flips), `CssomInheritanceDriftTests`, engine suite,
`PeachPDF.Tests` and generator tests green.
