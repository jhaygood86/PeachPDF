# An inline box's left padding and border are no longer counted twice

**Before (v0.9.18 and earlier):** an inline box carrying `padding-left` or `border-left` advanced the
line by that padding and border **twice**, so everything after it on the line was pushed right by one
padding+border width. An empty one is the clearest case:

```html
<span style="padding-left:20pt; border-left:1pt solid red"></span><span>BB</span>
```

`BB` began at 42pt (2 × 21pt) instead of 21pt. No white space is involved — the doubling happened with
the markup written on a single line with no spaces at all. An inline *with* content was wrong too: its
own text sat correctly, but whatever followed the box cleared it by an extra padding+border.

**Now:** the padding and border are charged once, and `BB` begins at 21pt — matching Chromium, which
puts it at 27.656px (its own rounding of the 1pt border; the exact value is 28px = 21pt).

**Why:** the engine compared the advance a box's *content* had used against the box's *border-box*
width, so every inline with left padding or a left border looked narrower than it was by exactly that
padding and border, and the shortfall was added to the line a second time (issue #1093).

**Related, same release:** a collapsible space after a content-less inline that carries padding is now
also removed when it begins a line, so

```html
<span style="padding-left:20pt; border-left:1pt solid red"></span> <span>BB</span>
```

lays out identically to the space-free form above. Chromium does the same.

**Unchanged, deliberately:**

- **Right** padding and border were never doubled and are untouched — `padding: 0 10pt` still reserves
  10pt on each side.
- An inline-level box with a **declared width wider than its content** still reserves that width, so
  what follows clears it. That is the case this machinery exists for.
- A space *between* two inlines that both have content still renders.

**What a document author may notice:** any line containing an inline element with `padding-left` or
`border-left` — a highlighted or badged run of text, an icon span, a bordered inline label — becomes
narrower by one padding+border per such element, and the content after it moves left. Text that
previously wrapped because of the extra width may now fit on one line, so line counts and page breaks
can change. The inline's own background and border also now paint from its true border-box left edge
in the one case they were registered from the content edge.
