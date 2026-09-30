# `@page :blank` now matches

Before: `@page :blank` never matched, so running headers/footers printed on pages inserted by
`break-before: left|right|recto|verso`. Now it matches those inserted pages, so a stylesheet using
`@page :blank { @top-center { content: none } }` suppresses them. Pages still count toward `counter(page)`.
