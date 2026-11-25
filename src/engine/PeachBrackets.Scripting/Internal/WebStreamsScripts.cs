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
                d.promise = new Promise(function (resolve, reject) { d.resolve = resolve; d.reject = reject; });
                return d;
            }
            function markHandled(promise) { promise.then(undefined, function () {}); return promise; }
            function rejectedHandled(error) { return markHandled(Promise.reject(error)); }
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
                var hwm = strategy.highWaterMark === undefined ? defaultHwm : Number(strategy.highWaterMark);
                if (hwm !== hwm || hwm < 0) throw new RangeError("Invalid highWaterMark");
                if (size !== undefined && typeof size !== "function") throw new TypeError("The strategy's size is not a function");
                return { hwm: hwm, size: size };
            }

            // Queuing strategies.
            function CountQueuingStrategy(init) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'CountQueuingStrategy': Please use the 'new' operator.");
                if (init === undefined || init === null || typeof init !== "object" && typeof init !== "function") throw new TypeError("Failed to construct 'CountQueuingStrategy': The provided value is not of type 'QueuingStrategyInit'.");
                if (init.highWaterMark === undefined) throw new TypeError("Failed to construct 'CountQueuingStrategy': required member highWaterMark is undefined.");
                Object.defineProperty(this, "highWaterMark", { value: Number(init.highWaterMark), writable: true, enumerable: true, configurable: true });
            }
            var countSize = function size() { return 1; };
            Object.defineProperty(CountQueuingStrategy.prototype, "size", { get: function () { return countSize; }, enumerable: true, configurable: true });
            tag(CountQueuingStrategy, "CountQueuingStrategy");
            function ByteLengthQueuingStrategy(init) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'ByteLengthQueuingStrategy': Please use the 'new' operator.");
                if (init === undefined || init === null || typeof init !== "object" && typeof init !== "function") throw new TypeError("Failed to construct 'ByteLengthQueuingStrategy': The provided value is not of type 'QueuingStrategyInit'.");
                if (init.highWaterMark === undefined) throw new TypeError("Failed to construct 'ByteLengthQueuingStrategy': required member highWaterMark is undefined.");
                Object.defineProperty(this, "highWaterMark", { value: Number(init.highWaterMark), writable: true, enumerable: true, configurable: true });
            }
            var byteLengthSize = function size(chunk) { return chunk.byteLength; };
            Object.defineProperty(ByteLengthQueuingStrategy.prototype, "size", { get: function () { return byteLengthSize; }, enumerable: true, configurable: true });
            tag(ByteLengthQueuingStrategy, "ByteLengthQueuingStrategy");

            // ---- ReadableStream
            var readableState = new WeakMap(), readableControllerState = new WeakMap(), readerState = new WeakMap();
            function readableOf(self) { var s = readableState.get(self); if (!s) throw new TypeError("Illegal invocation"); return s; }
            function desiredReadable(s) { return s.state === "errored" ? null : s.state === "closed" ? 0 : s.hwm - s.queueSize; }
            function shouldPull(s) {
                if (s.state !== "readable" || s.closeRequested || !s.started) return false;
                if (s.reader && s.reader.requests.length > 0) return true;
                return desiredReadable(s) > 0;
            }
            function callPull(s) {
                if (!shouldPull(s)) return;
                if (s.pulling) { s.pullAgain = true; return; }
                s.pulling = true;
                var p;
                try { p = Promise.resolve(s.pullFn ? s.pullFn.call(s.source, s.controller) : undefined); } catch (e) { p = Promise.reject(e); }
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
                    r.closed.reject(e); markHandled(r.closed.promise);
                }
            }
            function closeReadable(s) {
                s.state = "closed";
                var r = s.reader;
                if (r) {
                    r.requests.splice(0).forEach(function (q) { q.resolve({ value: undefined, done: true }); });
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
                            size = Number(s.size(chunk));
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
                else if (s.queue.length > 0) {
                    var item = s.queue.shift();
                    s.queueSize -= item.size;
                    if (s.queue.length === 0) s.queueSize = 0;
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
                closeReadable(s);
                var p;
                try { p = Promise.resolve(s.cancelFn ? s.cancelFn.call(s.source, reason) : undefined); } catch (e) { p = Promise.reject(e); }
                return p.then(function () { return undefined; });
            }
            function ReadableStream(underlyingSource, strategy) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'ReadableStream': Please use the 'new' operator, this DOM object constructor cannot be called as a function.");
                var source = underlyingSource === undefined || underlyingSource === null ? null : Object(underlyingSource);
                var type = source ? source.type : undefined;
                if (type !== undefined) { type = String(type); if (type !== "bytes") throw new RangeError("Failed to construct 'ReadableStream': Invalid type is specified"); }
                var q = strategyOf(strategy, type === "bytes" ? 0 : 1);
                if (type === "bytes" && q.size !== undefined) throw new RangeError("Failed to construct 'ReadableStream': The strategy for a byte stream cannot have a size function");
                var startFn = getMethod(source, "start"), pullFn = getMethod(source, "pull"), cancelFn = getMethod(source, "cancel");
                var s = { state: "readable", queue: [], queueSize: 0, hwm: q.hwm, size: type === "bytes" ? undefined : q.size, reader: null, disturbed: false, stored: undefined, closeRequested: false, pulling: false, pullAgain: false, started: false, source: source, pullFn: pullFn, cancelFn: cancelFn, controller: null };
                readableState.set(this, s);
                var controller = Object.create(ReadableStreamDefaultController.prototype);
                s.controller = controller;
                readableControllerState.set(controller, s);
                var result = startFn ? startFn.call(source, controller) : undefined;
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
                if (options !== undefined && options !== null && Object(options).mode !== undefined) {
                    if (String(Object(options).mode) !== "byob") throw new TypeError("Failed to execute 'getReader' on 'ReadableStream': The provided value is not a valid enum value of type ReadableStreamReaderMode.");
                    throw new TypeError("BYOB readers are not supported");
                }
                var reader = Object.create(ReadableStreamDefaultReader.prototype);
                attachReader(reader, s);
                return reader;
            });
            // The two branches of a stream.
            function teeReadable(stream) {
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
                        controllers.forEach(function (c, i) { if (!canceled[i]) try { c.enqueue(r.value); } catch (e) {} });
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
                    return new ReadableStream({ start: function (c) { controllers[i] = c; }, pull: pull, cancel: cancelBranch(i) });
                });
                reader.closed.then(undefined, function (e) { controllers.forEach(function (c) { try { c.error(e); } catch (x) {} }); if (!canceled[0] || !canceled[1]) cancelDone.resolve(undefined); });
                return branches;
            }
            method(ReadableStream.prototype, "tee", function tee() {
                var s = readableOf(this);
                if (s.reader) throw new TypeError("Failed to execute 'tee' on 'ReadableStream': Cannot tee a stream that already has a reader");
                return teeReadable(this);
            });
            method(ReadableStream.prototype, "pipeTo", function pipeTo(destination, options) {
                var s, d;
                try {
                    s = readableOf(this);
                    if (!(destination instanceof WritableStream)) throw new TypeError("Failed to execute 'pipeTo' on 'ReadableStream': parameter 1 is not of type 'WritableStream'.");
                    d = writableOf(destination);
                    options = options === undefined || options === null ? {} : Object(options);
                } catch (e) { return Promise.reject(e); }
                var preventClose = !!options.preventClose, preventAbort = !!options.preventAbort, preventCancel = !!options.preventCancel, signal = options.signal;
                if (signal !== undefined && !(signal instanceof AbortSignal)) return Promise.reject(new TypeError("Failed to execute 'pipeTo' on 'ReadableStream': member signal is not of type AbortSignal."));
                if (s.reader) return Promise.reject(new TypeError("Failed to execute 'pipeTo' on 'ReadableStream': Cannot pipe a locked stream"));
                if (d.writer) return Promise.reject(new TypeError("Failed to execute 'pipeTo' on 'ReadableStream': Cannot pipe to a locked stream"));
                var reader = this.getReader(), writer = destination.getWriter();
                return new Promise(function (resolve, reject) {
                    var shuttingDown = false, lastWrite = Promise.resolve(), pending = [];
                    function settle(hasError, error) {
                        writer.releaseLock(); reader.releaseLock();
                        if (signal) signal.removeEventListener("abort", onAbort);
                        if (hasError) reject(error); else resolve(undefined);
                    }
                    function shutdown(action, hasError, error) {
                        if (shuttingDown) return;
                        shuttingDown = true;
                        var chain = lastWrite.then(undefined, function () {});
                        chain.then(function () { return action ? action() : undefined; }).then(function () { settle(hasError, error); }, function (e) { settle(true, e); });
                    }
                    function onAbort() {
                        var error = signal.reason !== undefined ? signal.reason : new DOMException("The operation was aborted.", "AbortError");
                        var actions = [];
                        if (!preventAbort) actions.push(function () { return d.state === "writable" ? writer.abort(error) : Promise.resolve(); });
                        if (!preventCancel) actions.push(function () { return s.state === "readable" ? reader.cancel(error) : Promise.resolve(); });
                        shutdown(function () { return Promise.all(actions.map(function (a) { return a(); })); }, true, error);
                    }
                    if (signal) { if (signal.aborted) { onAbort(); return; } signal.addEventListener("abort", onAbort); }
                    function step() {
                        if (shuttingDown) return;
                        writer.ready.then(function () {
                            if (shuttingDown) return;
                            reader.read().then(function (r) {
                                if (r.done) {
                                    if (!preventClose) shutdown(function () { return writer.close(); }, false); else shutdown(null, false);
                                    return;
                                }
                                if (shuttingDown) return;
                                lastWrite = writer.write(r.value);
                                markHandled(lastWrite);
                                step();
                            }, function () {});
                        }, function () {});
                    }
                    reader.closed.then(undefined, function (e) {
                        if (!preventAbort) shutdown(function () { return writer.abort(e); }, true, e); else shutdown(null, true, e);
                    });
                    writer.closed.then(function () {
                        if (shuttingDown) return;
                        var e = new TypeError("The destination writable stream closed before all data could be piped to it");
                        if (!preventCancel) shutdown(function () { return reader.cancel(e); }, true, e); else shutdown(null, true, e);
                    }, function (e) {
                        if (!preventCancel) shutdown(function () { return reader.cancel(e); }, true, e); else shutdown(null, true, e);
                    });
                    step();
                });
            });
            method(ReadableStream.prototype, "pipeThrough", function pipeThrough(transform, options) {
                var s = readableOf(this);
                if (!isObject(transform)) throw new TypeError("Failed to execute 'pipeThrough' on 'ReadableStream': parameter 1 is not of type 'ReadableWritablePair'.");
                var readable = transform.readable, writable = transform.writable;
                if (!(readable instanceof ReadableStream)) throw new TypeError("Failed to execute 'pipeThrough' on 'ReadableStream': Failed to read the 'readable' property from 'ReadableWritablePair'");
                if (!(writable instanceof WritableStream)) throw new TypeError("Failed to execute 'pipeThrough' on 'ReadableStream': Failed to read the 'writable' property from 'ReadableWritablePair'");
                if (s.reader) throw new TypeError("Failed to execute 'pipeThrough' on 'ReadableStream': Cannot pipe a locked stream");
                if (writableOf(writable).writer) throw new TypeError("Failed to execute 'pipeThrough' on 'ReadableStream': Cannot pipe to a locked stream");
                markHandled(this.pipeTo(writable, options));
                return readable;
            });
            function iterate(stream, options) {
                var s = readableOf(stream);
                var preventCancel = !!(options !== undefined && options !== null && Object(options).preventCancel);
                var reader = stream.getReader(), finished = false, ongoing = null;
                var iterator = Object.create(asyncIteratorPrototype);
                function next() {
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
            var asyncIteratorPrototype = {};
            Object.defineProperty(asyncIteratorPrototype, Symbol.asyncIterator, { value: function () { return this; }, writable: true, configurable: true });
            Object.defineProperty(asyncIteratorPrototype, Symbol.toStringTag, { value: "ReadableStream AsyncIterator", configurable: true });
            method(ReadableStream.prototype, "values", function values(options) { return iterate(this, options); });
            Object.defineProperty(ReadableStream.prototype, Symbol.asyncIterator, { value: ReadableStream.prototype.values, writable: true, configurable: true });
            method(ReadableStream, "from", function from(asyncIterable) {
                if (asyncIterable === undefined || asyncIterable === null) throw new TypeError("Failed to execute 'from' on 'ReadableStream': The provided value is not iterable");
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

            // ---- WritableStream
            var writableState = new WeakMap(), writableControllerState = new WeakMap(), writerState = new WeakMap();
            function writableOf(self) { var s = writableState.get(self); if (!s) throw new TypeError("Illegal invocation"); return s; }
            function desiredWritable(s) { return s.state === "errored" ? null : s.state === "closed" ? 0 : s.hwm - s.queueSize; }
            function updateBackpressure(s) {
                var bp = s.state === "writable" && !s.closeRequested && desiredWritable(s) <= 0;
                if (s.writer && bp !== s.backpressure) {
                    if (bp) s.writer.ready = deferred(); else s.writer.ready.resolve(undefined);
                }
                s.backpressure = bp;
            }
            function errorWritable(s, e) {
                if (s.state === "errored" || s.state === "closed") return;
                s.state = "errored"; s.stored = e;
                s.abortController.abort(e);
                s.queue.splice(s.inFlight ? 1 : 0).forEach(function (item) { item.reject(e); });
                s.queueSize = s.inFlight ? s.queue[0].size : 0;
                var w = s.writer;
                if (w) {
                    w.closed.reject(e); markHandled(w.closed.promise);
                    if (s.backpressure) { w.ready.reject(e); markHandled(w.ready.promise); } else { w.ready = deferred(); w.ready.reject(e); markHandled(w.ready.promise); }
                }
                s.backpressure = false;
            }
            function processWrites(s) {
                if (!s.started || s.inFlight || s.state === "errored" || s.state === "closed" || s.queue.length === 0) return;
                var item = s.queue[0];
                s.inFlight = true;
                var p;
                if (item.close) {
                    try { p = Promise.resolve(s.closeFn ? s.closeFn.call(s.sink) : undefined); } catch (e) { p = Promise.reject(e); }
                    s.inFlightPromise = p;
                    p.then(function () {
                        s.inFlight = false; s.queue.shift();
                        if (s.state === "errored") { item.reject(s.stored); return; }
                        s.state = "closed";
                        item.resolve(undefined);
                        if (s.writer) s.writer.closed.resolve(undefined);
                    }, function (e) { s.inFlight = false; s.queue.shift(); errorWritable(s, e); item.reject(e); });
                } else {
                    try { p = Promise.resolve(s.writeFn ? s.writeFn.call(s.sink, item.chunk, s.controller) : undefined); } catch (e) { p = Promise.reject(e); }
                    s.inFlightPromise = p;
                    p.then(function () {
                        s.inFlight = false; s.queue.shift(); s.queueSize -= item.size;
                        if (s.queue.length === 0) s.queueSize = 0;
                        if (s.state === "errored") { item.reject(s.stored); return; }
                        item.resolve(undefined);
                        updateBackpressure(s);
                        processWrites(s);
                    }, function (e) { s.inFlight = false; s.queue.shift(); errorWritable(s, e); item.reject(e); });
                }
            }
            function WritableStream(underlyingSink, strategy) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'WritableStream': Please use the 'new' operator, this DOM object constructor cannot be called as a function.");
                var sink = underlyingSink === undefined || underlyingSink === null ? null : Object(underlyingSink);
                if (sink && sink.type !== undefined) throw new RangeError("Failed to construct 'WritableStream': Invalid type is specified");
                var startFn = getMethod(sink, "start"), writeFn = getMethod(sink, "write"), closeFn = getMethod(sink, "close"), abortFn = getMethod(sink, "abort");
                var q = strategyOf(strategy, 1);
                var s = { state: "writable", stored: undefined, writer: null, queue: [], queueSize: 0, hwm: q.hwm, size: q.size, inFlight: false, inFlightPromise: null, started: false, closeRequested: false, sink: sink, writeFn: writeFn, closeFn: closeFn, abortFn: abortFn, controller: null, abortController: new AbortController(), backpressure: q.hwm <= 0 };
                writableState.set(this, s);
                var controller = Object.create(WritableStreamDefaultController.prototype);
                s.controller = controller;
                writableControllerState.set(controller, s);
                var result = startFn ? startFn.call(sink, controller) : undefined;
                Promise.resolve(result).then(function () { s.started = true; processWrites(s); }, function (e) { s.started = true; errorWritable(s, e); });
            }
            function WritableStreamDefaultController() { throw new TypeError("Illegal constructor"); }
            function writableControllerOf(self) { var s = writableControllerState.get(self); if (!s) throw new TypeError("Illegal invocation"); return s; }
            readonly(WritableStreamDefaultController.prototype, "signal", function () { return writableControllerOf(this).abortController.signal; });
            method(WritableStreamDefaultController.prototype, "error", function error(e) {
                var s = writableControllerOf(this);
                if (s.state === "writable") errorWritable(s, e);
            });
            tag(WritableStreamDefaultController, "WritableStreamDefaultController");

            function writerOf(self) { var w = writerState.get(self); if (!w) throw new TypeError("Illegal invocation"); return w; }
            function attachWriter(writer, s) {
                var w = { stream: s, closed: deferred(), ready: deferred() };
                writerState.set(writer, w);
                s.writer = w;
                if (s.state === "writable") { if (!s.backpressure) w.ready.resolve(undefined); }
                else if (s.state === "closed") { w.ready.resolve(undefined); w.closed.resolve(undefined); }
                else { w.ready.reject(s.stored); markHandled(w.ready.promise); w.closed.reject(s.stored); markHandled(w.closed.promise); }
            }
            function WritableStreamDefaultWriter(stream) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'WritableStreamDefaultWriter': Please use the 'new' operator.");
                if (!(stream instanceof WritableStream)) throw new TypeError("Failed to construct 'WritableStreamDefaultWriter': parameter 1 is not of type 'WritableStream'.");
                var s = writableOf(stream);
                if (s.writer) throw new TypeError("Failed to construct 'WritableStreamDefaultWriter': This stream has already been locked for exclusive writing by another writer");
                attachWriter(this, s);
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
                return desiredWritable(w.stream);
            });
            method(WritableStreamDefaultWriter.prototype, "write", function write(chunk) {
                var w = writerState.get(this);
                if (!w) return Promise.reject(new TypeError("Illegal invocation"));
                var s = w.stream;
                if (!s) return Promise.reject(new TypeError("Cannot write to a released writer"));
                var size = 1;
                if (s.size) {
                    try { size = Number(s.size(chunk)); if (size !== size || size < 0 || size === Infinity) throw new RangeError("Invalid chunk size"); } catch (e) { if (s.state === "writable") errorWritable(s, e); return Promise.reject(e); }
                }
                if (s.state === "errored") return Promise.reject(s.stored);
                if (s.state === "closed" || s.closeRequested) return Promise.reject(new TypeError("Cannot write to a closing or closed stream"));
                var d = deferred();
                s.queue.push({ chunk: chunk, size: size, resolve: d.resolve, reject: d.reject });
                s.queueSize += size;
                updateBackpressure(s);
                processWrites(s);
                return d.promise;
            });
            method(WritableStreamDefaultWriter.prototype, "close", function close() {
                var w = writerState.get(this);
                if (!w) return Promise.reject(new TypeError("Illegal invocation"));
                var s = w.stream;
                if (!s) return Promise.reject(new TypeError("Cannot close a released writer"));
                if (s.state === "errored") return Promise.reject(s.stored);
                if (s.state === "closed" || s.closeRequested) return Promise.reject(new TypeError("Cannot close a closing or closed stream"));
                var d = deferred();
                s.closeRequested = true;
                s.queue.push({ close: true, size: 0, resolve: d.resolve, reject: d.reject });
                updateBackpressure(s);
                processWrites(s);
                return d.promise;
            });
            method(WritableStreamDefaultWriter.prototype, "abort", function abort(reason) {
                var w = writerState.get(this);
                if (!w) return Promise.reject(new TypeError("Illegal invocation"));
                var s = w.stream;
                if (!s) return Promise.reject(new TypeError("Cannot abort a released writer"));
                return abortWritable(s, reason);
            });
            function abortWritable(s, reason) {
                if (s.state === "closed" || s.state === "errored") return Promise.resolve(undefined);
                var flight = s.inFlight && s.inFlightPromise ? s.inFlightPromise : Promise.resolve();
                errorWritable(s, reason);
                return flight.then(undefined, function () {}).then(function () { return s.abortFn ? s.abortFn.call(s.sink, reason) : undefined; }).then(function () { return undefined; });
            }
            method(WritableStreamDefaultWriter.prototype, "releaseLock", function releaseLock() {
                var w = writerState.get(this);
                if (!w) throw new TypeError("Illegal invocation");
                if (!w.stream) return;
                var error = new TypeError("Writer was released");
                w.ready = deferred(); w.ready.reject(error); markHandled(w.ready.promise);
                var closed = deferred(); closed.reject(error); markHandled(closed.promise);
                w.closed = closed;
                w.stream.writer = null; w.stream = null;
            });
            tag(WritableStreamDefaultWriter, "WritableStreamDefaultWriter");

            readonly(WritableStream.prototype, "locked", function () { return writableOf(this).writer !== null; });
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
                if (s.state === "errored") return Promise.reject(s.stored);
                if (s.state === "closed" || s.closeRequested) return Promise.reject(new TypeError("Cannot close a closing or closed stream"));
                var writer = this.getWriter(), p = writer.close();
                return p.then(function (v) { writer.releaseLock(); return v; }, function (e) { writer.releaseLock(); throw e; });
            });
            method(WritableStream.prototype, "getWriter", function getWriter() {
                var s = writableOf(this);
                if (s.writer) throw new TypeError("Failed to execute 'getWriter' on 'WritableStream': Cannot get a writer for a stream that is locked");
                var writer = Object.create(WritableStreamDefaultWriter.prototype);
                attachWriter(writer, s);
                return writer;
            });
            tag(WritableStream, "WritableStream");

            // ---- TransformStream
            var transformState = new WeakMap(), transformControllerState = new WeakMap();
            function transformOf(self) { var t = transformState.get(self); if (!t) throw new TypeError("Illegal invocation"); return t; }
            function TransformStream(transformer, writableStrategy, readableStrategy) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'TransformStream': Please use the 'new' operator, this DOM object constructor cannot be called as a function.");
                var t = transformer === undefined || transformer === null ? null : Object(transformer);
                if (t && t.readableType !== undefined) throw new RangeError("Failed to construct 'TransformStream': Invalid readableType specified");
                if (t && t.writableType !== undefined) throw new RangeError("Failed to construct 'TransformStream': Invalid writableType specified");
                var startFn = getMethod(t, "start"), transformFn = getMethod(t, "transform"), flushFn = getMethod(t, "flush"), cancelFn = getMethod(t, "cancel");
                var wq = strategyOf(writableStrategy, 1), rq = strategyOf(readableStrategy, 0);
                var startDone = deferred(), state = { backpressure: true, change: null }, rcon = null, wcon = null, readable, writable;
                var controller = Object.create(TransformStreamDefaultController.prototype);
                function setBackpressure(value) {
                    if (state.change) state.change.resolve(undefined);
                    state.backpressure = value; state.change = deferred();
                }
                function errorWritableSide(e) {
                    if (writableOf(writable).state === "writable") wcon.error(e);
                    if (state.backpressure) setBackpressure(false);
                }
                function errorReadableSide(e) { errorReadable(readableOf(readable), e); }
                transformControllerState.set(controller, {
                    enqueue: function (chunk) {
                        var rs_ = readableOf(readable);
                        if (rs_.closeRequested || rs_.state !== "readable") throw new TypeError("The readable side is not in a state that permits enqueue");
                        try { enqueueReadable(rs_, chunk); } catch (e) { errorWritableSide(e); throw rs_.stored; }
                        var bp = desiredReadable(rs_) <= 0;
                        if (bp !== state.backpressure) setBackpressure(true);
                    },
                    error: function (e) { errorReadableSide(e); errorWritableSide(e); },
                    terminate: function () {
                        var rs_ = readableOf(readable);
                        if (rs_.state === "readable" && !rs_.closeRequested) { rs_.closeRequested = true; if (rs_.queue.length === 0) closeReadable(rs_); }
                        errorWritableSide(new TypeError("TransformStream terminated"));
                    },
                    desiredSize: function () { return desiredReadable(readableOf(readable)); }
                });
                function performTransform(chunk) {
                    var p;
                    try {
                        if (transformFn) p = Promise.resolve(transformFn.call(t, chunk, controller));
                        else { controller.enqueue(chunk); p = Promise.resolve(); }
                    } catch (e) { p = Promise.reject(e); }
                    return p.then(undefined, function (e) { errorReadableSide(e); throw e; });
                }
                readable = new ReadableStream({
                    start: function (c) { rcon = c; return startDone.promise; },
                    pull: function () { setBackpressure(false); return state.change.promise; },
                    cancel: function (reason) {
                        var p;
                        try { p = Promise.resolve(cancelFn ? cancelFn.call(t, reason) : undefined); } catch (e) { p = Promise.reject(e); }
                        return p.then(function () { errorWritableSide(reason); }, function (e) { errorWritableSide(e); throw e; });
                    }
                }, { highWaterMark: rq.hwm, size: rq.size });
                writable = new WritableStream({
                    start: function (c) { wcon = c; return startDone.promise; },
                    write: function (chunk) {
                        if (state.backpressure) {
                            return state.change.promise.then(function () {
                                var ws_ = writableOf(writable);
                                if (ws_.state === "errored") throw ws_.stored;
                                return performTransform(chunk);
                            });
                        }
                        return performTransform(chunk);
                    },
                    close: function () {
                        var p;
                        try { p = Promise.resolve(flushFn ? flushFn.call(t, controller) : undefined); } catch (e) { p = Promise.reject(e); }
                        return p.then(function () {
                            var rs_ = readableOf(readable);
                            if (rs_.state === "errored") throw rs_.stored;
                            if (rs_.state === "readable" && !rs_.closeRequested) { rs_.closeRequested = true; if (rs_.queue.length === 0) closeReadable(rs_); }
                        }, function (e) { errorReadableSide(e); throw readableOf(readable).stored; });
                    },
                    abort: function (reason) { errorReadableSide(reason); }
                }, { highWaterMark: wq.hwm, size: wq.size });
                setBackpressure(true);
                transformState.set(this, { readable: readable, writable: writable });
                var result = startFn ? startFn.call(t, controller) : undefined;
                Promise.resolve(result).then(startDone.resolve, startDone.reject);
            }
            readonly(TransformStream.prototype, "readable", function () { return transformOf(this).readable; });
            readonly(TransformStream.prototype, "writable", function () { return transformOf(this).writable; });
            tag(TransformStream, "TransformStream");
            function TransformStreamDefaultController() { throw new TypeError("Illegal constructor"); }
            function transformControllerOf(self) { var c = transformControllerState.get(self); if (!c) throw new TypeError("Illegal invocation"); return c; }
            readonly(TransformStreamDefaultController.prototype, "desiredSize", function () { return transformControllerOf(this).desiredSize(); });
            method(TransformStreamDefaultController.prototype, "enqueue", function enqueue(chunk) { transformControllerOf(this).enqueue(chunk); });
            method(TransformStreamDefaultController.prototype, "error", function error(e) { transformControllerOf(this).error(e); });
            method(TransformStreamDefaultController.prototype, "terminate", function terminate() { transformControllerOf(this).terminate(); });
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
