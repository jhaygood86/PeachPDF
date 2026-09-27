# The identity of a font is a collision-resistant hash of its bytes

The font source cache, the `name#suffix` face names and the PDF writer's embed cache (through `Typeface.ContentHash`) are content
addressed, and fonts come from documents. Whatever keys them must hold up against a font made to collide with another, which
is why `FontContentHash` is the first 128 bits of a SHA-256 and `Typeface.ContentHash` is that as text.

Do not put a sum (Adler-32, CRC, the OpenType table checksum) or a 64-bit value back in as an identity. Measured symptom: three
edited bytes (`+1, -2, +1`) gave a font the Adler-32 and length of another, and the cache served it the other font's parsed data.
The OpenType `checkSumAdjustment` and table checksums are a format detail and are not identities. See
[the fix](../recent-fixes/2026-09-26-font-identity-is-a-collision-resistant-hash.md) for what was measured.
