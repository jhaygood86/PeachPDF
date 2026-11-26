namespace PeachBrackets.Scripting.Internal;

/// <summary>
/// The geometry interfaces: <c>DOMMatrix</c> and <c>DOMMatrixReadOnly</c> (4 by 4 matrices with their 2D shortcut attributes, the transform operations, and
/// CSS transform strings), <c>DOMPoint</c> and <c>DOMPointReadOnly</c>, <c>DOMRect</c> and <c>DOMRectReadOnly</c>. In script, run at startup.
/// </summary>
internal static class GeometryScripts
{
    public const string Source = """
        (function (global) {
            "use strict";
            var TOKEN = {};
            function define(name, value) { Object.defineProperty(global, name, { value: value, writable: true, configurable: true }); }
            function method(proto, name, fn) { Object.defineProperty(proto, name, { value: fn, writable: true, configurable: true, enumerable: true }); }
            function getter(proto, name, get, set) { Object.defineProperty(proto, name, { get: get, set: set, enumerable: true, configurable: true }); }
            function tag(constructor, name) { Object.defineProperty(constructor.prototype, Symbol.toStringTag, { value: name, configurable: true }); }
            function num(v) { return Number(v); }
            function dflt(v, d) { return v === undefined ? d : Number(v); }

            // ---------------------------------------------------------------- DOMPoint
            function DOMPointReadOnly(x, y, z, w) {
                if (!(this instanceof DOMPointReadOnly)) throw new TypeError("Failed to construct 'DOMPointReadOnly': Please use the 'new' operator.");
                this._p = [dflt(x, 0), dflt(y, 0), dflt(z, 0), dflt(w, 1)];
            }
            ["x", "y", "z", "w"].forEach(function (name, i) {
                getter(DOMPointReadOnly.prototype, name, function () { return this._p[i]; });
            });
            method(DOMPointReadOnly.prototype, "matrixTransform", function matrixTransform(matrix) {
                var m = matrix === undefined ? new DOMMatrix() : (matrix instanceof DOMMatrixReadOnly ? matrix : new DOMMatrix(matrix));
                return transformPoint(m, this, DOMPoint);
            });
            method(DOMPointReadOnly.prototype, "toJSON", function toJSON() { return { x: this.x, y: this.y, z: this.z, w: this.w }; });
            method(DOMPointReadOnly, "fromPoint", function fromPoint(p) { p = p || {}; return new DOMPointReadOnly(p.x, p.y, p.z, p.w); });
            tag(DOMPointReadOnly, "DOMPointReadOnly");
            function DOMPoint(x, y, z, w) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'DOMPoint': Please use the 'new' operator.");
                DOMPointReadOnly.call(this, x, y, z, w);
            }
            Object.setPrototypeOf(DOMPoint, DOMPointReadOnly);
            DOMPoint.prototype = Object.create(DOMPointReadOnly.prototype, { constructor: { value: DOMPoint, writable: true, configurable: true } });
            ["x", "y", "z", "w"].forEach(function (name, i) {
                getter(DOMPoint.prototype, name, function () { return this._p[i]; }, function (v) { this._p[i] = num(v); });
            });
            method(DOMPoint, "fromPoint", function fromPoint(p) { p = p || {}; return new DOMPoint(p.x, p.y, p.z, p.w); });
            tag(DOMPoint, "DOMPoint");

            // ---------------------------------------------------------------- DOMRect
            function DOMRectReadOnly(x, y, width, height) {
                if (!(this instanceof DOMRectReadOnly)) throw new TypeError("Failed to construct 'DOMRectReadOnly': Please use the 'new' operator.");
                this._r = [dflt(x, 0), dflt(y, 0), dflt(width, 0), dflt(height, 0)];
            }
            ["x", "y", "width", "height"].forEach(function (name, i) {
                getter(DOMRectReadOnly.prototype, name, function () { return this._r[i]; });
            });
            function edge(r, which) {
                var x = r._r[0], y = r._r[1], w = r._r[2], h = r._r[3];
                return which === "top" ? Math.min(y, y + h) : which === "bottom" ? Math.max(y, y + h) : which === "left" ? Math.min(x, x + w) : Math.max(x, x + w);
            }
            ["top", "right", "bottom", "left"].forEach(function (name) {
                getter(DOMRectReadOnly.prototype, name, function () { return edge(this, name); });
            });
            method(DOMRectReadOnly.prototype, "toJSON", function toJSON() {
                return { x: this.x, y: this.y, width: this.width, height: this.height, top: this.top, right: this.right, bottom: this.bottom, left: this.left };
            });
            method(DOMRectReadOnly, "fromRect", function fromRect(r) { r = r || {}; return new DOMRectReadOnly(r.x, r.y, r.width, r.height); });
            tag(DOMRectReadOnly, "DOMRectReadOnly");
            function DOMRect(x, y, width, height) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'DOMRect': Please use the 'new' operator.");
                DOMRectReadOnly.call(this, x, y, width, height);
            }
            Object.setPrototypeOf(DOMRect, DOMRectReadOnly);
            DOMRect.prototype = Object.create(DOMRectReadOnly.prototype, { constructor: { value: DOMRect, writable: true, configurable: true } });
            ["x", "y", "width", "height"].forEach(function (name, i) {
                getter(DOMRect.prototype, name, function () { return this._r[i]; }, function (v) { this._r[i] = num(v); });
            });
            method(DOMRect, "fromRect", function fromRect(r) { r = r || {}; return new DOMRect(r.x, r.y, r.width, r.height); });
            tag(DOMRect, "DOMRect");

            // ---------------------------------------------------------------- DOMMatrix
            // The 16 values are column-major: m11 m12 m13 m14 m21 ... m44; the 2D matrix is a b c d e f = m11 m12 m21 m22 m41 m42.
            var identity = function () { return [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]; };
            function multiply(a, b) {
                var r = new Array(16);
                for (var col = 0; col < 4; col++)
                    for (var row = 0; row < 4; row++) {
                        var sum = 0;
                        for (var k = 0; k < 4; k++) sum += a[k * 4 + row] * b[col * 4 + k];
                        r[col * 4 + row] = sum;
                    }
                return r;
            }
            function invert(m) {
                var inv = new Array(16);
                inv[0] = m[5] * m[10] * m[15] - m[5] * m[11] * m[14] - m[9] * m[6] * m[15] + m[9] * m[7] * m[14] + m[13] * m[6] * m[11] - m[13] * m[7] * m[10];
                inv[4] = -m[4] * m[10] * m[15] + m[4] * m[11] * m[14] + m[8] * m[6] * m[15] - m[8] * m[7] * m[14] - m[12] * m[6] * m[11] + m[12] * m[7] * m[10];
                inv[8] = m[4] * m[9] * m[15] - m[4] * m[11] * m[13] - m[8] * m[5] * m[15] + m[8] * m[7] * m[13] + m[12] * m[5] * m[11] - m[12] * m[7] * m[9];
                inv[12] = -m[4] * m[9] * m[14] + m[4] * m[10] * m[13] + m[8] * m[5] * m[14] - m[8] * m[6] * m[13] - m[12] * m[5] * m[10] + m[12] * m[6] * m[9];
                inv[1] = -m[1] * m[10] * m[15] + m[1] * m[11] * m[14] + m[9] * m[2] * m[15] - m[9] * m[3] * m[14] - m[13] * m[2] * m[11] + m[13] * m[3] * m[10];
                inv[5] = m[0] * m[10] * m[15] - m[0] * m[11] * m[14] - m[8] * m[2] * m[15] + m[8] * m[3] * m[14] + m[12] * m[2] * m[11] - m[12] * m[3] * m[10];
                inv[9] = -m[0] * m[9] * m[15] + m[0] * m[11] * m[13] + m[8] * m[1] * m[15] - m[8] * m[3] * m[13] - m[12] * m[1] * m[11] + m[12] * m[3] * m[9];
                inv[13] = m[0] * m[9] * m[14] - m[0] * m[10] * m[13] - m[8] * m[1] * m[14] + m[8] * m[2] * m[13] + m[12] * m[1] * m[10] - m[12] * m[2] * m[9];
                inv[2] = m[1] * m[6] * m[15] - m[1] * m[7] * m[14] - m[5] * m[2] * m[15] + m[5] * m[3] * m[14] + m[13] * m[2] * m[7] - m[13] * m[3] * m[6];
                inv[6] = -m[0] * m[6] * m[15] + m[0] * m[7] * m[14] + m[4] * m[2] * m[15] - m[4] * m[3] * m[14] - m[12] * m[2] * m[7] + m[12] * m[3] * m[6];
                inv[10] = m[0] * m[5] * m[15] - m[0] * m[7] * m[13] - m[4] * m[1] * m[15] + m[4] * m[3] * m[13] + m[12] * m[1] * m[7] - m[12] * m[3] * m[5];
                inv[14] = -m[0] * m[5] * m[14] + m[0] * m[6] * m[13] + m[4] * m[1] * m[14] - m[4] * m[2] * m[13] - m[12] * m[1] * m[6] + m[12] * m[2] * m[5];
                inv[3] = -m[1] * m[6] * m[11] + m[1] * m[7] * m[10] + m[5] * m[2] * m[11] - m[5] * m[3] * m[10] - m[9] * m[2] * m[7] + m[9] * m[3] * m[6];
                inv[7] = m[0] * m[6] * m[11] - m[0] * m[7] * m[10] - m[4] * m[2] * m[11] + m[4] * m[3] * m[10] + m[8] * m[2] * m[7] - m[8] * m[3] * m[6];
                inv[11] = -m[0] * m[5] * m[11] + m[0] * m[7] * m[9] + m[4] * m[1] * m[11] - m[4] * m[3] * m[9] - m[8] * m[1] * m[7] + m[8] * m[3] * m[5];
                inv[15] = m[0] * m[5] * m[10] - m[0] * m[6] * m[9] - m[4] * m[1] * m[10] + m[4] * m[2] * m[9] + m[8] * m[1] * m[6] - m[8] * m[2] * m[5];
                var det = m[0] * inv[0] + m[1] * inv[4] + m[2] * inv[8] + m[3] * inv[12];
                if (det === 0 || !isFinite(det)) return null;
                for (var i = 0; i < 16; i++) inv[i] /= det;
                return inv;
            }
            var names = ["m11", "m12", "m13", "m14", "m21", "m22", "m23", "m24", "m31", "m32", "m33", "m34", "m41", "m42", "m43", "m44"];
            var aliases = { a: 0, b: 1, c: 4, d: 5, e: 12, f: 13 };
            var states = new WeakMap();
            function stateOf(self) { var s = states.get(self); if (!s) throw new TypeError("Illegal invocation"); return s; }

            function toRadians(angle) { return angle * Math.PI / 180; }
            function parseAngle(text) {
                var m = /^(-?[0-9.]+(?:e[-+]?[0-9]+)?)(deg|rad|grad|turn)?$/i.exec(text.trim());
                if (!m) throw new DOMException("Failed to parse the transform.", "SyntaxError");
                var v = parseFloat(m[1]), unit = (m[2] || "").toLowerCase();
                if (unit === "" && v !== 0) throw new DOMException("Failed to parse the transform.", "SyntaxError");
                return unit === "rad" ? v : unit === "grad" ? v * Math.PI / 200 : unit === "turn" ? v * 2 * Math.PI : toRadians(v);
            }
            function parseLength(text) {
                var m = /^(-?[0-9.]+(?:e[-+]?[0-9]+)?)(px)?$/i.exec(text.trim());
                if (!m) throw new DOMException("Failed to parse the transform.", "SyntaxError");
                return parseFloat(m[1]);
            }
            function parseNumber(text) {
                if (!/^-?[0-9.]+(?:e[-+]?[0-9]+)?$/i.test(text.trim())) throw new DOMException("Failed to parse the transform.", "SyntaxError");
                return parseFloat(text);
            }
            function fromTranslate(x, y, z) { var m = identity(); m[12] = x; m[13] = y; m[14] = z; return m; }
            function fromScale(x, y, z) { var m = identity(); m[0] = x; m[5] = y; m[10] = z; return m; }
            function fromAxisAngle(x, y, z, angle) {
                var len = Math.sqrt(x * x + y * y + z * z);
                if (len === 0) return identity();
                x /= len; y /= len; z /= len;
                var c = Math.cos(angle), s = Math.sin(angle), t = 1 - c, m = identity();
                m[0] = t * x * x + c; m[1] = t * x * y + s * z; m[2] = t * x * z - s * y;
                m[4] = t * x * y - s * z; m[5] = t * y * y + c; m[6] = t * y * z + s * x;
                m[8] = t * x * z + s * y; m[9] = t * y * z - s * x; m[10] = t * z * z + c;
                return m;
            }
            // A CSS transform list as a matrix; null is "none"/empty.
            function parseTransformList(text) {
                text = text.trim();
                var result = { m: identity(), is2D: true };
                if (text === "" || text === "none") return result;
                var re = /\s*([a-zA-Z0-9]+)\(([^)]*)\)\s*/y, pos = 0, m;
                while (pos < text.length) {
                    re.lastIndex = pos;
                    m = re.exec(text);
                    if (!m) throw new DOMException("Failed to parse the transform.", "SyntaxError");
                    pos = re.lastIndex;
                    var fn = m[1].toLowerCase(), args = m[2].split(",").map(function (s) { return s.trim(); }), step = null, d3 = false;
                    function n(i, d) { return args[i] === undefined ? d : parseNumber(args[i]); }
                    function l(i, d) { return args[i] === undefined ? d : parseLength(args[i]); }
                    switch (fn) {
                        case "matrix": if (args.length !== 6) throw new DOMException("Failed to parse the transform.", "SyntaxError"); step = identity(); step[0] = n(0); step[1] = n(1); step[4] = n(2); step[5] = n(3); step[12] = n(4); step[13] = n(5); break;
                        case "matrix3d": if (args.length !== 16) throw new DOMException("Failed to parse the transform.", "SyntaxError"); step = args.map(function (s) { return parseNumber(s); }); d3 = true; break;
                        case "translate": step = fromTranslate(l(0), l(1, 0), 0); break;
                        case "translatex": step = fromTranslate(l(0), 0, 0); break;
                        case "translatey": step = fromTranslate(0, l(0), 0); break;
                        case "translatez": step = fromTranslate(0, 0, l(0)); d3 = true; break;
                        case "translate3d": step = fromTranslate(l(0), l(1), l(2)); d3 = true; break;
                        case "scale": step = fromScale(n(0), args.length > 1 ? n(1) : n(0), 1); break;
                        case "scalex": step = fromScale(n(0), 1, 1); break;
                        case "scaley": step = fromScale(1, n(0), 1); break;
                        case "scalez": step = fromScale(1, 1, n(0)); d3 = true; break;
                        case "scale3d": step = fromScale(n(0), n(1), n(2)); d3 = true; break;
                        case "rotate": case "rotatez": step = fromAxisAngle(0, 0, 1, parseAngle(args[0])); d3 = fn === "rotatez"; break;
                        case "rotatex": step = fromAxisAngle(1, 0, 0, parseAngle(args[0])); d3 = true; break;
                        case "rotatey": step = fromAxisAngle(0, 1, 0, parseAngle(args[0])); d3 = true; break;
                        case "rotate3d": step = fromAxisAngle(n(0), n(1), n(2), parseAngle(args[3])); d3 = true; break;
                        case "skew": step = identity(); step[4] = Math.tan(parseAngle(args[0])); step[1] = args.length > 1 ? Math.tan(parseAngle(args[1])) : 0; break;
                        case "skewx": step = identity(); step[4] = Math.tan(parseAngle(args[0])); break;
                        case "skewy": step = identity(); step[1] = Math.tan(parseAngle(args[0])); break;
                        case "perspective": step = identity(); step[11] = -1 / l(0); d3 = true; break;
                        default: throw new DOMException("Failed to parse the transform.", "SyntaxError");
                    }
                    result.m = multiply(result.m, step);
                    if (d3) result.is2D = false;
                }
                return result;
            }

            function setFromInit(state, init) {
                if (init === undefined) { state.m = identity(); state.is2D = true; return; }
                if (typeof init === "string") { var parsed = parseTransformList(init); state.m = parsed.m; state.is2D = parsed.is2D; return; }
                if (init === null || typeof init !== "object" || typeof init[Symbol.iterator] !== "function") throw new TypeError("The provided value is not of type '(DOMString or sequence<unrestricted double>)'.");
                var values = Array.from(init, Number);
                if (values.length === 6) { var m = identity(); m[0] = values[0]; m[1] = values[1]; m[4] = values[2]; m[5] = values[3]; m[12] = values[4]; m[13] = values[5]; state.m = m; state.is2D = true; }
                else if (values.length === 16) { state.m = values; state.is2D = false; }
                else throw new TypeError("The sequence must contain 6 or 16 elements.");
            }
            function DOMMatrixReadOnly(init) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'DOMMatrixReadOnly': Please use the 'new' operator.");
                var state = { m: identity(), is2D: true };
                if (typeof init === "string" && !(typeof global.document !== "undefined")) throw new TypeError("Illegal constructor");
                setFromInit(state, init);
                states.set(this, state);
            }
            function fresh(Type, m, is2D) {
                var r = Object.create(Type.prototype);
                states.set(r, { m: m, is2D: is2D });
                return r;
            }
            names.forEach(function (name, i) {
                getter(DOMMatrixReadOnly.prototype, name, function () { return stateOf(this).m[i]; });
            });
            Object.keys(aliases).forEach(function (name) {
                getter(DOMMatrixReadOnly.prototype, name, function () { return stateOf(this).m[aliases[name]]; });
            });
            getter(DOMMatrixReadOnly.prototype, "is2D", function () { return stateOf(this).is2D; });
            getter(DOMMatrixReadOnly.prototype, "isIdentity", function () { return stateOf(this).m.every(function (v, i) { return v === identity()[i]; }); });
            function nonTwoD(i) { return [2, 3, 6, 7, 8, 9, 11, 14].indexOf(i) >= 0 || i === 10 || i === 15; }

            function copy(self) { var s = stateOf(self); return { m: s.m.slice(), is2D: s.is2D }; }
            function applied(Type, self, step, is2DStep, preMultiply) {
                var s = stateOf(self);
                return fresh(Type, preMultiply ? multiply(step, s.m) : multiply(s.m, step), s.is2D && is2DStep);
            }
            function definitions(Type, mutate) {
                function result(self, m, is2D, preMultiply) {
                    var s = stateOf(self);
                    var next = preMultiply ? multiply(m, s.m) : multiply(s.m, m);
                    if (mutate) { s.m = next; s.is2D = s.is2D && is2D; return self; }
                    return fresh(Type, next, s.is2D && is2D);
                }
                var suffix = mutate ? "Self" : "";
                var proto = mutate ? DOMMatrix.prototype : DOMMatrixReadOnly.prototype;
                method(proto, "translate" + suffix, function (tx, ty, tz) { tz = dflt(tz, 0); return result(this, fromTranslate(dflt(tx, 0), dflt(ty, 0), tz), tz === 0); });
                method(proto, "scale" + suffix, function (sx, sy, sz, ox, oy, oz) {
                    sx = dflt(sx, 1); sy = dflt(sy, sx); sz = dflt(sz, 1); ox = dflt(ox, 0); oy = dflt(oy, 0); oz = dflt(oz, 0);
                    var m = multiply(multiply(fromTranslate(ox, oy, oz), fromScale(sx, sy, sz)), fromTranslate(-ox, -oy, -oz));
                    return result(this, m, sz === 1 && oz === 0);
                });
                method(proto, "scale3d" + suffix, function (scale, ox, oy, oz) {
                    scale = dflt(scale, 1); ox = dflt(ox, 0); oy = dflt(oy, 0); oz = dflt(oz, 0);
                    var m = multiply(multiply(fromTranslate(ox, oy, oz), fromScale(scale, scale, scale)), fromTranslate(-ox, -oy, -oz));
                    return result(this, m, scale === 1);
                });
                method(proto, "rotate" + suffix, function (rotX, rotY, rotZ) {
                    if (rotY === undefined && rotZ === undefined) { rotZ = rotX; rotX = 0; rotY = 0; }
                    rotX = dflt(rotX, 0); rotY = dflt(rotY, 0); rotZ = dflt(rotZ, 0);
                    var m = identity();
                    if (rotZ !== 0) m = multiply(m, fromAxisAngle(0, 0, 1, toRadians(rotZ)));
                    if (rotY !== 0) m = multiply(m, fromAxisAngle(0, 1, 0, toRadians(rotY)));
                    if (rotX !== 0) m = multiply(m, fromAxisAngle(1, 0, 0, toRadians(rotX)));
                    return result(this, m, rotX === 0 && rotY === 0);
                });
                method(proto, "rotateFromVector" + suffix, function (x, y) {
                    x = dflt(x, 0); y = dflt(y, 0);
                    var angle = x === 0 && y === 0 ? 0 : Math.atan2(y, x);
                    return result(this, fromAxisAngle(0, 0, 1, angle), true);
                });
                method(proto, "rotateAxisAngle" + suffix, function (x, y, z, angle) {
                    x = dflt(x, 0); y = dflt(y, 0); z = dflt(z, 0); angle = dflt(angle, 0);
                    return result(this, fromAxisAngle(x, y, z, toRadians(angle)), x === 0 && y === 0);
                });
                method(proto, "skewX" + suffix, function (sx) { var m = identity(); m[4] = Math.tan(toRadians(dflt(sx, 0))); return result(this, m, true); });
                method(proto, "skewY" + suffix, function (sy) { var m = identity(); m[1] = Math.tan(toRadians(dflt(sy, 0))); return result(this, m, true); });
                if (mutate) {
                    method(proto, "multiplySelf", function (other) { var o = otherMatrix(other); var s = stateOf(this); s.m = multiply(s.m, o.m); s.is2D = s.is2D && o.is2D; return this; });
                    method(proto, "preMultiplySelf", function (other) { var o = otherMatrix(other); var s = stateOf(this); s.m = multiply(o.m, s.m); s.is2D = s.is2D && o.is2D; return this; });
                    method(proto, "invertSelf", function () {
                        var s = stateOf(this), inv = invert(s.m);
                        if (inv === null) { s.m = s.m.map(function () { return NaN; }); s.is2D = false; } else s.m = inv;
                        return this;
                    });
                    method(proto, "setMatrixValue", function (text) {
                        if (typeof text !== "string" && arguments.length === 0) throw new TypeError("1 argument required, but only 0 present.");
                        var parsed = parseTransformList(String(text)), s = stateOf(this);
                        s.m = parsed.m; s.is2D = parsed.is2D;
                        return this;
                    });
                } else {
                    method(proto, "multiply", function (other) { var o = otherMatrix(other), s = stateOf(this); return fresh(DOMMatrix, multiply(s.m, o.m), s.is2D && o.is2D); });
                    method(proto, "flipX", function () { var s = stateOf(this), m = identity(); m[0] = -1; return fresh(DOMMatrix, multiply(s.m, m), s.is2D); });
                    method(proto, "flipY", function () { var s = stateOf(this), m = identity(); m[5] = -1; return fresh(DOMMatrix, multiply(s.m, m), s.is2D); });
                    method(proto, "inverse", function () {
                        var s = stateOf(this), inv = invert(s.m);
                        return inv === null ? fresh(DOMMatrix, s.m.map(function () { return NaN; }), false) : fresh(DOMMatrix, inv, s.is2D);
                    });
                    method(proto, "transformPoint", function (point) { return transformPoint(this, point, DOMPoint); });
                    method(proto, "toFloat32Array", function () { return new Float32Array(stateOf(this).m); });
                    method(proto, "toFloat64Array", function () { return new Float64Array(stateOf(this).m); });
                    method(proto, "toJSON", function () {
                        var out = {}, s = stateOf(this);
                        Object.keys(aliases).forEach(function (k) { out[k] = s.m[aliases[k]]; });
                        names.forEach(function (k, i) { out[k] = s.m[i]; });
                        out.is2D = s.is2D; out.isIdentity = this.isIdentity;
                        return out;
                    });
                    method(proto, "toString", function () {
                        var s = stateOf(this);
                        if (!s.m.every(isFinite)) throw new DOMException("The matrix cannot be serialized because it has non-finite values.", "InvalidStateError");
                        var m = s.m;
                        return s.is2D ? "matrix(" + [m[0], m[1], m[4], m[5], m[12], m[13]].join(", ") + ")" : "matrix3d(" + m.join(", ") + ")";
                    });
                }
            }
            function otherMatrix(other) {
                if (other instanceof DOMMatrixReadOnly) return stateOf(other);
                var s = { m: identity(), is2D: true };
                var init = other === undefined ? {} : Object(other);
                function pick(name, alias, d) { return init[name] !== undefined ? Number(init[name]) : alias && init[alias] !== undefined ? Number(init[alias]) : d; }
                var m = identity(), flat = false;
                names.forEach(function (name, i) {
                    var a = Object.keys(aliases).filter(function (k) { return aliases[k] === i; })[0];
                    m[i] = pick(name, a, identity()[i]);
                    if (init[name] !== undefined && nonTwoD(i) && m[i] !== identity()[i]) flat = true;
                });
                s.m = m; s.is2D = init.is2D !== undefined ? !!init.is2D : !flat;
                return s;
            }
            function transformPoint(matrix, point, Type) {
                point = point || {};
                var x = dflt(point.x, 0), y = dflt(point.y, 0), z = dflt(point.z, 0), w = dflt(point.w, 1), m = stateOf(matrix).m;
                return new Type(
                    m[0] * x + m[4] * y + m[8] * z + m[12] * w, m[1] * x + m[5] * y + m[9] * z + m[13] * w,
                    m[2] * x + m[6] * y + m[10] * z + m[14] * w, m[3] * x + m[7] * y + m[11] * z + m[15] * w);
            }
            function fromMatrix(Type, other) {
                var o = otherMatrix(other);
                if (o.is2D === false) { /* keeps all 16 */ }
                return fresh(Type, o.m.slice(), o.is2D);
            }
            method(DOMMatrixReadOnly, "fromMatrix", function fromMatrixRO(other) { return fromMatrix(DOMMatrixReadOnly, other); });
            method(DOMMatrixReadOnly, "fromFloat32Array", function (array) { return fresh(DOMMatrixReadOnly, Array.from(array), array.length === 6); });
            method(DOMMatrixReadOnly, "fromFloat64Array", function (array) { return fresh(DOMMatrixReadOnly, Array.from(array), array.length === 6); });
            tag(DOMMatrixReadOnly, "DOMMatrixReadOnly");

            function DOMMatrix(init) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'DOMMatrix': Please use the 'new' operator.");
                var state = { m: identity(), is2D: true };
                setFromInit(state, init);
                states.set(this, state);
            }
            Object.setPrototypeOf(DOMMatrix, DOMMatrixReadOnly);
            DOMMatrix.prototype = Object.create(DOMMatrixReadOnly.prototype, { constructor: { value: DOMMatrix, writable: true, configurable: true } });
            // The attributes of the mutable matrix: setting one outside the 2D set makes the matrix 3D.
            names.forEach(function (name, i) {
                getter(DOMMatrix.prototype, name, function () { return stateOf(this).m[i]; }, function (v) {
                    var s = stateOf(this); s.m[i] = num(v);
                    if (nonTwoD(i) && s.m[i] !== identity()[i]) s.is2D = false;
                });
            });
            Object.keys(aliases).forEach(function (name) {
                getter(DOMMatrix.prototype, name, function () { return stateOf(this).m[aliases[name]]; }, function (v) { stateOf(this).m[aliases[name]] = num(v); });
            });
            definitions(DOMMatrix, false);
            definitions(DOMMatrix, true);
            method(DOMMatrix, "fromMatrix", function fromMatrixM(other) { return fromMatrix(DOMMatrix, other); });
            method(DOMMatrix, "fromFloat32Array", function (array) { return fresh(DOMMatrix, Array.from(array), array.length === 6); });
            method(DOMMatrix, "fromFloat64Array", function (array) { return fresh(DOMMatrix, Array.from(array), array.length === 6); });
            tag(DOMMatrix, "DOMMatrix");

            ["DOMPointReadOnly", "DOMPoint", "DOMRectReadOnly", "DOMRect", "DOMMatrixReadOnly", "DOMMatrix"].forEach(function (name) {
                define(name, { DOMPointReadOnly: DOMPointReadOnly, DOMPoint: DOMPoint, DOMRectReadOnly: DOMRectReadOnly, DOMRect: DOMRect, DOMMatrixReadOnly: DOMMatrixReadOnly, DOMMatrix: DOMMatrix }[name]);
            });
        })(globalThis);
        """;
}
