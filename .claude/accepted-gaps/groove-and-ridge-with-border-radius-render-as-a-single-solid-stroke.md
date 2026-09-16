# `groove`/`ridge` with `border-radius` render as a single solid stroke

A `groove` or `ridge` border on a box with a non-zero `border-radius` paints as one solid stroke at
the full border width, losing the two-tone bevel. CSS 2.1 §8.5.3 defines both styles' appearance as
UA-dependent, but "renders as a bevel" is clearly the intent and every browser draws one, so this is a
genuine deviation rather than a permitted choice.

**No tracking issue filed yet** - this repo's convention (CLAUDE.md, "Post-change review pass") is that
a gap which is a real spec deviation gets a GitHub issue, and this one still needs one. File it and
reference the number here.

## Why

The two border paths are structurally different. A square border paints each band as a **fill** (a
mitred quad), so a style needing two bands is simply two fills. A rounded border paints as a **stroke**
along a curve, and a pen has one width and one color: it draws exactly one band, centered on its path.

`double` had the same problem and was fixed by stroking the outline twice, once per line, at the
thirds - see `BordersDrawHandler.TryDrawUniformRoundedOutline`. That works because `double`'s two lines
are the same color all the way round.

`groove`/`ridge` cannot use it. Their whole effect is that each side is shaded differently - the outer
half reads as `inset` and the inner as `outset`, and `inset`/`outset` darken the top and left while
lightening the bottom and right (see `BorderBevelColors`). A single continuous stroke cannot change
color partway round, so the two-tone ring is inexpressible that way.

## What a real implementation would need

Per-edge arcs, each stroked twice with its own shaded pair - which reintroduces two problems this
change deliberately moved away from:

- Four strokes butting end-to-end seam against each other (see
  [the abutting-fills invariant](../invariants/paint-two-abutting-antialiased-fills-leave-a-seam-along-their-shared-edge.md)),
  and here the two colors meeting make the join structural rather than incidental.
- `GetRoundedBorderPath` gives **both** corner arcs to the top and bottom edges, so there is no
  existing answer to where the color should flip at a corner. A browser flips it on the corner's
  diagonal, which means splitting each arc - geometry this builder has no notion of.

So it is not a small change, and it was left out of the border-rendering work rather than done badly.

## Do not confuse with

A `double` border whose four sides do **not** share a style, color and width also falls back, for the
same one-pen-one-band reason. That one is narrower and would follow naturally from a per-edge banded
arc builder, if one ever gets written for `groove`/`ridge`.
