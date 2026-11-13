# PeachDrawing.PDF: randomness and the clock go through `PdfEntropy`

Any new code in `src/drawing/PeachDrawing.PDF` that needs random bytes, a GUID, the current time or an instant converted to local time must call `PdfEntropy` (`GetBytes`, `NewGuid`, `Now`, `UtcNow`, `ToLocal`), never `Guid.NewGuid()`, `RandomNumberGenerator`, `DateTime.Now`, `DateTimeOffset.UtcNow` or `DateTimeOffset.LocalDateTime` directly.

**Why.** `PeachDrawing.PDF.Tests/Baselines/PdfOutputBaselineTests.cs` pins the SHA-256 of a corpus of written documents so an internals refactor can prove it changed nothing. It only works because a test can replace the randomness and the clock for its async flow. A direct call
makes the output differ on every run, and the symptom is the baseline test failing (or `TheSameDocumentIsWrittenWithIdenticalBytesEveryTime`) with a hash that changes each time, not a wrong PDF.

**Time zone trap (seen on the first CI run).** A PDF date prints the offset of its `DateTime`'s kind, so a `.LocalDateTime` conversion puts the machine's time zone into the file. Baselines generated on a developer machine in a non-UTC zone failed on every CI leg with identical wrong hashes on Windows, Linux and macOS. The test source returns UTC-kind dates from `Now()` and `ToLocal()`, so the hashes no longer depend on the zone.

**Baselines.** A changed hash is a change in what the library writes: explain it, do not regenerate it blindly (`PEACH_UPDATE_BASELINE=1`). Compressed documents hash the same on Windows, Linux and macOS (the runtime's zlib does not differ), so a drift is the writer, not the platform.
