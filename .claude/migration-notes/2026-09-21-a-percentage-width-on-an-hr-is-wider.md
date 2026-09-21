# A percentage `width` on an `<hr>` is wider than it was

A `width` on a rule now resolves against its containing block's **content** width, per CSS 2.1
[§10.1](https://www.w3.org/TR/CSS21/visudet.html#containing-block-details) and
[§10.2](https://www.w3.org/TR/CSS21/visudet.html#the-width-property), and `auto` fills what is left of
that width once the rule's own margins and box-sizing edges come out
([§10.3.3](https://www.w3.org/TR/CSS21/visudet.html#blockwidth)). One expression used to do both jobs:
the available space, already reduced by the rule's own margins and borders, was also handed to the
percentage as its basis — so every percentage rule came out narrower than the author asked for, and
narrower than the equivalent `<div>` under the same declaration.

Every "before" figure below is measured from v0.9.19's own output. 1px = 0.75pt, so a `4px` border is
3pt a side and the UA stylesheet's own `1px` rule border is 0.75pt a side.

In a `width: 200pt` containing block:

| declaration | before | now | a browser |
| --- | --- | --- | --- |
| `width: 50%` (no border declared) | 99.25pt | 100pt | 100pt |
| `width: 50%; border: 4px solid` | 97pt | 100pt | 100pt |
| `width: 100%; border: 4px solid` | 194pt | 200pt | 200pt |
| `width: 50%; margin-left: 20pt; border: 4px solid` | 87pt | 100pt | 100pt |
| `width: 50%; margin: 0 auto` | **a 1.5pt dot** (border box) | 101.5pt border box, centred | 101.5pt, centred |
| `width: calc(50% - 10pt)` | 89.25pt | 90pt | 90pt |
| `width: 50%; box-sizing: border-box; border: 4px solid` | 97pt border box | 100pt border box | 100pt border box |
| `padding: 0 10pt; border: 4px solid` (`auto`) | 220pt border box, overflowing | 200pt | 200pt |
| `box-sizing: border-box; padding: 0 10pt` (`auto`) | 198.5pt border box | 200pt | 200pt |

And in a containing block with padding and a border of its own —
`width: 200pt; padding: 0 10pt; border: 5pt solid`, whose content width is the declared 200pt:

| declaration | before | now | a browser |
| --- | --- | --- | --- |
| `width: 50%; border: 4px solid` | 82pt | 100pt | 100pt |
| `width: 100%` | 168.5pt | 200pt | 200pt |
| `border: 4px solid` (`auto`) | 164pt | 194pt | 194pt |

Two rows are worth singling out. The first table's opening row, because the UA stylesheet gives every
rule a 1px border: a percentage rule was slightly narrow **even in a document that never declares a
border on it**. And `margin: 0 auto`, because it is the largest visible change here — the auto margins
were resolved before the width and consumed the whole basis, leaving a percentage of nothing, so the
rule collapsed to its own borders and painted as a dot.

## What changes for a document

- **Any `<hr>` with a percentage `width` gets wider**, by twice its border width, plus its horizontal
  margins, plus its own padding, plus anything the containing block's padding and border took off the
  basis. A rule that previously appeared to fit an adjacent element by coincidence may now extend past
  it — the new width is the one a browser has always drawn.
- **`width: 50%; margin: 0 auto` draws a rule** rather than a dot.
- **`width: 100%` on a rule with a border now overflows its container**, exactly as `width: 100%` on a
  `<div>` with a border does, because the border is added outside the resolved width. If the intent
  was "fill the container, borders included", that is what `width: auto` already does, and it is the
  UA default.
- **An `auto` rule with horizontal padding no longer overflows** its container by that padding under
  `content-box`, and no longer falls short by it under `box-sizing: border-box`.
- **A rule inside a padded or bordered container gets wider** at every `width`, `auto` included: the
  container's own padding and border used to come off a figure that, under `content-box`, was already
  its content width.

## What does not change

A `width` that is a plain length (`width: 200pt`) resolves to the same figure it always did — no
percentage, no basis. A `calc()` that mixes a percentage in (`calc(50% - 10pt)`) is **not** in that
group and does change, since it resolves that percentage against the same basis. `width: auto` is also
unchanged for the common case that carries none of the above: a `content-box` rule with no padding of
its own, in a container with no padding or border of its own. An unstyled rule's border box still spans
its containing block exactly, which is the case that made this bug survive as long as it did.
