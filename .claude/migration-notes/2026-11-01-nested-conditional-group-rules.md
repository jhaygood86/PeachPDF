# @media, @supports, @container and @layer can be nested inside a style rule

Before: `.card { color: black; @media (min-width: 40em) { padding: 2rem; } }` dropped the nested `@media` and its contents (the parser only skipped the at-keyword), so the declarations inside it never applied.

Now: a conditional group rule written inside a style rule applies its declarations to the parent's elements under its condition (CSS Nesting 1 section 3.2), and may hold nested rules and further groups. Its rules come after the parent's own in source order. Any other at-rule inside a style rule is still skipped.
