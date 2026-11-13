# PeachDrawing.PDF: randomness and the clock go through `PdfEntropy`

Any new code in `src/drawing/PeachDrawing.PDF` that needs random bytes, a GUID or the current time must call `PdfEntropy` (`GetBytes`, `NewGuid`, `Now`, `UtcNow`), never `Guid.NewGuid()`, `RandomNumberGenerator`, `DateTime.Now` or `DateTimeOffset.UtcNow` directly.

**Why.** `PeachDrawing.PDF.Tests/Baselines/PdfOutputBaselineTests.cs` pins the SHA-256 of a corpus of written documents so an internals refactor can prove it changed nothing. It only works because a test can replace the randomness and the clock for its async flow. A direct call
makes the output differ on every run, and the symptom is the baseline test failing (or `TheSameDocumentIsWrittenWithIdenticalBytesEveryTime`) with a hash that changes each time, not a wrong PDF.

**Baselines.** A changed hash is a change in what the library writes: explain it, do not regenerate it blindly (`PEACH_UPDATE_BASELINE=1`). Documents that embed Flate-compressed streams can also differ when the runtime's zlib does; the baselines were produced on Windows.
