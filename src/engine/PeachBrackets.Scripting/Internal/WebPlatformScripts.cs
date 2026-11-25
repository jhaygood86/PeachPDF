namespace PeachBrackets.Scripting.Internal;

/// <summary>
/// The network and data classes of the web platform, written in script on top of a few native functions (<c>net</c>): <c>URL</c> and
/// <c>URLSearchParams</c>, <c>Headers</c>, <c>Request</c> and <c>Response</c> with <c>fetch</c>, <c>XMLHttpRequest</c>, <c>Blob</c>, <c>File</c>,
/// <c>FormData</c>, <c>TextEncoder</c> and <c>TextDecoder</c>. Run once, the first time a script names one of them.
/// </summary>
internal static class WebPlatformScripts
{
    public const string Bundle = """
        (function (net, global) {
            "use strict";

            function define(name, value) {
                Object.defineProperty(global, name, { value: value, writable: true, configurable: true });
            }
            function tag(constructor, name) {
                Object.defineProperty(constructor.prototype, Symbol.toStringTag, { value: name, configurable: true });
            }
            function readonly(proto, name, get) {
                Object.defineProperty(proto, name, { get: get, enumerable: true, configurable: true });
            }
            function method(proto, name, fn) {
                Object.defineProperty(proto, name, { value: fn, writable: true, configurable: true, enumerable: true });
            }
            var TOKEN = {};

            // ---------------------------------------------------------------- bytes and text
            function bytesToBinary(bytes) {
                var text = "";
                for (var i = 0; i < bytes.length; i += 4096)
                    text += String.fromCharCode.apply(null, Array.prototype.slice.call(bytes, i, i + 4096));
                return text;
            }
            function binaryToBytes(text) {
                var bytes = new Uint8Array(text.length);
                for (var i = 0; i < text.length; i++) bytes[i] = text.charCodeAt(i) & 255;
                return bytes;
            }
            function copyBytes(source) {
                if (source instanceof ArrayBuffer) return new Uint8Array(source.slice(0));
                return new Uint8Array(source.buffer.slice(source.byteOffset, source.byteOffset + source.byteLength));
            }
            function isBufferSource(value) {
                return value instanceof ArrayBuffer || ArrayBuffer.isView(value);
            }
            function utf8(text) { return binaryToBytes(net.utf8Encode(String(text))); }
            function utf8Decode(bytes) { return net.utf8Decode(bytesToBinary(bytes), true); }
            function toArrayBuffer(bytes) { return bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength); }

            var encodingNames = {
                "utf-8": "utf-8", "utf8": "utf-8", "unicode-1-1-utf-8": "utf-8",
                "utf-16le": "utf-16le", "utf-16": "utf-16le", "ucs-2": "utf-16le", "unicode": "utf-16le",
                "utf-16be": "utf-16be",
                "iso-8859-1": "windows-1252", "latin1": "windows-1252", "l1": "windows-1252", "windows-1252": "windows-1252",
                "us-ascii": "windows-1252", "ascii": "windows-1252", "cp1252": "windows-1252"
            };
            function normalizeEncoding(label) {
                return encodingNames[String(label).replace(/^[ \t\n\f\r]+|[ \t\n\f\r]+$/g, "").toLowerCase()] || null;
            }

            function TextEncoder() {
                if (new.target === undefined) throw new TypeError("Failed to construct 'TextEncoder': Please use the 'new' operator.");
            }
            readonly(TextEncoder.prototype, "encoding", function () { return "utf-8"; });
            method(TextEncoder.prototype, "encode", function encode(input) {
                return utf8(input === undefined ? "" : input);
            });
            method(TextEncoder.prototype, "encodeInto", function encodeInto(source, destination) {
                var bytes = utf8(source), read = 0, written = 0, text = String(source);
                for (var i = 0; i < text.length;) {
                    var cp = text.codePointAt(i), width = cp > 0xFFFF ? 2 : 1;
                    var size = cp < 0x80 ? 1 : cp < 0x800 ? 2 : cp < 0x10000 ? 3 : 4;
                    if (written + size > destination.length) break;
                    written += size; read += width; i += width;
                }
                for (var j = 0; j < written; j++) destination[j] = bytes[j];
                return { read: read, written: written };
            });
            tag(TextEncoder, "TextEncoder");

            var decoderState = new WeakMap();
            function TextDecoder(label, options) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'TextDecoder': Please use the 'new' operator.");
                var name = normalizeEncoding(label === undefined ? "utf-8" : label);
                if (name === null) throw new RangeError("The encoding label provided ('" + label + "') is invalid.");
                decoderState.set(this, { name: name, fatal: !!(options && options.fatal), ignoreBOM: !!(options && options.ignoreBOM) });
            }
            readonly(TextDecoder.prototype, "encoding", function () { return decoderState.get(this).name; });
            readonly(TextDecoder.prototype, "fatal", function () { return decoderState.get(this).fatal; });
            readonly(TextDecoder.prototype, "ignoreBOM", function () { return decoderState.get(this).ignoreBOM; });
            method(TextDecoder.prototype, "decode", function decode(input) {
                var state = decoderState.get(this);
                var bytes = input === undefined ? new Uint8Array(0) : isBufferSource(input) ? copyBytes(input) : (function () { throw new TypeError("The provided value is not of type '(ArrayBuffer or ArrayBufferView)'."); })();
                var binary = bytesToBinary(bytes);
                if (state.name === "utf-8") {
                    var text = net.utf8Decode(binary, !state.ignoreBOM);
                    if (state.fatal && text.indexOf("�") >= 0 && utf8(text).length !== bytes.length) throw new TypeError("The encoded data was not valid for encoding utf-8");
                    return text;
                }
                var decoded = net.decode(binary, state.name);
                if (decoded === null) throw new TypeError("Unsupported encoding");
                return !state.ignoreBOM && decoded.charCodeAt(0) === 0xFEFF ? decoded.slice(1) : decoded;
            });
            tag(TextDecoder, "TextDecoder");

            // ---------------------------------------------------------------- URL
            var specialPorts = { "ftp": 21, "file": null, "http": 80, "https": 443, "ws": 80, "wss": 443 };
            function isSpecial(scheme) { return Object.prototype.hasOwnProperty.call(specialPorts, scheme); }

            function inC0(c) { return c <= 0x1F || c > 0x7E; }
            function inFragmentSet(c) { return inC0(c) || c === 0x20 || c === 0x22 || c === 0x3C || c === 0x3E || c === 0x60; }
            function inQuerySet(c) { return inC0(c) || c === 0x20 || c === 0x22 || c === 0x23 || c === 0x3C || c === 0x3E; }
            function inSpecialQuerySet(c) { return inQuerySet(c) || c === 0x27; }
            function inPathSet(c) { return inQuerySet(c) || c === 0x3F || c === 0x5E || c === 0x60 || c === 0x7B || c === 0x7D; }
            function inUserinfoSet(c) { return inPathSet(c) || c === 0x2F || c === 0x3A || c === 0x3B || c === 0x3D || c === 0x40 || (c >= 0x5B && c <= 0x5D) || c === 0x7C; }
            var HEX = "0123456789ABCDEF";
            function percentEncode(text, inSet) {
                var out = "";
                for (var i = 0; i < text.length; i++) {
                    var cp = text.codePointAt(i);
                    if (cp > 0xFFFF) i++;
                    if (!inSet(cp)) { out += String.fromCodePoint(cp); continue; }
                    var bytes = net.utf8Encode(String.fromCodePoint(cp));
                    for (var b = 0; b < bytes.length; b++) {
                        var v = bytes.charCodeAt(b);
                        out += "%" + HEX[v >> 4] + HEX[v & 15];
                    }
                }
                return out;
            }
            // The bytes a percent-encoded string stands for, as a string of bytes.
            function percentDecodeToBinary(text) {
                var out = "";
                for (var i = 0; i < text.length; i++) {
                    var c = text[i];
                    if (c === "%" && /^[0-9A-Fa-f]{2}$/.test(text.substr(i + 1, 2))) {
                        out += String.fromCharCode(parseInt(text.substr(i + 1, 2), 16));
                        i += 2;
                    } else {
                        var cp = text.codePointAt(i);
                        if (cp > 0xFFFF) i++;
                        out += net.utf8Encode(String.fromCodePoint(cp));
                    }
                }
                return out;
            }

            function parseIPv4Number(part) {
                if (part === "") return NaN;
                var radix = 10;
                if (part.length >= 2 && (part.slice(0, 2) === "0x" || part.slice(0, 2) === "0X")) { part = part.slice(2); radix = 16; }
                else if (part.length >= 2 && part[0] === "0") { part = part.slice(1); radix = 8; }
                if (part === "") return 0;
                var valid = radix === 16 ? /^[0-9A-Fa-f]+$/ : radix === 8 ? /^[0-7]+$/ : /^[0-9]+$/;
                return valid.test(part) ? parseInt(part, radix) : NaN;
            }
            function endsInANumber(host) {
                var parts = host.split(".");
                if (parts[parts.length - 1] === "") { if (parts.length === 1) return false; parts.pop(); }
                var last = parts[parts.length - 1];
                return /^[0-9]+$/.test(last) || !isNaN(parseIPv4Number(last)) && /^0[xX]/.test(last);
            }
            function parseIPv4(host) {
                var parts = host.split(".");
                if (parts[parts.length - 1] === "" && parts.length > 1) parts.pop();
                if (parts.length > 4) return null;
                var numbers = [];
                for (var i = 0; i < parts.length; i++) {
                    var n = parseIPv4Number(parts[i]);
                    if (isNaN(n)) return null;
                    numbers.push(n);
                }
                for (var j = 0; j < numbers.length - 1; j++) if (numbers[j] > 255) return null;
                if (numbers[numbers.length - 1] >= Math.pow(256, 5 - numbers.length)) return null;
                var ipv4 = numbers[numbers.length - 1];
                for (var k = 0; k < numbers.length - 1; k++) ipv4 += numbers[k] * Math.pow(256, 3 - k);
                var out = [];
                for (var m = 0; m < 4; m++) { out.unshift(String(ipv4 % 256)); ipv4 = Math.floor(ipv4 / 256); }
                return out.join(".");
            }
            function parseIPv6(text) {
                var address = [0, 0, 0, 0, 0, 0, 0, 0], piece = 0, compress = null, p = 0;
                if (text[p] === ":") { if (text[p + 1] !== ":") return null; p += 2; piece++; compress = piece; }
                while (p < text.length) {
                    if (piece === 8) return null;
                    if (text[p] === ":") { if (compress !== null) return null; p++; piece++; compress = piece; continue; }
                    var value = 0, length = 0;
                    while (length < 4 && p < text.length && /[0-9A-Fa-f]/.test(text[p])) { value = value * 16 + parseInt(text[p], 16); p++; length++; }
                    if (text[p] === ".") {
                        if (length === 0) return null;
                        p -= length;
                        if (piece > 6) return null;
                        var seen = 0;
                        while (p < text.length) {
                            var v4 = null;
                            if (seen > 0) { if (text[p] === "." && seen < 4) p++; else return null; }
                            if (p >= text.length || !/[0-9]/.test(text[p])) return null;
                            while (p < text.length && /[0-9]/.test(text[p])) {
                                var number = parseInt(text[p], 10);
                                if (v4 === null) v4 = number; else if (v4 === 0) return null; else v4 = v4 * 10 + number;
                                if (v4 > 255) return null;
                                p++;
                            }
                            address[piece] = address[piece] * 256 + v4;
                            seen++;
                            if (seen === 2 || seen === 4) piece++;
                        }
                        if (seen !== 4) return null;
                        break;
                    } else if (text[p] === ":") { p++; if (p >= text.length) return null; }
                    else if (p < text.length) return null;
                    address[piece] = value;
                    piece++;
                }
                if (compress !== null) {
                    var swaps = piece - compress;
                    piece = 7;
                    while (piece !== 0 && swaps > 0) {
                        var temp = address[compress + swaps - 1];
                        address[compress + swaps - 1] = address[piece];
                        address[piece] = temp;
                        piece--; swaps--;
                    }
                } else if (piece !== 8) return null;
                // Serialize: the longest run (two or more) of zero pieces becomes ::
                var best = -1, bestLength = 0;
                for (var s = 0; s < 8; s++) {
                    if (address[s] !== 0) continue;
                    var e = s;
                    while (e < 8 && address[e] === 0) e++;
                    if (e - s > bestLength) { best = s; bestLength = e - s; }
                    s = e;
                }
                if (bestLength < 2) best = -1;
                var out = "", ignore0 = false;
                for (var i = 0; i < 8; i++) {
                    if (ignore0 && address[i] === 0) continue;
                    ignore0 = false;
                    if (best === i) { out += i === 0 ? "::" : ":"; ignore0 = true; continue; }
                    out += address[i].toString(16);
                    if (i !== 7) out += ":";
                }
                return "[" + out + "]";
            }
            var forbiddenHost = /[\u0000\t\n\r #\/:<>?@\[\\\]^|]/;
            var forbiddenDomain = /[\u0000-\u001F #%\/:<>?@\[\\\]^|\u007F]/;
            function parseHost(input, special) {
                if (input[0] === "[") {
                    if (input[input.length - 1] !== "]") return null;
                    return parseIPv6(input.slice(1, -1));
                }
                if (!special) {
                    if (forbiddenHost.test(input)) return null;
                    return percentEncode(input, inC0);
                }
                var domain = net.utf8Decode(percentDecodeToBinary(input), false);
                var ascii = net.idna(domain);
                if (ascii === null || ascii === "") return null;
                ascii = ascii.toLowerCase();
                if (forbiddenDomain.test(ascii)) return null;
                if (endsInANumber(ascii)) return parseIPv4(ascii);
                return ascii;
            }

            function newRecord(scheme) {
                return { scheme: scheme, username: "", password: "", host: null, port: null, path: [], opaque: false, query: null, fragment: null };
            }
            function copyRecord(r) {
                return { scheme: r.scheme, username: r.username, password: r.password, host: r.host, port: r.port, path: r.path.slice(), opaque: r.opaque, query: r.query, fragment: r.fragment };
            }
            function isSingleDot(s) { s = s.toLowerCase(); return s === "." || s === "%2e"; }
            function isDoubleDot(s) { s = s.toLowerCase(); return s === ".." || s === ".%2e" || s === "%2e." || s === "%2e%2e"; }
            function isDriveLetter(s) { return /^[A-Za-z][:|]$/.test(s); }

            // Fills in the path, query and fragment from text that starts at the path.
            function parsePathAndAfter(url, text, special, basePath) {
                var hash = text.indexOf("#");
                if (hash >= 0) { url.fragment = percentEncode(text.slice(hash + 1), inFragmentSet); text = text.slice(0, hash); }
                var question = text.indexOf("?");
                if (question >= 0) { url.query = percentEncode(text.slice(question + 1), special ? inSpecialQuerySet : inQuerySet); text = text.slice(0, question); }

                var path = basePath || [];
                var segments = text.split(special ? /[\/\\]/ : "/");
                // A path that begins with a slash begins with an empty segment, which is the slash itself.
                if (/^[\/\\]/.test(text) || (!special && text[0] === "/")) segments.shift();
                else if (text === "") segments = [];
                for (var i = 0; i < segments.length; i++) {
                    var segment = segments[i], last = i === segments.length - 1;
                    if (isDoubleDot(segment)) {
                        if (path.length > 0 && !(url.scheme === "file" && path.length === 1 && isDriveLetter(path[0]))) path.pop();
                        if (last) path.push("");
                    } else if (isSingleDot(segment)) {
                        if (last) path.push("");
                    } else {
                        if (url.scheme === "file" && path.length === 0 && isDriveLetter(segment)) segment = segment[0] + ":";
                        path.push(percentEncode(segment, inPathSet));
                    }
                }
                url.path = path;
                if (special && url.path.length === 0) url.path = [""];
                return url;
            }

            // After `//`: the authority, then the path and what follows.
            function parseAuthority(url, text, special) {
                var end = text.length;
                for (var i = 0; i < text.length; i++) {
                    var c = text[i];
                    if (c === "/" || c === "?" || c === "#" || (special && c === "\\")) { end = i; break; }
                }
                var authority = text.slice(0, end), rest = text.slice(end);
                var at = authority.lastIndexOf("@");
                if (at >= 0) {
                    var userinfo = authority.slice(0, at);
                    authority = authority.slice(at + 1);
                    var colon = userinfo.indexOf(":");
                    url.username = percentEncode(colon < 0 ? userinfo : userinfo.slice(0, colon), inUserinfoSet);
                    url.password = colon < 0 ? "" : percentEncode(userinfo.slice(colon + 1), inUserinfoSet);
                    if (authority === "") return null;
                }
                var host = authority, port = "";
                var bracketEnd = authority.lastIndexOf("]");
                var portColon = authority.lastIndexOf(":");
                if (portColon > bracketEnd) { host = authority.slice(0, portColon); port = authority.slice(portColon + 1); }
                if (host === "") {
                    if (special || at >= 0 || port !== "") return null;
                    url.host = "";
                } else {
                    var parsed = parseHost(host, special);
                    if (parsed === null) return null;
                    url.host = parsed;
                }
                if (port !== "") {
                    if (!/^[0-9]+$/.test(port)) return null;
                    var n = parseInt(port, 10);
                    if (n > 65535) return null;
                    url.port = specialPorts[url.scheme] === n ? null : n;
                }
                return parsePathAndAfter(url, rest, special);
            }

            function parseFile(url, text, base) {
                if (/^[\/\\]{2}/.test(text)) {
                    var rest = text.slice(2), end = rest.search(/[\/\\?#]/);
                    if (end < 0) end = rest.length;
                    var host = rest.slice(0, end);
                    if (isDriveLetter(host)) { url.host = ""; return parsePathAndAfter(url, "/" + rest, true); }
                    if (host === "") url.host = "";
                    else {
                        var parsed = parseHost(host, true);
                        if (parsed === null) return null;
                        url.host = parsed === "localhost" ? "" : parsed;
                    }
                    return parsePathAndAfter(url, rest.slice(end), true);
                }
                url.host = "";
                if (/^[\/\\]/.test(text)) {
                    if (base && base.scheme === "file") url.host = base.host;
                    return parsePathAndAfter(url, text, true);
                }
                if (base && base.scheme === "file") {
                    url.host = base.host;
                    if (text === "") { url.path = base.path.slice(); url.query = base.query; return url; }
                    if (text[0] === "?") { url.path = base.path.slice(); return parsePathAndAfter(url, text, true, base.path.slice()); }
                    if (text[0] === "#") { url.path = base.path.slice(); url.query = base.query; url.fragment = percentEncode(text.slice(1), inFragmentSet); return url; }
                    return parsePathAndAfter(url, text, true, base.path.slice(0, -1));
                }
                return parsePathAndAfter(url, "/" + text, true);
            }

            function parseRelative(url, text, base) {
                var special = isSpecial(url.scheme);
                if (/^[\/]{2}/.test(text) || (special && /^[\/\\]{2}/.test(text))) return parseAuthority(url, text.slice(2), special);
                url.username = base.username; url.password = base.password; url.host = base.host; url.port = base.port;
                if (text === "") { url.path = base.path.slice(); url.query = base.query; return url; }
                if (text[0] === "#") { url.path = base.path.slice(); url.query = base.query; url.fragment = percentEncode(text.slice(1), inFragmentSet); return url; }
                if (text[0] === "?") { return parsePathAndAfter(url, text, special, base.path.slice()); }
                if (text[0] === "/" || (special && text[0] === "\\")) return parsePathAndAfter(url, text, special);
                return parsePathAndAfter(url, text, special, base.path.slice(0, -1));
            }

            function basicParse(input, base) {
                input = String(input).replace(/^[\u0000- ]+|[\u0000- ]+$/g, "").replace(/[\t\n\r]/g, "");
                var match = /^([A-Za-z][A-Za-z0-9+\-.]*):/.exec(input);
                if (match) {
                    var scheme = match[1].toLowerCase(), rest = input.slice(match[0].length), url = newRecord(scheme);
                    var special = isSpecial(scheme);
                    if (scheme === "file") return parseFile(url, rest, base && base.scheme === "file" ? base : null);
                    if (special) {
                        if (base && base.scheme === scheme && !/^[\/\\]/.test(rest)) return parseRelative(url, rest, base);
                        return parseAuthority(url, rest.replace(/^[\/\\]*/, ""), true);
                    }
                    if (rest.slice(0, 2) === "//") return parseAuthority(url, rest.slice(2), false);
                    if (rest[0] === "/") return parsePathAndAfter(url, rest, false);
                    // An opaque path: everything up to the query and fragment.
                    url.opaque = true;
                    var hash = rest.indexOf("#");
                    if (hash >= 0) { url.fragment = percentEncode(rest.slice(hash + 1), inFragmentSet); rest = rest.slice(0, hash); }
                    var question = rest.indexOf("?");
                    if (question >= 0) { url.query = percentEncode(rest.slice(question + 1), inQuerySet); rest = rest.slice(0, question); }
                    url.path = [percentEncode(rest, inC0)];
                    return url;
                }
                if (!base) return null;
                if (base.opaque) {
                    if (input[0] !== "#") return null;
                    var copy = copyRecord(base);
                    copy.fragment = percentEncode(input.slice(1), inFragmentSet);
                    return copy;
                }
                var relative = newRecord(base.scheme);
                if (base.scheme === "file") return parseFile(relative, input, base);
                return parseRelative(relative, input, base);
            }

            function serialize(u, withoutFragment) {
                var out = u.scheme + ":";
                if (u.host !== null) {
                    out += "//";
                    if (u.username !== "" || u.password !== "") {
                        out += u.username;
                        if (u.password !== "") out += ":" + u.password;
                        out += "@";
                    }
                    out += u.host;
                    if (u.port !== null) out += ":" + u.port;
                } else if (!u.opaque && u.path.length > 1 && u.path[0] === "") out += "/.";
                out += u.opaque ? u.path[0] : u.path.map(function (s) { return "/" + s; }).join("");
                if (u.query !== null) out += "?" + u.query;
                if (!withoutFragment && u.fragment !== null) out += "#" + u.fragment;
                return out;
            }

            var urlState = new WeakMap();
            var blobUrls = Object.create(null);

            function originOf(u) {
                if (u.scheme === "blob") {
                    var inner = basicParse(u.path[0]);
                    return inner && (inner.scheme === "http" || inner.scheme === "https") ? originOf(inner) : "null";
                }
                if (u.scheme === "http" || u.scheme === "https" || u.scheme === "ws" || u.scheme === "wss" || u.scheme === "ftp")
                    return u.scheme + "://" + u.host + (u.port !== null ? ":" + u.port : "");
                return "null";
            }

            function URL(url, base) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'URL': Please use the 'new' operator, this DOM object constructor cannot be called as a function.");
                if (arguments.length === 0) throw new TypeError("Failed to construct 'URL': 1 argument required, but only 0 present.");
                var baseRecord = null;
                if (base !== undefined) {
                    baseRecord = basicParse(String(base));
                    if (baseRecord === null) throw new TypeError("Failed to construct 'URL': Invalid base URL");
                }
                var record = basicParse(String(url), baseRecord);
                if (record === null) throw new TypeError("Failed to construct 'URL': Invalid URL");
                urlState.set(this, { record: record, params: null });
            }
            function stateOf(self) {
                var state = urlState.get(self);
                if (!state) throw new TypeError("Illegal invocation");
                return state;
            }
            function updateSearchParams(state) {
                if (state.params) paramsState.get(state.params).list = parseUrlencoded(state.record.query === null ? "" : state.record.query);
            }
            function accessor(name, get, set) {
                Object.defineProperty(URL.prototype, name, { get: function () { return get(stateOf(this).record, stateOf(this)); }, set: set ? function (value) {
                    // A setter that parses its value (all but username and password) first drops ASCII tabs and newlines, as the URL parser does.
                    value = String(value);
                    if (name !== "username" && name !== "password") value = value.replace(/[\t\n\r]/g, "");
                    set(stateOf(this).record, value, stateOf(this));
                } : undefined, enumerable: true, configurable: true });
            }
            accessor("href", function (r) { return serialize(r); }, function (r, value, state) {
                var parsed = basicParse(value);
                if (parsed === null) throw new TypeError("Invalid URL");
                state.record = parsed;
                updateSearchParams(state);
            });
            accessor("origin", function (r) { return originOf(r); });
            accessor("protocol", function (r) { return r.scheme + ":"; }, function (r, value) {
                var scheme = value.replace(/:.*$/, "").toLowerCase();
                if (!/^[a-z][a-z0-9+\-.]*$/.test(scheme) || isSpecial(scheme) !== isSpecial(r.scheme)) return;
                if (scheme === "file" && (r.username !== "" || r.password !== "" || r.port !== null)) return;
                if (r.scheme === "file" && r.host === "") return;
                r.scheme = scheme;
                if (specialPorts[scheme] === r.port) r.port = null;
            });
            accessor("username", function (r) { return r.username; }, function (r, value) {
                if (r.host === null || r.host === "" || r.scheme === "file") return;
                r.username = percentEncode(value, inUserinfoSet);
            });
            accessor("password", function (r) { return r.password; }, function (r, value) {
                if (r.host === null || r.host === "" || r.scheme === "file") return;
                r.password = percentEncode(value, inUserinfoSet);
            });
            accessor("host", function (r) { return r.host === null ? "" : r.host + (r.port !== null ? ":" + r.port : ""); }, function (r, value) {
                if (r.opaque) return;
                var end = value.search(/[\/?#\\]/);
                if (end >= 0) value = value.slice(0, end);
                var colon = value.lastIndexOf(":"), bracket = value.lastIndexOf("]");
                var hostPart = colon > bracket ? value.slice(0, colon) : value, portPart = colon > bracket ? value.slice(colon + 1) : "";
                if (hostPart === "" && isSpecial(r.scheme)) return;
                var parsed = hostPart === "" ? "" : parseHost(hostPart, isSpecial(r.scheme));
                if (parsed === null) return;
                r.host = parsed;
                var digits = /^[0-9]*/.exec(portPart)[0];
                if (digits !== "" && parseInt(digits, 10) <= 65535) r.port = specialPorts[r.scheme] === parseInt(digits, 10) ? null : parseInt(digits, 10);
            });
            accessor("hostname", function (r) { return r.host === null ? "" : r.host; }, function (r, value) {
                if (r.opaque) return;
                var end = value.search(/[\/?#:\\]/);
                if (end >= 0 && !(value[0] === "[")) value = value.slice(0, end);
                if (value === "" && isSpecial(r.scheme)) return;
                var parsed = value === "" ? "" : parseHost(value, isSpecial(r.scheme));
                if (parsed !== null) r.host = parsed;
            });
            accessor("port", function (r) { return r.port === null ? "" : String(r.port); }, function (r, value) {
                if (r.host === null || r.host === "" || r.scheme === "file") return;
                if (value === "") { r.port = null; return; }
                var digits = /^[0-9]*/.exec(value)[0];
                if (digits === "") return;
                var n = parseInt(digits, 10);
                if (n > 65535) return;
                r.port = specialPorts[r.scheme] === n ? null : n;
            });
            accessor("pathname", function (r) { return r.opaque ? r.path[0] : r.path.map(function (s) { return "/" + s; }).join(""); }, function (r, value) {
                if (r.opaque) return;
                var fresh = parsePathAndAfter(newRecord(r.scheme), (isSpecial(r.scheme) || value[0] === "/" ? "" : "/") + value.replace(/\?/g, "%3F").replace(/#/g, "%23"), isSpecial(r.scheme));
                r.path = fresh.path;
            });
            accessor("search", function (r) { return r.query === null || r.query === "" ? "" : "?" + r.query; }, function (r, value, state) {
                if (value === "") { r.query = null; updateSearchParams(state); return; }
                r.query = percentEncode(value[0] === "?" ? value.slice(1) : value, isSpecial(r.scheme) ? inSpecialQuerySet : inQuerySet);
                updateSearchParams(state);
            });
            accessor("hash", function (r) { return r.fragment === null || r.fragment === "" ? "" : "#" + r.fragment; }, function (r, value) {
                if (value === "") { r.fragment = null; return; }
                r.fragment = percentEncode(value[0] === "#" ? value.slice(1) : value, inFragmentSet);
            });
            readonlyAccessor();
            function readonlyAccessor() {
                Object.defineProperty(URL.prototype, "searchParams", {
                    get: function () {
                        var state = stateOf(this);
                        if (!state.params) {
                            state.params = new URLSearchParams(TOKEN);
                            paramsState.get(state.params).owner = state;
                            updateSearchParams(state);
                        }
                        return state.params;
                    },
                    enumerable: true, configurable: true
                });
            }
            method(URL.prototype, "toString", function toString() { return serialize(stateOf(this).record); });
            method(URL.prototype, "toJSON", function toJSON() { return serialize(stateOf(this).record); });
            method(URL, "canParse", function canParse(url, base) {
                var baseRecord = base === undefined ? null : basicParse(String(base));
                if (base !== undefined && baseRecord === null) return false;
                return basicParse(String(url), baseRecord) !== null;
            });
            method(URL, "parse", function parse(url, base) {
                try { return new URL(url, base); } catch (e) { return null; }
            });
            method(URL, "createObjectURL", function createObjectURL(blob) {
                if (!(blob instanceof Blob)) throw new TypeError("Failed to execute 'createObjectURL' on 'URL': Overload resolution failed.");
                var origin = originOf(basicParse(net.documentUrl()) || newRecord("about"));
                var id = "blob:" + (origin === "null" ? "null" : origin) + "/" + net.utf8Encode("") + Math.random().toString(16).slice(2) + Date.now().toString(16);
                blobUrls[id] = blob;
                return id;
            });
            method(URL, "revokeObjectURL", function revokeObjectURL(url) { delete blobUrls[String(url)]; });
            tag(URL, "URL");

            // ---------------------------------------------------------------- URLSearchParams
            var paramsState = new WeakMap();
            function parseUrlencoded(text) {
                var list = [];
                var parts = text.split("&");
                for (var i = 0; i < parts.length; i++) {
                    if (parts[i] === "") continue;
                    var equals = parts[i].indexOf("=");
                    var name = equals < 0 ? parts[i] : parts[i].slice(0, equals), value = equals < 0 ? "" : parts[i].slice(equals + 1);
                    list.push([net.utf8Decode(percentDecodeToBinary(name.replace(/\+/g, " ")), false), net.utf8Decode(percentDecodeToBinary(value.replace(/\+/g, " ")), false)]);
                }
                return list;
            }
            function urlencodeComponent(text) {
                var bytes = net.utf8Encode(text), out = "";
                for (var i = 0; i < bytes.length; i++) {
                    var c = bytes.charCodeAt(i);
                    if (c === 0x20) out += "+";
                    else if (c === 0x2A || c === 0x2D || c === 0x2E || c === 0x5F || (c >= 0x30 && c <= 0x39) || (c >= 0x41 && c <= 0x5A) || (c >= 0x61 && c <= 0x7A)) out += String.fromCharCode(c);
                    else out += "%" + HEX[c >> 4] + HEX[c & 15];
                }
                return out;
            }
            function serializeParams(list) {
                return list.map(function (p) { return urlencodeComponent(p[0]) + "=" + urlencodeComponent(p[1]); }).join("&");
            }
            function USVString(value) { return String(value).replace(/[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]/g, "�"); }
            function URLSearchParams(init) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'URLSearchParams': Please use the 'new' operator, this DOM object constructor cannot be called as a function.");
                var state = { list: [], owner: null };
                paramsState.set(this, state);
                if (init === TOKEN || init === undefined || init === null && false) return;
                if (init !== null && typeof init === "object" || typeof init === "function") {
                    if (typeof init[Symbol.iterator] === "function") {
                        for (var pair of init) {
                            var items = Array.from(pair);
                            if (items.length !== 2) throw new TypeError("Failed to construct 'URLSearchParams': Each query pair must be an iterable [name, value] tuple");
                            state.list.push([USVString(items[0]), USVString(items[1])]);
                        }
                    } else {
                        Object.keys(init).forEach(function (key) { state.list.push([USVString(key), USVString(init[key])]); });
                    }
                } else {
                    var text = String(init);
                    state.list = parseUrlencoded(text[0] === "?" ? text.slice(1) : text);
                }
            }
            function paramsOf(self) {
                var state = paramsState.get(self);
                if (!state) throw new TypeError("Illegal invocation");
                return state;
            }
            function paramsChanged(state) {
                if (state.owner) {
                    var serialized = serializeParams(state.list);
                    state.owner.record.query = serialized === "" ? null : serialized;
                }
            }
            method(URLSearchParams.prototype, "append", function append(name, value) {
                if (arguments.length < 2) throw new TypeError("Failed to execute 'append' on 'URLSearchParams': 2 arguments required, but only " + arguments.length + " present.");
                var s = paramsOf(this); s.list.push([USVString(name), USVString(value)]); paramsChanged(s);
            });
            method(URLSearchParams.prototype, "delete", function (name, value) {
                if (arguments.length < 1) throw new TypeError("Failed to execute 'delete' on 'URLSearchParams': 1 argument required, but only 0 present.");
                var s = paramsOf(this); name = USVString(name);
                s.list = s.list.filter(function (p) { return !(p[0] === name && (value === undefined || p[1] === USVString(value))); });
                paramsChanged(s);
            });
            method(URLSearchParams.prototype, "get", function get(name) {
                if (arguments.length < 1) throw new TypeError("Failed to execute 'get' on 'URLSearchParams': 1 argument required, but only 0 present.");
                name = USVString(name);
                var found = paramsOf(this).list.filter(function (p) { return p[0] === name; })[0];
                return found ? found[1] : null;
            });
            method(URLSearchParams.prototype, "getAll", function getAll(name) {
                if (arguments.length < 1) throw new TypeError("Failed to execute 'getAll' on 'URLSearchParams': 1 argument required, but only 0 present.");
                name = USVString(name);
                return paramsOf(this).list.filter(function (p) { return p[0] === name; }).map(function (p) { return p[1]; });
            });
            method(URLSearchParams.prototype, "has", function has(name, value) {
                if (arguments.length < 1) throw new TypeError("Failed to execute 'has' on 'URLSearchParams': 1 argument required, but only 0 present.");
                name = USVString(name);
                return paramsOf(this).list.some(function (p) { return p[0] === name && (value === undefined || p[1] === USVString(value)); });
            });
            method(URLSearchParams.prototype, "set", function set(name, value) {
                if (arguments.length < 2) throw new TypeError("Failed to execute 'set' on 'URLSearchParams': 2 arguments required, but only " + arguments.length + " present.");
                var s = paramsOf(this); name = USVString(name); value = USVString(value);
                var found = false, out = [];
                s.list.forEach(function (p) {
                    if (p[0] !== name) out.push(p);
                    else if (!found) { found = true; out.push([name, value]); }
                });
                if (!found) out.push([name, value]);
                s.list = out; paramsChanged(s);
            });
            method(URLSearchParams.prototype, "sort", function sort() {
                var s = paramsOf(this);
                var keyed = s.list.map(function (p, i) { return { p: p, i: i }; });
                keyed.sort(function (a, b) {
                    var x = a.p[0], y = b.p[0], n = Math.min(x.length, y.length);
                    for (var i = 0; i < n; i++) { var d = x.charCodeAt(i) - y.charCodeAt(i); if (d !== 0) return d; }
                    return x.length - y.length || a.i - b.i;
                });
                s.list = keyed.map(function (k) { return k.p; }); paramsChanged(s);
            });
            method(URLSearchParams.prototype, "toString", function toString() { return serializeParams(paramsOf(this).list); });
            method(URLSearchParams.prototype, "forEach", function forEach(callback, thisArg) {
                if (typeof callback !== "function") throw new TypeError("Failed to execute 'forEach' on 'URLSearchParams': parameter 1 is not of type 'Function'.");
                var s = paramsOf(this);
                for (var i = 0; i < s.list.length; i++) callback.call(thisArg, s.list[i][1], s.list[i][0], this);
            });
            function paramsIterator(self, kind) {
                var s = paramsOf(self), index = 0;
                var iterator = {
                    next: function () {
                        if (index >= s.list.length) return { value: undefined, done: true };
                        var p = s.list[index++];
                        return { value: kind === "keys" ? p[0] : kind === "values" ? p[1] : [p[0], p[1]], done: false };
                    }
                };
                iterator[Symbol.iterator] = function () { return this; };
                return iterator;
            }
            method(URLSearchParams.prototype, "keys", function keys() { return paramsIterator(this, "keys"); });
            method(URLSearchParams.prototype, "values", function values() { return paramsIterator(this, "values"); });
            method(URLSearchParams.prototype, "entries", function entries() { return paramsIterator(this, "entries"); });
            Object.defineProperty(URLSearchParams.prototype, Symbol.iterator, { value: URLSearchParams.prototype.entries, writable: true, configurable: true });
            readonly(URLSearchParams.prototype, "size", function () { return paramsOf(this).list.length; });
            tag(URLSearchParams, "URLSearchParams");

            // HTMLHyperlinkElementUtils: the URL parts an a or area element has, over its href and the base the element resolves against.
            var hyperlink = {
                parse: function (element) {
                    var href = element.getAttribute("href");
                    if (href === null) return null;
                    try { return new URL(href, element.baseURI); } catch (e) { return null; }
                },
                get: function (element, name) {
                    var url = this.parse(element);
                    if (name === "href") return url === null ? (element.getAttribute("href") || "") : url.href;
                    if (url === null) return name === "protocol" ? ":" : "";
                    return url[name];
                },
                set: function (element, name, value) {
                    value = String(value);
                    if (name === "href") { element.setAttribute("href", value); return; }
                    var url = this.parse(element);
                    if (url === null || name === "origin") return;
                    url[name] = value;
                    element.setAttribute("href", url.href);
                }
            };
            net.registerHyperlink(hyperlink);

            // ---------------------------------------------------------------- Blob, File, FormData
            var blobState = new WeakMap();
            function Blob(parts, options) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'Blob': Please use the 'new' operator, this DOM object constructor cannot be called as a function.");
                var chunks = [], total = 0;
                if (parts !== undefined) {
                    if (parts === null || typeof parts !== "object" || typeof parts[Symbol.iterator] !== "function") throw new TypeError("Failed to construct 'Blob': The provided value cannot be converted to a sequence.");
                    for (var part of parts) {
                        var bytes = part instanceof Blob ? blobState.get(part).bytes : isBufferSource(part) ? copyBytes(part) : utf8(USVString(part));
                        chunks.push(bytes); total += bytes.length;
                    }
                }
                var all = new Uint8Array(total), at = 0;
                chunks.forEach(function (c) { all.set(c, at); at += c.length; });
                var type = options && options.type !== undefined ? String(options.type) : "";
                if (/[^ -~]/.test(type)) type = "";
                blobState.set(this, { bytes: all, type: type.toLowerCase() });
            }
            function blobOf(self) {
                var state = blobState.get(self);
                if (!state) throw new TypeError("Illegal invocation");
                return state;
            }
            readonly(Blob.prototype, "size", function () { return blobOf(this).bytes.length; });
            readonly(Blob.prototype, "type", function () { return blobOf(this).type; });
            method(Blob.prototype, "slice", function slice(start, end, contentType) {
                var bytes = blobOf(this).bytes, size = bytes.length;
                start = start === undefined ? 0 : Math.trunc(Number(start)) || 0;
                end = end === undefined ? size : Math.trunc(Number(end)) || 0;
                var from = start < 0 ? Math.max(size + start, 0) : Math.min(start, size);
                var to = end < 0 ? Math.max(size + end, 0) : Math.min(end, size);
                var result = new Blob([bytes.slice(from, Math.max(from, to))], { type: contentType === undefined ? "" : contentType });
                return result;
            });
            method(Blob.prototype, "text", function text() { var s = blobOf(this); return Promise.resolve(utf8Decode(s.bytes)); });
            method(Blob.prototype, "arrayBuffer", function arrayBuffer() { return Promise.resolve(toArrayBuffer(blobOf(this).bytes)); });
            method(Blob.prototype, "bytes", function bytes() { return Promise.resolve(new Uint8Array(blobOf(this).bytes)); });
            tag(Blob, "Blob");

            function File(parts, name, options) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'File': Please use the 'new' operator, this DOM object constructor cannot be called as a function.");
                if (arguments.length < 2) throw new TypeError("Failed to construct 'File': 2 arguments required, but only " + arguments.length + " present.");
                var blob = new Blob(parts, options);
                blobState.set(this, blobState.get(blob));
                fileState.set(this, { name: String(name).replace(/\//g, ":"), lastModified: options && options.lastModified !== undefined ? Number(options.lastModified) : Date.now() });
            }
            var fileState = new WeakMap();
            File.prototype = Object.create(Blob.prototype, { constructor: { value: File, writable: true, configurable: true } });
            readonly(File.prototype, "name", function () { return fileState.get(this).name; });
            readonly(File.prototype, "lastModified", function () { return fileState.get(this).lastModified; });
            tag(File, "File");

            var formState = new WeakMap();
            function entryValue(value, filename) {
                if (value instanceof Blob) {
                    if (value instanceof File && filename === undefined) return value;
                    var name = filename !== undefined ? String(filename) : value instanceof File ? fileState.get(value).name : "blob";
                    return new File([blobOf(value).bytes], name, { type: blobOf(value).type });
                }
                return USVString(value);
            }
            function FormData(form) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'FormData': Please use the 'new' operator, this DOM object constructor cannot be called as a function.");
                formState.set(this, []);
            }
            function formOf(self) { var s = formState.get(self); if (!s) throw new TypeError("Illegal invocation"); return s; }
            method(FormData.prototype, "append", function append(name, value, filename) { formOf(this).push([USVString(name), entryValue(value, filename)]); });
            method(FormData.prototype, "delete", function (name) { name = USVString(name); var s = formOf(this); formState.set(this, s.filter(function (e) { return e[0] !== name; })); });
            method(FormData.prototype, "get", function get(name) { name = USVString(name); var e = formOf(this).filter(function (x) { return x[0] === name; })[0]; return e ? e[1] : null; });
            method(FormData.prototype, "getAll", function getAll(name) { name = USVString(name); return formOf(this).filter(function (x) { return x[0] === name; }).map(function (x) { return x[1]; }); });
            method(FormData.prototype, "has", function has(name) { name = USVString(name); return formOf(this).some(function (x) { return x[0] === name; }); });
            method(FormData.prototype, "set", function set(name, value, filename) {
                name = USVString(name);
                var s = formOf(this), done = false, out = [], v = entryValue(value, filename);
                s.forEach(function (e) { if (e[0] !== name) out.push(e); else if (!done) { done = true; out.push([name, v]); } });
                if (!done) out.push([name, v]);
                formState.set(this, out);
            });
            method(FormData.prototype, "forEach", function forEach(callback, thisArg) { formOf(this).slice().forEach(function (e) { callback.call(thisArg, e[1], e[0], this); }, this); });
            method(FormData.prototype, "entries", function entries() { return formOf(this).map(function (e) { return [e[0], e[1]]; })[Symbol.iterator](); });
            method(FormData.prototype, "keys", function keys() { return formOf(this).map(function (e) { return e[0]; })[Symbol.iterator](); });
            method(FormData.prototype, "values", function values() { return formOf(this).map(function (e) { return e[1]; })[Symbol.iterator](); });
            Object.defineProperty(FormData.prototype, Symbol.iterator, { value: FormData.prototype.entries, writable: true, configurable: true });
            tag(FormData, "FormData");

            function encodeMultipart(form) {
                var boundary = "----PeachFormBoundary" + Math.random().toString(16).slice(2), chunks = [];
                function add(text) { chunks.push(utf8(text)); }
                formOf(form).forEach(function (entry) {
                    var name = entry[0].replace(/\r?\n|\r/g, "\r\n").replace(/"/g, "%22").replace(/\r/g, "%0D").replace(/\n/g, "%0A"), value = entry[1];
                    if (typeof value === "string") { add("--" + boundary + "\r\nContent-Disposition: form-data; name=\"" + name + "\"\r\n\r\n" + value.replace(/\r\n|\r|\n/g, "\r\n") + "\r\n"); }
                    else {
                        add("--" + boundary + "\r\nContent-Disposition: form-data; name=\"" + name + "\"; filename=\"" + fileState.get(value).name.replace(/"/g, "%22") + "\"\r\nContent-Type: " + (blobOf(value).type || "application/octet-stream") + "\r\n\r\n");
                        chunks.push(blobOf(value).bytes); add("\r\n");
                    }
                });
                add("--" + boundary + "--\r\n");
                var total = chunks.reduce(function (n, c) { return n + c.length; }, 0), all = new Uint8Array(total), at = 0;
                chunks.forEach(function (c) { all.set(c, at); at += c.length; });
                return { bytes: all, type: "multipart/form-data; boundary=" + boundary };
            }

            // ---------------------------------------------------------------- Headers
            var tokenPattern = /^[!#$%&'*+\-.^_`|~0-9A-Za-z]+$/;
            function normalizeValue(value) { return String(value).replace(/^[\t\n\r ]+|[\t\n\r ]+$/g, ""); }
            function checkHeader(name, value) {
                if (!tokenPattern.test(name)) throw new TypeError("Invalid header name: '" + name + "'");
                if (/[\u0000\r\n]/.test(value)) throw new TypeError("Invalid header value: '" + value + "'");
            }
            var headersState = new WeakMap();
            function Headers(init) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'Headers': Please use the 'new' operator, this DOM object constructor cannot be called as a function.");
                var state = { list: [], guard: "none" };
                headersState.set(this, state);
                fillHeaders(this, init);
            }
            function fillHeaders(headers, init) {
                if (init === undefined || init === null && false) return;
                if (init instanceof Headers) { headersState.get(init).list.forEach(function (h) { appendHeader(headers, h[0], h[1]); }); return; }
                if (init !== null && typeof init === "object") {
                    if (typeof init[Symbol.iterator] === "function") {
                        for (var pair of init) {
                            var items = Array.from(pair);
                            if (items.length !== 2) throw new TypeError("Failed to construct 'Headers': Each header must be a [name, value] pair.");
                            appendHeader(headers, String(items[0]), String(items[1]));
                        }
                    } else Object.keys(init).forEach(function (key) { appendHeader(headers, key, String(init[key])); });
                    return;
                }
                if (init !== null) throw new TypeError("Failed to construct 'Headers': The provided value is not of type '(record<ByteString, ByteString> or sequence<sequence<ByteString>>)'.");
                throw new TypeError("Failed to construct 'Headers': The provided value is not of type '(record<ByteString, ByteString> or sequence<sequence<ByteString>>)'.");
            }
            function headersOf(self) { var s = headersState.get(self); if (!s) throw new TypeError("Illegal invocation"); return s; }
            var forbiddenRequestHeaders = /^(accept-charset|accept-encoding|access-control-request-headers|access-control-request-method|connection|content-length|cookie|cookie2|date|dnt|expect|host|keep-alive|origin|referer|set-cookie|te|trailer|transfer-encoding|upgrade|via)$/i;
            function appendHeader(headers, name, value) {
                name = String(name); value = normalizeValue(value);
                checkHeader(name, value);
                var s = headersOf(headers);
                if (s.guard === "immutable") throw new TypeError("Headers are immutable");
                if (s.guard === "request" && (forbiddenRequestHeaders.test(name) || /^(proxy-|sec-)/i.test(name))) return;
                if (s.guard === "response" && /^set-cookie2?$/i.test(name)) return;
                s.list.push([name.toLowerCase(), value]);
            }
            method(Headers.prototype, "append", function append(name, value) {
                if (arguments.length < 2) throw new TypeError("Failed to execute 'append' on 'Headers': 2 arguments required, but only " + arguments.length + " present.");
                appendHeader(this, name, value);
            });
            method(Headers.prototype, "delete", function (name) {
                if (arguments.length < 1) throw new TypeError("Failed to execute 'delete' on 'Headers': 1 argument required, but only 0 present.");
                name = String(name);
                checkHeader(name, "");
                var s = headersOf(this);
                if (s.guard === "immutable") throw new TypeError("Headers are immutable");
                if (s.guard === "request" && (forbiddenRequestHeaders.test(name) || /^(proxy-|sec-)/i.test(name))) return;
                var lower = name.toLowerCase();
                s.list = s.list.filter(function (h) { return h[0] !== lower; });
            });
            method(Headers.prototype, "get", function get(name) {
                if (arguments.length < 1) throw new TypeError("Failed to execute 'get' on 'Headers': 1 argument required, but only 0 present.");
                name = String(name); checkHeader(name, "");
                var lower = name.toLowerCase();
                var values = headersOf(this).list.filter(function (h) { return h[0] === lower; }).map(function (h) { return h[1]; });
                return values.length ? values.join(", ") : null;
            });
            method(Headers.prototype, "getSetCookie", function getSetCookie() {
                return headersOf(this).list.filter(function (h) { return h[0] === "set-cookie"; }).map(function (h) { return h[1]; });
            });
            method(Headers.prototype, "has", function has(name) {
                if (arguments.length < 1) throw new TypeError("Failed to execute 'has' on 'Headers': 1 argument required, but only 0 present.");
                name = String(name); checkHeader(name, "");
                var lower = name.toLowerCase();
                return headersOf(this).list.some(function (h) { return h[0] === lower; });
            });
            method(Headers.prototype, "set", function set(name, value) {
                if (arguments.length < 2) throw new TypeError("Failed to execute 'set' on 'Headers': 2 arguments required, but only " + arguments.length + " present.");
                name = String(name); value = normalizeValue(value);
                checkHeader(name, value);
                var s = headersOf(this);
                if (s.guard === "immutable") throw new TypeError("Headers are immutable");
                if (s.guard === "request" && (forbiddenRequestHeaders.test(name) || /^(proxy-|sec-)/i.test(name))) return;
                var lower = name.toLowerCase(), done = false, out = [];
                s.list.forEach(function (h) { if (h[0] !== lower) out.push(h); else if (!done) { done = true; out.push([lower, value]); } });
                if (!done) out.push([lower, value]);
                s.list = out;
            });
            function sortedHeaders(self) {
                var s = headersOf(self), names = [], combined = {};
                s.list.forEach(function (h) {
                    if (h[0] === "set-cookie") { names.push(["set-cookie", h[1]]); return; }
                    if (Object.prototype.hasOwnProperty.call(combined, h[0])) combined[h[0]] += ", " + h[1]; else combined[h[0]] = h[1];
                });
                Object.keys(combined).forEach(function (k) { names.push([k, combined[k]]); });
                return names.sort(function (a, b) { return a[0] < b[0] ? -1 : a[0] > b[0] ? 1 : 0; });
            }
            method(Headers.prototype, "forEach", function forEach(callback, thisArg) {
                if (typeof callback !== "function") throw new TypeError("Failed to execute 'forEach' on 'Headers': parameter 1 is not of type 'Function'.");
                sortedHeaders(this).forEach(function (h) { callback.call(thisArg, h[1], h[0], this); }, this);
            });
            method(Headers.prototype, "entries", function entries() { return sortedHeaders(this).map(function (h) { return [h[0], h[1]]; })[Symbol.iterator](); });
            method(Headers.prototype, "keys", function keys() { return sortedHeaders(this).map(function (h) { return h[0]; })[Symbol.iterator](); });
            method(Headers.prototype, "values", function values() { return sortedHeaders(this).map(function (h) { return h[1]; })[Symbol.iterator](); });
            Object.defineProperty(Headers.prototype, Symbol.iterator, { value: Headers.prototype.entries, writable: true, configurable: true });
            tag(Headers, "Headers");
            function makeHeaders(list, guard) {
                var headers = new Headers();
                var s = headersOf(headers);
                list.forEach(function (h) { s.list.push([String(h[0]).toLowerCase(), normalizeValue(h[1])]); });
                s.guard = guard;
                return headers;
            }
            function headerPairs(headers) { return headersOf(headers).list.map(function (h) { return [h[0], h[1]]; }); }

            // ---------------------------------------------------------------- bodies
            // Turns what a body can be into bytes and a default Content-Type.
            function extractBody(body) {
                if (body === null || body === undefined) return null;
                if (typeof body === "string") return { bytes: utf8(USVString(body)), type: "text/plain;charset=UTF-8" };
                if (body instanceof URLSearchParams) return { bytes: utf8(body.toString()), type: "application/x-www-form-urlencoded;charset=UTF-8" };
                if (body instanceof Blob) return { bytes: new Uint8Array(blobOf(body).bytes), type: blobOf(body).type || null };
                if (isBufferSource(body)) return { bytes: copyBytes(body), type: null };
                if (body instanceof FormData) return encodeMultipart(body);
                return { bytes: utf8(USVString(String(body))), type: "text/plain;charset=UTF-8" };
            }
            var bodyState = new WeakMap();
            function bodyOf(self) { var s = bodyState.get(self); if (!s) throw new TypeError("Illegal invocation"); return s; }
            function consume(self) {
                var s = bodyOf(self);
                if (s.used) return Promise.reject(new TypeError("Body has already been consumed."));
                s.used = true;
                return Promise.resolve(s.bytes || new Uint8Array(0));
            }
            function mimeOf(self) { return self.headers.get("content-type") || ""; }
            function installBodyMixin(proto) {
                readonly(proto, "bodyUsed", function () { return bodyOf(this).used; });
                readonly(proto, "body", function () { return bodyOf(this).bytes === null ? null : undefined; });
                method(proto, "text", function text() { return consume(this).then(utf8Decode); });
                method(proto, "json", function json() { return consume(this).then(function (b) { return JSON.parse(utf8Decode(b)); }); });
                method(proto, "arrayBuffer", function arrayBuffer() { return consume(this).then(toArrayBuffer); });
                method(proto, "bytes", function bytes() { return consume(this).then(function (b) { return new Uint8Array(b); }); });
                method(proto, "blob", function blob() {
                    var type = mimeOf(this);
                    return consume(this).then(function (b) { return new Blob([b], { type: type }); });
                });
                method(proto, "formData", function formData() {
                    return consume(this).then(function (b) {
                        var type = mimeOf(this);
                        if (/^application\/x-www-form-urlencoded/i.test(type)) {
                            var form = new FormData();
                            parseUrlencoded(utf8Decode(b)).forEach(function (p) { form.append(p[0], p[1]); });
                            return form;
                        }
                        throw new TypeError("Could not parse content as FormData.");
                    }.bind(this));
                });
            }

            // ---------------------------------------------------------------- Request and Response
            var requestState = new WeakMap();
            var standardMethods = { "DELETE": 1, "GET": 1, "HEAD": 1, "OPTIONS": 1, "POST": 1, "PUT": 1 };
            function normalizeMethod(value) {
                value = String(value);
                if (!tokenPattern.test(value)) throw new TypeError("'" + value + "' is not a valid HTTP method.");
                var upper = value.toUpperCase();
                if (upper === "CONNECT" || upper === "TRACE" || upper === "TRACK") throw new TypeError("'" + value + "' HTTP method is unsupported.");
                return standardMethods[upper] ? upper : value;
            }
            function oneOf(value, name, allowed) {
                if (allowed.indexOf(value) < 0) throw new TypeError("Failed to construct 'Request': The provided value '" + value + "' is not a valid enum value of type " + name + ".");
                return value;
            }
            function Request(input, init) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'Request': Please use the 'new' operator, this DOM object constructor cannot be called as a function.");
                if (arguments.length < 1) throw new TypeError("Failed to construct 'Request': 1 argument required, but only 0 present.");
                init = init === undefined || init === null ? {} : Object(init);
                var state;
                if (input instanceof Request) {
                    var other = requestState.get(input);
                    if (bodyOf(input).used) throw new TypeError("Cannot construct a Request with a Request object that has already been used.");
                    state = { url: other.url, method: other.method, headers: null, mode: other.mode, credentials: other.credentials, cache: other.cache, redirect: other.redirect, referrer: other.referrer, referrerPolicy: other.referrerPolicy, integrity: other.integrity, keepalive: other.keepalive, signal: other.signal };
                    var inherited = { bytes: bodyOf(input).bytes, used: false };
                    var inheritedHeaders = input.headers;
                } else {
                    var parsed;
                    try { parsed = basicParse(String(input), basicParse(net.documentUrl())); } catch (e) { parsed = null; }
                    if (parsed === null) throw new TypeError("Failed to construct 'Request': Failed to parse URL from " + input);
                    if (parsed.username !== "" || parsed.password !== "") throw new TypeError("Failed to construct 'Request': Request cannot be constructed from a URL that includes credentials: " + input);
                    state = { url: serialize(parsed, true), method: "GET", headers: null, mode: "cors", credentials: "same-origin", cache: "default", redirect: "follow", referrer: "about:client", referrerPolicy: "", integrity: "", keepalive: false, signal: null };
                    inherited = { bytes: null, used: false };
                    inheritedHeaders = null;
                }
                if (init.method !== undefined) state.method = normalizeMethod(init.method);
                if (init.mode !== undefined) { state.mode = oneOf(String(init.mode), "RequestMode", ["same-origin", "cors", "no-cors", "navigate", "websocket"]); if (state.mode === "navigate") throw new TypeError("Failed to construct 'Request': Cannot construct a Request with a RequestInit whose mode member is set as 'navigate'."); }
                if (init.credentials !== undefined) state.credentials = oneOf(String(init.credentials), "RequestCredentials", ["omit", "same-origin", "include"]);
                if (init.cache !== undefined) state.cache = oneOf(String(init.cache), "RequestCache", ["default", "no-store", "reload", "no-cache", "force-cache", "only-if-cached"]);
                if (init.redirect !== undefined) state.redirect = oneOf(String(init.redirect), "RequestRedirect", ["follow", "error", "manual"]);
                if (init.referrer !== undefined) state.referrer = String(init.referrer);
                if (init.referrerPolicy !== undefined) state.referrerPolicy = String(init.referrerPolicy);
                if (init.integrity !== undefined) state.integrity = String(init.integrity);
                if (init.keepalive !== undefined) state.keepalive = !!init.keepalive;
                if (init.signal !== undefined && init.signal !== null) {
                    if (!(init.signal instanceof AbortSignal)) throw new TypeError("Failed to construct 'Request': Failed to read the 'signal' property from 'RequestInit': Failed to convert value to 'AbortSignal'.");
                    state.signal = init.signal;
                }
                if (state.mode === "no-cors" && state.method !== "GET" && state.method !== "HEAD" && state.method !== "POST") throw new TypeError("Failed to construct 'Request': '" + state.method + "' is unsupported in no-cors mode.");
                var headers = new Headers();
                headersOf(headers).guard = state.mode === "no-cors" ? "request-no-cors" : "request";
                fillHeaders(headers, init.headers !== undefined ? init.headers : inheritedHeaders === null ? undefined : inheritedHeaders);
                var body = inherited;
                if (init.body !== undefined && init.body !== null) {
                    if (state.method === "GET" || state.method === "HEAD") throw new TypeError("Failed to construct 'Request': Request with GET/HEAD method cannot have body.");
                    var extracted = extractBody(init.body);
                    body = { bytes: extracted.bytes, used: false };
                    if (extracted.type && !headersOf(headers).list.some(function (h) { return h[0] === "content-type"; })) appendHeader(headers, "Content-Type", extracted.type);
                } else if (inherited.bytes !== null && (state.method === "GET" || state.method === "HEAD")) {
                    throw new TypeError("Failed to construct 'Request': Request with GET/HEAD method cannot have body.");
                }
                if (input instanceof Request && init.body === undefined && inherited.bytes !== null) bodyOf(input).used = true;
                state.headers = headers;
                requestState.set(this, state);
                bodyState.set(this, body);
            }
            function requestOf(self) { var s = requestState.get(self); if (!s) throw new TypeError("Illegal invocation"); return s; }
            readonly(Request.prototype, "method", function () { return requestOf(this).method; });
            readonly(Request.prototype, "url", function () { return requestOf(this).url; });
            readonly(Request.prototype, "headers", function () { return requestOf(this).headers; });
            readonly(Request.prototype, "destination", function () { return ""; });
            readonly(Request.prototype, "referrer", function () { return requestOf(this).referrer === "about:client" ? "about:client" : requestOf(this).referrer; });
            readonly(Request.prototype, "referrerPolicy", function () { return requestOf(this).referrerPolicy; });
            readonly(Request.prototype, "mode", function () { return requestOf(this).mode; });
            readonly(Request.prototype, "credentials", function () { return requestOf(this).credentials; });
            readonly(Request.prototype, "cache", function () { return requestOf(this).cache; });
            readonly(Request.prototype, "redirect", function () { return requestOf(this).redirect; });
            readonly(Request.prototype, "integrity", function () { return requestOf(this).integrity; });
            readonly(Request.prototype, "keepalive", function () { return requestOf(this).keepalive; });
            readonly(Request.prototype, "isReloadNavigation", function () { return false; });
            readonly(Request.prototype, "isHistoryNavigation", function () { return false; });
            readonly(Request.prototype, "signal", function () { var s = requestOf(this); return s.signal || (s.signal = new AbortController().signal); });
            readonly(Request.prototype, "duplex", function () { return "half"; });
            method(Request.prototype, "clone", function clone() {
                if (bodyOf(this).used) throw new TypeError("Failed to execute 'clone' on 'Request': Request body is already used");
                return new Request(this);
            });
            installBodyMixin(Request.prototype);
            tag(Request, "Request");

            var responseState = new WeakMap();
            var nullBodyStatus = { 101: 1, 204: 1, 205: 1, 304: 1 };
            function reasonOf(status) {
                return { 200: "OK", 201: "Created", 202: "Accepted", 204: "No Content", 206: "Partial Content", 301: "Moved Permanently", 302: "Found", 303: "See Other", 304: "Not Modified", 307: "Temporary Redirect", 308: "Permanent Redirect", 400: "Bad Request", 401: "Unauthorized", 403: "Forbidden", 404: "Not Found", 405: "Method Not Allowed", 500: "Internal Server Error", 501: "Not Implemented", 502: "Bad Gateway", 503: "Service Unavailable" }[status] || "";
            }
            function Response(body, init) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'Response': Please use the 'new' operator, this DOM object constructor cannot be called as a function.");
                init = init === undefined || init === null ? {} : Object(init);
                var status = init.status === undefined ? 200 : Math.trunc(Number(init.status));
                if (!(status >= 200 && status <= 599)) throw new RangeError("Failed to construct 'Response': The status provided (" + init.status + ") is outside the range [200, 599].");
                var statusText = init.statusText === undefined ? "" : String(init.statusText);
                if (!/^[\t -~\u0080-ÿ]*$/.test(statusText)) throw new TypeError("Failed to construct 'Response': Invalid statusText");
                var headers = new Headers();
                fillHeaders(headers, init.headers);
                headersOf(headers).guard = "response";
                var bytes = null;
                if (body !== undefined && body !== null) {
                    if (nullBodyStatus[status]) throw new TypeError("Failed to construct 'Response': Response with null body status cannot have body");
                    var extracted = extractBody(body);
                    bytes = extracted.bytes;
                    if (extracted.type && !headersOf(headers).list.some(function (h) { return h[0] === "content-type"; })) appendHeader(headers, "Content-Type", extracted.type);
                }
                responseState.set(this, { type: "default", url: "", redirected: false, status: status, statusText: statusText, headers: headers });
                bodyState.set(this, { bytes: bytes, used: false });
            }
            function responseOf(self) { var s = responseState.get(self); if (!s) throw new TypeError("Illegal invocation"); return s; }
            readonly(Response.prototype, "type", function () { return responseOf(this).type; });
            readonly(Response.prototype, "url", function () { return responseOf(this).url; });
            readonly(Response.prototype, "redirected", function () { return responseOf(this).redirected; });
            readonly(Response.prototype, "status", function () { return responseOf(this).status; });
            readonly(Response.prototype, "ok", function () { var s = responseOf(this).status; return s >= 200 && s <= 299; });
            readonly(Response.prototype, "statusText", function () { return responseOf(this).statusText; });
            readonly(Response.prototype, "headers", function () { return responseOf(this).headers; });
            method(Response.prototype, "clone", function clone() {
                var s = responseOf(this);
                if (bodyOf(this).used) throw new TypeError("Failed to execute 'clone' on 'Response': Response body is already used");
                var copy = Object.create(Response.prototype);
                responseState.set(copy, { type: s.type, url: s.url, redirected: s.redirected, status: s.status, statusText: s.statusText, headers: makeHeaders(headerPairs(s.headers), "response") });
                bodyState.set(copy, { bytes: bodyOf(this).bytes === null ? null : new Uint8Array(bodyOf(this).bytes), used: false });
                return copy;
            });
            method(Response, "error", function error() {
                var r = Object.create(Response.prototype);
                responseState.set(r, { type: "error", url: "", redirected: false, status: 0, statusText: "", headers: makeHeaders([], "immutable") });
                bodyState.set(r, { bytes: null, used: false });
                return r;
            });
            method(Response, "redirect", function redirect(url, status) {
                var parsed = basicParse(String(url), basicParse(net.documentUrl()));
                if (parsed === null) throw new TypeError("Failed to execute 'redirect' on 'Response': Invalid URL");
                status = status === undefined ? 302 : Number(status);
                if ([301, 302, 303, 307, 308].indexOf(status) < 0) throw new RangeError("Failed to execute 'redirect' on 'Response': Invalid status code");
                var r = new Response(null, { status: 200 });
                var s = responseOf(r);
                s.status = status; s.headers = makeHeaders([["location", serialize(parsed)]], "immutable");
                return r;
            });
            method(Response, "json", function json(data, init) {
                var text = JSON.stringify(data);
                if (text === undefined) throw new TypeError("Failed to execute 'json' on 'Response': The data is not JSON serializable");
                var response = new Response(text, init);
                // The body made text/plain; JSON is the default unless the init's headers named a type of their own.
                var own = init && init.headers !== undefined && new Headers(init.headers).has("content-type");
                if (!own) {
                    var list = headersOf(response.headers).list;
                    headersOf(response.headers).list = list.filter(function (h) { return h[0] !== "content-type"; }).concat([["content-type", "application/json"]]);
                }
                return response;
            });
            installBodyMixin(Response.prototype);
            tag(Response, "Response");

            // A Response made from the answer to a request.
            function responseFrom(result, requestUrl) {
                var r = Object.create(Response.prototype);
                var headers = makeHeaders(result[2], "immutable");
                responseState.set(r, { type: "basic", url: result[4], redirected: result[5], status: result[0], statusText: result[1], headers: headers });
                bodyState.set(r, { bytes: nullBodyStatus[result[0]] ? null : binaryToBytes(result[3]), used: false });
                return r;
            }

            // blob: addresses the scripts made are answered here, without the network.
            function blobAnswer(url) {
                var blob = blobUrls[url];
                if (!blob) return null;
                var bytes = blobOf(blob).bytes;
                return [200, "OK", [["content-type", blobOf(blob).type], ["content-length", String(bytes.length)]], bytesToBinary(bytes), url, false];
            }

            function abortReason(signal) { return signal.reason !== undefined ? signal.reason : new DOMException("The operation was aborted.", "AbortError"); }

            // The address a Referer is made from: the page's for "about:client", none for "", else the URL the script gave.
            function referrerOf(referrer) {
                if (referrer === "") return null;
                var page = net.documentUrl();
                if (referrer === "about:client") return page === "about:blank" ? null : page;
                try { return new URL(referrer, page).href; } catch (e) { return null; }
            }
            function fetch(input, init) {
                return new Promise(function (resolve, reject) {
                    var request;
                    try { request = new Request(input, init); } catch (e) { reject(e); return; }
                    var state = requestOf(request);
                    var signal = state.signal;
                    if (signal && signal.aborted) { reject(abortReason(signal)); return; }
                    var settled = false;
                    if (signal) signal.addEventListener("abort", function () { if (settled) return; settled = true; reject(abortReason(signal)); });

                    function finish(result) {
                        if (settled) return;
                        settled = true;
                        if (result === null) { reject(new TypeError("Failed to fetch")); return; }
                        if (state.redirect === "error" && result[0] >= 300 && result[0] < 400 && result[0] !== 304) { reject(new TypeError("Failed to fetch: redirect mode is error")); return; }
                        resolve(responseFrom(result, state.url));
                    }
                    var blobResult = state.url.slice(0, 5) === "blob:" ? blobAnswer(state.url) : null;
                    if (blobResult !== null || state.url.slice(0, 5) === "blob:") {
                        Promise.resolve().then(function () { finish(blobResult); });
                        return;
                    }
                    var body = bodyOf(request).bytes;
                    net.send(state.method, state.url, headerPairs(state.headers), body === null ? null : bytesToBinary(body), state.redirect === "follow", finish, referrerOf(state.referrer), state.referrerPolicy);
                });
            }

            // ---------------------------------------------------------------- XMLHttpRequest
            function handlerAttribute(proto, type) {
                var store = new WeakMap();
                Object.defineProperty(proto, "on" + type, {
                    get: function () { var rec = store.get(this); return rec ? rec.fn : null; },
                    set: function (value) {
                        var rec = store.get(this);
                        if (!rec) {
                            var self = this;
                            rec = { fn: null };
                            rec.listener = function (event) { if (typeof rec.fn === "function") rec.fn.call(self, event); };
                            store.set(this, rec);
                            this.addEventListener(type, rec.listener);
                        }
                        rec.fn = typeof value === "function" || (value !== null && typeof value === "object") ? value : null;
                    },
                    enumerable: true, configurable: true
                });
            }
            function XMLHttpRequestEventTarget(token) {
                if (token !== TOKEN) throw new TypeError("Illegal constructor");
                return Reflect.construct(EventTarget, [], new.target);
            }
            XMLHttpRequestEventTarget.prototype = Object.create(EventTarget.prototype, { constructor: { value: XMLHttpRequestEventTarget, writable: true, configurable: true } });
            ["loadstart", "progress", "abort", "error", "load", "timeout", "loadend"].forEach(function (t) { handlerAttribute(XMLHttpRequestEventTarget.prototype, t); });
            tag(XMLHttpRequestEventTarget, "XMLHttpRequestEventTarget");

            function XMLHttpRequestUpload(token) {
                if (token !== TOKEN) throw new TypeError("Illegal constructor");
                return Reflect.construct(EventTarget, [], new.target);
            }
            XMLHttpRequestUpload.prototype = Object.create(XMLHttpRequestEventTarget.prototype, { constructor: { value: XMLHttpRequestUpload, writable: true, configurable: true } });
            tag(XMLHttpRequestUpload, "XMLHttpRequestUpload");

            var xhrState = new WeakMap();
            function XMLHttpRequest() {
                if (new.target === undefined) throw new TypeError("Failed to construct 'XMLHttpRequest': Please use the 'new' operator, this DOM object constructor cannot be called as a function.");
                var self = Reflect.construct(EventTarget, [], new.target);
                xhrState.set(self, {
                    state: 0, method: "GET", url: null, async: true, sendFlag: false, headers: [], body: null, responseType: "", timeout: 0, withCredentials: false,
                    status: 0, statusText: "", responseHeaders: [], responseBytes: null, responseUrl: "", mime: null, errorFlag: false, token: 0, doneFlag: false,
                    upload: Reflect.construct(EventTarget, [], XMLHttpRequestUpload)
                });
                Object.setPrototypeOf(xhrState.get(self).upload, XMLHttpRequestUpload.prototype);
                return self;
            }
            XMLHttpRequest.prototype = Object.create(XMLHttpRequestEventTarget.prototype, { constructor: { value: XMLHttpRequest, writable: true, configurable: true } });
            Object.setPrototypeOf(XMLHttpRequest, XMLHttpRequestEventTarget);
            handlerAttribute(XMLHttpRequest.prototype, "readystatechange");
            [["UNSENT", 0], ["OPENED", 1], ["HEADERS_RECEIVED", 2], ["LOADING", 3], ["DONE", 4]].forEach(function (c) {
                Object.defineProperty(XMLHttpRequest, c[0], { value: c[1], enumerable: true });
                Object.defineProperty(XMLHttpRequest.prototype, c[0], { value: c[1], enumerable: true });
            });
            function xhrOf(self) { var s = xhrState.get(self); if (!s) throw new TypeError("Illegal invocation"); return s; }
            function fire(target, type, loaded, total) {
                var event = type === "readystatechange" ? new Event(type) : new ProgressEvent(type, { lengthComputable: total > 0, loaded: loaded || 0, total: total || 0 });
                target.dispatchEvent(event);
            }
            function setState(xhr, s, value) {
                s.state = value;
                fire(xhr, "readystatechange");
            }
            readonly(XMLHttpRequest.prototype, "readyState", function () { return xhrOf(this).state; });
            readonly(XMLHttpRequest.prototype, "upload", function () { return xhrOf(this).upload; });
            readonly(XMLHttpRequest.prototype, "status", function () { return xhrOf(this).status; });
            readonly(XMLHttpRequest.prototype, "statusText", function () { return xhrOf(this).statusText; });
            readonly(XMLHttpRequest.prototype, "responseURL", function () { return xhrOf(this).responseUrl; });
            readonly(XMLHttpRequest.prototype, "responseXML", function () {
                var s = xhrOf(this);
                if (s.responseType !== "" && s.responseType !== "document") throw new DOMException("The value is only accessible if the object's 'responseType' is '' or 'document' (was '" + s.responseType + "').", "InvalidStateError");
                return null;
            });
            Object.defineProperty(XMLHttpRequest.prototype, "timeout", { get: function () { return xhrOf(this).timeout; }, set: function (v) { xhrOf(this).timeout = Number(v) >>> 0; }, enumerable: true, configurable: true });
            Object.defineProperty(XMLHttpRequest.prototype, "withCredentials", {
                get: function () { return xhrOf(this).withCredentials; },
                set: function (v) { var s = xhrOf(this); if (s.state !== 0 && s.state !== 1 || s.sendFlag) throw new DOMException("The XMLHttpRequest state must be unsent or opened.", "InvalidStateError"); s.withCredentials = !!v; },
                enumerable: true, configurable: true
            });
            Object.defineProperty(XMLHttpRequest.prototype, "responseType", {
                get: function () { return xhrOf(this).responseType; },
                set: function (v) {
                    var s = xhrOf(this);
                    v = String(v);
                    if (["", "arraybuffer", "blob", "document", "json", "text"].indexOf(v) < 0) return;
                    if (s.state === 3 || s.state === 4) throw new DOMException("The response type cannot be set if the object's state is LOADING or DONE.", "InvalidStateError");
                    if (!s.async && s.state !== 0 && false) throw new DOMException("Synchronous requests cannot set a responseType.", "InvalidAccessError");
                    s.responseType = v;
                },
                enumerable: true, configurable: true
            });
            method(XMLHttpRequest.prototype, "open", function open(methodName, url, async, user, password) {
                if (arguments.length < 2) throw new TypeError("Failed to execute 'open' on 'XMLHttpRequest': 2 arguments required, but only " + arguments.length + " present.");
                var s = xhrOf(this);
                methodName = String(methodName);
                if (!tokenPattern.test(methodName)) throw new DOMException("'" + methodName + "' is not a valid HTTP method.", "SyntaxError");
                var upper = methodName.toUpperCase();
                if (upper === "CONNECT" || upper === "TRACE" || upper === "TRACK") throw new DOMException("'" + methodName + "' HTTP method is unsupported.", "SecurityError");
                var parsed = basicParse(String(url), basicParse(net.documentUrl()));
                if (parsed === null) throw new DOMException("Failed to execute 'open' on 'XMLHttpRequest': Invalid URL", "SyntaxError");
                var isAsync = arguments.length < 3 ? true : !!async;
                if (!isAsync && (s.timeout !== 0 || s.withCredentials || s.responseType !== "") && false) throw new DOMException("Synchronous requests must not set timeout, withCredentials or responseType.", "InvalidAccessError");
                if (user !== undefined && user !== null && parsed.host !== null && parsed.host !== "") parsed.username = percentEncode(String(user), inUserinfoSet);
                if (password !== undefined && password !== null && parsed.host !== null && parsed.host !== "") parsed.password = percentEncode(String(password), inUserinfoSet);
                s.token++;
                s.method = standardMethods[upper] ? upper : methodName;
                s.url = parsed; s.async = isAsync; s.sendFlag = false; s.headers = []; s.body = null; s.errorFlag = false;
                s.status = 0; s.statusText = ""; s.responseHeaders = []; s.responseBytes = null; s.responseUrl = ""; s.mime = null; s.doneFlag = false;
                if (s.state !== 1) setState(this, s, 1);
            });
            method(XMLHttpRequest.prototype, "setRequestHeader", function setRequestHeader(name, value) {
                if (arguments.length < 2) throw new TypeError("Failed to execute 'setRequestHeader' on 'XMLHttpRequest': 2 arguments required, but only " + arguments.length + " present.");
                var s = xhrOf(this);
                if (s.state !== 1 || s.sendFlag) throw new DOMException("The object's state must be OPENED.", "InvalidStateError");
                name = String(name); value = normalizeValue(value);
                if (!tokenPattern.test(name) || /[\u0000\r\n]/.test(value)) throw new DOMException("'" + name + "' is not a valid HTTP header field name or value.", "SyntaxError");
                if (forbiddenRequestHeaders.test(name) || /^(proxy-|sec-)/i.test(name)) return;
                var lower = name.toLowerCase(), existing = s.headers.filter(function (h) { return h[0] === lower; })[0];
                if (existing) existing[1] += ", " + value; else s.headers.push([lower, value]);
            });
            method(XMLHttpRequest.prototype, "overrideMimeType", function overrideMimeType(mime) {
                var s = xhrOf(this);
                if (s.state === 3 || s.state === 4) throw new DOMException("MimeType cannot be overridden when the state is LOADING or DONE.", "InvalidStateError");
                s.mime = String(mime);
            });
            method(XMLHttpRequest.prototype, "send", function send(body) {
                var s = xhrOf(this), self = this;
                if (s.state !== 1 || s.sendFlag) throw new DOMException("The object's state must be OPENED.", "InvalidStateError");
                var bytes = null;
                if (body !== undefined && body !== null && s.method !== "GET" && s.method !== "HEAD") {
                    var extracted = extractBody(body);
                    bytes = extracted.bytes;
                    if (extracted.type && !s.headers.some(function (h) { return h[0] === "content-type"; })) s.headers.push(["content-type", extracted.type]);
                }
                s.sendFlag = true; s.errorFlag = false; s.doneFlag = false;
                var url = serialize(s.url, true), token = s.token;
                var payload = bytes === null ? null : bytesToBinary(bytes);
                function complete(result) {
                    if (s.token !== token || !s.sendFlag) return;
                    if (result === null) { fail("error"); return; }
                    s.status = result[0]; s.statusText = result[1]; s.responseHeaders = result[2].map(function (h) { return [String(h[0]).toLowerCase(), h[1]]; });
                    s.responseUrl = serialize(basicParse(result[4]) || s.url, true); s.responseBytes = binaryToBytes(result[3]);
                    var total = s.responseBytes.length;
                    if (s.async) {
                        setState(self, s, 2);
                        if (s.token !== token) return;
                        setState(self, s, 3);
                        if (s.token !== token) return;
                        fire(self, "progress", total, total);
                    } else s.state = 2;
                    if (s.token !== token) return;
                    s.sendFlag = false; s.doneFlag = true;
                    setState(self, s, 4);
                    if (s.token !== token) return;
                    fire(self, "load", total, total);
                    fire(self, "loadend", total, total);
                }
                function fail(kind) {
                    s.sendFlag = false; s.errorFlag = true; s.doneFlag = true;
                    s.status = 0; s.statusText = ""; s.responseHeaders = []; s.responseBytes = null;
                    setState(self, s, 4);
                    if (s.token !== token) return;
                    fire(self, kind); fire(self, "loadend");
                }
                if (!s.async) {
                    var result;
                    if (url.slice(0, 5) === "blob:") result = blobAnswer(url);
                    else result = net.sendSync(s.method, url, s.headers, payload, true, referrerOf("about:client"), "");
                    s.sendFlag = false;
                    if (result === null) {
                        s.errorFlag = true; s.state = 4;
                        throw new DOMException("Failed to execute 'send' on 'XMLHttpRequest': A network error occurred.", "NetworkError");
                    }
                    s.sendFlag = true;
                    complete(result);
                    return;
                }
                fire(self, "loadstart", 0, 0);
                if (s.token !== token) return;
                if (url.slice(0, 5) === "blob:") { var blobResult = blobAnswer(url); Promise.resolve().then(function () { complete(blobResult); }); return; }
                net.send(s.method, url, s.headers, payload, true, complete, referrerOf("about:client"), "");
            });
            method(XMLHttpRequest.prototype, "abort", function abort() {
                var s = xhrOf(this);
                if (s.state === 1 && s.sendFlag || s.state === 2 || s.state === 3) {
                    s.sendFlag = false; s.errorFlag = true; s.token++;
                    s.status = 0; s.statusText = ""; s.responseHeaders = []; s.responseBytes = null;
                    s.state = 4;
                    fire(this, "readystatechange");
                    fire(this, "abort"); fire(this, "loadend");
                }
                if (s.state === 4) s.state = 0;
            });
            method(XMLHttpRequest.prototype, "getResponseHeader", function getResponseHeader(name) {
                if (arguments.length < 1) throw new TypeError("Failed to execute 'getResponseHeader' on 'XMLHttpRequest': 1 argument required, but only 0 present.");
                var s = xhrOf(this);
                if (s.state < 2 || s.errorFlag) return null;
                name = String(name).toLowerCase();
                if (name === "set-cookie" || name === "set-cookie2") return null;
                var values = s.responseHeaders.filter(function (h) { return h[0] === name; }).map(function (h) { return h[1]; });
                return values.length ? values.join(", ") : null;
            });
            method(XMLHttpRequest.prototype, "getAllResponseHeaders", function getAllResponseHeaders() {
                var s = xhrOf(this);
                if (s.state < 2 || s.errorFlag) return "";
                var combined = {}, order = [];
                s.responseHeaders.forEach(function (h) {
                    if (h[0] === "set-cookie" || h[0] === "set-cookie2") return;
                    if (Object.prototype.hasOwnProperty.call(combined, h[0])) combined[h[0]] += ", " + h[1]; else { combined[h[0]] = h[1]; order.push(h[0]); }
                });
                return order.sort().map(function (k) { return k + ": " + combined[k] + "\r\n"; }).join("");
            });
            function responseText(s) {
                var type = (s.mime || (s.responseHeaders.filter(function (h) { return h[0] === "content-type"; })[0] || [0, ""])[1]);
                var charset = /charset\s*=\s*"?([^;"\s]+)/i.exec(type);
                var name = charset ? normalizeEncoding(charset[1]) : null;
                var bytes = s.responseBytes || new Uint8Array(0);
                if (name === null || name === "utf-8") return net.utf8Decode(bytesToBinary(bytes), true);
                return net.decode(bytesToBinary(bytes), name) || net.utf8Decode(bytesToBinary(bytes), true);
            }
            readonly(XMLHttpRequest.prototype, "responseText", function () {
                var s = xhrOf(this);
                if (s.responseType !== "" && s.responseType !== "text") throw new DOMException("The value is only accessible if the object's 'responseType' is '' or 'text' (was '" + s.responseType + "').", "InvalidStateError");
                if (s.state !== 3 && s.state !== 4 || s.errorFlag) return "";
                return responseText(s);
            });
            readonly(XMLHttpRequest.prototype, "response", function () {
                var s = xhrOf(this);
                if (s.responseType === "" || s.responseType === "text") {
                    if (s.state !== 3 && s.state !== 4 || s.errorFlag) return "";
                    return responseText(s);
                }
                if (s.state !== 4 || s.errorFlag || s.responseBytes === null) return null;
                if (s.responseType === "arraybuffer") return toArrayBuffer(s.responseBytes);
                if (s.responseType === "blob") return new Blob([s.responseBytes], { type: ((s.responseHeaders.filter(function (h) { return h[0] === "content-type"; })[0] || [0, ""])[1]) });
                if (s.responseType === "json") { try { return JSON.parse(responseText(s)); } catch (e) { return null; } }
                return null;
            });
            tag(XMLHttpRequest, "XMLHttpRequest");

            // ---------------------------------------------------------------- FileReader (the part that reads a Blob)
            function FileReader() {
                if (new.target === undefined) throw new TypeError("Failed to construct 'FileReader': Please use the 'new' operator, this DOM object constructor cannot be called as a function.");
                var self = Reflect.construct(EventTarget, [], new.target);
                readerState.set(self, { state: 0, result: null, error: null });
                return self;
            }
            var readerState = new WeakMap();
            FileReader.prototype = Object.create(XMLHttpRequestEventTarget.prototype, { constructor: { value: FileReader, writable: true, configurable: true } });
            Object.setPrototypeOf(FileReader, EventTarget);
            [["EMPTY", 0], ["LOADING", 1], ["DONE", 2]].forEach(function (c) {
                Object.defineProperty(FileReader, c[0], { value: c[1], enumerable: true });
                Object.defineProperty(FileReader.prototype, c[0], { value: c[1], enumerable: true });
            });
            readonly(FileReader.prototype, "readyState", function () { return readerState.get(this).state; });
            readonly(FileReader.prototype, "result", function () { return readerState.get(this).result; });
            readonly(FileReader.prototype, "error", function () { return readerState.get(this).error; });
            function readBlob(reader, blob, convert) {
                if (!(blob instanceof Blob)) throw new TypeError("Failed to execute 'read' on 'FileReader': parameter 1 is not of type 'Blob'.");
                var s = readerState.get(reader);
                if (s.state === 1) throw new DOMException("The object is already busy reading Blobs.", "InvalidStateError");
                s.state = 1; s.result = null;
                fire(reader, "loadstart", 0, 0);
                setTimeout(function () {
                    if (s.state !== 1) return;
                    var bytes = blobOf(blob).bytes;
                    s.result = convert(bytes, blobOf(blob).type); s.state = 2;
                    fire(reader, "progress", bytes.length, bytes.length);
                    fire(reader, "load", bytes.length, bytes.length);
                    fire(reader, "loadend", bytes.length, bytes.length);
                }, 0);
            }
            method(FileReader.prototype, "readAsText", function readAsText(blob) { readBlob(this, blob, utf8Decode); });
            method(FileReader.prototype, "readAsArrayBuffer", function readAsArrayBuffer(blob) { readBlob(this, blob, toArrayBuffer); });
            method(FileReader.prototype, "readAsBinaryString", function readAsBinaryString(blob) { readBlob(this, blob, bytesToBinary); });
            method(FileReader.prototype, "readAsDataURL", function readAsDataURL(blob) {
                readBlob(this, blob, function (bytes, type) { return "data:" + (type || "application/octet-stream") + ";base64," + btoa(bytesToBinary(bytes)); });
            });
            method(FileReader.prototype, "abort", function abort() {
                var s = readerState.get(this);
                if (s.state !== 1) return;
                s.state = 2; s.result = null;
                fire(this, "abort"); fire(this, "loadend");
            });
            tag(FileReader, "FileReader");

            [["URL", URL], ["URLSearchParams", URLSearchParams], ["Headers", Headers], ["Request", Request], ["Response", Response], ["fetch", fetch],
                ["XMLHttpRequest", XMLHttpRequest], ["XMLHttpRequestEventTarget", XMLHttpRequestEventTarget], ["XMLHttpRequestUpload", XMLHttpRequestUpload],
                ["Blob", Blob], ["File", File], ["FormData", FormData], ["TextEncoder", TextEncoder], ["TextDecoder", TextDecoder], ["FileReader", FileReader]
            ].forEach(function (pair) { define(pair[0], pair[1]); });
        })
        """;
}
