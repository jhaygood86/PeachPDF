# The footnote counter is now a real, author-controllable counter

**Before:** footnote numbering was a bare per-page loop that wrote the number straight into the
call's and marker's text. An author's `counter-reset`/`counter-increment: footnote` was inert, and
`content: counter(footnote)` on `::footnote-call`/`::footnote-marker` resolved to the document
counter (effectively always `1`), not the footnote's real number.

**Now:** the number is published through the cascade. `@page { counter-reset: footnote }` and
`@footnote { counter-increment: footnote }` are the user-agent defaults, so a document that declares
nothing numbers exactly as it did - restarting at 1 on every page. Declaring `counter-reset` yourself
on an applicable `@page` replaces that default, and `counter-increment` on `@footnote` sets the step.
`content: counter(footnote)` and `counters(footnote, <sep>)` now resolve to the live,
pagination-resolved number, with the counter-style argument honoured
(`counter(footnote, lower-roman)` gives `i`, `ii`, `iii`).

**The one real regression risk for an existing document.** `counter-reset` is a single property, so
an author declaration replaces the UA one wholesale - including one that never mentions `footnote`.
A document that already wrote, say, `@page { counter-reset: chapter 1 }` and also uses footnotes
therefore **loses the per-page footnote reset and gets continuous numbering across the document**.
Adding `footnote` to the same declaration restores the old behaviour:

```css
@page { counter-reset: chapter 1 footnote; }
```

This is the correct cascade reading rather than a special case, and it is what makes continuous
numbering - previously impossible, and the most-asked-for thing here - reachable at all. Nothing in
this repository's own tests, docs or showcases was affected.

**Two defects fixed on the way**, both reachable before this change and both reproduced by a failing
test first:

- A `::footnote-call`/`::footnote-marker` whose `content` resolved to an image or to `none` left the
  box with no text, and the per-pass re-parse dereferenced it - a `NullReferenceException` for any
  document styling either pseudo-element that way.
- A footnote number that changed *width* (`"9"` to `"10"`) indexed past the end of the box's bidi
  level array and threw `IndexOutOfRangeException`. Reachable before this change on any page carrying
  ten or more footnotes; continuous numbering reaches it immediately.

**Separately:** `counters(<name>, <separator> [, <style>])` now works in the `content` property, not
only in `string-set`. It was previously rejected outright there: the CSS-OM grammar nested the
`counters()` alternative *inside* `counter()`'s own argument converter, so a top-level `counters()`
never validated and the whole declaration was dropped. The `string-set` implementation that did work
silently ignored its counter-style argument; both now go through one shared implementation that
reads it, so `counters(item, '.', upper-roman)` renders `II` rather than `2`.
