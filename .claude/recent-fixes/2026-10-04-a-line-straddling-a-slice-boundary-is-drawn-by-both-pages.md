# A line straddling a slice boundary is drawn by both pages

_css-break-3 §4.4 (slicing monolithic content). Tracker: #1328._

A capped scroll container (or other monolith) taller than a page is laid out whole and its graphical
representation sliced across pages. A line that crossed a slice boundary was lost: `FragmentEmitter.ClaimsLine`
gave it to the page its top is on, and that page's clip showed only the upper part, while the next page never
claimed it. Through the CLI, with `overflow:auto` and a height cap over a page, padding 12pt lost L27 and L54
and padding 41pt lost L26 and L53.

- **Fix.** `ClaimsLine` takes an optional `slicesStraddlingLines` callback. When the line's rectangle runs on
  from its nominal page's band by more than `PageBoundaryEpsilon` and the box sits in sliced content
  (`IsInSlicedContent`: a monolith, inline-block or inline-table at or above it), the later page claims it
  too, and each page's clip shows its own part. Ordinary flow is untouched, since layout moves every straddling
  line there to the next page; snapshot and capture passes do not take the callback.
- **Evidence.** Pixel inspection showed the line cut exactly at the page clip. `SlicedMonolithLinesTests`
  asserts every line's visible fractions sum to a whole line, and fails without the change.
- **Not done.** The #1439 one-line fix (read the inline-block's own `vertical-align` correctly) is separate.
  It was blocked on this loss and is re-measured on its own.
- Plain text extraction counts clipped text as present; check word boxes against the content band, not text.
