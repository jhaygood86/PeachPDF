# An inline image with `vertical-align: middle` is sized into its line, and aligned, by its margin box

**Before.** The line holding an inline `<img>` or `<svg>` with `vertical-align: middle` was sized as if the image sat on the baseline, and the image was then moved to the middle of
the line afterwards. The line came out shorter than a browser's and the image lower than it: about half its own height, so a logo overlapped the text under it. The alignment also
used the border box's midpoint, so an image with a margin on one side was a half-margin off.

**Now.** The line is as tall as the image's margin box centred on the parent's baseline plus half its x-height, and the margin box's midpoint is what sits there (CSS 2.1 section
10.8.1). A page that uses Tailwind's preflight (`img, svg { vertical-align: middle }`) lays out like a browser. Pages with a middle-aligned image that sat flush on the baseline
before (a small icon beside text) may move by a pixel or two toward the browser's position; PDF output with such images changes the same way.

Not verified against `v0.9.26-preview1`.
