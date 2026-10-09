# A shorthand slot must consume through its own `.For(...)`-wrapped converter

`TimeBasedShorthandConverter` (the shared converter behind `animation` and `transition`) decides which of two `<time>`
values is the duration and which the delay, so it has to peek at tokens with the bare `Converters.TimeConverter`. It
used to **also consume with that bare converter** and store the result in the slot. The bare value is not tagged with a
longhand name, so `ExtractFor(name)` - which is how a shorthand is split into longhands - hands it to every name.

Measured symptom (via `StylesheetParser.Default.Parse` on `#a { ... }`):

| Declaration | Longhands before the fix |
|---|---|
| `animation: fadeIn 10s` | name `initial`, duration `10s`, **delay `10s`**, everything else `initial` |
| `animation: fadeIn 10s 2s` | every longhand `initial` (the declaration was dropped) |
| `transition: opacity 300ms ease-in 1s` | every longhand `initial` |
| `animation: fadeIn` (no time) | correct |

So any time value in either shorthand silently turned the whole thing into its defaults. Nothing failed loudly: a
stylesheet using `animation: x 2s` simply had no animation, and the CSS-OM unit tests never exercised the shorthand.

**Rule:** a converter slot built with `.Option().For(PropertyNames.X)` is the only thing that knows which longhand its
tokens belong to. Peek with whatever you like, but **consume through `_converters[i].VaryStart(...)`** so the value that
lands in `options[i]` is the tagged one. `AnimationTransitionShorthandTests` covers the expansion; add a case there for
any new shorthand built on this converter.
