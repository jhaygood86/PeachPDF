# PDF/A with signing, and the signed hole bound to the declared /Contents

Builds on the [review fixes](2026-11-23-pdf-signature-review-fixes.md).

**PDF/A:** a signed file keeps its claimed level. The composer's signature widget is `/F 132` (Print + Locked: Print set, Invisible, Hidden and
NoView clear, as ISO 19005 requires of an annotation) with `/Rect [0 0 0 0]` (a zero-size rectangle is exempt from needing an appearance
stream), the form has no `/NeedAppearances` and the field no actions. `PdfFileSigner` keeps the XMP and output intent of a PDF/A source
byte for byte (it only rewrites the catalog, form and page). Tests (PDF/A-1b, 2b, 3b, 4 and an in-place signature of a 2b file) assert those
properties and that the signature still verifies. **Not run through a real validator:** veraPDF is not installed here (installing it means
downloading and running an external tool), so this is structural evidence only; the earlier PDF/A work in this repo
([real validator gaps](2026-09-06-pdfa-real-validator-conformance-gaps.md)) shows how often a structural check missed what veraPDF found.

**Binding the hole:** `GetSignatures` now requires, for an unencrypted file, that the bytes the `/ByteRange` leaves out equal the value of
the signature dictionary's own `/Contents`; otherwise the signature is reported not intact ("leaves out"). A reader that takes `/Contents`
from the dictionary would otherwise verify something other than what was checked here. An encrypted file's strings are decrypted on load and
a signature's `/Contents` never is, so the comparison cannot be made there (the gap note says so and is narrower than before).
