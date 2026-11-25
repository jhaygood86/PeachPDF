# PeachBrackets.Scripting: MessageChannel, window.postMessage and transferable streams

Phase K. `streams` 1130 -> 1189 of 1300 (floor 1180); no other floor moved down.

- `MessagingScripts` is a bundle fragment after the streams one (marker `/*MESSAGING*/`, same function scope). `MessagePort` is an `EventTarget` built with `Reflect.construct(EventTarget, [], MessagePort)`
  (like `XMLHttpRequest`), over a *record* (`peer`, `queue`, `started`, `closed`) kept apart from the wrapper object: transferring a port makes a new wrapper over the same record and leaves the old
  wrapper on a dead record, so messages queued for the port follow it. A port delivers one message per task (`setTimeout(0)` on the virtual clock) and only once started (`start()` or setting `onmessage`;
  `addEventListener` does not start it).
- `structuredClone` was split: `serializeWithTransfer(value, list)` returns `{ value, transferred }` (the `postMessage` paths need the transferred objects to fill `MessageEvent.ports`), and
  `transferHandlers` is the registry an object kind joins to be transferable (`test`, `check` for DataCloneError, `transfer`). Ports, readable and writable streams register; a `TransformStream` does not,
  which is what the "Transferring [t, t.readable] should fail" tests expect. `DOMException` is cloneable now (error values go through the port).
- A transferred stream is the Streams standard's cross-realm transform pair, done in one realm: the original is piped (`pipeTo`, which locks it) into a writable whose sink posts `chunk`/`close`/`error`
  on port1, and the new stream is a `highWaterMark: 0` readable on port2 that posts `pull` for backpressure. Writable transfer is the mirror image. Because of the port queue a chunk written to a
  transferred writable reaches the original sink a task later than `write()` resolves, and the original reads as unlocked once its pipe has closed.
- `window.postMessage(message, targetOrigin, transfer)` and `postMessage(message, { targetOrigin, transfer })` dispatch a `message` event on the global (`source` is the window, `origin` is
  `location.origin`); a `targetOrigin` that is another origin drops the message, an invalid one throws `SyntaxError`.
- Not done: workers, iframe globals and service workers (the `transferable/*.html` pages for them), `type: "owning"` streams and their `MessagePort` chunks, `postMessage` between documents.
