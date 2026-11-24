# XMLHttpRequest and fetch for PeachBrackets.Scripting

**Idea.** Jint is synchronous but a network is not. A script's `send` records a pending `Task` and returns; `PlayTimersAsync` awaits every pending request at the top of each loop turn and queues each answer as a zero-delay timer at the *current* virtual time, so the clock never advances past an unanswered request. Sync XHR blocks with `GetAwaiter().GetResult()` (documented cost).

**Seam.** `IScriptNetwork` (PeachBrackets, Jint-free) via `HtmlDocumentOptions.ScriptNetwork`; the fallback `LoaderScriptNetwork` is GET-only over the render context's resource loader. The classes themselves are one JS bundle (`WebPlatformScripts`) evaluated lazily by accessors on the global object, so documents that never name them pay nothing.

**Found by running it.** `DOMException` needed `toString` on its prototype; `?query` relative URL references must not gain a trailing `/`; a UTF-8 BOM in served pages put css-grid tests in quirks mode.

**Not done.** Streams (50+ fetch failures are `ReadableStream is not defined`), UTS46 IDNA conformance (most remaining `url` failures), CORS, `.py` handlers: xhr skips 157 tests for unported ones (`content.py`, `status.py`, `redirect.py`, `inspect-headers.py`, `headers.py` first).

**Evidence.** Floors (passing subtests): url 4735, xhr 285, fetch-api 647, fileapi 410. Solution rebuilds with 0 warnings; PeachBrackets.Tests, Scripting.Tests (86) and WptRunner.Tests pass on net10.0.
