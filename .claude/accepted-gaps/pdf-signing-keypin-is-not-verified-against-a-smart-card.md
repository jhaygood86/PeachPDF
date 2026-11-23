# `PdfSigningOptions.KeyPin` is not verified against a smart card or token

`KeyPin` sets CNG's `SmartCardPin` property on the key of a Windows certificate (`RSACng.Key`/`ECDsaCng.Key`) and signs with the same key
object. That is the documented pattern for passing a PIN to a smart-card key, and `RSACng.Key` returns a handle that shares the native key
with the object that signs, so the PIN reaches the signing call. It has **not** been run against a real smart card or token: this
repository's machines have none, and a software CNG key (all a PFX can give) rejects the property outright, so no test can reach the
positive path. The tests prove what they can: the provider's refusal is reported rather than ignored, `KeyPin` fails at validation off
Windows, and the same signing path is exercised through `PrivateKey` with custom keys.

If `KeyPin` misbehaves on a card, the supported route is `PrivateKey` with a key object the caller opens and configures itself, which does
not depend on this property. The first person with a card to test with can turn this note into a test or delete `KeyPin`.
