# The document skeleton is supplied, and `<template>` contents are parsed

Before: only `html` and `body` were added (after the parse, and only for a non-empty document); a leading `<title>` or `<style>` stayed directly under `<html>`; a repeated `<body>` nested; text
after `</body>` landed outside it; `<template>` contents were parsed like any markup; `<svg><p>` kept the paragraph inside the SVG; a `<pre>` kept its first line feed.

Now, as in a browser:

- Every document is `html > head, body`, including an empty one; head elements before the body go into the head (`html > head > title`, not `html > title`).
- A second `<html>` or `<body>` start tag adds its missing attributes to the first; text or elements after `</body>` or `</html>` are part of the body.
- HTML elements that cannot be inside an `<svg>` or `<math>` (`<p>`, `<div>`, `<span>`, `<b>`...) end it.
- A line feed straight after `<pre>`, `<listing>` or `<textarea>` is dropped; `<image>` is an `<img>`.
- `<template>` contents are parsed by their first tag (a `<tr>` as a row) and are not rendered.

A selector like `html > title` or `body + *` that relied on the old shape matches differently.
