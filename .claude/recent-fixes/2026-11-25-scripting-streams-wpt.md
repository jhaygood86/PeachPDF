# PeachBrackets.Scripting: running the streams WPT, and what it found

Phase J. `streams/` is now fetched (`wanted` in `Program.cs`, cache key `wpt-v5-`) and run as the `streams` module: 776/1284 on the first run, 1130/1300 after.

- **A real bug in every page, found by the first failing file:** the lazy bundle globals (`WritableStream`, `fetch`, `Blob`...) loaded the bundle with `Evaluate(...)`
  then `.Call(...)`. Jint runs the waiting promise jobs when a script ends, and testharness had a `promise_test` callback queued; it ran *inside* the global's getter,
  named the same global again, and got `undefined` ("X is not a constructor") for every later subtest of the file. The bundle is now defined and run in one
  `Execute` (`LoadWebBundle`). The job still runs before the script that asked for the global continues (a Jint checkpoint at the end of `Execute`).
- **WritableStream, TransformStream and `pipeTo` are now ports of the standard's algorithms** (the erroring state, `pendingAbortRequest`, in-flight write/close
  requests, the transform `finishPromise` cancel/flush protocol, `ReadableStreamPipeTo`'s shutdown-after-writes). The earlier hand-rolled version differed in
  corners the tests found at once (an in-flight write rejected on abort, a pipe dropping its last chunk). Callbacks the page gives are called through
  `callFn` (`Reflect.apply`), because a page can replace `.call`/`.apply`; a strategy's `size` is called with no `this`.
- **Byte streams are implemented** (`ReadableByteStreamController`, `ReadableStreamBYOBReader`, `ReadableStreamBYOBRequest`, `autoAllocateChunkSize`, `read(view, {min})`,
  `respond`/`respondWithNewView`, detaching with `ArrayBuffer.prototype.transfer`), with `s.bc` on the stream record and hooks in the shared read/cancel/error/close paths.
- `structuredClone` exists now (values, collections, buffers and views, `{transfer}` of ArrayBuffers; other platform objects throw `DataCloneError`).
- Not done (the remaining 170): `postMessage`/`MessageChannel`/transferable streams (82), byte-stream `tee()` with BYOB branches (~15), `WebAssembly.Memory` buffers, other
  realms (iframe globals have no streams), tests that patch `Promise.prototype.then` (the internals use `.then`), the tentative `type: "owning"` streams.
