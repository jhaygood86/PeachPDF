# An unexpandable line under `text-align-last: justify` start-aligns, where §6.4.3 says centre

Tracked as **#1031**.

Found in review of the #1021 work, which implemented `text-align-last` (see
[../recent-fixes/2026-09-13-justify-ends-a-paragraph-at-a-forced-break.md](../recent-fixes/2026-09-13-justify-ends-a-paragraph-at-a-forced-break.md)).
A sibling of [text-align-is-not-a-shorthand-of-text-align-all-and-last.md](text-align-is-not-a-shorthand-of-text-align-all-and-last.md),
and left for the same reason.

[css-text-3 §6.4.3](https://www.w3.org/TR/css-text-3/#justify-algos) reads, in full:

> If the inline contents of a line cannot be stretched to the full width of the line box, then they
> must be aligned as specified by the `text-align-last` property. **(If `text-align-last` is
> `justify`, then they must be aligned as for `center`.)**

PeachPDF implements the first sentence and not the parenthetical: `CssLayoutEngine`'s
`ResolveUsedAlignment` resolves a **doubly-unexpandable** line — one that is being justified, has no
justification opportunity at all, *and* whose `text-align-last` itself resolves to `justify` — to
**start**, not to centre.

```html
<p style="text-align: justify; text-align-last: justify; width: 200pt">
  …several wrapped lines…<br>solo</p>
```

The spec centres `solo` in the measure. PeachPDF puts it at the start edge (physical left under LTR,
physical right under RTL).

## Why it was left

**No browser implements the parenthetical either.** Chromium, Gecko and WebKit were each measured on
this shape and all three start-align. Centring here would make PeachPDF the only renderer that does,
and would disagree with every engine an author checks their document against — the same trade, and the
same answer, as the `text-align-all` gap in the sibling file.

The cost of closing it is one enum value (`HorizontalAlignment.Center` instead of `towardStart` in
`ResolveUsedAlignment`'s fallback), so this is a deliberate choice about *which* behaviour is right,
not a difficulty. That is exactly why it needs writing down: the fix is trivial enough that a future
reader would otherwise assume it was an oversight and "correct" it into a browser-divergent result.

Note that the start-alignment is **not** arbitrary — it is what §6.1 specifies for every other
unexpandable/overflowing case, and resolving to it rather than leaving the line where the flow put it
is load-bearing under RTL (see
`TextAlignLastTests.AnRtlTextAlignLastJustify_StillStartAlignsAClosingLineItCannotStretch`). Only the
choice of start over centre is the deviation.

## What would close it

Return `HorizontalAlignment.Center` from `ResolveUsedAlignment`'s `fallback == Justify` branch, and
update that method's remarks, `TextAlignLastTests`' RTL assertion, and the `text-align-last` row in
`docs/html-css-support.md`. Revisit if a browser ever ships the parenthetical.
