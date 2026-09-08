# A large inline `data:` image now renders on .NET 8 (it previously vanished)

Before .NET 10, `System.Uri` rejects any URI string longer than 65,519 characters — roughly 48 KB of
payload once base64 expansion is accounted for. `RUri`'s single-string constructors already worked
around that by holding a `data:` URI verbatim, but the two **base-relative** constructors did not,
and those are the ones an `<img src="data:…">` actually reaches (via
`CommonUtils.ResolveAgainstDocumentBase`). The resulting `UriFormatException` is caught upstream and
turned into a skipped resource, so an embedded logo, photograph or chart past that size rendered as
nothing at all, with no error surfaced to the caller.

Such an image now renders. There is no length limit on a `data:` URI on any target framework.

Two smaller changes ride along:

- The `data:` scheme is now matched case-insensitively (RFC 3986 §3.1), so a document writing
  `DATA:` takes the same path.
- On .NET 10 nothing changes: its `System.Uri` already round-trips a `data:` URI exactly and has no
  length ceiling, so it keeps using it — including the percent-escaping of a non-base64 payload
  (`data:text/plain,Hello World!` → `data:text/plain,Hello%20World!`) that the pre-.NET-10 path does
  not do.

See [Embedded Content](../../docs/html-css-support.md#embedded-content) in `docs/html-css-support.md`.
