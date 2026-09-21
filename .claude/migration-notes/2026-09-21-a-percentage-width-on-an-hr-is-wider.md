# A percentage `width` on an `<hr>` is wider than it was

A percentage `width` on a rule now resolves against its containing block's content width, per CSS 2.1
[§10.2](https://www.w3.org/TR/CSS21/visudet.html#the-width-property). It used to resolve against a
basis already reduced by the rule's own margins and borders, so every percentage rule came out
narrower than the author asked for — and narrower than the equivalent `<div>` under the same
declaration.

Measured in a 200pt containing block (1px = 0.75pt, so a `4px` border is 3pt a side):

| declaration | before | now | a browser |
| --- | --- | --- | --- |
| `width: 50%` (no border declared) | 99.25pt | 100pt | 100pt |
| `width: 50%; border: 4px solid` | 97pt | 100pt | 100pt |
| `width: 100%; border: 4px solid` | 194pt | 200pt | 200pt |
| `width: 50%; margin-left: 20pt; border: 4px solid` | 87pt | 100pt | 100pt |
| `width: 50%; box-sizing: border-box; border: 4px solid` | 96pt border box | 100pt border box | 100pt border box |
| `padding: 0 10pt; border: 4px solid` (`auto`) | 194pt, overflowing | 174pt | 174pt |

The first row is worth singling out: the UA stylesheet gives every rule a 1px border, so a percentage
rule was slightly narrow **even in a document that never declares a border on it**.

## What changes for a document

- **Any `<hr>` with a percentage `width` gets wider**, by twice its border width (plus its horizontal
  margins, if any). A rule that previously appeared to fit an adjacent element by coincidence may now
  extend past it — the new width is the one a browser has always drawn.
- **`width: 100%` on a rule with a border now overflows its container**, exactly as `width: 100%` on a
  `<div>` with a border does, because the border is added outside the resolved width. If the intent
  was "fill the container, borders included", that is what `width: auto` already does, and it is the
  UA default.
- **An `auto` rule with horizontal padding no longer overflows** its container by that padding.

## What does not change

`width: auto` (the default) is unchanged for every rule that declares no padding, which is the
overwhelming majority: an unstyled rule's border box still spans its containing block exactly. A
length `width` (`width: 200pt`) was never affected.
