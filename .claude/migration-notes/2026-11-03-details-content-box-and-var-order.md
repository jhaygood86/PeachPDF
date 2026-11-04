# details content, and var() in shorthands

- A closed `<details>` now hides its content whatever `display` the author gave it (a panel written `display: flex` used to show). A second `<summary>` is content and is hidden while closed (it used to show). `details` and `summary` are `display: block` in the default style sheet.
- A declaration that uses `var()` is now ordered with the rest by where it was written. `background: ... var(--x); background-clip: text` used to reset the clip to `border-box` (a box behind the text); it now keeps `text`. The reverse order (longhand first, shorthand last) still lets the shorthand win, as in a browser.
