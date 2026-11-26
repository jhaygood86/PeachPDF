namespace PeachBrackets.Scripting.Internal;

/// <summary>
/// The Popover API of the HTML standard, in script over the DOM objects: the <c>popover</c> attribute, <c>showPopover</c>, <c>hidePopover</c> and
/// <c>togglePopover</c> with their validity checks and the <c>beforetoggle</c>/<c>toggle</c> events, the stack of open <c>auto</c> and <c>hint</c>
/// popovers (opening one closes the ones it is not inside), invoker elements (<c>popovertarget</c> and <c>popovertargetaction</c>), and light dismiss
/// (a click outside, the Escape key). The showing state is what <c>:popover-open</c> matches (<c>native.setOpen</c>); nothing is drawn in a top layer.
/// </summary>
internal static class PopoverScripts
{
    /// <summary>
    /// Focus: <c>document.activeElement</c>, <c>focus()</c> and <c>blur()</c> with the focusable-area rules of the HTML standard (links, form controls that
    /// are not disabled, <c>tabindex</c>, <c>contenteditable</c>, <c>summary</c>; nothing that is hidden, inert or inside a popover that is not showing)
    /// and the <c>blur</c>/<c>focusout</c>/<c>focus</c>/<c>focusin</c> events.
    /// </summary>
    public const string Focus = """
        (function (global) {
            "use strict";
            var HTMLElementPrototype = global.HTMLElement.prototype, doc = global.document, focused = null;
            function disabledControl(element) {
                if (element.hasAttribute("disabled")) return true;
                for (var p = element.parentNode; p; p = p.parentNode) {
                    if (p.localName === "fieldset" && p.hasAttribute("disabled")) {
                        var legend = null;
                        for (var c = p.firstElementChild; c; c = c.nextElementSibling) if (c.localName === "legend") { legend = c; break; }
                        if (!legend || !legend.contains(element)) return true;
                    }
                }
                return false;
            }
            function blockedByAncestor(element) {
                for (var n = element; n && n.nodeType === 1; n = n.parentNode) {
                    if (n.hasAttribute("hidden") && n.getAttribute("hidden") !== "until-found") return true;
                    if (n.hasAttribute("inert")) return true;
                    if (n.hasAttribute("popover") && !n.matches(":popover-open")) return true;
                }
                return false;
            }
            function focusable(element) {
                if (!element || element.nodeType !== 1 || !element.isConnected || blockedByAncestor(element)) return false;
                var tag = element.localName, tabindex = element.getAttribute("tabindex");
                if (tabindex !== null && /^\s*[+-]?\d+\s*$/.test(tabindex)) return !(["button", "input", "select", "textarea"].indexOf(tag) >= 0 && disabledControl(element)) || false;
                switch (tag) {
                    case "a": case "area": return element.hasAttribute("href");
                    case "button": case "select": case "textarea": return !disabledControl(element);
                    case "input": return (element.getAttribute("type") || "").toLowerCase() !== "hidden" && !disabledControl(element);
                    case "summary": return element.parentNode && element.parentNode.localName === "details" && element === element.parentNode.firstElementChild;
                    case "iframe": return true;
                }
                var editable = element.getAttribute("contenteditable");
                return editable !== null && editable.toLowerCase() !== "false";
            }
            function fire(target, type, related, bubbles) {
                target.dispatchEvent(new global.FocusEvent(type, { relatedTarget: related, bubbles: bubbles, composed: true }));
            }
            function current() { return focused && focused.isConnected ? focused : null; }
            Object.defineProperty(HTMLElementPrototype, "focus", { value: function focus() {
                if (!focusable(this)) return;
                var old = current();
                if (old === this) return;
                focused = this;
                if (old) { fire(old, "blur", this, false); fire(old, "focusout", this, true); }
                if (focused === this) { fire(this, "focus", old, false); fire(this, "focusin", old, true); }
            }, writable: true, configurable: true, enumerable: true });
            Object.defineProperty(HTMLElementPrototype, "blur", { value: function blur() {
                if (current() !== this) return;
                focused = null;
                fire(this, "blur", null, false); fire(this, "focusout", null, true);
            }, writable: true, configurable: true, enumerable: true });
            Object.defineProperty(Object.getPrototypeOf(doc), "activeElement", { get: function () { return current() || this.body; }, enumerable: true, configurable: true });
            global.__peachFocusable = focusable;
        })
        """;

    public const string Source = """
        (function (native, global) {
            "use strict";
            var HTMLElementPrototype = global.HTMLElement.prototype;
            var doc = global.document;
            var records = new WeakMap();
            var autoStack = [], hintStack = [];

            function record(element) {
                var r = records.get(element);
                if (!r) { r = { showing: false, tracker: null, invoker: null }; records.set(element, r); }
                return r;
            }
            function popoverType(element) {
                var value = element.getAttribute("popover");
                if (value === null) return null;
                value = value.toLowerCase();
                if (value === "" || value === "auto") return "auto";
                if (value === "hint") return "hint";
                return "manual";
            }
            function isPopover(element) { return element && element.nodeType === 1 && popoverType(element) !== null; }
            function isShowing(element) { return record(element).showing; }

            Object.defineProperty(HTMLElementPrototype, "popover", {
                get: function () { return popoverType(this); },
                set: function (value) { if (value === null) this.removeAttribute("popover"); else this.setAttribute("popover", String(value)); },
                enumerable: true, configurable: true
            });

            function error(name, message) { return new DOMException(message, name); }

            // Whether the popover is in the state the call expects; throws for a call that can never work.
            function checkValidity(element, expectedToBeShowing, throwExceptions) {
                if (popoverType(element) === null) {
                    if (throwExceptions) throw error("NotSupportedError", "Not supported on elements that do not have a valid value for the 'popover' attribute.");
                    return false;
                }
                if (expectedToBeShowing !== isShowing(element)) return false;
                if (!element.isConnected) {
                    if (throwExceptions) throw error("InvalidStateError", "Invalid on disconnected popover elements.");
                    return false;
                }
                if (element.localName === "dialog" && element.hasAttribute("open") && element.__modal) {
                    if (throwExceptions) throw error("InvalidStateError", "The element is a modal dialog, which cannot be a popover.");
                    return false;
                }
                return true;
            }

            function fire(element, type, oldState, newState, cancelable, source) {
                var event = new global.ToggleEvent(type, { oldState: oldState, newState: newState, cancelable: cancelable, source: source || null });
                return element.dispatchEvent(event);
            }
            function queueToggle(element, oldState, newState) {
                var r = record(element);
                if (r.tracker) { oldState = r.tracker.oldState; r.tracker.cancelled = true; }
                var tracker = { oldState: oldState, cancelled: false };
                r.tracker = tracker;
                global.setTimeout(function () {
                    if (tracker.cancelled) return;
                    r.tracker = null;
                    fire(element, "toggle", oldState, newState, false, null);
                }, 0);
            }

            // The nearest inclusive ancestor that is a showing auto popover.
            function nearestOpenPopover(node) {
                for (var n = node; n; n = n.parentNode) {
                    if (n.nodeType === 1 && popoverType(n) === "auto" && isShowing(n)) return n;
                }
                return null;
            }
            // The invoker relationship also makes a popover an ancestor: a popover opened from a button inside another popover is its child.
            function topmostAncestor(newPopover, stack, invoker) {
                var positions = new Map(), index = 0;
                stack.forEach(function (p) { positions.set(p, index++); });
                if (newPopover) positions.set(newPopover, index++);
                index++;
                var topmost = null;
                function check(candidate) {
                    var found = nearestOpenPopover(candidate);
                    if (!found || !positions.has(found)) return;
                    if (topmost === null || positions.get(found) > positions.get(topmost)) topmost = found;
                }
                check(invoker);
                if (newPopover) for (var p = newPopover.parentNode; p; p = p.parentNode) check(p);
                return topmost;
            }

            function listFor(element) { return popoverType(element) === "hint" ? hintStack : autoStack; }
            function hideUntil(endpoint, stack, focusPrevious, fireEvents) {
                while (stack.length > 0 && stack[stack.length - 1] !== endpoint) {
                    var top = stack[stack.length - 1];
                    hideInternal(top, focusPrevious, fireEvents, false, null);
                    if (stack.length > 0 && stack[stack.length - 1] === top) stack.pop();
                }
            }
            function hideAllUntil(endpoint, focusPrevious, fireEvents) {
                if (endpoint === null || popoverType(endpoint) === "auto") {
                    hideUntil(null === endpoint ? null : endpoint, hintStack, focusPrevious, fireEvents);
                    hideUntil(endpoint, autoStack, focusPrevious, fireEvents);
                } else hideUntil(endpoint, hintStack, focusPrevious, fireEvents);
            }

            function showInternal(element, throwExceptions, source) {
                if (!checkValidity(element, false, throwExceptions)) return;
                if (!fire(element, "beforetoggle", "closed", "open", true, source)) return;
                if (!checkValidity(element, false, throwExceptions)) return;
                var type = popoverType(element), r = record(element);
                if (type === "auto" || type === "hint") {
                    var stack = listFor(element);
                    var ancestor = topmostAncestor(element, autoStack, source || null);
                    if (type === "hint") {
                        var hintAncestor = topmostAncestor(element, hintStack, source || null);
                        var until = hintAncestor || ancestor;
                        if (hintAncestor) hideUntil(hintAncestor, hintStack, false, true);
                        else { hideUntil(null, hintStack, false, true); hideUntil(ancestor, autoStack, false, true); }
                        void until;
                    } else {
                        hideUntil(null, hintStack, false, true);
                        hideUntil(ancestor, autoStack, false, true);
                    }
                    if (!checkValidity(element, false, throwExceptions)) return;
                    stack.push(element);
                }
                r.showing = true;
                r.invoker = source || null;
                native.setOpen(element, true);
                queueToggle(element, "closed", "open");
            }
            function hideInternal(element, focusPrevious, fireEvents, throwExceptions, source) {
                if (!checkValidity(element, true, throwExceptions)) return;
                var type = popoverType(element), r = record(element);
                if (type === "auto" || type === "hint") {
                    var stack = listFor(element);
                    if (stack.indexOf(element) >= 0) hideUntil(element, stack, focusPrevious, fireEvents);
                    if (type === "auto" && hintStack.length) hideUntil(null, hintStack, focusPrevious, fireEvents);
                    if (!checkValidity(element, true, throwExceptions)) return;
                }
                if (fireEvents) {
                    fire(element, "beforetoggle", "open", "closed", false, source);
                    if (!checkValidity(element, true, throwExceptions)) return;
                }
                var at = listFor(element).indexOf(element);
                if (at >= 0) listFor(element).splice(at, 1);
                r.showing = false;
                r.invoker = null;
                native.setOpen(element, false);
                queueToggle(element, "open", "closed");
            }

            function sourceOf(options) {
                if (options === undefined || options === null) return undefined;
                if (typeof options !== "object" && typeof options !== "function") throw new TypeError("The options argument is not an object.");
                var source = options.source;
                if (source === undefined || source === null) return undefined;
                if (!(source instanceof global.Element)) throw new TypeError("The 'source' member is not of type Element.");
                return source;
            }
            function method(name, fn) {
                Object.defineProperty(HTMLElementPrototype, name, { value: fn, writable: true, configurable: true, enumerable: true });
            }
            method("showPopover", function showPopover(options) {
                var source = sourceOf(options);
                showInternal(this, true, source);
            });
            method("hidePopover", function hidePopover() { hideInternal(this, true, true, true, null); });
            method("togglePopover", function togglePopover(options) {
                var force, source;
                if (typeof options === "boolean") force = options;
                else if (options !== undefined && options !== null) {
                    source = sourceOf(options);
                    if (options.force !== undefined) force = !!options.force;
                }
                if (isShowing(this) && (force === undefined || force === false)) hideInternal(this, true, true, true, null);
                else if (force === undefined || force === true) showInternal(this, true, source);
                else if (!checkValidity(this, isShowing(this), true)) { /* throws above for a non popover */ }
                return isShowing(this);
            });

            // ---- invokers
            var explicitTargets = new WeakMap();
            function isInvoker(element) {
                if (element.nodeType !== 1) return false;
                if (element.localName === "button") return true;
                return element.localName === "input" && ["button", "reset", "submit", "image"].indexOf((element.getAttribute("type") || "text").toLowerCase()) >= 0;
            }
            // What the IDL attribute reports: the popover the content attribute (or an element set through the IDL) names.
            function resolvedTarget(element) {
                var attr = element.getAttribute("popovertarget");
                if (attr === null) return null;
                var explicit = explicitTargets.get(element), candidate = null;
                if (explicit && explicit.isConnected) {
                    var a = explicit.getRootNode(), b = element.getRootNode();
                    candidate = a === b ? explicit : null;
                } else candidate = element.getRootNode().getElementById ? element.getRootNode().getElementById(attr) : null;
                return isPopover(candidate) ? candidate : null;
            }
            // The target an activation uses: only an enabled button, or a button-like input outside a form's submission, invokes.
            function targetOf(element) {
                if (!isInvoker(element)) return null;
                if (element.disabled) return null;
                if (element.form && element.localName === "button" && element.type !== "button") return null;
                if (element.form && element.localName === "input" && element.type !== "button") return null;
                return resolvedTarget(element);
            }
            function actionOf(element) {
                var v = (element.getAttribute("popovertargetaction") || "").toLowerCase();
                return v === "show" || v === "hide" ? v : "toggle";
            }
            Object.defineProperty(HTMLElementPrototype, "popoverTargetElement", {
                get: function () { return resolvedTarget(this); },
                set: function (value) {
                    if (value !== null && !(value instanceof global.Element)) throw new TypeError("The provided value is not of type 'Element'.");
                    if (value === null) { explicitTargets.delete(this); this.removeAttribute("popovertarget"); return; }
                    explicitTargets.set(this, value);
                    this.setAttribute("popovertarget", "");
                },
                enumerable: true, configurable: true
            });
            Object.defineProperty(HTMLElementPrototype, "popoverTargetAction", {
                get: function () { return actionOf(this); },
                set: function (value) { this.setAttribute("popovertargetaction", String(value)); },
                enumerable: true, configurable: true
            });

            function activate(invoker) {
                var target = targetOf(invoker);
                if (!target) return;
                var action = actionOf(invoker);
                if (isShowing(target) && action === "show") return;
                if (!isShowing(target) && action === "hide") return;
                if (isShowing(target)) hideInternal(target, true, true, false, invoker);
                else showInternal(target, false, invoker);
            }
            var downTarget = null;
            function nearestInvokerOrPopover(node) {
                var popover = nearestOpenPopover(node);
                // A click on an invoker whose popover is open counts as inside that popover.
                for (var n = node; n; n = n.parentNode) {
                    if (n.nodeType === 1 && isInvoker(n)) {
                        var t = targetOf(n);
                        if (t && isShowing(t) && popoverType(t) === "auto") return t;
                    }
                }
                return popover;
            }
            function lightDismiss(target) {
                if (autoStack.length === 0 && hintStack.length === 0) return;
                var inside = nearestInvokerOrPopover(target);
                if (hintStack.length && (!inside || popoverType(inside) !== "hint")) hideUntil(null, hintStack, false, true);
                hideUntil(inside, autoStack, false, true);
            }
            doc.addEventListener("pointerdown", function (e) { downTarget = e.target; }, true);
            doc.addEventListener("pointerup", function (e) {
                var down = downTarget; downTarget = null;
                if (down && e.target && nearestInvokerOrPopover(down) === nearestInvokerOrPopover(e.target)) lightDismiss(e.target);
            }, true);
            doc.addEventListener("click", function (e) {
                var t = e.target;
                if (!t || t.nodeType !== 1) return;
                for (var n = t; n; n = n.parentNode) if (n.nodeType === 1 && isInvoker(n)) { if (!e.defaultPrevented) activate(n); return; }
            });
            doc.addEventListener("keydown", function (e) {
                if (e.key !== "Escape" || e.defaultPrevented) return;
                if (hintStack.length) hideInternal(hintStack[hintStack.length - 1], true, true, false, null);
                else if (autoStack.length) hideInternal(autoStack[autoStack.length - 1], true, true, false, null);
            });

            // Changing or removing the popover attribute of a showing popover hides it.
            ["setAttribute", "removeAttribute"].forEach(function (name) {
                var original = HTMLElementPrototype[name];
                Object.defineProperty(HTMLElementPrototype, name, {
                    value: function () {
                        var wasShowing = this.nodeType === 1 && arguments.length > 0 && String(arguments[0]).toLowerCase() === "popover" && isShowing(this), before = wasShowing ? popoverType(this) : null;
                        var result = original.apply(this, arguments);
                        if (wasShowing && popoverType(this) !== before && isShowing(this)) {
                            var r = record(this);
                            var stack = autoStack.indexOf(this) >= 0 ? autoStack : hintStack, at = stack.indexOf(this);
                            if (at >= 0) stack.splice(at, 1);
                            r.showing = false; native.setOpen(this, false);
                            fire(this, "beforetoggle", "open", "closed", false, null);
                            queueToggle(this, "open", "closed");
                        }
                        return result;
                    }, writable: true, configurable: true
                });
            });
        })
        """;
}
