# A number interpolated into a test's CSS fixture must be formatted with InvariantCulture

_A trap that only fires on a developer machine, never on CI._

CSS numbers are culture-invariant by spec — `213.98pt` is the only spelling the parser accepts.
A test that builds a fixture with `$"width: {someDouble}pt"` formats that double with
`CurrentCulture`, so under any comma-decimal locale (nl-NL, de-DE, fr-FR, …) the declaration
reaches the parser as `width: 213,98193359375pt`, which is invalid CSS. Nothing throws: the
declaration is simply dropped, the property falls back to its initial value, and the test
compares against a document that isn't the one it meant to build.

CI's agents run invariant culture, so the test is green there and red only locally — the
report is always "works on CI, fails on my machine", which reads like a font/platform
difference and sends you into the layout engine instead of the fixture.

Two confirmed sightings:

- `CssLayoutEngineTableTests.TableLayout_AsymmetricWrappableHeaders_…_Issue1157` interpolated a
  computed `desiredWidth` into `table { width: … }`. The dropped declaration left the table at
  `width: auto`, so it laid out at exactly `maxSum` and tripped its own "table overflowed its
  specified width" assertion — a failure that looks like the very bug the test guards.
- `OutlineStylePaintIntegrationTests` (the `outline-style: auto` ring comparison) hit it first;
  there `outline-width`/`outline-offset` silently fell back to `medium`/`0`, which would have
  compared `auto` against the wrong ring and passed *green* for the wrong reason.

Only a **computed** value is at risk. A literal in the format string, or an integral `double`
from `[InlineData]`, formats identically in every culture — that is why most fixtures in the
suite are fine and why this doesn't show up in a grep for interpolations.

So: `value.ToString(CultureInfo.InvariantCulture)` before it goes into a fixture string. The
whole suite currently passes under nl-NL; keep it that way rather than relying on CI's locale.
