# `hover` / `pointer` media features can report a pointing device

Before: `hover`, `any-hover`, `pointer` and `any-pointer` always reported `none`.

Now: they report `HtmlDocumentOptions.Input`, which is `null` (no device) by default, so PDF output and
every caller that does not set it are unchanged. A screen host sets `InputCapabilities.Mouse`, `.Touch`
or its own mix and `@media (hover: hover)` / `(pointer: coarse)` rules then apply.
