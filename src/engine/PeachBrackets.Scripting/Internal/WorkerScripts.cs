namespace PeachBrackets.Scripting.Internal;

/// <summary>
/// <c>Worker</c> and what both ends of one need (the message codec), written in script on top of the native functions of <c>net</c>. A fragment of
/// <see cref="WebPlatformScripts.Bundle"/> put where <see cref="WebPlatformScripts.WorkersMarker"/> stands, after the messaging fragment whose
/// <c>serializeWithTransfer</c> and <c>transferListOf</c> it uses. A worker is another engine, so a message does not carry objects: the page encodes the
/// structured clone of the message as text, and the worker decodes it into objects of its own.
/// </summary>
internal static class WorkerScripts
{
    public const string Source = """
            // ---------------------------------------------------------------- Workers
            var workerById = new Map(), workerIds = new WeakMap();
            function encodeMessage(message, transferArg) {
                var list = transferListOf(transferArg);
                return encodeTree(serializeWithTransfer(message, list).value);
            }
            // The text of a value already cloned (what a port end in another engine is sent).
            function encodeTree(clonedValue) {
                var cloned = { value: clonedValue };
                var ids = new Map(), next = 0;
                function pairs(object) { return Object.keys(object).map(function (k) { return [k, enc(object[k])]; }); }
                function enc(v) {
                    if (v === undefined) return { t: "u" };
                    if (v === null || typeof v === "boolean" || typeof v === "string") return v;
                    if (typeof v === "number") return Number.isFinite(v) && !Object.is(v, -0) ? v : { t: "n", v: Object.is(v, -0) ? "-0" : String(v) };
                    if (typeof v === "bigint") return { t: "bi", v: String(v) };
                    if (ids.has(v)) return { t: "r", id: ids.get(v) };
                    var id = next++; ids.set(v, id);
                    if (v instanceof ArrayBuffer) return { t: "ab", id: id, b: bytesToBinary(new Uint8Array(v)) };
                    if (ArrayBuffer.isView(v)) return { t: "vw", id: id, c: v instanceof DataView ? "DataView" : v.constructor.name, buf: enc(v.buffer), o: v.byteOffset, l: v instanceof DataView ? v.byteLength : v.length };
                    if (v instanceof Date) return { t: "d", id: id, v: v.getTime() };
                    if (v instanceof RegExp) return { t: "re", id: id, s: v.source, f: v.flags };
                    if (v instanceof Boolean || v instanceof Number || v instanceof String || Object.prototype.toString.call(v) === "[object BigInt]") return { t: "w", id: id, k: Object.prototype.toString.call(v).slice(8, -1), v: enc(v.valueOf()) };
                    if (typeof global.File === "function" && v instanceof global.File) return { t: "f", id: id, b: bytesToBinary(blobOf(v).bytes), type: v.type, name: v.name, lm: v.lastModified };
                    if (typeof global.Blob === "function" && v instanceof global.Blob) return { t: "bl", id: id, b: bytesToBinary(blobOf(v).bytes), type: v.type };
                    if (typeof global.ImageData === "function" && v instanceof global.ImageData) return { t: "id", id: id, w: v.width, h: v.height, cs: v.colorSpace, b: bytesToBinary(new Uint8Array(v.data.buffer, v.data.byteOffset, v.data.byteLength)) };
                    if (v instanceof Map) { var entries = []; v.forEach(function (val, key) { entries.push([enc(key), enc(val)]); }); return { t: "m", id: id, e: entries }; }
                    if (v instanceof Set) { var items = []; v.forEach(function (val) { items.push(enc(val)); }); return { t: "s", id: id, e: items }; }
                    if (v instanceof DOMException) return { t: "de", id: id, n: v.name, m: v.message };
                    if (v instanceof Error) return { t: "er", id: id, n: v.name, m: Object.prototype.hasOwnProperty.call(v, "message") ? String(v.message) : null, st: typeof v.stack === "string" ? v.stack : null, c: Object.prototype.hasOwnProperty.call(v, "cause") ? enc(v.cause) : { t: "u" } };
                    if (Array.isArray(v)) return { t: "a", id: id, n: v.length, p: pairs(v) };
                    if (portState.has(v)) {
                        var port = portRecord(v);
                        if (port.remote === undefined) {
                            // The end moves to the other engine: what it already holds goes ahead of what comes later, and this one stands for it from now on.
                            port.remote = net.portCreate();
                            portRemote.set(port.remote, port);
                            var waiting = port.queue; port.queue = [];
                            waiting.forEach(function (item) { net.portSend(port.remote, encodeTree(item.data)); });
                        }
                        return { t: "p", id: id, pid: port.remote };
                    }
                    if (net.isHandleable(v)) return { t: "h", id: id, h: net.putHandle(v) };
                    if (Object.prototype.toString.call(v) === "[object Object]") return { t: "o", id: id, p: pairs(v) };
                    throw dataCloneError("An object could not be cloned");
                }
                return JSON.stringify(enc(cloned.value));
            }
            var portRemote = new Map();
            function decodeMessage(text, ports) {
                var memory = new Map();
                function dec(n) {
                    if (n === null || typeof n !== "object") return n;
                    if (n.t === "u") return undefined;
                    if (n.t === "n") return n.v === "-0" ? -0 : Number(n.v);
                    if (n.t === "bi") return BigInt(n.v);
                    if (n.t === "r") return memory.get(n.id);
                    var result;
                    switch (n.t) {
                        case "ab": result = binaryToBytes(n.b).buffer; memory.set(n.id, result); return result;
                        case "vw": {
                            var buffer = dec(n.buf);
                            result = n.c === "DataView" ? new DataView(buffer, n.o, n.l) : new global[n.c](buffer, n.o, n.l);
                            memory.set(n.id, result); return result;
                        }
                        case "d": result = new Date(n.v); break;
                        case "re": result = new RegExp(n.s, n.f); break;
                        case "w": result = Object(dec(n.v)); break;
                        case "f": result = new global.File([binaryToBytes(n.b)], n.name, { type: n.type, lastModified: n.lm }); break;
                        case "bl": result = new global.Blob([binaryToBytes(n.b)], { type: n.type }); break;
                        case "id": result = new global.ImageData(new Uint8ClampedArray(binaryToBytes(n.b).buffer), n.w, n.h, { colorSpace: n.cs }); break;
                        case "m": result = new Map(); memory.set(n.id, result); n.e.forEach(function (e) { result.set(dec(e[0]), dec(e[1])); }); return result;
                        case "s": result = new Set(); memory.set(n.id, result); n.e.forEach(function (e) { result.add(dec(e)); }); return result;
                        case "de": result = new DOMException(n.m, n.n); break;
                        case "er": {
                            var ctor = ["Error", "EvalError", "RangeError", "ReferenceError", "SyntaxError", "TypeError", "URIError"].indexOf(n.n) >= 0 ? global[n.n] : Error;
                            result = new ctor(); memory.set(n.id, result);
                            if (n.m !== null) Object.defineProperty(result, "message", { value: n.m, writable: true, configurable: true });
                            if (n.c !== undefined && !(n.c && n.c.t === "u")) Object.defineProperty(result, "cause", { value: dec(n.c), writable: true, configurable: true });
                            if (n.st !== null) Object.defineProperty(result, "stack", { value: n.st, writable: true, configurable: true });
                            return result;
                        }
                        case "a": result = new Array(n.n); memory.set(n.id, result); n.p.forEach(function (p) { result[p[0]] = dec(p[1]); }); return result;
                        case "o": result = {}; memory.set(n.id, result); n.p.forEach(function (p) { result[p[0]] = dec(p[1]); }); return result;
                        case "h": result = net.takeHandle(n.h); break;
                        case "p": {
                            // A port end of another engine: this one is entangled with a proxy that sends what it is given there.
                            net.portAttach(n.pid);
                            var here = newPortRecord(), proxy = newPortRecord();
                            here.peer = proxy; proxy.peer = here; proxy.remote = n.pid;
                            portRemote.set(n.pid, proxy);
                            result = wrapPort(here);
                            if (ports) ports.push(result);
                            break;
                        }
                        default: throw new DOMException("A message could not be deserialized", "DataCloneError");
                    }
                    memory.set(n.id, result);
                    return result;
                }
                return dec(JSON.parse(text));
            }

            // An ImageBitmap and an OffscreenCanvas are transferable: the object that takes their place in a message is made of what they held.
            [function (v) { return typeof ElementImage === "function" && v instanceof ElementImage; }, function (v) { return typeof ImageBitmap === "function" && v instanceof ImageBitmap; }, function (v) { return typeof OffscreenCanvas === "function" && v instanceof OffscreenCanvas; }].forEach(function (test) {
                transferHandlers.push({
                    test: test,
                    check: function (v) { net.checkTransfer(v); },
                    transfer: function (v) { return net.transferObject(v); }
                });
            });

            function dataUrlText(url) {
                var comma = url.indexOf(",");
                var meta = url.slice(5, comma), body = url.slice(comma + 1);
                return /;base64$/i.test(meta) ? utf8Decode(binaryToBytes(atob(decodeURIComponent(body)))) : decodeURIComponent(body);
            }
            function Worker(scriptURL, options) {
                if (!new.target) throw new TypeError("Failed to construct 'Worker': Please use the 'new' operator, this DOM object constructor cannot be called as a function.");
                if (arguments.length < 1) throw new TypeError("Failed to construct 'Worker': 1 argument required, but only 0 present.");
                var self = Reflect.construct(EventTarget, [], new.target);
                var absolute;
                try { absolute = new URL(String(scriptURL), net.documentUrl()).href; }
                catch (e) { throw new DOMException("Failed to construct 'Worker': Script at '" + String(scriptURL) + "' cannot be accessed from origin '" + global.location.origin + "'.", "SyntaxError"); }
                var type = isObject(options) && options.type !== undefined ? String(options.type) : "classic";
                if (type !== "classic" && type !== "module") throw new TypeError("Failed to construct 'Worker': The provided value '" + type + "' is not a valid enum value of type WorkerType.");
                var name = isObject(options) && options.name !== undefined ? String(options.name) : "";
                var source = null;
                if (/^blob:/i.test(absolute)) {
                    var blob = blobUrls[absolute];
                    if (blob) source = utf8Decode(blobOf(blob).bytes);
                } else if (/^data:/i.test(absolute)) source = dataUrlText(absolute);
                var id = net.workerCreate(absolute, name, type, source);
                workerById.set(id, self); workerIds.set(self, id);
                return self;
            }
            Worker.prototype = Object.create(EventTarget.prototype, { constructor: { value: Worker, writable: true, configurable: true } });
            function workerId(worker) {
                var id = workerIds.get(worker);
                if (id === undefined) throw new TypeError("Illegal invocation");
                return id;
            }
            method(Worker.prototype, "postMessage", function postMessage(message, transfer) {
                var id = workerId(this);
                if (arguments.length < 1) throw new TypeError("Failed to execute 'postMessage' on 'Worker': 1 argument required, but only 0 present.");
                var text = encodeMessage(message, transfer);
                net.workerPost(id, text);
            });
            method(Worker.prototype, "terminate", function terminate() { net.workerTerminate(workerId(this)); });
            handlerAttribute(Worker.prototype, "message");
            handlerAttribute(Worker.prototype, "messageerror");
            handlerAttribute(Worker.prototype, "error");
            tag(Worker, "Worker");
            define("Worker", Worker);

            // What the host calls: a message or an error arriving at a Worker object in the page, or at the global of a worker.
            net.registerWorkerCodec({
                toWorkerObject: function (id, kind, text, a, b, c) {
                    var worker = workerById.get(id);
                    if (!worker) return;
                    if (kind === "message") { var ports = []; var data = decodeMessage(text, ports); worker.dispatchEvent(new MessageEvent("message", { data: data, origin: "", lastEventId: "", source: null, ports: Object.freeze(ports) })); }
                    else if (kind === "messageerror") worker.dispatchEvent(new MessageEvent("messageerror", { data: null, origin: "", lastEventId: "", source: null, ports: [] }));
                    else worker.dispatchEvent(new ErrorEvent("error", { message: text, filename: a, lineno: b, colno: c, error: null, cancelable: true }));
                },
                toWorkerGlobal: function (text) {
                    var ports = [], data = decodeMessage(text, ports);
                    global.dispatchEvent(new MessageEvent("message", { data: data, origin: "", lastEventId: "", source: null, ports: Object.freeze(ports) }));
                },
                // A message for a port end this engine stands for: it goes to the end entangled with it, or on to the next engine.
                portReceive: function (pid, text) {
                    var proxy = portRemote.get(pid);
                    if (!proxy) return;
                    var target = proxy.peer;
                    if (!target) return;
                    if (target.remote !== undefined) { net.portSend(target.remote, text); return; }
                    if (text === "\u0000close") { target.peer = null; return; }
                    if (target.closed) return;
                    var ports = [], data = decodeMessage(text, ports);
                    target.queue.push({ data: data, ports: Object.freeze(ports) });
                    schedulePort(target);
                },
                setup: function (name) {
                    function WorkerGlobalScope() { throw new TypeError("Illegal constructor"); }
                    var parent = Object.getPrototypeOf(global);
                    WorkerGlobalScope.prototype = Object.create(parent, { constructor: { value: WorkerGlobalScope, writable: true, configurable: true } });
                    tag(WorkerGlobalScope, "WorkerGlobalScope");
                    function DedicatedWorkerGlobalScope() { throw new TypeError("Illegal constructor"); }
                    DedicatedWorkerGlobalScope.prototype = Object.create(WorkerGlobalScope.prototype, { constructor: { value: DedicatedWorkerGlobalScope, writable: true, configurable: true } });
                    tag(DedicatedWorkerGlobalScope, "DedicatedWorkerGlobalScope");
                    define("WorkerGlobalScope", WorkerGlobalScope);
                    define("DedicatedWorkerGlobalScope", DedicatedWorkerGlobalScope);
                    Object.setPrototypeOf(global, DedicatedWorkerGlobalScope.prototype);
                    define("self", global);
                    define("name", name);
                    define("postMessage", function postMessage(message, transfer) {
                        if (arguments.length < 1) throw new TypeError("Failed to execute 'postMessage' on 'DedicatedWorkerGlobalScope': 1 argument required, but only 0 present.");
                        net.workerOut(encodeMessage(message, transfer));
                    });
                    define("close", function close() { net.workerClose(); });
                    define("importScripts", function importScripts() {
                        for (var i = 0; i < arguments.length; i++) {
                            var absolute;
                            try { absolute = new URL(String(arguments[i]), global.location.href).href; }
                            catch (e) { throw new DOMException("The URL '" + String(arguments[i]) + "' is invalid.", "SyntaxError"); }
                            net.workerImport(absolute);
                        }
                    });
                }
            });
""";
}
