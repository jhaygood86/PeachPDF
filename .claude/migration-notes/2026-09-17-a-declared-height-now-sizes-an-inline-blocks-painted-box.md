# A declared `height`/`min-height` now sizes an inline-block's painted box

An `inline-block` whose own content is inlines-only (or empty) is flowed into the surrounding
block's line boxes rather than laid out as a box of its own. An explicit `height` or `min-height` on
such a box used to have no effect on it at all — the box's background, border and overflow clip were
painted at whatever height its content happened to measure.

**Before.** `<span style="display:inline-block;height:100px;padding:4px;border:2px solid;background:#fee">x</span>`
painted a box just tall enough for the `x`'s own line — the declared height was silently ignored. An
empty box of the same shape painted nothing at all vertically beyond its own padding and border,
having no content for its rectangle to be derived from.

**Now.** The declared height is the box's used height (CSS 2.1 §10.6.3): both paint the same 100px
content area plus their padding and border, growing downward from the box's own top so the extra
space appears below the content rather than around it. `min-height` behaves identically whenever the
content is shorter than it, and acts only as a floor when the content is already taller — it never
shrinks a box below its natural content height. A declared `height` smaller than the content's
natural height is likewise never shrunk to; the painted box keeps at least its natural height,
mirroring how a declared `width` narrower than unbreakable content already worked.

A **percentage** `height`/`min-height` is unchanged: such a box is still sized as though `height:
auto` were declared.

## What this does not change

The *line* the box sits on still does not reserve the declared height — only the box's own painted
rectangle does. A tall `inline-block` can still visually overlap what follows it on the line's own
axis if nothing else on the line is as tall. This is a separate, tracked gap (issue #1166).

Growing the box only ever extends it downward, which is right for the default `vertical-align:
baseline` (when the box's own content supplies the baseline) and for `vertical-align: top`, but can
position the box too low under an explicit `bottom`/`middle`/`sub`/`super`/`text-top`/`text-bottom`/
length offset, or `baseline` on an empty or `overflow`-hidden box (issue #1169).

Neither change affects a box whose content is block-level (a `<div>` inside the `inline-block`, say)
or one whose own inline content must wrap onto several lines: those already take a different layout
path that sized height correctly before this change.
