namespace PeachBrackets.Scripting.Internal;

/// <summary>
/// The parts of CSS Typed OM a canvas takes colors from: <c>CSSUnitValue</c> (the result of <c>CSS.number()</c>, <c>CSS.percent()</c>, <c>CSS.deg()</c>...), <c>CSSRGB</c> and
/// <c>CSSHSL</c> with their settable components. A canvas reads them when a style is set (see <c>ScriptRuntime.TryObjectColor</c>). In script, run at startup.
/// </summary>
internal static class ColorScripts
{
    public const string Source = """
        (function (global) {
            "use strict";
            function define(name, value) { Object.defineProperty(global, name, { value: value, writable: true, configurable: true }); }
            function tag(constructor, name) { Object.defineProperty(constructor.prototype, Symbol.toStringTag, { value: name, configurable: true }); }
            function attribute(proto, name, get, set) { Object.defineProperty(proto, name, { get: get, set: set, enumerable: true, configurable: true }); }

            function CSSStyleValue() { throw new TypeError("Illegal constructor"); }
            tag(CSSStyleValue, "CSSStyleValue");
            function CSSNumericValue() { throw new TypeError("Illegal constructor"); }
            Object.setPrototypeOf(CSSNumericValue, CSSStyleValue);
            CSSNumericValue.prototype = Object.create(CSSStyleValue.prototype, { constructor: { value: CSSNumericValue, writable: true, configurable: true } });
            tag(CSSNumericValue, "CSSNumericValue");

            var UNITS = ["number", "percent", "px", "em", "rem", "deg", "grad", "rad", "turn", "s", "ms"];
            function CSSUnitValue(value, unit) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'CSSUnitValue': Please use the 'new' operator.");
                if (arguments.length < 2) throw new TypeError("Failed to construct 'CSSUnitValue': 2 arguments required.");
                unit = String(unit);
                if (UNITS.indexOf(unit) < 0) throw new TypeError("Failed to construct 'CSSUnitValue': Invalid unit: " + unit);
                this._value = Number(value);
                this._unit = unit;
            }
            Object.setPrototypeOf(CSSUnitValue, CSSNumericValue);
            CSSUnitValue.prototype = Object.create(CSSNumericValue.prototype, { constructor: { value: CSSUnitValue, writable: true, configurable: true } });
            attribute(CSSUnitValue.prototype, "value", function () { return this._value; }, function (v) { this._value = Number(v); });
            attribute(CSSUnitValue.prototype, "unit", function () { return this._unit; });
            Object.defineProperty(CSSUnitValue.prototype, "toString", { value: function toString() { return this._unit === "number" ? String(this._value) : this._value + (this._unit === "percent" ? "%" : this._unit); }, writable: true, configurable: true });
            tag(CSSUnitValue, "CSSUnitValue");

            var css = global.CSS;
            UNITS.forEach(function (unit) {
                Object.defineProperty(css, unit, { value: function (value) { return new CSSUnitValue(value, unit); }, writable: true, configurable: true, enumerable: true });
            });

            function CSSColorValue() { throw new TypeError("Illegal constructor"); }
            Object.setPrototypeOf(CSSColorValue, CSSStyleValue);
            CSSColorValue.prototype = Object.create(CSSStyleValue.prototype, { constructor: { value: CSSColorValue, writable: true, configurable: true } });
            tag(CSSColorValue, "CSSColorValue");

            // The components are CSSNumberish: a number or a CSSUnitValue. They are kept as given and read when a canvas takes the color.
            function numberish(name, v) {
                if (typeof v === "number" || v instanceof CSSNumericValue) return v;
                throw new TypeError("Failed to set the '" + name + "' property: The provided value is not a number or a CSSNumericValue.");
            }
            function component(proto, name, field) {
                attribute(proto, name, function () { return this[field]; }, function (v) { this[field] = numberish(name, v); });
            }

            function CSSRGB(r, g, b, alpha) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'CSSRGB': Please use the 'new' operator.");
                if (arguments.length < 3) throw new TypeError("Failed to construct 'CSSRGB': 3 arguments required.");
                this._r = numberish("r", r); this._g = numberish("g", g); this._b = numberish("b", b);
                this._alpha = alpha === undefined ? CSS.number(1) : numberish("alpha", alpha);
            }
            Object.setPrototypeOf(CSSRGB, CSSColorValue);
            CSSRGB.prototype = Object.create(CSSColorValue.prototype, { constructor: { value: CSSRGB, writable: true, configurable: true } });
            component(CSSRGB.prototype, "r", "_r"); component(CSSRGB.prototype, "g", "_g"); component(CSSRGB.prototype, "b", "_b"); component(CSSRGB.prototype, "alpha", "_alpha");
            tag(CSSRGB, "CSSRGB");

            function CSSHSL(h, s, l, alpha) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'CSSHSL': Please use the 'new' operator.");
                if (arguments.length < 3) throw new TypeError("Failed to construct 'CSSHSL': 3 arguments required.");
                if (!(h instanceof CSSNumericValue)) throw new TypeError("Failed to construct 'CSSHSL': The hue is not a CSSNumericValue.");
                this._h = h; this._s = numberish("s", s); this._l = numberish("l", l);
                this._alpha = alpha === undefined ? CSS.number(1) : numberish("alpha", alpha);
            }
            Object.setPrototypeOf(CSSHSL, CSSColorValue);
            CSSHSL.prototype = Object.create(CSSColorValue.prototype, { constructor: { value: CSSHSL, writable: true, configurable: true } });
            attribute(CSSHSL.prototype, "h", function () { return this._h; }, function (v) {
                if (!(v instanceof CSSNumericValue)) throw new TypeError("Failed to set the 'h' property: The provided value is not a CSSNumericValue.");
                this._h = v;
            });
            component(CSSHSL.prototype, "s", "_s"); component(CSSHSL.prototype, "l", "_l"); component(CSSHSL.prototype, "alpha", "_alpha");
            tag(CSSHSL, "CSSHSL");

            // The other color interfaces, which a canvas takes through the CSS text they stand for (_colorText) so that it reads back as that color serialized.
            function text(v) {
                if (typeof v === "number") return String(v);
                return v._unit === "number" ? String(v._value) : v._value + (v._unit === "percent" ? "%" : v._unit);
            }
            // A number from 0 to 1 is a fraction (hwb's whiteness, hsl's lightness): CSS writes it as a percentage.
            function fraction(v) {
                if (typeof v === "number") return (v * 100) + "%";
                return v._unit === "number" ? (v._value * 100) + "%" : text(v);
            }
            function makeColor(name, props, hueProp, toText) {
                var ctor = function () {
                    if (new.target === undefined) throw new TypeError("Failed to construct '" + name + "': Please use the 'new' operator.");
                    if (arguments.length < props.length) throw new TypeError("Failed to construct '" + name + "': " + props.length + " arguments required.");
                    for (var i = 0; i < props.length; i++) {
                        var v = arguments[i];
                        if (props[i] === hueProp && !(v instanceof CSSNumericValue)) throw new TypeError("Failed to construct '" + name + "': The hue is not a CSSNumericValue.");
                        this["_" + props[i]] = props[i] === hueProp ? v : numberish(props[i], v);
                    }
                    var alpha = arguments[props.length];
                    this._alpha = alpha === undefined ? CSS.number(1) : numberish("alpha", alpha);
                };
                Object.defineProperty(ctor, "name", { value: name, configurable: true });
                Object.setPrototypeOf(ctor, CSSColorValue);
                ctor.prototype = Object.create(CSSColorValue.prototype, { constructor: { value: ctor, writable: true, configurable: true } });
                props.forEach(function (prop) {
                    attribute(ctor.prototype, prop, function () { return this["_" + prop]; }, function (v) {
                        if (prop === hueProp && !(v instanceof CSSNumericValue)) throw new TypeError("Failed to set the '" + prop + "' property: The provided value is not a CSSNumericValue.");
                        this["_" + prop] = prop === hueProp ? v : numberish(prop, v);
                    });
                });
                component(ctor.prototype, "alpha", "_alpha");
                Object.defineProperty(ctor.prototype, "_colorText", { value: function () { return toText(this); }, writable: true, configurable: true });
                tag(ctor, name);
                define(name, ctor);
                return ctor;
            }
            function slash(o) { return " / " + text(o._alpha) + ")"; }
            makeColor("CSSHWB", ["h", "w", "b"], "h", function (o) { return "hwb(" + text(o._h) + " " + fraction(o._w) + " " + fraction(o._b) + slash(o); });
            makeColor("CSSLab", ["l", "a", "b"], null, function (o) { return "lab(" + text(o._l) + " " + text(o._a) + " " + text(o._b) + slash(o); });
            makeColor("CSSOKLab", ["l", "a", "b"], null, function (o) { return "oklab(" + text(o._l) + " " + text(o._a) + " " + text(o._b) + slash(o); });
            makeColor("CSSLCH", ["l", "c", "h"], "h", function (o) { return "lch(" + text(o._l) + " " + text(o._c) + " " + text(o._h) + slash(o); });
            makeColor("CSSOKLCH", ["l", "c", "h"], "h", function (o) { return "oklch(" + text(o._l) + " " + text(o._c) + " " + text(o._h) + slash(o); });

            // CSSColor(colorSpace, channels, alpha): the color() function.
            function CSSColor(colorSpace, channels, alpha) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'CSSColor': Please use the 'new' operator.");
                if (arguments.length < 2) throw new TypeError("Failed to construct 'CSSColor': 2 arguments required.");
                this._colorSpace = String(colorSpace);
                this._channels = Array.prototype.map.call(channels, function (c) { return numberish("channels", c); });
                this._alpha = alpha === undefined ? CSS.number(1) : numberish("alpha", alpha);
            }
            Object.setPrototypeOf(CSSColor, CSSColorValue);
            CSSColor.prototype = Object.create(CSSColorValue.prototype, { constructor: { value: CSSColor, writable: true, configurable: true } });
            attribute(CSSColor.prototype, "colorSpace", function () { return this._colorSpace; }, function (v) { this._colorSpace = String(v); });
            attribute(CSSColor.prototype, "channels", function () { return this._channels; }, function (v) { this._channels = Array.prototype.map.call(v, function (c) { return numberish("channels", c); }); });
            component(CSSColor.prototype, "alpha", "_alpha");
            Object.defineProperty(CSSColor.prototype, "_colorText", { value: function () { return "color(" + this._colorSpace + " " + this._channels.map(text).join(" ") + slash(this); }, writable: true, configurable: true });
            tag(CSSColor, "CSSColor");

            [CSSStyleValue, CSSNumericValue, CSSUnitValue, CSSColorValue, CSSRGB, CSSHSL, CSSColor].forEach(function (c) { define(c.name, c); });
        })(globalThis);
        """;
}
