namespace PeachBrackets.Scripting.Internal;

/// <summary>
/// The Streams standard in script: <c>ReadableStream</c>, <c>WritableStream</c>, <c>TransformStream</c>, the queuing strategies and the text
/// encoding streams. It is a fragment of <see cref="WebPlatformScripts.Bundle"/>, put where <see cref="WebPlatformScripts.StreamsMarker"/> stands
/// so it shares the bundle's helpers (<c>method</c>, <c>readonly</c>, <c>tag</c>, <c>utf8</c>, ...). The streams are default streams (a
/// <c>type: "bytes"</c> source is accepted but read through a default reader), with the queue, backpressure and the pull / write algorithms the
/// standard gives them.
/// </summary>
internal static class WebStreamsScripts
{
    public const string Source = """
            // ---------------------------------------------------------------- Streams
            function deferred() {
                var d = {};
                d.settled = false;
                d.promise = new Promise(function (resolve, reject) {
                    d.resolve = function (v) { d.settled = true; resolve(v); };
                    d.reject = function (e) { d.settled = true; reject(e); };
                });
                return d;
            }
            function markHandled(promise) { promise.then(undefined, function () {}); return promise; }
            function rejectedHandled(error) { return markHandled(Promise.reject(error)); }
            // A callback the page gave, called without looking up .call or .apply on it (a page can replace them).
            function callFn(f, thisArg) {
                var args = [];
                for (var i = 2; i < arguments.length; i++) args.push(arguments[i]);
                return Reflect.apply(f, thisArg, args);
            }
            function isObject(v) { return v !== null && (typeof v === "object" || typeof v === "function"); }
            function getMethod(object, name) {
                if (object === null) return undefined;
                var fn = object[name];
                if (fn === undefined || fn === null) return undefined;
                if (typeof fn !== "function") throw new TypeError("The " + name + " member is not a function");
                return fn;
            }
            function strategyOf(strategy, defaultHwm) {
                strategy = strategy === undefined || strategy === null ? {} : Object(strategy);
                var size = strategy.size;
                var rawHwm = strategy.highWaterMark;
                if (size !== undefined && typeof size !== "function") throw new TypeError("The strategy's size is not a function");
                var hwm = rawHwm === undefined ? defaultHwm : Number(rawHwm);
                if (hwm !== hwm || hwm < 0) throw new RangeError("Invalid highWaterMark");
                return { hwm: hwm, size: size };
            }

            // Queuing strategies.
            function CountQueuingStrategy(init) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'CountQueuingStrategy': Please use the 'new' operator.");
                if (init === undefined || init === null || typeof init !== "object" && typeof init !== "function") throw new TypeError("Failed to construct 'CountQueuingStrategy': The provided value is not of type 'QueuingStrategyInit'.");
                if (init.highWaterMark === undefined) throw new TypeError("Failed to construct 'CountQueuingStrategy': required member highWaterMark is undefined.");
                Object.defineProperty(this, "highWaterMark", { value: Number(init.highWaterMark), writable: true, enumerable: true, configurable: true });
            }
            var countSize = { size() { return 1; } }.size;
            Object.defineProperty(CountQueuingStrategy.prototype, "size", { get: function () { return countSize; }, enumerable: true, configurable: true });
            tag(CountQueuingStrategy, "CountQueuingStrategy");
            function ByteLengthQueuingStrategy(init) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'ByteLengthQueuingStrategy': Please use the 'new' operator.");
                if (init === undefined || init === null || typeof init !== "object" && typeof init !== "function") throw new TypeError("Failed to construct 'ByteLengthQueuingStrategy': The provided value is not of type 'QueuingStrategyInit'.");
                if (init.highWaterMark === undefined) throw new TypeError("Failed to construct 'ByteLengthQueuingStrategy': required member highWaterMark is undefined.");
                Object.defineProperty(this, "highWaterMark", { value: Number(init.highWaterMark), writable: true, enumerable: true, configurable: true });
            }
            var byteLengthSize = { size(chunk) { return chunk.byteLength; } }.size;
            Object.defineProperty(ByteLengthQueuingStrategy.prototype, "size", { get: function () { return byteLengthSize; }, enumerable: true, configurable: true });
            tag(ByteLengthQueuingStrategy, "ByteLengthQueuingStrategy");

            // ---- ReadableStream
            var readableState = new WeakMap(), readableControllerState = new WeakMap(), readerState = new WeakMap();
            function readableOf(self) { var s = readableState.get(self); if (!s) throw new TypeError("Illegal invocation"); return s; }
            function desiredReadable(s) { return s.state === "errored" ? null : s.state === "closed" ? 0 : s.hwm - (s.bc ? s.bc.queueTotalSize : s.queueSize); }
            function shouldPull(s) {
                if (s.bc) return bcShouldPull(s);
                if (s.state !== "readable" || s.closeRequested || !s.started) return false;
                if (s.reader && s.reader.requests.length > 0) return true;
                return desiredReadable(s) > 0;
            }
            function callPull(s) {
                if (s.bc) { bcCallPull(s); return; }
                if (!shouldPull(s)) return;
                if (s.pulling) { s.pullAgain = true; return; }
                s.pulling = true;
                var p;
                try { p = Promise.resolve(s.pullFn ? callFn(s.pullFn, s.source, s.controller) : undefined); } catch (e) { p = Promise.reject(e); }
                p.then(function () {
                    s.pulling = false;
                    if (s.pullAgain) { s.pullAgain = false; callPull(s); }
                }, function (e) { errorReadable(s, e); });
            }
            function errorReadable(s, e) {
                if (s.state !== "readable") return;
                s.state = "errored"; s.stored = e; s.queue = []; s.queueSize = 0;
                var r = s.reader;
                if (r) {
                    r.requests.splice(0).forEach(function (q) { q.reject(e); });
                    if (r.readIntoRequests) r.readIntoRequests.splice(0).forEach(function (q) { q.reject(e); });
                    r.closed.reject(e); markHandled(r.closed.promise);
                }
            }
            function closeReadable(s) {
                s.state = "closed";
                var r = s.reader;
                if (r) {
                    r.requests.splice(0).forEach(function (q) { q.resolve({ value: undefined, done: true }); });
                    if (r.readIntoRequests) r.readIntoRequests.splice(0).forEach(function (q) { q.resolve({ value: undefined, done: true }); });
                    r.closed.resolve(undefined);
                }
            }
            function enqueueReadable(s, chunk) {
                if (s.closeRequested || s.state !== "readable") throw new TypeError("Cannot enqueue a chunk into a stream that is closed or errored");
                if (s.reader && s.reader.requests.length > 0) {
                    s.reader.requests.shift().resolve({ value: chunk, done: false });
                } else {
                    var size = 1;
                    if (s.size) {
                        try {
                            size = Number(callFn(s.size, undefined, chunk));
                            if (size !== size || size < 0 || size === Infinity) throw new RangeError("The return value of a queuing strategy's size function must be a finite, non-NaN, non-negative number");
                        } catch (e) { errorReadable(s, e); throw e; }
                    }
                    s.queue.push({ chunk: chunk, size: size }); s.queueSize += size;
                }
                callPull(s);
            }
            function readFrom(s, request) {
                s.disturbed = true;
                if (s.state === "closed") request.resolve({ value: undefined, done: true });
                else if (s.state === "errored") request.reject(s.stored);
                else if (s.bc) bcPullSteps(s.bc, request);
                else if (s.queue.length > 0) {
                    var item = s.queue.shift();
                    s.queueSize -= item.size;
                    if (s.queueSize < 0) s.queueSize = 0;
                    if (s.closeRequested && s.queue.length === 0) closeReadable(s); else callPull(s);
                    request.resolve({ value: item.chunk, done: false });
                } else {
                    s.reader.requests.push(request);
                    callPull(s);
                }
            }
            function cancelReadable(s, reason) {
                s.disturbed = true;
                if (s.state === "closed") return Promise.resolve(undefined);
                if (s.state === "errored") return Promise.reject(s.stored);
                s.queue = []; s.queueSize = 0;
                if (s.bc) { bcClearPendingPullIntos(s.bc); bcResetQueue(s.bc); }
                closeReadable(s);
                var p;
                try { p = Promise.resolve(s.cancelFn ? callFn(s.cancelFn, s.source, reason) : undefined); } catch (e) { p = Promise.reject(e); }
                return p.then(function () { return undefined; });
            }
            function ReadableStream(underlyingSource, strategy) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'ReadableStream': Please use the 'new' operator, this DOM object constructor cannot be called as a function.");
                if (underlyingSource === null) throw new TypeError("Failed to construct 'ReadableStream': The underlying source is null");
                var source = underlyingSource === undefined ? null : Object(underlyingSource);
                var type = source ? source.type : undefined;
                if (type !== undefined) { type = String(type); if (type !== "bytes") throw new TypeError("Failed to construct 'ReadableStream': Invalid type is specified"); }
                var q = strategyOf(strategy, type === "bytes" ? 0 : 1);
                if (type === "bytes" && q.size !== undefined) throw new RangeError("Failed to construct 'ReadableStream': The strategy for a byte stream cannot have a size function");
                var startFn = getMethod(source, "start"), pullFn = getMethod(source, "pull"), cancelFn = getMethod(source, "cancel");
                var autoAllocate;
                if (type === "bytes") {
                    autoAllocate = source.autoAllocateChunkSize;
                    if (autoAllocate !== undefined) {
                        autoAllocate = Number(autoAllocate);
                        if (!(autoAllocate > 0) || autoAllocate !== Math.trunc(autoAllocate) || autoAllocate === Infinity) throw new TypeError("autoAllocateChunkSize must be a positive integer");
                    }
                }
                var s = { state: "readable", queue: [], queueSize: 0, hwm: q.hwm, size: type === "bytes" ? undefined : q.size, reader: null, disturbed: false, stored: undefined, closeRequested: false, pulling: false, pullAgain: false, started: false, source: source, pullFn: pullFn, cancelFn: cancelFn, controller: null };
                readableState.set(this, s);
                if (type === "bytes") {
                    var byteController = Object.create(ReadableByteStreamController.prototype);
                    var bc = { stream: s, queue: [], queueTotalSize: 0, started: false, closeRequested: false, pulling: false, pullAgain: false, pullFn: pullFn, cancelFn: cancelFn, source: source,
                        byobRequest: null, pendingPullIntos: [], autoAllocateChunkSize: autoAllocate, controllerObject: byteController };
                    s.bc = bc;
                    byteControllerState.set(byteController, bc);
                    var byteResult = startFn ? callFn(startFn, source, byteController) : undefined;
                    Promise.resolve(byteResult).then(function () { bc.started = true; bcCallPull(s); }, function (e) { bcError(bc, e); });
                    return;
                }
                var controller = Object.create(ReadableStreamDefaultController.prototype);
                s.controller = controller;
                readableControllerState.set(controller, s);
                var result = startFn ? callFn(startFn, source, controller) : undefined;
                Promise.resolve(result).then(function () { s.started = true; callPull(s); }, function (e) { errorReadable(s, e); });
            }
            function ReadableStreamDefaultController() { throw new TypeError("Illegal constructor"); }
            function controllerOf(self) { var s = readableControllerState.get(self); if (!s) throw new TypeError("Illegal invocation"); return s; }
            readonly(ReadableStreamDefaultController.prototype, "desiredSize", function () { return desiredReadable(controllerOf(this)); });
            method(ReadableStreamDefaultController.prototype, "close", function close() {
                var s = controllerOf(this);
                if (s.closeRequested || s.state !== "readable") throw new TypeError("Cannot close a stream that is closed or errored");
                s.closeRequested = true;
                if (s.queue.length === 0) closeReadable(s);
            });
            method(ReadableStreamDefaultController.prototype, "enqueue", function enqueue(chunk) { enqueueReadable(controllerOf(this), chunk); });
            method(ReadableStreamDefaultController.prototype, "error", function error(e) { errorReadable(controllerOf(this), e); });
            tag(ReadableStreamDefaultController, "ReadableStreamDefaultController");

            function readerOf(self) { var r = readerState.get(self); if (!r) throw new TypeError("Illegal invocation"); return r; }
            function attachReader(reader, s) {
                if (s.reader) throw new TypeError("ReadableStream is locked");
                var r = { stream: s, requests: [], closed: deferred() };
                readerState.set(reader, r);
                s.reader = r;
                if (s.state === "closed") r.closed.resolve(undefined);
                else if (s.state === "errored") { r.closed.reject(s.stored); markHandled(r.closed.promise); }
            }
            function ReadableStreamDefaultReader(stream) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'ReadableStreamDefaultReader': Please use the 'new' operator.");
                if (!(stream instanceof ReadableStream)) throw new TypeError("Failed to construct 'ReadableStreamDefaultReader': parameter 1 is not of type 'ReadableStream'.");
                attachReader(this, readableOf(stream));
            }
            readonly(ReadableStreamDefaultReader.prototype, "closed", function () {
                var r = readerState.get(this);
                return r ? r.closed.promise : rejectedHandled(new TypeError("Illegal invocation"));
            });
            method(ReadableStreamDefaultReader.prototype, "read", function read() {
                var r = readerState.get(this);
                if (!r) return Promise.reject(new TypeError("Illegal invocation"));
                if (!r.stream) return Promise.reject(new TypeError("This readable stream reader has been released and cannot be used to read from its previous owner stream"));
                var d = deferred();
                readFrom(r.stream, d);
                return d.promise;
            });
            method(ReadableStreamDefaultReader.prototype, "cancel", function cancel(reason) {
                var r = readerState.get(this);
                if (!r) return Promise.reject(new TypeError("Illegal invocation"));
                if (!r.stream) return Promise.reject(new TypeError("This readable stream reader has been released and cannot be used to cancel its previous owner stream"));
                return cancelReadable(r.stream, reason);
            });
            function releaseReader(r) {
                if (!r.stream) return;
                var s = r.stream;
                var error = new TypeError("Reader was released");
                r.requests.splice(0).forEach(function (q) { q.reject(error); });
                if (r.readIntoRequests) r.readIntoRequests.splice(0).forEach(function (q) { q.reject(error); });
                if (s.bc) bcReleaseSteps(s.bc);
                if (s.state === "readable") r.closed.reject(error); else r.closed = deferred(), r.closed.reject(error);
                markHandled(r.closed.promise);
                s.reader = null; r.stream = null;
            }
            method(ReadableStreamDefaultReader.prototype, "releaseLock", function releaseLock() {
                var r = readerState.get(this);
                if (!r) throw new TypeError("Illegal invocation");
                releaseReader(r);
            });
            tag(ReadableStreamDefaultReader, "ReadableStreamDefaultReader");

            readonly(ReadableStream.prototype, "locked", function () { return readableOf(this).reader !== null; });
            method(ReadableStream.prototype, "cancel", function cancel(reason) {
                var s;
                try { s = readableOf(this); } catch (e) { return Promise.reject(e); }
                if (s.reader) return Promise.reject(new TypeError("Cannot cancel a stream that already has a reader"));
                return cancelReadable(s, reason);
            });
            method(ReadableStream.prototype, "getReader", function getReader(options) {
                var s = readableOf(this);
                if (options !== undefined && options !== null && !isObject(options)) throw new TypeError("Failed to execute 'getReader' on 'ReadableStream': The options are not an object");
                if (options !== undefined && options !== null && Object(options).mode !== undefined) {
                    if (String(Object(options).mode) !== "byob") throw new TypeError("Failed to execute 'getReader' on 'ReadableStream': The provided value is not a valid enum value of type ReadableStreamReaderMode.");
                    var byobReader = Object.create(ReadableStreamBYOBReader.prototype);
                    attachByobReader(byobReader, s);
                    return byobReader;
                }
                var reader = Object.create(ReadableStreamDefaultReader.prototype);
                attachReader(reader, s);
                return reader;
            });
            // The two branches of a stream.
            function teeReadable(stream) {
                var byteStream = !!readableOf(stream).bc;
                var reader = stream.getReader(), reading = false, again = false, canceled = [false, false], reasons = [undefined, undefined], controllers = [null, null];
                var cancelDone = deferred();
                function pull() {
                    if (reading) { again = true; return Promise.resolve(); }
                    reading = true;
                    reader.read().then(function (r) {
                        reading = false;
                        if (r.done) {
                            controllers.forEach(function (c, i) { if (!canceled[i] && c.desiredSize !== null) try { c.close(); } catch (e) {} });
                            if (!canceled[0] || !canceled[1]) cancelDone.resolve(undefined);
                            return;
                        }
                        controllers.forEach(function (c, i) { if (!canceled[i]) try { c.enqueue(byteStream ? new Uint8Array(r.value) : r.value); } catch (e) {} });
                        if (again) { again = false; pull(); }
                    }, function () { reading = false; });
                    return Promise.resolve();
                }
                function cancelBranch(index) {
                    return function (reason) {
                        canceled[index] = true; reasons[index] = reason;
                        if (canceled[0] && canceled[1]) cancelReadable(readableOf(stream), [reasons[0], reasons[1]]).then(cancelDone.resolve, cancelDone.reject);
                        return cancelDone.promise;
                    };
                }
                var branches = [0, 1].map(function (i) {
                    return new ReadableStream({ type: byteStream ? "bytes" : undefined, start: function (c) { controllers[i] = c; }, pull: pull, cancel: cancelBranch(i) });
                });
                reader.closed.then(undefined, function (e) { controllers.forEach(function (c) { try { c.error(e); } catch (x) {} }); if (!canceled[0] || !canceled[1]) cancelDone.resolve(undefined); });
                return branches;
            }
            method(ReadableStream.prototype, "tee", function tee() {
                var s = readableOf(this);
                if (s.reader) throw new TypeError("Failed to execute 'tee' on 'ReadableStream': Cannot tee a stream that already has a reader");
                return teeReadable(this);
            });
            // ReadableStreamPipeTo (the Streams standard, section 4.9): errors and closing travel along, and the pipe shuts down once the writes in flight have finished.
            function pipeOptions(options) {
                if (options !== undefined && options !== null && !isObject(options)) throw new TypeError("The options are not an object");
                options = options === undefined || options === null ? {} : options;
                var preventAbort = !!options.preventAbort, preventCancel = !!options.preventCancel, preventClose = !!options.preventClose, signal = options.signal;
                if (signal !== undefined && !(signal instanceof AbortSignal)) throw new TypeError("The signal option is not an AbortSignal");
                return { preventAbort: preventAbort, preventCancel: preventCancel, preventClose: preventClose, signal: signal };
            }
            function pipeInternal(source, destination, o) {
                var s = readableOf(source), d = writableOf(destination);
                if (s.reader) return Promise.reject(new TypeError("Cannot pipe a locked stream"));
                if (d.writer) return Promise.reject(new TypeError("Cannot pipe to a locked stream"));
                var readerObject = Object.create(ReadableStreamDefaultReader.prototype), writerObject = Object.create(WritableStreamDefaultWriter.prototype);
                attachReader(readerObject, s); attachWriter(writerObject, d);
                var r = readerState.get(readerObject), w = writerState.get(writerObject);
                s.disturbed = true;
                var shuttingDown = false, currentWrite = Promise.resolve(undefined), result = deferred(), abortAlgorithm = null;

                function finalize(isError, error) {
                    releaseWriter(w); releaseReader(r);
                    if (o.signal && abortAlgorithm) o.signal.removeEventListener("abort", abortAlgorithm);
                    if (isError) result.reject(error); else result.resolve(undefined);
                }
                function waitForWritesToFinish() {
                    var oldCurrentWrite = currentWrite;
                    return currentWrite.then(function () { return oldCurrentWrite !== currentWrite ? waitForWritesToFinish() : undefined; });
                }
                function shutdownWithAction(action, originalIsError, originalError) {
                    if (shuttingDown) return;
                    shuttingDown = true;
                    function doTheRest() {
                        promiseFrom(action).then(function () { finalize(originalIsError, originalError); }, function (e) { finalize(true, e); });
                    }
                    if (d.state === "writable" && !closeQueuedOrInFlight(d)) waitForWritesToFinish().then(doTheRest, doTheRest); else doTheRest();
                }
                function shutdown(isError, error) {
                    if (shuttingDown) return;
                    shuttingDown = true;
                    if (d.state === "writable" && !closeQueuedOrInFlight(d)) waitForWritesToFinish().then(function () { finalize(isError, error); }, function () { finalize(isError, error); });
                    else finalize(isError, error);
                }

                if (o.signal) {
                    abortAlgorithm = function () {
                        var error = o.signal.reason;
                        var actions = [];
                        if (!o.preventAbort) actions.push(function () { return d.state === "writable" ? abortWritable(d, error) : Promise.resolve(undefined); });
                        if (!o.preventCancel) actions.push(function () { return s.state === "readable" ? cancelReadable(s, error) : Promise.resolve(undefined); });
                        shutdownWithAction(function () { return Promise.all(actions.map(function (a) { return a(); })); }, true, error);
                    };
                    if (o.signal.aborted) { abortAlgorithm(); return result.promise; }
                    o.signal.addEventListener("abort", abortAlgorithm);
                }

                // Errors are propagated forward.
                function sourceErrored() {
                    if (!o.preventAbort) shutdownWithAction(function () { return abortWritable(d, s.stored); }, true, s.stored); else shutdown(true, s.stored);
                }
                if (s.state === "errored") sourceErrored(); else r.closed.promise.then(undefined, function () { sourceErrored(); });
                // Errors are propagated backward.
                function destErrored() {
                    if (!o.preventCancel) shutdownWithAction(function () { return cancelReadable(s, d.storedError); }, true, d.storedError); else shutdown(true, d.storedError);
                }
                if (d.state === "errored") destErrored(); else w.closed.promise.then(undefined, function () { destErrored(); });
                // Closing is propagated forward.
                function sourceClosed() {
                    if (!o.preventClose) shutdownWithAction(function () {
                        if (closeQueuedOrInFlight(d) || d.state === "closed") return Promise.resolve(undefined);
                        if (d.state === "errored") return Promise.reject(d.storedError);
                        return closeWritable(d);
                    }); else shutdown();
                }
                if (s.state === "closed") sourceClosed(); else r.closed.promise.then(function () { sourceClosed(); }, function () {});
                // Closing is propagated backward.
                if (closeQueuedOrInFlight(d) || d.state === "closed") {
                    var destClosed = new TypeError("The destination writable stream closed before all data could be piped to it");
                    if (!o.preventCancel) shutdownWithAction(function () { return cancelReadable(s, destClosed); }, true, destClosed); else shutdown(true, destClosed);
                }

                // The chunks: wait for the writer to want one, read it, write it.
                (function loop() {
                    if (shuttingDown) return;
                    w.ready.promise.then(function () {
                        if (shuttingDown) return;
                        readFrom(s, {
                            resolve: function (result) {
                                if (result.done) return;
                                Promise.resolve().then(function () {
                                var c = d.controller, size = chunkSizeOf(c, result.value);
                                if (d.state === "errored") currentWrite = Promise.reject(d.storedError);
                                else if (closeQueuedOrInFlight(d) || d.state === "closed") currentWrite = Promise.reject(new TypeError("Cannot write to a closing or closed stream"));
                                else if (d.state === "erroring") currentWrite = Promise.reject(d.storedError);
                                else { var request = deferred(); d.writeRequests.push(request); controllerWrite(c, result.value, size); currentWrite = request.promise; }
                                markHandled(currentWrite);
                                loop();
                                });
                            },
                            reject: function () {}
                        });
                    }, function () {});
                })();
                return result.promise;
            }
            method(ReadableStream.prototype, "pipeTo", function pipeTo(destination, options) {
                try {
                    readableOf(this);
                    if (!(destination instanceof WritableStream)) throw new TypeError("Failed to execute 'pipeTo' on 'ReadableStream': parameter 1 is not of type 'WritableStream'.");
                    writableOf(destination);
                    var o = pipeOptions(options);
                    return pipeInternal(this, destination, o);
                } catch (e) { return Promise.reject(e); }
            });
            method(ReadableStream.prototype, "pipeThrough", function pipeThrough(transform, options) {
                readableOf(this);
                if (!isObject(transform)) throw new TypeError("Failed to execute 'pipeThrough' on 'ReadableStream': parameter 1 is not of type 'ReadableWritablePair'.");
                var readable = transform.readable;
                if (!(readable instanceof ReadableStream)) throw new TypeError("Failed to execute 'pipeThrough' on 'ReadableStream': Failed to read the 'readable' property from 'ReadableWritablePair'");
                var writable = transform.writable;
                if (!(writable instanceof WritableStream)) throw new TypeError("Failed to execute 'pipeThrough' on 'ReadableStream': Failed to read the 'writable' property from 'ReadableWritablePair'");
                var o = pipeOptions(options);
                if (readableOf(this).reader) throw new TypeError("Failed to execute 'pipeThrough' on 'ReadableStream': Cannot pipe a locked stream");
                if (writableOf(writable).writer) throw new TypeError("Failed to execute 'pipeThrough' on 'ReadableStream': Cannot pipe to a locked stream");
                markHandled(pipeInternal(this, writable, o));
                return readable;
            });
            function iterate(stream, options) {
                var s = readableOf(stream);
                var preventCancel = !!(options !== undefined && options !== null && Object(options).preventCancel);
                var reader = stream.getReader(), finished = false, ongoing = null;
                var iterator = Object.create(asyncIteratorPrototype);
                function next() {
                    if (finished) return Promise.resolve({ value: undefined, done: true });
                    return reader.read().then(function (r) {
                        if (r.done) { finished = true; reader.releaseLock(); return { value: undefined, done: true }; }
                        return { value: r.value, done: false };
                    }, function (e) { finished = true; reader.releaseLock(); throw e; });
                }
                function ret(value) {
                    if (finished) return Promise.resolve({ value: value, done: true });
                    finished = true;
                    if (!preventCancel) { var c = reader.cancel(value); reader.releaseLock(); return c.then(function () { return { value: value, done: true }; }); }
                    reader.releaseLock();
                    return Promise.resolve({ value: value, done: true });
                }
                Object.defineProperty(iterator, "next", { value: function next_() {
                    ongoing = ongoing ? ongoing.then(next, next) : next();
                    return ongoing;
                }, writable: true, configurable: true, enumerable: true });
                Object.defineProperty(iterator, "return", { value: function return_(value) {
                    ongoing = ongoing ? ongoing.then(function () { return ret(value); }, function () { return ret(value); }) : ret(value);
                    return ongoing;
                }, writable: true, configurable: true, enumerable: true });
                return iterator;
            }
            var asyncIteratorPrototype;
            try { asyncIteratorPrototype = Object.getPrototypeOf(Object.getPrototypeOf(async function* () {}.prototype)); } catch (e) { asyncIteratorPrototype = {}; }
            if (!(Symbol.asyncIterator in asyncIteratorPrototype)) Object.defineProperty(asyncIteratorPrototype, Symbol.asyncIterator, { value: function () { return this; }, writable: true, configurable: true });
            Object.defineProperty(asyncIteratorPrototype, Symbol.toStringTag, { value: "ReadableStream AsyncIterator", configurable: true });
            method(ReadableStream.prototype, "values", function values(options) { return iterate(this, options); });
            Object.defineProperty(ReadableStream.prototype, Symbol.asyncIterator, { value: ReadableStream.prototype.values, writable: true, configurable: true });
            method(ReadableStream, "from", function from(asyncIterable) {
                if (asyncIterable === undefined || asyncIterable === null || typeof asyncIterable === "string") throw new TypeError("Failed to execute 'from' on 'ReadableStream': The provided value is not iterable");
                var method_ = asyncIterable[Symbol.asyncIterator], iterator, sync = false;
                if (method_ === undefined || method_ === null) {
                    method_ = asyncIterable[Symbol.iterator]; sync = true;
                    if (method_ === undefined || method_ === null) throw new TypeError("Failed to execute 'from' on 'ReadableStream': The provided value is not iterable");
                }
                if (typeof method_ !== "function") throw new TypeError("Failed to execute 'from' on 'ReadableStream': The provided value is not iterable");
                iterator = method_.call(asyncIterable);
                if (!isObject(iterator)) throw new TypeError("The iterator is not an object");
                var nextMethod = iterator.next, this_;
                return new ReadableStream({
                    pull: function () {
                        return Promise.resolve(nextMethod.call(iterator)).then(function (r) {
                            if (!isObject(r)) throw new TypeError("The iterator result is not an object");
                            if (r.done) { this_.close(); return; }
                            if (sync) return Promise.resolve(r.value).then(function (v) { this_.enqueue(v); });
                            this_.enqueue(r.value);
                        });
                    },
                    cancel: function (reason) {
                        var ret = iterator["return"];
                        if (ret === undefined || ret === null) return undefined;
                        return Promise.resolve(ret.call(iterator, reason)).then(function (r) { if (!isObject(r)) throw new TypeError("The iterator result is not an object"); });
                    },
                    start: function (c) { this_ = c; }
                }, { highWaterMark: 0 });
            });
            tag(ReadableStream, "ReadableStream");

            // ---- byte streams (the Streams standard, sections 4.7 to 4.10): the controller, BYOB reader and request
            function isDetached(buffer) { return buffer.detached === true; }
            function transferBuffer(buffer) {
                if (isDetached(buffer)) throw new TypeError("The buffer is detached");
                return buffer.transfer();
            }
            function isView(v) { return ArrayBuffer.isView(v); }
            function elementSizeOf(view) { return view instanceof DataView ? 1 : view.BYTES_PER_ELEMENT; }
            function viewOver(ctor, buffer, byteOffset, byteLength, elementSize) {
                return ctor === DataView ? new DataView(buffer, byteOffset, byteLength) : new ctor(buffer, byteOffset, byteLength / elementSize);
            }
            var byobRequestState = new WeakMap();

            function bcClearAlgorithms(c) { c.pullFn = undefined; c.cancelFn = undefined; }
            function bcResetQueue(c) { c.queue = []; c.queueTotalSize = 0; }
            function bcInvalidateByobRequest(c) {
                if (c.byobRequest === null) return;
                var st = byobRequestState.get(c.byobRequest);
                st.controller = undefined; st.view = null;
                c.byobRequest = null;
            }
            function bcClearPendingPullIntos(c) { bcInvalidateByobRequest(c); c.pendingPullIntos = []; }
            function bcHasDefaultReader(s) { return !!s.reader && !s.reader.byob; }
            function bcHasByobReader(s) { return !!s.reader && !!s.reader.byob; }
            function bcShouldPull(s) {
                var c = s.bc;
                if (s.state !== "readable" || c.closeRequested || !c.started) return false;
                if (bcHasDefaultReader(s) && s.reader.requests.length > 0) return true;
                if (bcHasByobReader(s) && s.reader.readIntoRequests.length > 0) return true;
                return desiredReadable(s) > 0;
            }
            function bcCallPull(s) {
                var c = s.bc;
                if (!bcShouldPull(s)) return;
                if (c.pulling) { c.pullAgain = true; return; }
                c.pulling = true;
                var result = promiseFrom(function () { return c.pullFn ? callFn(c.pullFn, c.source, c.controllerObject) : undefined; });
                result.then(function () {
                    c.pulling = false;
                    if (c.pullAgain) { c.pullAgain = false; bcCallPull(s); }
                }, function (e) { bcError(c, e); });
            }
            function bcError(c, e) {
                var s = c.stream;
                if (s.state !== "readable") return;
                bcClearPendingPullIntos(c);
                bcResetQueue(c);
                bcClearAlgorithms(c);
                errorReadable(s, e);
            }
            function bcEnqueueChunk(c, buffer, byteOffset, byteLength) {
                c.queue.push({ buffer: buffer, byteOffset: byteOffset, byteLength: byteLength });
                c.queueTotalSize += byteLength;
            }
            function bcEnqueueCloned(c, buffer, byteOffset, byteLength) {
                var clone;
                try { clone = buffer.slice(byteOffset, byteOffset + byteLength); } catch (e) { bcError(c, e); throw e; }
                bcEnqueueChunk(c, clone, 0, byteLength);
            }
            function bcEnqueueDetachedPullInto(c, d) {
                if (d.bytesFilled > 0) bcEnqueueCloned(c, d.buffer, d.byteOffset, d.bytesFilled);
                bcShiftPendingPullInto(c);
            }
            function bcShiftPendingPullInto(c) { var d = c.pendingPullIntos.shift(); bcInvalidateByobRequest(c); return d; }
            function bcHandleQueueDrain(c) {
                var s = c.stream;
                if (c.queueTotalSize === 0 && c.closeRequested) { bcClearAlgorithms(c); closeReadable(s); }
                else bcCallPull(s);
            }
            function bcFillReadRequest(c, request) {
                var entry = c.queue.shift();
                c.queueTotalSize -= entry.byteLength;
                bcHandleQueueDrain(c);
                request.resolve({ value: new Uint8Array(entry.buffer, entry.byteOffset, entry.byteLength), done: false });
            }
            function bcProcessReadRequests(c) {
                var r = c.stream.reader;
                while (r.requests.length > 0) {
                    if (c.queueTotalSize === 0) return;
                    bcFillReadRequest(c, r.requests.shift());
                }
            }
            function bcFillHeadPullInto(c, size, d) { bcInvalidateByobRequest(c); d.bytesFilled += size; }
            function bcFillPullIntoFromQueue(c, d) {
                var maxToCopy = Math.min(c.queueTotalSize, d.byteLength - d.bytesFilled);
                var maxFilled = d.bytesFilled + maxToCopy, remaining = maxToCopy, ready = false;
                var maxAligned = maxFilled - (maxFilled % d.elementSize);
                if (maxAligned >= d.minimumFill) { remaining = maxAligned - d.bytesFilled; ready = true; }
                while (remaining > 0) {
                    var head = c.queue[0], toCopy = Math.min(remaining, head.byteLength), destStart = d.byteOffset + d.bytesFilled;
                    new Uint8Array(d.buffer, destStart, toCopy).set(new Uint8Array(head.buffer, head.byteOffset, toCopy));
                    if (head.byteLength === toCopy) c.queue.shift(); else { head.byteOffset += toCopy; head.byteLength -= toCopy; }
                    c.queueTotalSize -= toCopy;
                    bcFillHeadPullInto(c, toCopy, d);
                    remaining -= toCopy;
                }
                return ready;
            }
            function bcConvertPullInto(d) {
                return viewOver(d.viewConstructor, d.buffer, d.byteOffset, d.bytesFilled, d.elementSize);
            }
            function bcCommitPullInto(s, d) {
                var done = s.state === "closed";
                var view = bcConvertPullInto(d);
                var r = s.reader;
                if (d.readerType === "default") r.requests.shift().resolve({ value: view, done: done });
                else r.readIntoRequests.shift().resolve({ value: view, done: done });
            }
            function bcProcessPullIntos(c) {
                while (c.pendingPullIntos.length > 0) {
                    if (c.queueTotalSize === 0) return;
                    var d = c.pendingPullIntos[0];
                    if (bcFillPullIntoFromQueue(c, d)) { bcShiftPendingPullInto(c); bcCommitPullInto(c.stream, d); }
                }
            }
            function bcEnqueue(c, chunk) {
                var s = c.stream;
                if (c.closeRequested || s.state !== "readable") throw new TypeError("Cannot enqueue a chunk into a stream that is closed or errored");
                var buffer = chunk.buffer, byteOffset = chunk.byteOffset, byteLength = chunk.byteLength;
                if (isDetached(buffer)) throw new TypeError("The chunk's buffer is detached");
                var transferred = transferBuffer(buffer);
                if (c.pendingPullIntos.length > 0) {
                    var first = c.pendingPullIntos[0];
                    if (isDetached(first.buffer)) throw new TypeError("The pending pull-into buffer is detached");
                    bcInvalidateByobRequest(c);
                    first.buffer = transferBuffer(first.buffer);
                    if (first.readerType === "none") bcEnqueueDetachedPullInto(c, first);
                }
                if (bcHasDefaultReader(s)) {
                    bcProcessReadRequests(c);
                    if (s.reader.requests.length === 0) bcEnqueueChunk(c, transferred, byteOffset, byteLength);
                    else {
                        if (c.pendingPullIntos.length > 0) bcShiftPendingPullInto(c);
                        s.reader.requests.shift().resolve({ value: new Uint8Array(transferred, byteOffset, byteLength), done: false });
                    }
                } else if (bcHasByobReader(s)) {
                    bcEnqueueChunk(c, transferred, byteOffset, byteLength);
                    bcProcessPullIntos(c);
                } else bcEnqueueChunk(c, transferred, byteOffset, byteLength);
                bcCallPull(s);
            }
            function bcClose(c) {
                var s = c.stream;
                if (c.closeRequested || s.state !== "readable") return;
                if (c.queueTotalSize > 0) { c.closeRequested = true; return; }
                if (c.pendingPullIntos.length > 0) {
                    var first = c.pendingPullIntos[0];
                    if (first.bytesFilled % first.elementSize !== 0) { var e = new TypeError("Insufficient bytes to fill elements in the given buffer"); bcError(c, e); throw e; }
                }
                bcClearAlgorithms(c);
                closeReadable(s);
            }
            function bcRespondInternal(c, bytesWritten) {
                var d = c.pendingPullIntos[0];
                bcInvalidateByobRequest(c);
                var s = c.stream;
                if (s.state === "closed") {
                    if (d.readerType === "none") bcShiftPendingPullInto(c);
                    if (bcHasByobReader(s)) {
                        while (s.reader.readIntoRequests.length > 0) bcCommitPullInto(s, bcShiftPendingPullInto(c));
                    }
                } else {
                    bcFillHeadPullInto(c, bytesWritten, d);
                    if (d.readerType === "none") { bcEnqueueDetachedPullInto(c, d); bcProcessPullIntos(c); }
                    else if (d.bytesFilled >= d.minimumFill) {
                        bcShiftPendingPullInto(c);
                        var remainder = d.bytesFilled % d.elementSize;
                        if (remainder > 0) {
                            var end = d.byteOffset + d.bytesFilled;
                            bcEnqueueCloned(c, d.buffer, end - remainder, remainder);
                            d.bytesFilled -= remainder;
                        }
                        bcCommitPullInto(s, d);
                        bcProcessPullIntos(c);
                    }
                }
                bcCallPull(s);
            }
            function bcRespond(c, bytesWritten) {
                var d = c.pendingPullIntos[0], state = c.stream.state;
                if (state === "closed") { if (bytesWritten !== 0) throw new TypeError("bytesWritten must be 0 when the stream is closed"); }
                else {
                    if (bytesWritten === 0) throw new TypeError("bytesWritten must be greater than 0 when the stream is readable");
                    if (d.bytesFilled + bytesWritten > d.byteLength) throw new RangeError("bytesWritten out of range");
                }
                d.buffer = transferBuffer(d.buffer);
                bcRespondInternal(c, bytesWritten);
            }
            function bcRespondWithNewView(c, view) {
                var d = c.pendingPullIntos[0], state = c.stream.state;
                if (state === "closed") { if (view.byteLength !== 0) throw new TypeError("The view's length must be 0 when the stream is closed"); }
                else if (view.byteLength === 0) throw new TypeError("The view's length must be greater than 0 when the stream is readable");
                if (d.byteOffset + d.bytesFilled !== view.byteOffset) throw new RangeError("The region specified by view does not match byobRequest");
                if (d.bufferByteLength !== view.buffer.byteLength) throw new RangeError("The buffer of view has different capacity than byobRequest");
                if (d.bytesFilled + view.byteLength > d.byteLength) throw new RangeError("The region specified by view is larger than byobRequest");
                var viewByteLength = view.byteLength;
                d.buffer = transferBuffer(view.buffer);
                bcRespondInternal(c, viewByteLength);
            }
            function bcByobRequest(c) {
                if (c.byobRequest === null && c.pendingPullIntos.length > 0) {
                    var first = c.pendingPullIntos[0];
                    var view = new Uint8Array(first.buffer, first.byteOffset + first.bytesFilled, first.byteLength - first.bytesFilled);
                    var request = Object.create(ReadableStreamBYOBRequest.prototype);
                    byobRequestState.set(request, { controller: c, view: view });
                    c.byobRequest = request;
                }
                return c.byobRequest;
            }
            function bcPullInto(c, view, min, request) {
                var s = c.stream;
                var elementSize = elementSizeOf(view), ctor = view instanceof DataView ? DataView : view.constructor;
                var minimumFill = min * elementSize, byteOffset = view.byteOffset, byteLength = view.byteLength;
                var buffer;
                try { buffer = transferBuffer(view.buffer); } catch (e) { request.reject(e); return; }
                var d = { buffer: buffer, bufferByteLength: buffer.byteLength, byteOffset: byteOffset, byteLength: byteLength, bytesFilled: 0, minimumFill: minimumFill, elementSize: elementSize, viewConstructor: ctor, readerType: "byob" };
                if (c.pendingPullIntos.length > 0) { c.pendingPullIntos.push(d); s.reader.readIntoRequests.push(request); return; }
                if (s.state === "closed") { request.resolve({ value: viewOver(ctor, d.buffer, byteOffset, 0, elementSize), done: true }); return; }
                if (c.queueTotalSize > 0) {
                    if (bcFillPullIntoFromQueue(c, d)) {
                        var filled = bcConvertPullInto(d);
                        bcHandleQueueDrain(c);
                        request.resolve({ value: filled, done: false });
                        return;
                    }
                    if (c.closeRequested) { var e = new TypeError("Insufficient bytes to fill elements in the given buffer"); bcError(c, e); request.reject(e); return; }
                }
                c.pendingPullIntos.push(d);
                s.reader.readIntoRequests.push(request);
                bcCallPull(s);
            }
            // The read request of a default reader on a byte stream.
            function bcPullSteps(c, request) {
                var s = c.stream;
                if (c.queueTotalSize > 0) { bcFillReadRequest(c, request); return; }
                if (c.autoAllocateChunkSize !== undefined) {
                    var buffer;
                    try { buffer = new ArrayBuffer(c.autoAllocateChunkSize); } catch (e) { request.reject(e); return; }
                    c.pendingPullIntos.push({ buffer: buffer, bufferByteLength: c.autoAllocateChunkSize, byteOffset: 0, byteLength: c.autoAllocateChunkSize, bytesFilled: 0, minimumFill: 1, elementSize: 1, viewConstructor: Uint8Array, readerType: "default" });
                }
                s.reader.requests.push(request);
                bcCallPull(s);
            }
            function bcReleaseSteps(c) {
                if (c.pendingPullIntos.length > 0) { var first = c.pendingPullIntos[0]; first.readerType = "none"; c.pendingPullIntos = [first]; }
            }

            function ReadableStreamBYOBRequest() { throw new TypeError("Illegal constructor"); }
            function byobRequestOf(self) { var st = byobRequestState.get(self); if (!st) throw new TypeError("Illegal invocation"); return st; }
            readonly(ReadableStreamBYOBRequest.prototype, "view", function () { return byobRequestOf(this).view; });
            method(ReadableStreamBYOBRequest.prototype, "respond", function respond(bytesWritten) {
                var st = byobRequestOf(this);
                if (arguments.length < 1) throw new TypeError("1 argument required, but only 0 present.");
                bytesWritten = Number(bytesWritten);
                if (!(bytesWritten >= 0) || bytesWritten === Infinity) throw new TypeError("bytesWritten is not a valid length");
                if (st.controller === undefined) throw new TypeError("This BYOB request has been invalidated");
                if (isDetached(st.view.buffer)) throw new TypeError("The BYOB request's buffer has been detached");
                bcRespond(st.controller, Math.trunc(bytesWritten));
            });
            method(ReadableStreamBYOBRequest.prototype, "respondWithNewView", function respondWithNewView(view) {
                var st = byobRequestOf(this);
                if (!isView(view)) throw new TypeError("The view is not an ArrayBuffer view");
                if (st.controller === undefined) throw new TypeError("This BYOB request has been invalidated");
                if (isDetached(view.buffer)) throw new TypeError("The view's buffer has been detached");
                bcRespondWithNewView(st.controller, view);
            });
            tag(ReadableStreamBYOBRequest, "ReadableStreamBYOBRequest");

            var byteControllerState = new WeakMap();
            function ReadableByteStreamController() { throw new TypeError("Illegal constructor"); }
            function byteControllerOf(self) { var c = byteControllerState.get(self); if (!c) throw new TypeError("Illegal invocation"); return c; }
            readonly(ReadableByteStreamController.prototype, "byobRequest", function () { return bcByobRequest(byteControllerOf(this)); });
            readonly(ReadableByteStreamController.prototype, "desiredSize", function () { return desiredReadable(byteControllerOf(this).stream); });
            method(ReadableByteStreamController.prototype, "close", function close() {
                var c = byteControllerOf(this);
                if (c.closeRequested) throw new TypeError("The stream has already been requested to close");
                if (c.stream.state !== "readable") throw new TypeError("The stream is not in a state that permits close");
                bcClose(c);
            });
            method(ReadableByteStreamController.prototype, "enqueue", function enqueue(chunk) {
                var c = byteControllerOf(this);
                if (arguments.length < 1) throw new TypeError("1 argument required, but only 0 present.");
                if (!isView(chunk)) throw new TypeError("The chunk is not an ArrayBuffer view");
                if (chunk.byteLength === 0) throw new TypeError("chunk must have non-zero byteLength");
                if (chunk.buffer.byteLength === 0) throw new TypeError("chunk's buffer must have non-zero byteLength");
                bcEnqueue(c, chunk);
            });
            method(ReadableByteStreamController.prototype, "error", function error(e) { bcError(byteControllerOf(this), e); });
            tag(ReadableByteStreamController, "ReadableByteStreamController");

            // The BYOB reader.
            function ReadableStreamBYOBReader(stream) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'ReadableStreamBYOBReader': Please use the 'new' operator.");
                if (!(stream instanceof ReadableStream)) throw new TypeError("Failed to construct 'ReadableStreamBYOBReader': parameter 1 is not of type 'ReadableStream'.");
                attachByobReader(this, readableOf(stream));
            }
            function attachByobReader(reader, s) {
                if (!s.bc) throw new TypeError("Cannot construct a ReadableStreamBYOBReader for a stream not constructed with a byte source");
                attachReader(reader, s);
                s.reader.byob = true; s.reader.readIntoRequests = [];
            }
            readonly(ReadableStreamBYOBReader.prototype, "closed", function () {
                var r = readerState.get(this);
                return r ? r.closed.promise : rejectedHandled(new TypeError("Illegal invocation"));
            });
            method(ReadableStreamBYOBReader.prototype, "read", function read(view, options) {
                var r = readerState.get(this);
                if (!r || !r.byob) return Promise.reject(new TypeError("Illegal invocation"));
                if (!isView(view)) return Promise.reject(new TypeError("The view is not an ArrayBuffer view"));
                if (view.byteLength === 0) return Promise.reject(new TypeError("view must have non-zero byteLength"));
                if (view.buffer.byteLength === 0) return Promise.reject(new TypeError("view's buffer must have non-zero byteLength"));
                if (isDetached(view.buffer)) return Promise.reject(new TypeError("view's buffer has been detached"));
                var min = 1;
                if (options !== undefined && options !== null && !isObject(options)) return Promise.reject(new TypeError("The options are not an object"));
                if (options !== undefined && options !== null && options.min !== undefined) {
                    min = Number(options.min);
                    if (min !== min || min === Infinity || min === -Infinity) return Promise.reject(new TypeError("min is not a finite number"));
                    min = Math.trunc(min);
                    if (min < 0 || min > 18446744073709551615) return Promise.reject(new TypeError("min is out of range"));
                }
                if (min === 0) return Promise.reject(new TypeError("options.min must be greater than 0"));
                var length = view instanceof DataView ? view.byteLength : view.length;
                if (min > length) return Promise.reject(new RangeError("options.min must be less than or equal to view's length"));
                if (!r.stream) return Promise.reject(new TypeError("This readable stream reader has been released and cannot be used to read from its previous owner stream"));
                var d = deferred(), s = r.stream;
                s.disturbed = true;
                if (s.state === "errored") d.reject(s.stored);
                else bcPullInto(s.bc, view, min, d);
                return d.promise;
            });
            method(ReadableStreamBYOBReader.prototype, "cancel", function cancel(reason) {
                var r = readerState.get(this);
                if (!r || !r.byob) return Promise.reject(new TypeError("Illegal invocation"));
                if (!r.stream) return Promise.reject(new TypeError("This readable stream reader has been released and cannot be used to cancel its previous owner stream"));
                return cancelReadable(r.stream, reason);
            });
            method(ReadableStreamBYOBReader.prototype, "releaseLock", function releaseLock() {
                var r = readerState.get(this);
                if (!r || !r.byob) throw new TypeError("Illegal invocation");
                releaseReader(r);
            });
            tag(ReadableStreamBYOBReader, "ReadableStreamBYOBReader");

            // ---- WritableStream (the Streams standard's algorithms, section 5)
            var writableState = new WeakMap(), writableControllerState = new WeakMap(), writerState = new WeakMap();
            var closeSentinel = {};
            function writableOf(self) { var s = writableState.get(self); if (!s) throw new TypeError("Illegal invocation"); return s; }
            function writerOf(self) { var w = writerState.get(self); if (!w) throw new TypeError("Illegal invocation"); return w; }
            function writableControllerOf(self) { var s = writableControllerState.get(self); if (!s) throw new TypeError("Illegal invocation"); return s; }

            function closeQueuedOrInFlight(s) { return s.closeRequest !== undefined || s.inFlightCloseRequest !== undefined; }
            function hasOperationMarkedInFlight(s) { return s.inFlightWriteRequest !== undefined || s.inFlightCloseRequest !== undefined; }
            function wcDesiredSize(c) { return c.hwm - c.queueTotalSize; }
            function wcBackpressure(c) { return wcDesiredSize(c) <= 0; }
            function resetQueue(c) { c.queue = []; c.queueTotalSize = 0; }
            function enqueueWithSize(c, value, size) {
                size = Number(size);
                if (size !== size || size < 0 || size === Infinity) throw new RangeError("The return value of a queuing strategy's size function must be a finite, non-NaN, non-negative number");
                c.queue.push({ value: value, size: size }); c.queueTotalSize += size;
            }
            function dequeueValue(c) { var pair = c.queue.shift(); c.queueTotalSize -= pair.size; if (c.queueTotalSize < 0) c.queueTotalSize = 0; return pair.value; }
            function peekQueueValue(c) { return c.queue[0].value; }
            function promiseFrom(fn) { try { return Promise.resolve(fn()); } catch (e) { return Promise.reject(e); } }

            function updateBackpressure(s, backpressure) {
                if (s.writer && backpressure !== s.backpressure) {
                    if (backpressure) s.writer.ready = deferred(); else s.writer.ready.resolve(undefined);
                }
                s.backpressure = backpressure;
            }
            function rejectCloseAndClosedPromiseIfNeeded(s) {
                if (s.closeRequest !== undefined) { s.closeRequest.reject(s.storedError); s.closeRequest = undefined; }
                if (s.writer) { s.writer.closed.reject(s.storedError); markHandled(s.writer.closed.promise); }
            }
            function startErroring(s, reason) {
                s.state = "erroring"; s.storedError = reason;
                if (s.writer) ensureReadyRejected(s.writer, reason);
                if (!hasOperationMarkedInFlight(s) && s.controller.started) finishErroring(s);
            }
            function finishErroring(s) {
                s.state = "errored";
                resetQueue(s.controller);
                var stored = s.storedError;
                s.writeRequests.forEach(function (r) { r.reject(stored); });
                s.writeRequests = [];
                if (s.pendingAbortRequest === undefined) { rejectCloseAndClosedPromiseIfNeeded(s); return; }
                var abortRequest = s.pendingAbortRequest;
                s.pendingAbortRequest = undefined;
                if (abortRequest.wasAlreadyErroring) { abortRequest.promise.reject(stored); rejectCloseAndClosedPromiseIfNeeded(s); return; }
                var c = s.controller;
                var result = promiseFrom(function () { return c.abortFn ? callFn(c.abortFn, c.sink, abortRequest.reason) : undefined; });
                clearAlgorithms(c);
                result.then(function () { abortRequest.promise.resolve(undefined); rejectCloseAndClosedPromiseIfNeeded(s); },
                    function (e) { abortRequest.promise.reject(e); rejectCloseAndClosedPromiseIfNeeded(s); });
            }
            function dealWithRejection(s, error) {
                if (s.state === "writable") { startErroring(s, error); return; }
                finishErroring(s);
            }
            function finishInFlightWrite(s) { s.inFlightWriteRequest.resolve(undefined); s.inFlightWriteRequest = undefined; }
            function finishInFlightWriteWithError(s, error) { s.inFlightWriteRequest.reject(error); s.inFlightWriteRequest = undefined; dealWithRejection(s, error); }
            function finishInFlightClose(s) {
                s.inFlightCloseRequest.resolve(undefined); s.inFlightCloseRequest = undefined;
                if (s.state === "erroring") {
                    s.storedError = undefined;
                    if (s.pendingAbortRequest !== undefined) { s.pendingAbortRequest.promise.resolve(undefined); s.pendingAbortRequest = undefined; }
                }
                s.state = "closed";
                if (s.writer) s.writer.closed.resolve(undefined);
            }
            function finishInFlightCloseWithError(s, error) {
                s.inFlightCloseRequest.reject(error); s.inFlightCloseRequest = undefined;
                if (s.pendingAbortRequest !== undefined) { s.pendingAbortRequest.promise.reject(error); s.pendingAbortRequest = undefined; }
                dealWithRejection(s, error);
            }
            function abortWritable(s, reason) {
                if (s.state === "closed" || s.state === "errored") return Promise.resolve(undefined);
                s.controller.abortController.abort(reason);
                if (s.state === "closed" || s.state === "errored") return Promise.resolve(undefined);
                if (s.pendingAbortRequest !== undefined) return s.pendingAbortRequest.promise.promise;
                var wasAlreadyErroring = s.state === "erroring";
                if (wasAlreadyErroring) reason = undefined;
                var promise = deferred();
                s.pendingAbortRequest = { promise: promise, reason: reason, wasAlreadyErroring: wasAlreadyErroring };
                if (!wasAlreadyErroring) startErroring(s, reason);
                return promise.promise;
            }
            function closeWritable(s) {
                if (s.state === "closed" || s.state === "errored") return Promise.reject(new TypeError("The stream is not in a state that permits close"));
                var promise = deferred();
                s.closeRequest = promise;
                if (s.writer && s.backpressure && s.state === "writable") s.writer.ready.resolve(undefined);
                // The controller's close
                enqueueWithSize(s.controller, closeSentinel, 0);
                advanceQueueIfNeeded(s.controller);
                return promise.promise;
            }
            function clearAlgorithms(c) { c.writeFn = undefined; c.closeFn = undefined; c.abortFn = undefined; c.sizeFn = undefined; }
            function errorIfNeeded(c, error) { if (c.stream.state === "writable") controllerError(c, error); }
            function controllerError(c, error) { clearAlgorithms(c); startErroring(c.stream, error); }
            function advanceQueueIfNeeded(c) {
                var s = c.stream;
                if (!c.started) return;
                if (s.inFlightWriteRequest !== undefined) return;
                if (s.state === "closed" || s.state === "errored") return;
                if (s.state === "erroring") { finishErroring(s); return; }
                if (c.queue.length === 0) return;
                var value = peekQueueValue(c);
                if (value === closeSentinel) processClose(c); else processWrite(c, value);
            }
            function processClose(c) {
                var s = c.stream;
                s.inFlightCloseRequest = s.closeRequest; s.closeRequest = undefined;
                dequeueValue(c);
                var closeFn = c.closeFn, sink = c.sink;
                var result = promiseFrom(function () { return closeFn ? callFn(closeFn, sink) : undefined; });
                clearAlgorithms(c);
                result.then(function () { finishInFlightClose(s); }, function (e) { finishInFlightCloseWithError(s, e); });
            }
            function processWrite(c, chunk) {
                var s = c.stream;
                s.inFlightWriteRequest = s.writeRequests.shift();
                var result = promiseFrom(function () { return c.writeFn ? callFn(c.writeFn, c.sink, chunk, c.controllerObject) : undefined; });
                result.then(function () {
                    finishInFlightWrite(s);
                    var state = s.state;
                    dequeueValue(c);
                    if (!closeQueuedOrInFlight(s) && state === "writable") updateBackpressure(s, wcBackpressure(c));
                    advanceQueueIfNeeded(c);
                }, function (e) {
                    if (s.state === "writable") clearAlgorithms(c);
                    finishInFlightWriteWithError(s, e);
                });
            }
            function chunkSizeOf(c, chunk) {
                if (!c.sizeFn) return 1;
                try { return callFn(c.sizeFn, undefined, chunk); } catch (e) { errorIfNeeded(c, e); return 1; }
            }
            function controllerWrite(c, chunk, size) {
                try { enqueueWithSize(c, chunk, size); } catch (e) { errorIfNeeded(c, e); return; }
                var s = c.stream;
                if (!closeQueuedOrInFlight(s) && s.state === "writable") updateBackpressure(s, wcBackpressure(c));
                advanceQueueIfNeeded(c);
            }

            function WritableStream(underlyingSink, strategy) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'WritableStream': Please use the 'new' operator, this DOM object constructor cannot be called as a function.");
                var sink = underlyingSink === undefined || underlyingSink === null ? null : Object(underlyingSink);
                if (underlyingSink === undefined) sink = null;
                if (sink && sink.type !== undefined) throw new RangeError("Failed to construct 'WritableStream': Invalid type is specified");
                var q = strategyOf(strategy, 1);
                var startFn = getMethod(sink, "start"), writeFn = getMethod(sink, "write"), closeFn = getMethod(sink, "close"), abortFn = getMethod(sink, "abort");
                var s = { state: "writable", storedError: undefined, writer: undefined, controller: undefined, inFlightWriteRequest: undefined, closeRequest: undefined, inFlightCloseRequest: undefined,
                    pendingAbortRequest: undefined, writeRequests: [], backpressure: false };
                writableState.set(this, s);
                var c = { stream: s, queue: [], queueTotalSize: 0, started: false, hwm: q.hwm, sizeFn: q.size, sink: sink, writeFn: writeFn, closeFn: closeFn, abortFn: abortFn, abortController: new AbortController() };
                var controller = Object.create(WritableStreamDefaultController.prototype);
                c.controllerObject = controller;
                s.controller = c;
                writableControllerState.set(controller, c);
                updateBackpressure(s, wcBackpressure(c));
                var startResult;
                try { startResult = startFn ? callFn(startFn, sink, controller) : undefined; } catch (e) { throw e; }
                Promise.resolve(startResult).then(function () { c.started = true; advanceQueueIfNeeded(c); }, function (e) { c.started = true; dealWithRejection(s, e); });
            }
            function WritableStreamDefaultController() { throw new TypeError("Illegal constructor"); }
            readonly(WritableStreamDefaultController.prototype, "signal", function () { return writableControllerOf(this).abortController.signal; });
            method(WritableStreamDefaultController.prototype, "error", function error(e) {
                var c = writableControllerOf(this);
                if (c.stream.state !== "writable") return;
                controllerError(c, e);
            });
            tag(WritableStreamDefaultController, "WritableStreamDefaultController");

            // ---- the writer
            function ensureReadyRejected(w, error) {
                if (!w.ready.settled) w.ready.reject(error);
                else { w.ready = deferred(); w.ready.reject(error); }
                markHandled(w.ready.promise);
            }
            function ensureClosedRejected(w, error) {
                if (!w.closed.settled) w.closed.reject(error);
                else { w.closed = deferred(); w.closed.reject(error); }
                markHandled(w.closed.promise);
            }
            function attachWriter(writer, s) {
                if (s.writer) throw new TypeError("This stream has already been locked for exclusive writing by another writer");
                var w = { stream: s, closed: deferred(), ready: deferred() };
                writerState.set(writer, w);
                s.writer = w;
                if (s.state === "writable") {
                    if (!closeQueuedOrInFlight(s) && s.backpressure) { /* ready stays pending */ } else w.ready.resolve(undefined);
                } else if (s.state === "erroring") {
                    w.ready.reject(s.storedError); markHandled(w.ready.promise);
                } else if (s.state === "closed") {
                    w.ready.resolve(undefined); w.closed.resolve(undefined);
                } else {
                    w.ready.reject(s.storedError); markHandled(w.ready.promise);
                    w.closed.reject(s.storedError); markHandled(w.closed.promise);
                }
            }
            function WritableStreamDefaultWriter(stream) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'WritableStreamDefaultWriter': Please use the 'new' operator.");
                if (!(stream instanceof WritableStream)) throw new TypeError("Failed to construct 'WritableStreamDefaultWriter': parameter 1 is not of type 'WritableStream'.");
                attachWriter(this, writableOf(stream));
            }
            readonly(WritableStreamDefaultWriter.prototype, "closed", function () {
                var w = writerState.get(this);
                return w ? w.closed.promise : rejectedHandled(new TypeError("Illegal invocation"));
            });
            readonly(WritableStreamDefaultWriter.prototype, "ready", function () {
                var w = writerState.get(this);
                return w ? w.ready.promise : rejectedHandled(new TypeError("Illegal invocation"));
            });
            readonly(WritableStreamDefaultWriter.prototype, "desiredSize", function () {
                var w = writerOf(this);
                if (!w.stream) throw new TypeError("The writer has been released");
                var s = w.stream;
                if (s.state === "errored" || s.state === "erroring") return null;
                if (s.state === "closed") return 0;
                return wcDesiredSize(s.controller);
            });
            method(WritableStreamDefaultWriter.prototype, "write", function write(chunk) {
                var w = writerState.get(this);
                if (!w) return Promise.reject(new TypeError("Illegal invocation"));
                var s = w.stream;
                if (!s) return Promise.reject(new TypeError("Cannot write to a released writer"));
                var c = s.controller;
                var size = chunkSizeOf(c, chunk);
                if (s !== w.stream) return Promise.reject(new TypeError("The writer has been released"));
                if (s.state === "errored") return Promise.reject(s.storedError);
                if (closeQueuedOrInFlight(s) || s.state === "closed") return Promise.reject(new TypeError("Cannot write to a closing or closed stream"));
                if (s.state === "erroring") return Promise.reject(s.storedError);
                var d = deferred();
                s.writeRequests.push(d);
                controllerWrite(c, chunk, size);
                return d.promise;
            });
            method(WritableStreamDefaultWriter.prototype, "close", function close() {
                var w = writerState.get(this);
                if (!w) return Promise.reject(new TypeError("Illegal invocation"));
                var s = w.stream;
                if (!s) return Promise.reject(new TypeError("Cannot close a released writer"));
                if (closeQueuedOrInFlight(s)) return Promise.reject(new TypeError("Cannot close a closing stream"));
                return closeWritable(s);
            });
            method(WritableStreamDefaultWriter.prototype, "abort", function abort(reason) {
                var w = writerState.get(this);
                if (!w) return Promise.reject(new TypeError("Illegal invocation"));
                if (!w.stream) return Promise.reject(new TypeError("Cannot abort a released writer"));
                return abortWritable(w.stream, reason);
            });
            function releaseWriter(w) {
                var s = w.stream, error = new TypeError("Writer was released");
                ensureReadyRejected(w, error);
                ensureClosedRejected(w, error);
                s.writer = undefined; w.stream = undefined;
            }
            method(WritableStreamDefaultWriter.prototype, "releaseLock", function releaseLock() {
                var w = writerState.get(this);
                if (!w) throw new TypeError("Illegal invocation");
                if (!w.stream) return;
                releaseWriter(w);
            });
            tag(WritableStreamDefaultWriter, "WritableStreamDefaultWriter");

            readonly(WritableStream.prototype, "locked", function () { return writableOf(this).writer !== undefined; });
            method(WritableStream.prototype, "abort", function abort(reason) {
                var s;
                try { s = writableOf(this); } catch (e) { return Promise.reject(e); }
                if (s.writer) return Promise.reject(new TypeError("Cannot abort a stream that already has a writer"));
                return abortWritable(s, reason);
            });
            method(WritableStream.prototype, "close", function close() {
                var s;
                try { s = writableOf(this); } catch (e) { return Promise.reject(e); }
                if (s.writer) return Promise.reject(new TypeError("Cannot close a stream that already has a writer"));
                if (closeQueuedOrInFlight(s)) return Promise.reject(new TypeError("Cannot close an already-closing stream"));
                return closeWritable(s);
            });
            method(WritableStream.prototype, "getWriter", function getWriter() {
                var s = writableOf(this);
                var writer = Object.create(WritableStreamDefaultWriter.prototype);
                attachWriter(writer, s);
                return writer;
            });
            tag(WritableStream, "WritableStream");

            // ---- TransformStream (the Streams standard, section 6)
            var transformState = new WeakMap(), transformControllerState = new WeakMap();
            function transformOf(self) { var t = transformState.get(self); if (!t) throw new TypeError("Illegal invocation"); return t; }
            function transformControllerOf(self) { var c = transformControllerState.get(self); if (!c) throw new TypeError("Illegal invocation"); return c; }
            function TransformStream(transformer, writableStrategy, readableStrategy) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'TransformStream': Please use the 'new' operator, this DOM object constructor cannot be called as a function.");
                var t = transformer === undefined || transformer === null ? null : Object(transformer);
                if (transformer === null) t = null;
                if (t && t.readableType !== undefined) throw new RangeError("Failed to construct 'TransformStream': Invalid readableType specified");
                if (t && t.writableType !== undefined) throw new RangeError("Failed to construct 'TransformStream': Invalid writableType specified");
                var startFn = getMethod(t, "start"), transformFn = getMethod(t, "transform"), flushFn = getMethod(t, "flush"), cancelFn = getMethod(t, "cancel");
                var rq = strategyOf(readableStrategy, 0), wq = strategyOf(writableStrategy, 1);
                var stream = { backpressure: undefined, backpressureChange: undefined, readable: undefined, writable: undefined, controller: undefined };
                var startPromise = deferred();
                var controllerObject = Object.create(TransformStreamDefaultController.prototype);
                var controller = { stream: stream, finishPromise: undefined, transformFn: transformFn, flushFn: flushFn, cancelFn: cancelFn, transformer: t, object: controllerObject };
                transformControllerState.set(controllerObject, controller);
                stream.controller = controller;

                var readableObject = new ReadableStream({
                    start: function () { return startPromise.promise; },
                    pull: function () { return sourcePull(stream); },
                    cancel: function (reason) { return sourceCancel(stream, reason); }
                }, { highWaterMark: rq.hwm, size: rq.size });
                var writableObject = new WritableStream({
                    start: function () { return startPromise.promise; },
                    write: function (chunk) { return sinkWrite(stream, chunk); },
                    abort: function (reason) { return sinkAbort(stream, reason); },
                    close: function () { return sinkClose(stream); }
                }, { highWaterMark: wq.hwm, size: wq.size });
                stream.readable = readableState.get(readableObject);
                stream.writable = writableState.get(writableObject);
                stream.readableObject = readableObject; stream.writableObject = writableObject;
                setBackpressure(stream, true);
                transformState.set(this, stream);
                var startResult = startFn ? callFn(startFn, t, controllerObject) : undefined;
                startPromise.resolve(startResult);
            }
            function setBackpressure(stream, value) {
                if (stream.backpressureChange !== undefined) stream.backpressureChange.resolve(undefined);
                stream.backpressureChange = deferred();
                stream.backpressure = value;
            }
            function clearTransformAlgorithms(c) { c.transformFn = undefined; c.flushFn = undefined; c.cancelFn = undefined; }
            function unblockWrite(stream) { if (stream.backpressure === true) setBackpressure(stream, false); }
            function errorWritableAndUnblockWrite(stream, e) {
                clearTransformAlgorithms(stream.controller);
                errorIfNeeded(stream.writable.controller, e);
                unblockWrite(stream);
            }
            function transformError(stream, e) { errorReadable(stream.readable, e); errorWritableAndUnblockWrite(stream, e); }
            function transformEnqueue(stream, chunk) {
                var r = stream.readable;
                if (r.closeRequested || r.state !== "readable") throw new TypeError("The readable side is not in a state that permits enqueue");
                try { enqueueReadable(r, chunk); } catch (e) { errorWritableAndUnblockWrite(stream, e); throw r.stored; }
                var backpressure = !shouldPull(r);
                if (backpressure !== stream.backpressure) setBackpressure(stream, true);
            }
            function performTransform(stream, chunk) {
                var c = stream.controller;
                var p = promiseFrom(function () { return c.transformFn ? callFn(c.transformFn, c.transformer, chunk, c.object) : (transformEnqueue(stream, chunk), undefined); });
                return p.then(undefined, function (e) { transformError(stream, e); throw e; });
            }
            function sinkWrite(stream, chunk) {
                if (stream.backpressure) {
                    return stream.backpressureChange.promise.then(function () {
                        var w = stream.writable;
                        if (w.state === "erroring") throw w.storedError;
                        return performTransform(stream, chunk);
                    });
                }
                return performTransform(stream, chunk);
            }
            function sinkAbort(stream, reason) {
                var c = stream.controller;
                if (c.finishPromise !== undefined) return c.finishPromise.promise;
                var readable = stream.readable;
                c.finishPromise = deferred();
                var finish = c.finishPromise;
                var cancelPromise = promiseFrom(function () { return c.cancelFn ? callFn(c.cancelFn, c.transformer, reason) : undefined; });
                clearTransformAlgorithms(c);
                cancelPromise.then(function () {
                    if (readable.state === "errored") finish.reject(readable.stored);
                    else { errorReadable(readable, reason); finish.resolve(undefined); }
                }, function (e) { errorReadable(readable, e); finish.reject(e); });
                return finish.promise;
            }
            function sinkClose(stream) {
                var c = stream.controller;
                if (c.finishPromise !== undefined) return c.finishPromise.promise;
                var readable = stream.readable;
                c.finishPromise = deferred();
                var finish = c.finishPromise;
                var flushPromise = promiseFrom(function () { return c.flushFn ? callFn(c.flushFn, c.transformer, c.object) : undefined; });
                clearTransformAlgorithms(c);
                flushPromise.then(function () {
                    if (readable.state === "errored") finish.reject(readable.stored);
                    else {
                        if (readable.state === "readable" && !readable.closeRequested) { readable.closeRequested = true; if (readable.queue.length === 0) closeReadable(readable); }
                        finish.resolve(undefined);
                    }
                }, function (e) { transformError(stream, e); finish.reject(readable.stored); });
                return finish.promise;
            }
            function sourcePull(stream) {
                setBackpressure(stream, false);
                return stream.backpressureChange.promise;
            }
            function sourceCancel(stream, reason) {
                var c = stream.controller;
                if (c.finishPromise !== undefined) return c.finishPromise.promise;
                var writable = stream.writable;
                c.finishPromise = deferred();
                var finish = c.finishPromise;
                var cancelPromise = promiseFrom(function () { return c.cancelFn ? callFn(c.cancelFn, c.transformer, reason) : undefined; });
                clearTransformAlgorithms(c);
                cancelPromise.then(function () {
                    if (writable.state === "errored") finish.reject(writable.storedError);
                    else { errorIfNeeded(writable.controller, reason); unblockWrite(stream); finish.resolve(undefined); }
                }, function (e) { errorIfNeeded(writable.controller, e); unblockWrite(stream); finish.reject(e); });
                return finish.promise;
            }
            readonly(TransformStream.prototype, "readable", function () { return transformOf(this).readableObject; });
            readonly(TransformStream.prototype, "writable", function () { return transformOf(this).writableObject; });
            tag(TransformStream, "TransformStream");
            function TransformStreamDefaultController() { throw new TypeError("Illegal constructor"); }
            readonly(TransformStreamDefaultController.prototype, "desiredSize", function () {
                var c = transformControllerOf(this);
                return desiredReadable(c.stream.readable);
            });
            method(TransformStreamDefaultController.prototype, "enqueue", function enqueue(chunk) { transformEnqueue(transformControllerOf(this).stream, chunk); });
            method(TransformStreamDefaultController.prototype, "error", function error(e) { transformError(transformControllerOf(this).stream, e); });
            method(TransformStreamDefaultController.prototype, "terminate", function terminate() {
                var stream = transformControllerOf(this).stream, r = stream.readable;
                if (r.state === "readable" && !r.closeRequested) { r.closeRequested = true; if (r.queue.length === 0) closeReadable(r); }
                errorWritableAndUnblockWrite(stream, new TypeError("TransformStream terminated"));
            });
            tag(TransformStreamDefaultController, "TransformStreamDefaultController");

            // ---- TextEncoderStream and TextDecoderStream
            var textStreamState = new WeakMap();
            function TextEncoderStream() {
                if (new.target === undefined) throw new TypeError("Failed to construct 'TextEncoderStream': Please use the 'new' operator.");
                var pendingHigh = null;
                var transform = new TransformStream({
                    transform: function (chunk, controller) {
                        var text = String(chunk);
                        if (pendingHigh !== null) { text = pendingHigh + text; pendingHigh = null; }
                        if (text.length > 0) {
                            var last = text.charCodeAt(text.length - 1);
                            if (last >= 0xD800 && last <= 0xDBFF) { pendingHigh = text.charAt(text.length - 1); text = text.slice(0, -1); }
                        }
                        if (text.length > 0) controller.enqueue(utf8(USVString(text)));
                    },
                    flush: function (controller) { if (pendingHigh !== null) { pendingHigh = null; controller.enqueue(new Uint8Array([0xEF, 0xBF, 0xBD])); } }
                });
                textStreamState.set(this, { transform: transform, encoding: "utf-8" });
            }
            readonly(TextEncoderStream.prototype, "encoding", function () { return textStreamState.get(this).encoding; });
            readonly(TextEncoderStream.prototype, "readable", function () { return textStreamState.get(this).transform.readable; });
            readonly(TextEncoderStream.prototype, "writable", function () { return textStreamState.get(this).transform.writable; });
            tag(TextEncoderStream, "TextEncoderStream");
            function TextDecoderStream(label, options) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'TextDecoderStream': Please use the 'new' operator.");
                var decoder = new TextDecoder(label, options);
                var transform = new TransformStream({
                    transform: function (chunk, controller) {
                        if (!isBufferSource(chunk)) throw new TypeError("The chunk is not an ArrayBuffer or ArrayBufferView");
                        var text = decoder.decode(chunk, { stream: true });
                        if (text.length > 0) controller.enqueue(text);
                    },
                    flush: function (controller) {
                        var text = decoder.decode();
                        if (text.length > 0) controller.enqueue(text);
                    }
                });
                textStreamState.set(this, { transform: transform, decoder: decoder });
            }
            readonly(TextDecoderStream.prototype, "encoding", function () { return textStreamState.get(this).decoder.encoding; });
            readonly(TextDecoderStream.prototype, "fatal", function () { return textStreamState.get(this).decoder.fatal; });
            readonly(TextDecoderStream.prototype, "ignoreBOM", function () { return textStreamState.get(this).decoder.ignoreBOM; });
            readonly(TextDecoderStream.prototype, "readable", function () { return textStreamState.get(this).transform.readable; });
            readonly(TextDecoderStream.prototype, "writable", function () { return textStreamState.get(this).transform.writable; });
            tag(TextDecoderStream, "TextDecoderStream");

            // The bytes of a body as a stream, and a stream read to the end as bytes.
            function streamOfBytes(bytes) {
                return new ReadableStream({
                    pull: function (controller) { if (bytes.length > 0) controller.enqueue(new Uint8Array(bytes)); controller.close(); }
                });
            }

            // ---- structuredClone: values, collections, buffers and views, with the transfer option for ArrayBuffers
            function dataCloneError(message) { return new DOMException(message, "DataCloneError"); }
            function structuredClone(value, options) {
                if (arguments.length < 1) throw new TypeError("Failed to execute 'structuredClone' on 'Window': 1 argument required, but only 0 present.");
                var transferList = [];
                if (options !== undefined && options !== null) {
                    if (!isObject(options)) throw new TypeError("The options are not an object");
                    var list = options.transfer;
                    if (list !== undefined) {
                        if (!isObject(list) || typeof list[Symbol.iterator] !== "function") throw new TypeError("The transfer list is not a sequence");
                        transferList = Array.from(list);
                    }
                }
                return serializeWithTransfer(value, transferList).value;
            }
            // Objects other than ArrayBuffers a transfer list may hold (message ports, readable and writable streams): each registers
            // { test(v), check(v) throws DataCloneError, transfer(v) -> the object that takes its place }.
            var transferHandlers = [];
            function transferHandlerOf(item) {
                for (var i = 0; i < transferHandlers.length; i++) if (transferHandlers[i].test(item)) return transferHandlers[i];
                return null;
            }
            function serializeWithTransfer(value, transferList) {
                var seen = new Set(), transferred = new Map();
                transferList.forEach(function (item) {
                    var handler = isObject(item) && !(item instanceof ArrayBuffer) ? transferHandlerOf(item) : null;
                    if (!(item instanceof ArrayBuffer) && !handler) throw dataCloneError("An object could not be transferred");
                    if (seen.has(item)) throw dataCloneError("Transfer list contains duplicate entries");
                    if (handler) handler.check(item, transferList);
                    else if (isDetached(item)) throw dataCloneError("An ArrayBuffer is detached and could not be cloned");
                    seen.add(item);
                });
                var memory = new Map();
                function clone(v) {
                    if (v === null || (typeof v !== "object" && typeof v !== "function")) {
                        if (typeof v === "symbol") throw dataCloneError("A Symbol object could not be cloned");
                        return v;
                    }
                    if (typeof v === "function") throw dataCloneError("A function could not be cloned");
                    if (memory.has(v)) return memory.get(v);
                    var result;
                    if (seen.has(v) && !(v instanceof ArrayBuffer)) result = transferred.get(v);
                    else if (v instanceof ArrayBuffer) {
                        if (seen.has(v)) result = transferred.get(v);
                        else if (isDetached(v)) throw dataCloneError("An ArrayBuffer is detached and could not be cloned");
                        else result = v.slice(0);
                    } else if (ArrayBuffer.isView(v)) {
                        var buffer = clone(v.buffer);
                        result = v instanceof DataView ? new DataView(buffer, v.byteOffset, v.byteLength) : new v.constructor(buffer, v.byteOffset, v.length);
                    } else if (v instanceof Date) result = new Date(v.getTime());
                    else if (v instanceof RegExp) result = new RegExp(v.source, v.flags);
                    else if (v instanceof Boolean || v instanceof Number || v instanceof String || Object.prototype.toString.call(v) === "[object BigInt]") result = Object(v.valueOf());
                    else if (typeof global.File === "function" && v instanceof global.File) result = new global.File([v], v.name, { type: v.type, lastModified: v.lastModified });
                    else if (typeof global.Blob === "function" && v instanceof global.Blob) result = new global.Blob([v], { type: v.type });
                    else if (typeof global.ImageData === "function" && v instanceof global.ImageData) result = new global.ImageData(new Uint8ClampedArray(v.data), v.width, v.height, { colorSpace: v.colorSpace });
                    else if (v instanceof Map) {
                        result = new Map(); memory.set(v, result);
                        v.forEach(function (val, key) { result.set(clone(key), clone(val)); });
                        return result;
                    } else if (v instanceof Set) {
                        result = new Set(); memory.set(v, result);
                        v.forEach(function (val) { result.add(clone(val)); });
                        return result;
                    } else if (v instanceof DOMException) {
                        result = new DOMException(v.message, v.name); memory.set(v, result);
                        return result;
                    } else if (v instanceof Error) {
                        var names = ["Error", "EvalError", "RangeError", "ReferenceError", "SyntaxError", "TypeError", "URIError"];
                        var ctor = names.indexOf(v.name) >= 0 ? global[v.name] : Error;
                        result = new ctor(); memory.set(v, result);
                        if (Object.prototype.hasOwnProperty.call(v, "message")) Object.defineProperty(result, "message", { value: String(v.message), writable: true, configurable: true });
                        if (Object.prototype.hasOwnProperty.call(v, "cause")) Object.defineProperty(result, "cause", { value: clone(v.cause), writable: true, configurable: true });
                        if (typeof v.stack === "string") Object.defineProperty(result, "stack", { value: v.stack, writable: true, configurable: true });
                        return result;
                    } else if (Array.isArray(v)) {
                        result = new Array(v.length); memory.set(v, result);
                        Object.keys(v).forEach(function (k) { result[k] = clone(v[k]); });
                        return result;
                    } else if (Object.prototype.toString.call(v) === "[object Object]") {
                        result = {}; memory.set(v, result);
                        Object.keys(v).forEach(function (k) { result[k] = clone(v[k]); });
                        return result;
                    } else if (typeof ImageBitmap === "function" && v instanceof ImageBitmap) {
                        // An ImageBitmap is serializable: the copy shows the same picture (and is as clean or as tainted).
                        result = global.__cloneImageBitmap(v);
                        if (result === null) throw dataCloneError("An ImageBitmap is closed and could not be cloned");
                    } else throw dataCloneError("An object could not be cloned");
                    memory.set(v, result);
                    return result;
                }
                // Transferred buffers move before the clone reads them, so views over them follow.
                transferList.forEach(function (item) {
                    var handler = item instanceof ArrayBuffer ? null : transferHandlerOf(item);
                    transferred.set(item, handler ? handler.transfer(item) : item.transfer());
                });
                return { value: clone(value), transferred: transferred };
            }
            function readAllBytes(stream) {
                var reader = stream.getReader(), chunks = [], total = 0;
                return new Promise(function (resolve, reject) {
                    function next() {
                        reader.read().then(function (r) {
                            if (r.done) {
                                var all = new Uint8Array(total), at = 0;
                                chunks.forEach(function (c) { all.set(c, at); at += c.length; });
                                resolve(all);
                                return;
                            }
                            if (!(r.value instanceof Uint8Array)) { reject(new TypeError("Received a non-Uint8Array chunk")); return; }
                            chunks.push(r.value); total += r.value.length;
                            next();
                        }, reject);
                    }
                    next();
                });
            }

            // ---- streamHTMLUnsafe and its positional relatives: a writable stream whose text is parsed as markup into the tree.
            net.registerHtmlStream(function (target, position) {
                var parent = position === "replace" || position === "append" || position === "prepend" ? target : target.parentNode;
                var anchor = null, accumulated = "", inserted = [];
                if (position === "replace") { while (target.firstChild) target.removeChild(target.firstChild); }
                else if (position === "prepend") anchor = target.firstChild;
                else if (position === "before") anchor = target;
                else if (position === "after") anchor = target.nextSibling;
                else if (position === "replaceWith") { anchor = target.nextSibling; if (parent) parent.removeChild(target); }
                function apply() {
                    inserted.forEach(function (n) { if (n.parentNode) n.parentNode.removeChild(n); });
                    inserted = [];
                    if (!parent) return;
                    var holder = target.ownerDocument.createElement("div");
                    holder.innerHTML = accumulated;
                    var end = anchor && anchor.parentNode === parent ? anchor : null;
                    while (holder.firstChild) { var n = holder.firstChild; parent.insertBefore(n, end); inserted.push(n); }
                }
                return new WritableStream({
                    write: function (chunk) { accumulated += String(chunk); apply(); },
                    close: function () { apply(); }
                });
            });

        """;
}
