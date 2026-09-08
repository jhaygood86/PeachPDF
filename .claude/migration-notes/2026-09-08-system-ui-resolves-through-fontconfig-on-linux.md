# `system-ui` resolves through fontconfig on Linux (it used to be the platform default font)

On Linux, PeachPDF already delegated `serif`, `sans-serif`, `monospace`, `cursive` and `fantasy` to
the host's own fontconfig — the managed equivalent of `fc-match <generic>` — so the resolved family
matches whatever that distribution actually configures. `system-ui` was the one exception: it was
hardcoded to the platform default font, which made it the only generic family that ignored the
host's own configuration. Chromium delegates `system-ui` to fontconfig on Linux too.

`system-ui` now goes through fontconfig on Linux like the other five, falling back to the platform
default font only when fontconfig cannot answer (no `libfontconfig.so.1`, or a resolution failure)
or names a family that is not actually installed. Windows, macOS and Android are unchanged.

**What a document author may notice.** Text in `system-ui` can render in a different face than
before, with the line-height consequences that carries. Measured on a host with the common
deployment font set installed: `fc-match system-ui` gives FreeSans while the platform default is
Liberation Sans, and those two disagree on line box height by 11.7% (ascent+descent 1.000em against
1.117em). `system-ui` is the family generated header and footer bands are drawn in, so such a band
was 11.7% taller than the same band under Chromium — enough, on a page whose top margin the band
already fills, to tip body text into overprinting it.

The two documentation pages disagreed with each other before this change:
[docs/html-css-support.md](../../docs/html-css-support.md#color--typography) already described `system-ui` as
fontconfig-delegated on Linux, while `docs/usage-examples.md` described the old behaviour. The code
now matches the former, and the latter has been corrected.

See [Generic families and `system-ui`](../../docs/usage-examples.md#generic-families-and-system-ui)
in `docs/usage-examples.md`.
