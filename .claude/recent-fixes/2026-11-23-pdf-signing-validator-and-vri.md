# Validating signed PDF/A with veraPDF, and per-signature /VRI entries

Follow-up to [long-term validation](2026-11-23-pdf-long-term-validation.md) and the PDF/A tests in
[the contents binding note](2026-11-23-pdf-signing-pdfa-and-contents-binding.md).

**veraPDF found what the structural tests missed.** The earlier PDF/A signing tests asserted the annotation flags and a zero-size
`/Rect` (ISO 19005-2 6.3.3 exempts a zero-size annotation from needing an appearance). veraPDF 1.30.4 (the official `verapdf/cli` image) agreed
for PDF/A-2b, 3b and 4 - but **failed every signed PDF/A-1b file on 6.9 test 2** ("every form field shall have an appearance dictionary"),
a rule part 1 states with no zero-size exemption. Fix: an invisible signature widget now has an empty appearance (`/AP << /N form >>`, the
form's `/BBox [0 0 0 0]`) on both paths. **Trap:** the first attempt made the form's stream empty, and the engine writes an empty stream as
`stream` directly followed by `endstream` with no line break, which fails ISO 19005 6.1.7 (the stream length) on *every* level and broke the
composer path for 2b/3b/4 that had passed: the stream is now `q Q` (a no-op), on both paths. The incremental path writes its own line break, so
it never had the problem. 28 files now pass: 1b, 2b, 3b and 4, each composer-signed (PKCS#7, CAdES, timestamped), signed in place, with a
security store, with a document timestamp (1b needs `Creator` set: [separate, pre-existing gap](../accepted-gaps/pdfa1b-info-creator-differs-from-xmp-creatortool.md)).
The signature subfilters, the `/DSS`, the document timestamp's `/DocTimeStamp` and the incremental sections all pass as they are.

**Per-signature `/VRI`.** `PdfFileSigner.AddValidationData` writes a `/VRI` entry (`/Type /VRI /Cert /CRL /OCSP /TU`) for each signature the
data is for (all, or `signatureFieldName`), keyed by the uppercase SHA-1 of the *hexadecimal text of the signature's `/Contents` including the
zero padding*. That is the rule pyHanko uses (`sig_content_identifier(pkcs7_content.hex())`), measured by running it on one of these files
(hashing the raw bytes or only the DER gives different keys); it is not checked against Acrobat, and a reader that finds no entry uses the
file-wide lists, so a different rule elsewhere costs a missed narrowing, not a failure. A call merges into an existing entry (the references it
had plus the new ones), leaves other signatures' entries untouched, and changes nothing when there is nothing new. Document timestamps get no
entry. The reader got `ReadIdentities` and shares one field enumeration and one byte-range check with `GetSignatures` (the
two byte-range problem messages became one that still names `/ByteRange`).

**Evidence:** `PdfVriTests` (10) compute the expected keys independently from the file's own bytes; pyHanko finds both entries of a
two-signature file with its own key rule and each lists the 2 certificates; qpdf parses the dictionaries; veraPDF passes the VRI files at
every PDF/A level. **Also checked:** a NativeAOT publish of an app that signs, draws a visible box, signs again, stores validation data and reads
it all back runs correctly with no AOT warnings, and the `PeachPDF.Cli` publish itself succeeds (the earlier compiler crashes were the old CPU).
