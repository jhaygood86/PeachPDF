# Misnested formatting, paragraphs and end tags follow the HTML parsing algorithm

Before: an end tag closed everything up to the nearest open element of its name, a start tag closed an open `<p>`/`<li>`/`<dd>`/`<dt>` only when it was
the innermost open element, and nothing remembered a formatting element once its container had closed.

Now: the tree matches what a browser builds.

- `<b>1<p>2</b>3</p>` is `<b>1</b><p><b>2</b>3</p>` (the `3` used to lose its place and its paragraph); `<b><i>x</b>y</i>` keeps `y` italic.
- A formatting element still open when its container ends continues into what follows: `<p><b>a</p>b` is bold in both places, `<div><i>a</div><div>b</div>`
  is italic in both.
- `<p><span><div>x</div></span>` ends the `<p>` (the `<div>` is its sibling); an `<li>`, `<dt>`/`<dd>` or heading start tag closes the open item or heading
  even with an inline element open inside it.
- An end tag is ignored when its element is not in scope (`</div>` inside a table cell with the `<div>` outside the table), or when a block is open
  inside it (`<span><div></span>`: the `<div>` stays in the `<span>`).
- `</br>` is a line break; `</h4>` ends an open `<h3>`.

Documents that were already correctly nested are unchanged.
