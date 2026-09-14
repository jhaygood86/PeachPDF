# A formatting-context root now contains its floats

A box that establishes its own formatting context and takes its height from content now grows to cover
any floating descendant hanging below it, per CSS 2.1
[§10.6.7](https://www.w3.org/TR/CSS21/visudet.html#root-height). It previously did not — for any box
kind — so such a box holding nothing but a float came out **zero-height**.

```html
<div style="overflow: hidden; border: 1px solid">
  <img style="float: left" src="…">
</div>
```

The wrapper used to collapse to nothing, its border drawn as a line across the top of the float. It now
wraps the float, which is what the `overflow: hidden` containment idiom has always been for.

This applies to every formatting-context root: `float`, an `overflow` other than `visible`,
`inline-block`, `position: absolute`/`fixed`, a table cell or caption, and a flex or grid item. An
**ordinary block still does not contain its floats** — unchanged, and correct: that is why `clear` and
the containment idiom exist.

Two consequences a document author can see:

- **Wrappers around floated content get taller**, from zero (or from their text's height) to the float's
  full extent. Anything below them moves down, so documents using this idiom will repaginate — and will
  now look the way they were written to look.
- **A definite `height` still wins.** §10.6.3 is unchanged: the float overflows a box with a declared
  height rather than growing it.

Separately, an **absolutely-positioned box with an `auto` width is no longer too wide**. Its
shrink-to-fit width counted its own border and padding twice, so a box with 24pt borders either side of
48pt of content measured 144pt instead of 96pt. Such boxes now measure what their content and decoration
actually need, which for right-positioned or centred ones also moves them.

`display: flow-root` — css-display-3's dedicated way to ask for containment — is still not implemented
and computes as `block`, so it contains nothing. Use `overflow: hidden` for now.
