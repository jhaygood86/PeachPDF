# A stray `</p>` now generates an empty paragraph

PeachPDF used to ignore every end tag whose element was not open. The HTML Standard makes `</p>` the
exception: when no `<p>` is open it
[generates one](https://html.spec.whatwg.org/multipage/parsing.html#parsing-main-inbody) — "insert an
HTML element for a `p` start tag token with no attributes" — empty, and immediately closed.

```html
<div><p><table>…</table></p><p class="bad">…</p></div>
```

`<table>` closes the first paragraph itself, so the author's own `</p>` after it is stray. That `<div>`
used to have three element children (`p`, `table`, `p.bad`); it now has **four**, with an empty `<p>`
between the table and `p.bad`.

Nothing moves and nothing renders differently on its own — the generated paragraph is empty. What
changes is the **element tree your selectors match against**:

- **Sibling combinators shift.** `p + table + p` now matches the generated paragraph, not the next
  authored one. `p + p` can now match where it previously could not.
- **`:first-child`, `:last-child` and `:nth-child()` count it**, so positional selectors over a
  container holding malformed markup may select a different element.

Only documents with unmatched `</p>` tags are affected; well-formed markup parses exactly as before.
This is what every browser does, so a document that looked right in a browser and wrong in PeachPDF may
now agree.

`</br>` — which the spec turns into a `<br>` *start* tag — is still not implemented and is ignored like
any other unmatched end tag.
