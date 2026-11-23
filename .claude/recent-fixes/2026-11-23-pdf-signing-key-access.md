# Signing fails instead of blocking, and a key or PIN can be supplied (PdfSigningOptions)

**What landed:** `PdfSigningOptions.AllowKeyPrompt` (default `false`), `PrivateKey` and `KeyPin`. Closes the gap the review of the signing
work left open (signing used the non-silent `ComputeSignature`, so a PIN-protected key could show a prompt or hang a headless host). See the
[review fixes](2026-11-23-pdf-signature-review-fixes.md).

**Load-bearing ideas:** `ComputeSignature(signer, silent: !AllowKeyPrompt)`: a key that needs interaction throws, and the
`CryptographicException` is rewrapped as an `InvalidOperationException` that names `KeyPin`, `PrivateKey` and `AllowKeyPrompt`. The default
is silent because the feature was unreleased, so there was no behavior to keep and a library is mostly run on servers. `PrivateKey` is passed
to `new CmsSigner(identifier, certificate, key)`, which lets the certificate be the public one: that one option covers a key that already
holds its PIN, an HSM or PKCS#11 wrapper (derive from `RSA`/`ECDsa` and forward `SignHash`), and a cloud key service, and it is the
callback signer the plan had put off. It is the caller's and is never disposed. `KeyPin` sets CNG's `SmartCardPin` property (a zero-terminated
UTF-16 string) on `RSACng.Key`/`ECDsaCng.Key`, whose handle is shared with the object that signs.

**Found by running it:** a software CNG key (any key loaded from a PFX) rejects the `SmartCardPin` property with "The parameter is
incorrect", so `KeyPin` can only be exercised against a real smart-card provider. The tests therefore prove the error path (the provider's
refusal is reported, never ignored; non-Windows fails at validation) and exercise everything else with `PrivateKey`, including a custom `RSA`
subclass that counts `SignHash` calls and one that throws, standing in for an HSM. `KeyPin` together with `PrivateKey` is rejected as
contradictory, a non-RSA/ECDSA `PrivateKey` is rejected, and the certificate needs a private key only when no `PrivateKey` is given.

**Not done:** [the PIN path is not verified against hardware](../accepted-gaps/pdf-signing-keypin-is-not-verified-against-a-smart-card.md);
a `SecureString`/`ReadOnlyMemory<char>` PIN (the string stays in memory); macOS and Linux have no `KeyPin`, only `PrivateKey`.
