# outline-style: auto ignores outline-width, and rings the offset edge

`outline: 8px auto` painted an 8px ring. CSS-UI-4 §3.3 says it must not:

> The `auto` value permits the user agent to render a custom outline style […] User agents may enable
> authors to influence the rendering of auto style outlines via the `outline-color` property, but this
> specification does not define how the rendering is impacted (if at all). **The `outline-width`
> property is ignored when `outline-style` is `auto`.** User agents may treat `auto` as `solid`.

## The load-bearing reading

The two sentences do different jobs, and the old code conflated them. "May treat `auto` as `solid`" is
a permission about the *style* — it is what lets us keep painting four mitred solid quads instead of
inventing a platform focus ring. "The `outline-width` property is ignored" is normative and
unconditional, and applies regardless of which style the UA picks. Treating `auto` as `solid` therefore
means *solid at the UA's own width*, not solid at the author's.

## What Chrome actually does (measured, not recalled)

Headless Chrome at `--force-device-scale-factor=1` and `=2`, thickness read off the raster:

| declared | ring |
| --- | --- |
| `8px auto`, `1px auto`, `20px auto`, `0 auto`, `outline-style: auto` alone | 2 CSS px, every one |
| `8px solid` | 8 px |

So the width is ignored *including a declared zero*, and the 2 is CSS px, not device px — it measures
4 device px at a 2× device scale factor. Both facts are pinned by tests, because both are easy to get
wrong in the other direction.

The placement was the surprise. Chrome **centres** the `auto` ring on the rectangle `outline-offset`
inflates the border box to, where every other style sits wholly outside that rectangle:

| case | box top edge | ring rows |
| --- | --- | --- |
| `8px auto`, offset 0 | 60 | 59–60 — one px either side of the edge |
| `8px auto`, offset 6 | 120 | 113–114 — centred on 114 |
| `8px auto`, offset −6 | 180 | 185–186 — centred on 186 |
| `8px solid`, offset 6 | 240 | 226–233 — wholly outside |

## The implementation trick worth knowing

Centring needed no new geometry. `DrawRing` already takes the band's two reach distances, so pulling
the *offset* back half a width before anything else runs turns the ordinary outward-facing band into a
centred one, and every style arm, mitre and corner below inherits it untouched:

```csharp
width = AutoRingWidth(g);
offset -= width / 2;
```

`auto` also maps to `Solid` before `DrawSide` dispatches, so the dotted/dashed path is unreachable for
it and needs no thought.

Two ordering traps: the `if (width <= 0) return;` guard had to move *inside* the non-`auto` arm, or
`outline: 0 auto` would bail before the UA width was ever substituted (Chrome paints it). And the UA
width is a CSS length like any other, so it carries the `Length.PointsPerPx * g.PixelsPerPoint`
inflation — the same correction issue #856 applied to the cascaded width, now needed for a constant.

## Evidence

Rendered the same page through PeachPDF and Chrome and compared at 1 CSS px = 1 raster px: all four
`auto` variants, the `8px solid` control and the `2px solid` control line up on both thickness and
position. PDFium and MuPDF agree byte-for-byte on the measurement, per the two-renderer convention.
Full suite 12145 passing, solution rebuild clean.

## Deliberately not done

Chrome's focus ring has slightly rounded corners; ours stays square. That is the pre-existing,
spec-permitted "outline never follows `border-radius`" choice already documented in
`docs/html-css-support.md`, not a new gap — CSS-UI-4 says a UA *may* round an outline, never that it
must.

## A dating mistake worth not repeating

The first pass here concluded there was no migration note to write, reasoning that `outline` support
landed 2026-08-10 — after the oldest still-pending file in `.claude/migration-notes/` — so nothing in
it could have shipped. That inference is invalid, and the folder cannot support it: a pending note
means only that nobody folded it into release notes and deleted it, not that no release has happened
since. Releases have in fact been cut steadily (v0.9.18 on 2026-09-11), and `outline` shipped in
v0.9.10.

Date a behaviour change against the tag, never against the contents of these folders:

```
gh release list --repo jhaygood86/PeachPDF --limit 5
git merge-base --is-ancestor <commit> <tag>      # did it ship?
git show <tag>:<path>                            # what did it do when it did?
```

`git show v0.9.18:src/PeachPDF/Html/Core/Handlers/OutlineDrawHandler.cs` shows the released code
reading `box.ActualOutlineWidth` for `auto`, and the released `docs/html-css-support.md` promising it
"renders identically to `solid`" — so this is a genuine, user-visible change and carries a migration
note. The same check run against #1126 (border styles) confirms the opposite for it: not an ancestor
of v0.9.18, hence still unreleased, which is why its own note was amended in place rather than joined
by a second one.
