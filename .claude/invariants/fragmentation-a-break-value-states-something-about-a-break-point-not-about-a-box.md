# A break value states something about a break point, not about a box

_CSS Fragmentation Level 3. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

§3.1's break point before a container's first in-flow child *is* the break point before the container, so both sides are read through the chains they begin and end and the outermost such container takes the break and travels with it (`BreakPropagation`). Combination is **not** "the innermost wins": a directional value subsumes a plain `page` whichever is deeper, and a page break subsumes `column`. And governance is a separate question from action — a box whose forced break the container acted on still *adjoins* a forced break point, so §5.2 must not truncate its margin.
