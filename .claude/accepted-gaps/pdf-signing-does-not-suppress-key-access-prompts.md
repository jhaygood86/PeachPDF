# Signing may prompt for the key (PIN, smart card) on a desktop and block on a headless host

`PdfDocumentSigner.BuildCms` calls `SignedCms.ComputeSignature(signer)` with the default, non-silent overload. For a private key held in a
smart card or a CNG provider that wants a PIN, the platform may show a prompt; on a server with no desktop that can hang or fail. Passing
`silent: true` would make a key that needs interaction fail instead of prompting, which breaks the desktop smart-card case.

Not done: there is no `PdfSigningOptions` switch for it yet, and the right default is not obvious (desktop apps want the prompt, services
want the failure). A host that signs with a hardware key on a server should use a key that does not prompt. If this is wanted, add an
opt-in option rather than changing the default.
