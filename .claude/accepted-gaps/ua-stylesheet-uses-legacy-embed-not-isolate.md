# UA stylesheet uses legacy `embed`/`bidi-override` instead of `isolate`/`isolate-override`

Tracking issue: [#554](https://github.com/jhaygood86/PeachPDF/issues/554).

The HTML Standard's Rendering section ("Bidirectional text") specifies `unicode-bidi: isolate` for
the `[dir=ltr]`/`[dir=rtl]`/`[dir=auto]` UA-stylesheet rules and `isolate-override` for `bdo[dir]` —
real browsers adopted this years ago, replacing the older CSS 2.1 sample style sheet's
`embed`/`bidi-override`. `CssDefaults.cs` still uses `embed`/`bidi-override` for these rules (it does
correctly use `isolate` for `<bdi>` already — a partial adoption of the newer sheet).

Now that `unicode-bidi` actually drives real UAX #9 resolution, the difference is observable:
`isolate` makes a `dir`-bearing element opaque to the surrounding paragraph's bidi resolution (treated
like a neutral placeholder for N-rule purposes), while `embed` lets its levels leak into adjacent
weak/neutral resolution. `<p>1 <span dir="rtl">עברית</span> 2</p>` resolves the digits and neutral
characters around the span differently under each.

**Deliberately out of scope for now.** A one-line UA-stylesheet change (`embed`→`isolate`,
`bidi-override`→`isolate-override` in `CssDefaults.cs`), but left for a follow-up so it can be
verified against the existing `direction`/`unicode-bidi` test suite rather than folded into an
already-large change.
