namespace PeachBrackets.Scripting.Internal;

/// <summary>
/// <c>MessageChannel</c>, <c>MessagePort</c> and <c>window.postMessage</c> of the HTML standard (one realm: both ends of a channel are in this
/// document), and the transfer of ports, readable streams and writable streams through <c>structuredClone</c> and <c>postMessage</c>, which the
/// Streams standard builds on the cross-realm transform algorithms (a stream is carried by a port pair). A fragment of
/// <see cref="WebPlatformScripts.Bundle"/> put where <see cref="WebPlatformScripts.MessagingMarker"/> stands, after the streams fragment whose
/// <c>serializeWithTransfer</c> and <c>transferHandlers</c> it uses.
/// </summary>
internal static class MessagingScripts
{
    public const string Source = """
            // ---------------------------------------------------------------- MessageChannel, MessagePort, window.postMessage
            var portState = new WeakMap(), channelState = new WeakMap(), portToken = {};
            function newPortRecord() { return { wrapper: null, peer: null, queue: [], started: false, closed: false, scheduled: false }; }
            function MessagePort(token) {
                if (token !== portToken) throw new TypeError("Illegal constructor");
                return Reflect.construct(EventTarget, [], MessagePort);
            }
            MessagePort.prototype = Object.create(EventTarget.prototype, { constructor: { value: MessagePort, writable: true, configurable: true } });
            function wrapPort(record) {
                var port = new MessagePort(portToken);
                record.wrapper = port;
                portState.set(port, record);
                return port;
            }
            function portRecord(port) {
                var record = portState.get(port);
                if (!record) throw new TypeError("Illegal invocation");
                return record;
            }
            function schedulePort(record) {
                if (record.scheduled || !record.started || record.closed || record.queue.length === 0) return;
                record.scheduled = true;
                setTimeout(function () {
                    record.scheduled = false;
                    if (!record.started || record.closed || record.queue.length === 0) return;
                    var message = record.queue.shift();
                    var event = new MessageEvent("message", { data: message.data, origin: "", lastEventId: "", source: null, ports: Object.freeze(message.ports) });
                    record.wrapper.dispatchEvent(event);
                    schedulePort(record);
                }, 0);
            }
            function transferListOf(arg) {
                if (arg === undefined || arg === null) return [];
                if (!isObject(arg)) throw new TypeError("The transfer list is not a sequence or an options object");
                if (typeof arg[Symbol.iterator] === "function") return Array.from(arg);
                var list = arg.transfer;
                if (list === undefined) return [];
                if (!isObject(list) || typeof list[Symbol.iterator] !== "function") throw new TypeError("The transfer list is not a sequence");
                return Array.from(list);
            }
            function portsOf(serialized, list) {
                return list.filter(function (item) { return portState.has(item); }).map(function (item) { return serialized.transferred.get(item); });
            }
            method(MessagePort.prototype, "postMessage", function postMessage(message, options) {
                var record = portRecord(this);
                if (arguments.length < 1) throw new TypeError("Failed to execute 'postMessage' on 'MessagePort': 1 argument required, but only 0 present.");
                var list = transferListOf(options), self = this;
                list.forEach(function (item) {
                    if (item === self || (record.peer && item === record.peer.wrapper)) throw dataCloneError("A port cannot be transferred through itself or its entangled port");
                });
                var serialized = serializeWithTransfer(message, list);
                if (record.closed || !record.peer) return;
                record.peer.queue.push({ data: serialized.value, ports: portsOf(serialized, list) });
                schedulePort(record.peer);
            });
            method(MessagePort.prototype, "start", function start() {
                var record = portRecord(this);
                record.started = true;
                schedulePort(record);
            });
            method(MessagePort.prototype, "close", function close() {
                var record = portRecord(this);
                record.closed = true; record.queue = [];
                if (record.peer) { record.peer.peer = null; record.peer = null; }
            });
            handlerAttribute(MessagePort.prototype, "message");
            handlerAttribute(MessagePort.prototype, "messageerror");
            (function () {
                var descriptor = Object.getOwnPropertyDescriptor(MessagePort.prototype, "onmessage"), set = descriptor.set;
                descriptor.set = function (value) { set.call(this, value); var record = portRecord(this); record.started = true; schedulePort(record); };
                Object.defineProperty(MessagePort.prototype, "onmessage", descriptor);
            })();
            tag(MessagePort, "MessagePort");

            function MessageChannel() {
                if (!new.target) throw new TypeError("Failed to construct 'MessageChannel': Please use the 'new' operator");
                var first = newPortRecord(), second = newPortRecord();
                first.peer = second; second.peer = first;
                var self = Reflect.construct(Object, [], new.target);
                channelState.set(self, { port1: wrapPort(first), port2: wrapPort(second) });
                return self;
            }
            ["port1", "port2"].forEach(function (name) {
                readonly(MessageChannel.prototype, name, function () {
                    var state = channelState.get(this);
                    if (!state) throw new TypeError("Illegal invocation");
                    return state[name];
                });
            });
            tag(MessageChannel, "MessageChannel");

            transferHandlers.push({
                test: function (v) { return portState.has(v); },
                check: function (v) { if (portRecord(v).closed) throw dataCloneError("The MessagePort is closed and could not be transferred"); },
                transfer: function (v) {
                    var record = portRecord(v);
                    portState.set(v, { wrapper: v, peer: null, queue: [], started: false, closed: true, scheduled: false });
                    return wrapPort(record);
                }
            });

            function windowPostMessage(message, targetOrigin, transfer) {
                if (arguments.length < 1) throw new TypeError("Failed to execute 'postMessage' on 'Window': 1 argument required, but only 0 present.");
                var list, origin = "/";
                if (isObject(targetOrigin)) {
                    list = transferListOf(targetOrigin);
                    if (targetOrigin.targetOrigin !== undefined) origin = String(targetOrigin.targetOrigin);
                } else {
                    if (targetOrigin !== undefined) origin = String(targetOrigin);
                    list = transferListOf(transfer);
                }
                var deliver = true;
                if (origin !== "*" && origin !== "/") {
                    var parsed;
                    try { parsed = new URL(origin).origin; } catch (e) { throw new DOMException("Invalid target origin '" + origin + "' in a call to 'postMessage'.", "SyntaxError"); }
                    deliver = parsed === global.location.origin;
                }
                var serialized = serializeWithTransfer(message, list);
                if (!deliver) return;
                var ports = portsOf(serialized, list);
                setTimeout(function () {
                    global.dispatchEvent(new MessageEvent("message", { data: serialized.value, origin: global.location.origin, lastEventId: "", source: global, ports: Object.freeze(ports) }));
                }, 0);
            }

            // ---- Transferring streams: a stream is carried by a port pair (the cross-realm transform algorithms of the Streams standard).
            function postStreamMessage(port, type, value) {
                try { port.postMessage({ type: type, value: value }); }
                catch (e) { port.postMessage({ type: "error", value: e }); port.close(); throw e; }
            }
            function setUpCrossRealmReadable(port) {
                var controller;
                var readable = new ReadableStream({
                    start: function (c) { controller = c; },
                    pull: function () { postStreamMessage(port, "pull", undefined); return Promise.resolve(); },
                    cancel: function (reason) {
                        try { postStreamMessage(port, "error", reason); } finally { port.close(); }
                        return Promise.resolve();
                    }
                }, { highWaterMark: 0 });
                port.onmessage = function (event) {
                    var data = event.data;
                    try {
                        if (data.type === "chunk") controller.enqueue(data.value);
                        else if (data.type === "close") { controller.close(); port.close(); }
                        else if (data.type === "error") { controller.error(data.value); port.close(); }
                    } catch (e) { /* the stream was cancelled or errored already */ }
                };
                port.onmessageerror = function () {
                    var error = new DOMException("A message could not be deserialized", "DataCloneError");
                    try { postStreamMessage(port, "error", error); } catch (e) { }
                    controller.error(error);
                    port.close();
                };
                return readable;
            }
            function setUpCrossRealmWritable(port) {
                var controller, backpressure = deferred();
                port.onmessage = function (event) {
                    var data = event.data;
                    if (data.type === "pull") {
                        if (backpressure) { backpressure.resolve(undefined); backpressure = undefined; }
                    } else if (data.type === "error") {
                        controller.error(data.value);
                        if (backpressure) { backpressure.resolve(undefined); backpressure = undefined; }
                        port.close();
                    }
                };
                port.onmessageerror = function () {
                    var error = new DOMException("A message could not be deserialized", "DataCloneError");
                    try { postStreamMessage(port, "error", error); } catch (e) { }
                    controller.error(error);
                    if (backpressure) { backpressure.resolve(undefined); backpressure = undefined; }
                    port.close();
                };
                return new WritableStream({
                    start: function (c) { controller = c; },
                    write: function (chunk) {
                        if (!backpressure) { backpressure = deferred(); backpressure.resolve(undefined); }
                        return backpressure.promise.then(function () {
                            backpressure = deferred();
                            try { postStreamMessage(port, "chunk", chunk); } catch (e) { port.close(); throw e; }
                        });
                    },
                    close: function () { postStreamMessage(port, "close", undefined); port.close(); return Promise.resolve(); },
                    abort: function (reason) {
                        try { postStreamMessage(port, "error", reason); } finally { port.close(); }
                        return Promise.resolve();
                    }
                }, { highWaterMark: 1, size: function () { return 1; } });
            }
            transferHandlers.push({
                test: function (v) { return readableState.has(v); },
                check: function (v) { if (v.locked) throw dataCloneError("A ReadableStream that is locked could not be transferred"); },
                transfer: function (v) {
                    var channel = new MessageChannel();
                    var writable = setUpCrossRealmWritable(channel.port1);
                    markHandled(v.pipeTo(writable));
                    return setUpCrossRealmReadable(channel.port2);
                }
            });
            transferHandlers.push({
                test: function (v) { return writableState.has(v); },
                check: function (v) { if (v.locked) throw dataCloneError("A WritableStream that is locked could not be transferred"); },
                transfer: function (v) {
                    var channel = new MessageChannel();
                    var readable = setUpCrossRealmReadable(channel.port1);
                    markHandled(readable.pipeTo(v));
                    return setUpCrossRealmWritable(channel.port2);
                }
            });
""";
}
