# Stylesheets honor `<base href>`; a relative stylesheet with no base address is skipped

Before: a `<link rel=stylesheet>` or `@import` was resolved against the host's base address only - a `<base href>` in the document was ignored for
them (it applied to images and links). With a host that has no base address, a relative stylesheet reference made the load fail with an
exception. A `<base>` with a malformed or relative `href` also threw.
Now: the first `<base>` that has an `href` is the base for stylesheets too (relative values resolve against the host's base address); a relative
reference with no address to resolve it against is skipped and the document renders without that stylesheet; a bad `<base href>` is ignored.
A document that relied on `<base>` being ignored for its stylesheets will now load them from the `<base>` address.
