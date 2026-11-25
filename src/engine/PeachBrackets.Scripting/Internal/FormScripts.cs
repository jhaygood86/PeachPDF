namespace PeachBrackets.Scripting.Internal;

/// <summary>
/// The form control model and the constraint validation API of the HTML standard, in script over the DOM objects: the value and checkedness of
/// <c>input</c>, <c>textarea</c> and <c>select</c> (the dirty flags, the value sanitization of each input type), the form owner and
/// <c>elements</c>, <c>ValidityState</c> with all its flags, <c>willValidate</c>, <c>checkValidity</c>/<c>reportValidity</c> with the
/// <c>invalid</c> event, <c>setCustomValidity</c>, and the number and date conversions (<c>valueAsNumber</c>, <c>stepUp</c>) the range checks need.
/// </summary>
internal static class FormScripts
{
    public const string Source = """
        (function (native, global) {
            "use strict";
            var doc = global.document;
            var records = new WeakMap();
            function rec(el) {
                var r = records.get(el);
                if (!r) { r = { value: "", dirty: false, checked: false, checkedDirty: false, custom: "", userEdited: false }; records.set(el, r); }
                return r;
            }
            function define(proto, name, get, set) {
                Object.defineProperty(proto, name, { get: get, set: set, enumerable: true, configurable: true });
            }
            function method(proto, name, fn) {
                Object.defineProperty(proto, name, { value: fn, writable: true, configurable: true, enumerable: true });
            }
            function ownerOf(el) { return el.ownerDocument; }

            // ---------------------------------------------------------------- input types
            var TYPES = ["hidden", "text", "search", "tel", "url", "email", "password", "date", "month", "week", "time", "datetime-local", "number", "range", "color", "checkbox", "radio", "file", "submit", "image", "reset", "button"];
            function inputType(el) {
                var t = (el.getAttribute("type") || "").toLowerCase();
                return TYPES.indexOf(t) >= 0 ? t : "text";
            }
            function isTextLike(t) { return ["text", "search", "tel", "url", "email", "password"].indexOf(t) >= 0; }
            function isValueMode(t) { return ["hidden", "checkbox", "radio", "file", "submit", "image", "reset", "button"].indexOf(t) < 0; }

            // ---------------------------------------------------------------- numbers and dates (HTML "Common microsyntaxes")
            var FLOAT = /^-?(?:\d+|\d*\.\d+)(?:[eE][-+]?\d+)?$/;
            function parseFloatStrict(s) { return FLOAT.test(s) && isFinite(Number(s)) ? Number(s) : null; }
            function isLeap(y) { return (y % 4 === 0 && y % 100 !== 0) || y % 400 === 0; }
            function daysIn(y, m) { return m === 2 ? (isLeap(y) ? 29 : 28) : [4, 6, 9, 11].indexOf(m) >= 0 ? 30 : 31; }
            function pad(n, w) { var s = String(n); while (s.length < w) s = "0" + s; return s; }
            function parseDate(s) {
                var m = /^(\d{4,})-(\d\d)-(\d\d)$/.exec(s);
                if (!m) return null;
                var y = +m[1], mo = +m[2], d = +m[3];
                if (y < 1 || mo < 1 || mo > 12 || d < 1 || d > daysIn(y, mo)) return null;
                return { y: y, m: mo, d: d };
            }
            function parseTime(s) {
                var m = /^(\d\d):(\d\d)(?::(\d\d)(?:\.(\d{1,3}))?)?$/.exec(s);
                if (!m) return null;
                var h = +m[1], mi = +m[2], se = m[3] === undefined ? 0 : +m[3], ms = m[4] === undefined ? 0 : +(m[4] + "00").slice(0, 3);
                if (h > 23 || mi > 59 || se > 59) return null;
                return { h: h, mi: mi, s: se, ms: ms, ssGiven: m[3] !== undefined, msGiven: m[4] };
            }
            function parseMonth(s) {
                var m = /^(\d{4,})-(\d\d)$/.exec(s);
                if (!m) return null;
                var y = +m[1], mo = +m[2];
                return y < 1 || mo < 1 || mo > 12 ? null : { y: y, m: mo };
            }
            function weeksInYear(y) {
                var jan1 = new Date(Date.UTC(2000, 0, 1)); jan1.setUTCFullYear(y);
                var dow = jan1.getUTCDay();
                return dow === 4 || (dow === 3 && isLeap(y)) ? 53 : 52;
            }
            function parseWeek(s) {
                var m = /^(\d{4,})-W(\d\d)$/.exec(s);
                if (!m) return null;
                var y = +m[1], w = +m[2];
                return y < 1 || w < 1 || w > weeksInYear(y) ? null : { y: y, w: w };
            }
            function parseLocal(s) {
                var m = /^(.+)[T ](.+)$/.exec(s);
                if (!m) return null;
                var d = parseDate(m[1]), t = parseTime(m[2]);
                return d && t ? { d: d, t: t } : null;
            }
            function utc(y, mo, d, h, mi, s, ms) {
                var date = new Date(Date.UTC(2000, 0, 1, h || 0, mi || 0, s || 0, ms || 0));
                date.setUTCFullYear(y, mo - 1, d);
                return date.getTime();
            }
            function weekStart(y, w) {
                var jan4 = new Date(Date.UTC(2000, 0, 4)); jan4.setUTCFullYear(y);
                var dow = (jan4.getUTCDay() + 6) % 7;
                return jan4.getTime() - dow * 86400000 + (w - 1) * 604800000;
            }
            // The number a value of the type stands for, or null.
            function toNumber(type, s) {
                switch (type) {
                    case "number": case "range": return parseFloatStrict(s);
                    case "date": var d = parseDate(s); return d ? utc(d.y, d.m, d.d) : null;
                    case "month": var m = parseMonth(s); return m ? (m.y - 1970) * 12 + (m.m - 1) : null;
                    case "week": var w = parseWeek(s); return w ? weekStart(w.y, w.w) : null;
                    case "time": var t = parseTime(s); return t ? ((t.h * 60 + t.mi) * 60 + t.s) * 1000 + t.ms : null;
                    case "datetime-local": var l = parseLocal(s); return l ? utc(l.d.y, l.d.m, l.d.d, l.t.h, l.t.mi, l.t.s, l.t.ms) : null;
                }
                return null;
            }
            function fromNumber(type, n) {
                if (!isFinite(n)) return "";
                switch (type) {
                    case "number": case "range": return String(n);
                    case "date": { var d = new Date(n); return pad(d.getUTCFullYear(), 4) + "-" + pad(d.getUTCMonth() + 1, 2) + "-" + pad(d.getUTCDate(), 2); }
                    case "month": { var y = Math.floor(n / 12) + 1970, mo = ((n % 12) + 12) % 12 + 1; return pad(y, 4) + "-" + pad(mo, 2); }
                    case "week": { var dt = new Date(n + 3 * 86400000), th = new Date(Date.UTC(2000, 0, 1)); th.setTime(dt.getTime()); var wy = th.getUTCFullYear(); var w = Math.floor((n - weekStart(wy, 1)) / 604800000) + 1; if (w < 1) { wy--; w = Math.floor((n - weekStart(wy, 1)) / 604800000) + 1; } return pad(wy, 4) + "-W" + pad(w, 2); }
                    case "time": return timeString(((n % 86400000) + 86400000) % 86400000);
                    case "datetime-local": { var l = new Date(n); return fromNumber("date", n) + "T" + timeString(((n % 86400000) + 86400000) % 86400000); }
                }
                return "";
            }
            function timeString(ms) {
                var h = Math.floor(ms / 3600000), mi = Math.floor(ms / 60000) % 60, s = Math.floor(ms / 1000) % 60, f = ms % 1000;
                return pad(h, 2) + ":" + pad(mi, 2) + (s || f ? ":" + pad(s, 2) + (f ? "." + pad(f, 3).replace(/0+$/, "") : "") : "");
            }
            function isValidValue(type, s) { return toNumber(type, s) !== null; }

            // The value sanitization algorithm of each type.
            function sanitize(type, v) {
                switch (type) {
                    case "text": case "search": case "tel": case "password": return v.replace(/[\r\n]/g, "");
                    case "url": return v.replace(/[\r\n]/g, "").replace(/^[ \t\n\f\r]+|[ \t\n\f\r]+$/g, "");
                    case "email": return v.replace(/[\r\n]/g, "").replace(/^[ \t\n\f\r]+|[ \t\n\f\r]+$/g, "");
                    case "number": return parseFloatStrict(v) === null ? "" : v;
                    case "range": return rangeSanitize(v);
                    case "date": case "month": case "week": case "time": return isValidValue(type, v) ? v : "";
                    case "datetime-local": { var l = parseLocal(v); return l ? v.replace(" ", "T") : ""; }
                    case "color": return /^#[0-9a-fA-F]{6}$/.test(v) ? v.toLowerCase() : "#000000";
                }
                return v;
            }
            function rangeSanitize(v, el) { return parseFloatStrict(v) === null ? "" : v; }

            // ---------------------------------------------------------------- input.value and checkedness
            var HTMLInput = global.HTMLInputElement, HTMLTextArea = global.HTMLTextAreaElement, HTMLSelect = global.HTMLSelectElement, HTMLForm = global.HTMLFormElement;
            var HTMLButton = global.HTMLButtonElement, HTMLFieldSet = global.HTMLFieldSetElement, HTMLOutput = global.HTMLOutputElement, HTMLOption = global.HTMLOptionElement;
            function defaultRangeValue(el) {
                var min = parseFloatStrict(el.getAttribute("min") || ""), max = parseFloatStrict(el.getAttribute("max") || "");
                if (min === null) min = 0; if (max === null) max = 100;
                return max < min ? String(min) : String(min + (max - min) / 2);
            }
            function inputValue(el) {
                var t = inputType(el), r = rec(el);
                if (isValueMode(t)) {
                    var v = r.dirty ? r.value : sanitize(t, el.getAttribute("value") || "");
                    if (t === "range") v = clampRange(el, v);
                    return v;
                }
                if (t === "checkbox" || t === "radio") return el.hasAttribute("value") ? el.getAttribute("value") : "on";
                if (t === "file") return "";
                return el.getAttribute("value") || "";
            }
            function clampRange(el, v) {
                var n = parseFloatStrict(v);
                if (n === null) return defaultRangeValue(el);
                var min = parseFloatStrict(el.getAttribute("min") || ""), max = parseFloatStrict(el.getAttribute("max") || "");
                if (min === null) min = 0; if (max === null) max = 100;
                if (max < min) max = min;
                if (n < min) n = min; if (n > max) n = max;
                var step = stepInfo(el, "range");
                if (step && step.scaled > 0) {
                    var k = Math.round((n - step.base) / step.scaled);
                    var snapped = step.base + k * step.scaled;
                    if (snapped > max) snapped -= step.scaled;
                    if (snapped < min) snapped += step.scaled;
                    n = snapped;
                }
                return String(Number(n.toPrecision(15)));
            }
            define(HTMLInput.prototype, "value", function () { return inputValue(this); }, function (v) {
                var t = inputType(this), r = rec(this);
                v = v === null ? "" : String(v);
                if (isValueMode(t)) { var before = inputValue(this); r.value = sanitize(t, v); r.dirty = true; r.userEdited = false; if (inputValue(this) !== before) r.sel = { start: r.value.length, end: r.value.length, dir: "none" }; }
                else if (t === "file") { if (v !== "") throw new DOMException("This input element accepts a filename, which may only be programmatically set to the empty string.", "InvalidStateError"); }
                else this.setAttribute("value", v);
            });
            define(HTMLInput.prototype, "checked", function () {
                var r = rec(this);
                return r.checkedDirty ? r.checked : this.hasAttribute("checked");
            }, function (v) {
                var r = rec(this); r.checkedDirty = true; r.checked = !!v;
                if (r.checked && inputType(this) === "radio") uncheckGroup(this);
            });
            function groupOf(el) {
                var name = el.getAttribute("name");
                if (!name) return [];
                var owner = formOwner(el), root = el.getRootNode();
                return Array.prototype.filter.call(root.querySelectorAll("input"), function (o) {
                    return o !== el && inputType(o) === "radio" && o.getAttribute("name") === name && formOwner(o) === owner;
                });
            }
            function uncheckGroup(el) { groupOf(el).forEach(function (o) { var r = rec(o); r.checkedDirty = true; r.checked = false; }); }
            define(HTMLInput.prototype, "defaultChecked", function () { return this.hasAttribute("checked"); }, function (v) { if (v) this.setAttribute("checked", ""); else this.removeAttribute("checked"); });
            define(HTMLInput.prototype, "defaultValue", function () { return this.getAttribute("value") || ""; }, function (v) { this.setAttribute("value", String(v)); });
            define(HTMLInput.prototype, "valueAsNumber", function () {
                var t = inputType(this);
                if (["date", "month", "week", "time", "datetime-local", "number", "range"].indexOf(t) < 0) return NaN;
                var n = toNumber(t, inputValue(this));
                return n === null ? NaN : n;
            }, function (v) {
                var t = inputType(this);
                if (["date", "month", "week", "time", "datetime-local", "number", "range"].indexOf(t) < 0) throw new DOMException("This input element does not support Number values.", "InvalidStateError");
                if (v !== null && v !== undefined && typeof v !== "number" && typeof v !== "string" && typeof v !== "boolean") { v = Number(v); }
                v = Number(v);
                if (!isFinite(v)) { if (isNaN(v) || true) { if (v !== v) { this.value = ""; return; } throw new TypeError("The provided double value is non-finite."); } }
                this.value = fromNumber(t, v);
            });
            define(HTMLInput.prototype, "valueAsDate", function () {
                var t = inputType(this);
                if (["date", "month", "week", "time"].indexOf(t) < 0) return null;
                var n = toNumber(t, inputValue(this));
                if (n === null) return null;
                if (t === "month") { var d = new Date(Date.UTC(1970, 0, 1)); d.setUTCMonth(n); return d; }
                return new Date(n);
            }, function (v) {
                var t = inputType(this);
                if (["date", "month", "week", "time"].indexOf(t) < 0) throw new DOMException("This input element does not support Date values.", "InvalidStateError");
                if (v === null) { this.value = ""; return; }
                if (!(v instanceof Date)) throw new TypeError("The provided value is not a Date.");
                var n = v.getTime();
                if (isNaN(n)) { this.value = ""; return; }
                if (t === "month") n = (v.getUTCFullYear() - 1970) * 12 + v.getUTCMonth();
                else if (t === "date") n = utc(v.getUTCFullYear(), v.getUTCMonth() + 1, v.getUTCDate());
                this.value = fromNumber(t, n);
            });

            // textarea
            function textareaDefault(el) { return (el.textContent || "").replace(/\r\n?/g, "\n"); }
            define(HTMLTextArea.prototype, "value", function () { var r = rec(this); return r.dirty ? r.value : textareaDefault(this); }, function (v) {
                var r = rec(this), before = this.value; r.value = (v === null ? "" : String(v)).replace(/\r\n?/g, "\n"); r.dirty = true; r.userEdited = false;
                if (r.value !== before) r.sel = { start: r.value.length, end: r.value.length, dir: "none" };
            });
            define(HTMLTextArea.prototype, "defaultValue", function () { return textareaDefault(this); }, function (v) { this.textContent = String(v); });
            define(HTMLTextArea.prototype, "textLength", function () { return this.value.length; });

            // select and option
            function optionsOf(select) {
                var list = [];
                (function walk(n) {
                    for (var c = n.firstElementChild; c; c = c.nextElementSibling) {
                        if (c.localName === "option") list.push(c);
                        else if (c.localName === "optgroup" && n === select) walk(c);
                    }
                })(select);
                return list;
            }
            function selectedOptions(select) {
                var options = optionsOf(select), picked = options.filter(function (o) { return rec(o).selectedDirty ? rec(o).selected : o.hasAttribute("selected"); });
                if (!select.multiple && picked.length > 1) picked = [picked[picked.length - 1]];
                if (picked.length === 0 && !select.multiple && selectSize(select) === 1) {
                    var firstEnabled = options.filter(function (o) { return !o.disabled; })[0];
                    if (firstEnabled) picked = [firstEnabled];
                }
                return picked;
            }
            function selectSize(select) { var n = parseInt(select.getAttribute("size"), 10); return n > 0 ? n : (select.hasAttribute("multiple") ? 4 : 1); }
            define(HTMLSelect.prototype, "options", function () { return optionsOf(this); });
            define(HTMLSelect.prototype, "selectedOptions", function () { return selectedOptions(this); });
            define(HTMLSelect.prototype, "selectedIndex", function () { var s = selectedOptions(this)[0]; return s ? optionsOf(this).indexOf(s) : -1; }, function (v) {
                var options = optionsOf(this);
                options.forEach(function (o, i) { var r = rec(o); r.selectedDirty = true; r.selected = i === v; });
            });
            define(HTMLSelect.prototype, "value", function () { var s = selectedOptions(this)[0]; return s ? s.value : ""; }, function (v) {
                var options = optionsOf(this), found = false;
                options.forEach(function (o) { var r = rec(o); r.selectedDirty = true; r.selected = !found && o.value === String(v); if (r.selected) found = true; });
            });
            define(HTMLSelect.prototype, "length", function () { return optionsOf(this).length; });
            define(HTMLOption.prototype, "selected", function () { var r = rec(this); return r.selectedDirty ? r.selected : this.hasAttribute("selected"); }, function (v) {
                var r = rec(this); r.selectedDirty = true; r.selected = !!v;
                var select = this.closest("select");
                if (r.selected && select && !select.multiple) optionsOf(select).forEach(function (o) { if (o !== this) { var q = rec(o); q.selectedDirty = true; q.selected = false; } }, this);
            });
            define(HTMLOption.prototype, "value", function () { return this.hasAttribute("value") ? this.getAttribute("value") : (this.textContent || "").replace(/[ \t\n\f\r]+/g, " ").trim(); }, function (v) { this.setAttribute("value", String(v)); });
            define(HTMLOption.prototype, "text", function () { return (this.textContent || "").replace(/[ \t\n\f\r]+/g, " ").trim(); }, function (v) { this.textContent = String(v); });
            define(HTMLOption.prototype, "index", function () { var s = this.closest("select"); return s ? optionsOf(s).indexOf(this) : 0; });

            // ---------------------------------------------------------------- form owner and form.elements
            function isListed(el) { return ["button", "fieldset", "input", "object", "output", "select", "textarea"].indexOf(el.localName) >= 0; }
            function formOwner(el) {
                if (!isListed(el) || el.nodeType !== 1) return null;
                var id = el.getAttribute("form");
                if (id !== null) {
                    var target = el.getRootNode().getElementById ? el.getRootNode().getElementById(id) : null;
                    return target && target.localName === "form" ? target : null;
                }
                for (var p = el.parentNode; p; p = p.parentNode) if (p.localName === "form") return p;
                return null;
            }
            [HTMLInput, HTMLButton, HTMLSelect, HTMLTextArea, HTMLFieldSet, HTMLOutput, global.HTMLObjectElement].forEach(function (c) {
                if (c) define(c.prototype, "form", function () { return formOwner(this); });
            });
            function formElements(form) {
                return Array.prototype.filter.call(form.getRootNode().querySelectorAll("button,fieldset,input,object,output,select,textarea"), function (e) {
                    return formOwner(e) === form && !(e.localName === "input" && inputType(e) === "image");
                });
            }
            define(HTMLForm.prototype, "elements", function () { return formElements(this); });
            define(HTMLForm.prototype, "length", function () { return formElements(this).length; });

            // ---------------------------------------------------------------- constraint validation
            function isBarred(el) {
                var tag = el.localName;
                if (["input", "button", "select", "textarea"].indexOf(tag) < 0) return true;
                if (el.hasAttribute("disabled")) return true;
                for (var p = el.parentNode; p; p = p.parentNode) {
                    if (p.localName === "datalist") return true;
                    if (p.localName === "fieldset" && p.hasAttribute("disabled")) {
                        var legend = null;
                        for (var c = p.firstElementChild; c; c = c.nextElementSibling) if (c.localName === "legend") { legend = c; break; }
                        if (!legend || !legend.contains(el)) return true;
                    }
                }
                if (tag === "button") return inputButtonType(el) !== "submit";
                if (tag === "input") {
                    var t = inputType(el);
                    if (t === "hidden" || t === "reset" || t === "button") return true;
                    if (el.hasAttribute("readonly") && ["checkbox", "radio", "range", "color", "file", "submit", "image"].indexOf(t) < 0) return true;
                }
                if (tag === "textarea" && el.hasAttribute("readonly")) return true;
                return false;
            }
            function inputButtonType(el) { var t = (el.getAttribute("type") || "").toLowerCase(); return t === "reset" || t === "button" ? t : "submit"; }
            function mutable(el) { return !el.hasAttribute("disabled") && !el.hasAttribute("readonly"); }

            var EMAIL = /^[a-zA-Z0-9.!#$%&'*+\/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$/;
            function stepInfo(el, type) {
                var scale = { number: 1, range: 1, date: 86400000, month: 1, week: 604800000, time: 1000, "datetime-local": 1000 }[type];
                var def = { number: 1, range: 1, date: 1, month: 1, week: 1, time: 60, "datetime-local": 60 }[type];
                var attr = el.getAttribute("step");
                if (attr !== null && attr.toLowerCase() === "any") return null;
                var s = attr === null ? null : parseFloatStrict(attr);
                if (s === null || s <= 0) s = def;
                var min = toNumber(type, el.getAttribute("min") || ""), base = min;
                if (base === null) base = toNumber(type, el.getAttribute("value") || "");
                if (base === null) base = type === "week" ? -259200000 : 0;
                return { base: base, scaled: s * scale };
            }
            function flags(el) {
                var f = { valueMissing: false, typeMismatch: false, patternMismatch: false, tooLong: false, tooShort: false, rangeUnderflow: false, rangeOverflow: false, stepMismatch: false, badInput: false, customError: false };
                var r = rec(el);
                f.customError = r.custom !== "";
                var tag = el.localName;
                if (tag === "input") {
                    var t = inputType(el), value = inputValue(el), required = el.hasAttribute("required");
                    if (required) {
                        if (t === "checkbox") f.valueMissing = !el.checked;
                        else if (t === "radio") f.valueMissing = !el.checked && !groupOf(el).some(function (o) { return o.checked; });
                        else if (t === "file") f.valueMissing = true;
                        else if (isValueMode(t) && mutable(el)) f.valueMissing = value === "";
                    }
                    if (t === "email") {
                        var parts = el.hasAttribute("multiple") ? value.split(",").map(function (s) { return s.replace(/^[ \t\n\f\r]+|[ \t\n\f\r]+$/g, ""); }) : [value];
                        f.typeMismatch = value !== "" && parts.some(function (p) { return !EMAIL.test(p); });
                    } else if (t === "url") {
                        if (value !== "") { try { new global.URL(value); } catch (e) { f.typeMismatch = true; } }
                    }
                    if (isTextLike(t) && value !== "") {
                        var pattern = el.getAttribute("pattern");
                        if (pattern !== null) {
                            var re = null;
                            try { new RegExp(pattern, "v"); re = new RegExp("^(?:" + pattern + ")$", "v"); } catch (e) { re = null; }
                            if (re) {
                                var each = t === "email" && el.hasAttribute("multiple") ? value.split(",").map(function (s) { return s.trim(); }) : [value];
                                f.patternMismatch = each.some(function (v) { return !re.test(v); });
                            }
                        }
                        var max = parseInt(el.getAttribute("maxlength"), 10), min = parseInt(el.getAttribute("minlength"), 10);
                        if (r.userEdited && r.dirty) {
                            if (max >= 0 && codeLength(value) > max) f.tooLong = true;
                            if (min >= 0 && codeLength(value) < min) f.tooShort = true;
                        }
                    }
                    if (["date", "month", "week", "time", "datetime-local", "number", "range"].indexOf(t) >= 0 && value !== "") {
                        var n = toNumber(t, value), lo = toNumber(t, el.getAttribute("min") || ""), hi = toNumber(t, el.getAttribute("max") || "");
                        if (n !== null) {
                            var reversed = (t === "time") && lo !== null && hi !== null && hi < lo;
                            if (reversed) {
                                if (n < lo && n > hi) { f.rangeUnderflow = true; f.rangeOverflow = true; }
                            } else {
                                if (lo !== null && n < lo) f.rangeUnderflow = true;
                                if (hi !== null && n > hi) f.rangeOverflow = true;
                            }
                            var info = stepInfo(el, t);
                            if (info && lo !== null || info) {
                                var q = (n - info.base) / info.scaled;
                                if (Math.abs(q - Math.round(q)) > 1e-9 * Math.max(1, Math.abs(q))) f.stepMismatch = true;
                            }
                        }
                    }
                } else if (tag === "textarea") {
                    var tv = el.value;
                    if (el.hasAttribute("required") && mutable(el) && tv === "") f.valueMissing = true;
                    var tmax = parseInt(el.getAttribute("maxlength"), 10), tmin = parseInt(el.getAttribute("minlength"), 10);
                    if (r.userEdited && r.dirty && tv !== "") {
                        if (tmax >= 0 && codeLength(tv) > tmax) f.tooLong = true;
                        if (tmin >= 0 && codeLength(tv) < tmin) f.tooShort = true;
                    }
                } else if (tag === "select") {
                    if (el.hasAttribute("required")) {
                        var chosen = selectedOptions(el), options = optionsOf(el);
                        var placeholder = !el.multiple && selectSize(el) === 1 && options.length > 0 && options[0].value === "" && (options[0].parentNode === el);
                        f.valueMissing = chosen.length === 0 || chosen.every(function (o) { return o.value === "" && placeholder && options.indexOf(o) === 0; });
                    }
                }
                return f;
            }
            function codeLength(s) { var n = 0; for (var i = 0; i < s.length; i++) { var c = s.charCodeAt(i); if (c >= 0xD800 && c <= 0xDBFF && i + 1 < s.length) i++; n++; } return n; }

            var ValidityToken = {};
            function ValidityState(token) { if (token !== ValidityToken) throw new TypeError("Illegal constructor"); }
            var validityOwner = new WeakMap();
            ["valueMissing", "typeMismatch", "patternMismatch", "tooLong", "tooShort", "rangeUnderflow", "rangeOverflow", "stepMismatch", "badInput", "customError"].forEach(function (name) {
                Object.defineProperty(ValidityState.prototype, name, { get: function () { var el = validityOwner.get(this); if (!el) throw new TypeError("Illegal invocation"); return flags(el)[name]; }, enumerable: true, configurable: true });
            });
            Object.defineProperty(ValidityState.prototype, "valid", { get: function () {
                var el = validityOwner.get(this); if (!el) throw new TypeError("Illegal invocation");
                var f = flags(el);
                return !Object.keys(f).some(function (k) { return f[k]; });
            }, enumerable: true, configurable: true });
            Object.defineProperty(ValidityState.prototype, Symbol.toStringTag, { value: "ValidityState", configurable: true });
            var validities = new WeakMap();
            function validityOf(el) {
                var v = validities.get(el);
                if (!v) { v = new ValidityState(ValidityToken); validityOwner.set(v, el); validities.set(el, v); }
                return v;
            }
            function isInvalid(el) { return !validityOf(el).valid; }
            function messageOf(el) {
                if (isBarred(el)) return "";
                var r = rec(el);
                if (r.custom !== "") return r.custom;
                var f = flags(el);
                var order = ["valueMissing", "typeMismatch", "patternMismatch", "tooLong", "tooShort", "rangeUnderflow", "rangeOverflow", "stepMismatch", "badInput"];
                var text = { valueMissing: "Please fill out this field.", typeMismatch: "Please enter a valid value.", patternMismatch: "Please match the requested format.", tooLong: "Please shorten this text.", tooShort: "Please lengthen this text.", rangeUnderflow: "Value must be greater than or equal to the minimum.", rangeOverflow: "Value must be less than or equal to the maximum.", stepMismatch: "Please enter a valid value.", badInput: "Please enter a valid value." };
                for (var i = 0; i < order.length; i++) if (f[order[i]]) return text[order[i]];
                return "";
            }
            function interactiveValidate(el) {
                if (isBarred(el) || !isInvalid(el)) return true;
                native.fire(el, "invalid", false, true);
                return false;
            }
            [HTMLInput, HTMLButton, HTMLSelect, HTMLTextArea, HTMLFieldSet, HTMLOutput, global.HTMLObjectElement].forEach(function (c) {
                if (!c) return;
                var p = c.prototype;
                define(p, "willValidate", function () { return !isBarred(this); });
                define(p, "validity", function () { return validityOf(this); });
                define(p, "validationMessage", function () { return messageOf(this); });
                method(p, "checkValidity", function checkValidity() { return interactiveValidate(this); });
                method(p, "reportValidity", function reportValidity() { return interactiveValidate(this); });
                method(p, "setCustomValidity", function setCustomValidity(message) { rec(this).custom = String(message); });
            });
            function validateForm(form) {
                var invalid = formElements(form).filter(function (e) { return !isBarred(e) && isInvalid(e); });
                invalid.forEach(function (e) { native.fire(e, "invalid", false, true); });
                return invalid.length === 0;
            }
            method(HTMLForm.prototype, "checkValidity", function checkValidity() { return validateForm(this); });
            method(HTMLForm.prototype, "reportValidity", function reportValidity() { return validateForm(this); });
            define(HTMLForm.prototype, "noValidate", function () { return this.hasAttribute("novalidate"); }, function (v) { if (v) this.setAttribute("novalidate", ""); else this.removeAttribute("novalidate"); });

            // ---------------------------------------------------------------- stepUp and stepDown
            function stepBy(el, count, direction) {
                var t = inputType(el);
                if (["date", "month", "week", "time", "datetime-local", "number", "range"].indexOf(t) < 0) throw new DOMException("This form element is not steppable.", "InvalidStateError");
                var info = stepInfo(el, t);
                if (!info) throw new DOMException("This form element does not have a step.", "InvalidStateError");
                var value = toNumber(t, inputValue(el)); if (value === null) value = 0;
                var min = toNumber(t, el.getAttribute("min") || ""), max = toNumber(t, el.getAttribute("max") || "");
                if (min !== null && max !== null && min > max) return;
                var delta = info.scaled * count * direction, scaleIsMonth = t === "month";
                var next = value + delta;
                var offset = (value - info.base) % info.scaled;
                if (offset !== 0) { next = direction > 0 ? value - offset + info.scaled * (count - (offset < 0 ? 1 : 0) + (offset < 0 ? 1 : 0)) : value - offset - (offset < 0 ? info.scaled : 0) - info.scaled * (count - 1); next = direction > 0 ? Math.ceil((value - info.base) / info.scaled) * info.scaled + info.base + info.scaled * (count - 1) : Math.floor((value - info.base) / info.scaled) * info.scaled + info.base - info.scaled * (count - 1); }
                if (min !== null && next < min) next = min + ((info.base - min) % info.scaled + info.scaled) % info.scaled;
                if (max !== null && next > max) next = max - (((max - info.base) % info.scaled) + info.scaled) % info.scaled;
                if (min !== null && next < min) return;
                if (max !== null && next > max) return;
                if (direction > 0 && value > next && count > 0 && max !== null) return;
                el.value = fromNumber(t, Number(next.toPrecision(15)));
            }
            method(HTMLInput.prototype, "stepUp", function stepUp(n) { stepBy(this, n === undefined ? 1 : Math.trunc(Number(n)), 1); });
            method(HTMLInput.prototype, "stepDown", function stepDown(n) { stepBy(this, n === undefined ? 1 : Math.trunc(Number(n)), -1); });

            // ---------------------------------------------------------------- text selection
            function selectable(el) { return el.localName === "textarea" || (el.localName === "input" && ["text", "search", "tel", "url", "password"].indexOf(inputType(el)) >= 0); }
            function selection(el) { var r = rec(el); if (!r.sel) r.sel = { start: 0, end: 0, dir: "none" }; return r.sel; }
            function requireSelectable(el) { if (!selectable(el)) throw new DOMException("The input element's type does not support selection.", "InvalidStateError"); }
            function setSel(el, start, end, dir) {
                var len = el.value.length;
                start = Math.min(start, len); end = Math.min(end, len);
                if (end < start) end = start;
                var sel = selection(el), changed = sel.start !== start || sel.end !== end || sel.dir !== dir;
                sel.start = start; sel.end = end; sel.dir = dir;
                if (changed) global.setTimeout(function () { native.fire(el, "select", true, false); }, 0);
            }
            function toIndex(v) { v = Number(v); return v !== v ? 0 : v < 0 ? 0 : Math.min(Math.trunc(v), 4294967295); }
            function dirOf(d) { return d === "forward" || d === "backward" ? d : "none"; }
            [HTMLInput, HTMLTextArea].forEach(function (c) {
                var p = c.prototype;
                define(p, "selectionStart", function () { if (!selectable(this)) return null; return Math.min(selection(this).start, this.value.length); }, function (v) {
                    requireSelectable(this); var sel = selection(this); setSel(this, toIndex(v), Math.max(sel.end, toIndex(v)), sel.dir);
                });
                define(p, "selectionEnd", function () { if (!selectable(this)) return null; return Math.min(selection(this).end, this.value.length); }, function (v) {
                    requireSelectable(this); var sel = selection(this); setSel(this, sel.start, toIndex(v), sel.dir);
                });
                define(p, "selectionDirection", function () { return selectable(this) ? selection(this).dir : null; }, function (v) {
                    requireSelectable(this); var sel = selection(this); setSel(this, sel.start, sel.end, dirOf(String(v)));
                });
                method(p, "select", function select() {
                    if (!selectable(this)) return;
                    var el = this, sel = selection(el), end = el.value.length, changed = sel.start !== 0 || sel.end !== end || sel.dir !== "none";
                    sel.start = 0; sel.end = end; sel.dir = "none";
                    if (changed) global.setTimeout(function () { native.fire(el, "select", true, false); }, 0);
                });
                method(p, "setSelectionRange", function setSelectionRange(start, end, direction) {
                    if (arguments.length < 2) throw new TypeError("Failed to execute 'setSelectionRange': 2 arguments required, but only " + arguments.length + " present.");
                    requireSelectable(this);
                    setSel(this, toIndex(start), toIndex(end), dirOf(direction === undefined ? "none" : String(direction)));
                });
                method(p, "setRangeText", function setRangeText(replacement, start, end, mode) {
                    requireSelectable(this);
                    replacement = String(replacement);
                    var sel = selection(this), value = this.value;
                    if (arguments.length < 3) { start = sel.start; end = sel.end; }
                    else { start = toIndex(start); end = toIndex(end); }
                    mode = mode === undefined ? "preserve" : String(mode);
                    if (["select", "start", "end", "preserve"].indexOf(mode) < 0) throw new TypeError("The provided value '" + mode + "' is not a valid enum value of type SelectionMode.");
                    if (start > end) throw new DOMException("The start index is greater than the end index.", "IndexSizeError");
                    start = Math.min(start, value.length); end = Math.min(end, value.length);
                    var oldStart = sel.start, oldEnd = sel.end, oldLength = end - start;
                    var next = value.slice(0, start) + replacement + value.slice(end);
                    var r = rec(this); r.value = this.localName === "textarea" ? next.replace(/\r\n?/g, "\n") : sanitize(inputType(this), next); r.dirty = true;
                    var newEnd = start + replacement.length, delta = replacement.length - oldLength;
                    if (mode === "select") { sel.start = start; sel.end = newEnd; }
                    else if (mode === "start") { sel.start = sel.end = start; }
                    else if (mode === "end") { sel.start = sel.end = newEnd; }
                    else {
                        var ns = oldStart, ne = oldEnd;
                        if (ns > end) ns += delta; else if (ns > start) ns = start;
                        if (ne > end) ne += delta; else if (ne > start) ne = newEnd;
                        sel.start = ns; sel.end = ne;
                    }
                    sel.dir = "none";
                });
            });

            // ---------------------------------------------------------------- changing the type of an input
            function modeOf(t) { return isValueMode(t) ? "value" : (t === "checkbox" || t === "radio") ? "default/on" : t === "file" ? "filename" : "default"; }
            var HTMLElementPrototype = global.HTMLElement.prototype;
            // Runs a change of the type attribute and then the value sanitization and mode switch of the HTML standard.
            function changingType(el, change, setValueAttribute) {
                var oldType = inputType(el), oldValue = inputValue(el);
                var result = change();
                var newType = inputType(el), r = rec(el), oldMode = modeOf(oldType), newMode = modeOf(newType);
                if (oldType !== newType) {
                    if (oldMode === "value" && newMode !== "value") {
                        if (oldValue !== "") setValueAttribute(oldValue);
                        r.dirty = false;
                    } else if (oldMode !== "value" && newMode === "value") {
                        r.dirty = false;
                    } else if (oldMode === "value" && newMode === "value" && r.dirty) {
                        r.value = sanitize(newType, r.value);
                    }
                    if (r.sel) { var len = inputValue(el).length; r.sel.start = Math.min(r.sel.start, len); r.sel.end = Math.min(r.sel.end, len); }
                }
                return result;
            }
            ["setAttribute", "removeAttribute"].forEach(function (name) {
                var original = HTMLElementPrototype[name];
                Object.defineProperty(HTMLElementPrototype, name, { value: function () {
                    var self = this, args = arguments;
                    if (this.localName !== "input" || args.length === 0 || String(args[0]).toLowerCase() !== "type") return original.apply(this, args);
                    return changingType(this, function () { return original.apply(self, args); }, function (v) { original.call(self, "value", v); });
                }, writable: true, configurable: true });
            });
            (function () {
                var descriptor = Object.getOwnPropertyDescriptor(HTMLInput.prototype, "type");
                if (!descriptor || !descriptor.set) return;
                var set = descriptor.set;
                descriptor.set = function (v) {
                    var self = this;
                    changingType(this, function () { set.call(self, v); }, function (x) { self.setAttribute("value", x); });
                };
                Object.defineProperty(HTMLInput.prototype, "type", descriptor);
            })();

            // ---------------------------------------------------------------- ValidityState as a global
            Object.defineProperty(global, "ValidityState", { value: ValidityState, writable: true, configurable: true });
        })
        """;
}
