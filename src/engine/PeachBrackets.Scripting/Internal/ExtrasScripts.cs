namespace PeachBrackets.Scripting.Internal;

/// <summary>The parts of the page's global environment that are plainest as script: they use only what the node and event objects already expose.</summary>
internal static class ExtrasScripts
{
    /// <summary>The <c>DOMException</c> interface: an Error with a name, a message and the legacy numeric code of its name.</summary>
    public const string DomException = """
        (function () {
            var codes = {
                IndexSizeError: 1, HierarchyRequestError: 3, WrongDocumentError: 4, InvalidCharacterError: 5, NoModificationAllowedError: 7,
                NotFoundError: 8, NotSupportedError: 9, InUseAttributeError: 10, InvalidStateError: 11, SyntaxError: 12, InvalidModificationError: 13,
                NamespaceError: 14, InvalidAccessError: 15, TypeMismatchError: 17, SecurityError: 18, NetworkError: 19, AbortError: 20,
                URLMismatchError: 21, QuotaExceededError: 22, TimeoutError: 23, InvalidNodeTypeError: 24, DataCloneError: 25
            };
            var constants = {
                INDEX_SIZE_ERR: 1, DOMSTRING_SIZE_ERR: 2, HIERARCHY_REQUEST_ERR: 3, WRONG_DOCUMENT_ERR: 4, INVALID_CHARACTER_ERR: 5,
                NO_DATA_ALLOWED_ERR: 6, NO_MODIFICATION_ALLOWED_ERR: 7, NOT_FOUND_ERR: 8, NOT_SUPPORTED_ERR: 9, INUSE_ATTRIBUTE_ERR: 10,
                INVALID_STATE_ERR: 11, SYNTAX_ERR: 12, INVALID_MODIFICATION_ERR: 13, NAMESPACE_ERR: 14, INVALID_ACCESS_ERR: 15,
                VALIDATION_ERR: 16, TYPE_MISMATCH_ERR: 17, SECURITY_ERR: 18, NETWORK_ERR: 19, ABORT_ERR: 20, URL_MISMATCH_ERR: 21,
                QUOTA_EXCEEDED_ERR: 22, TIMEOUT_ERR: 23, INVALID_NODE_TYPE_ERR: 24, DATA_CLONE_ERR: 25
            };
            var store = new WeakMap();
            function DOMException(message, name) {
                if (new.target === undefined)
                    throw new TypeError("Failed to construct 'DOMException': Please use the 'new' operator, this DOM object constructor cannot be called as a function.");
                store.set(this, { message: message === undefined ? "" : String(message), name: name === undefined ? "Error" : String(name) });
            }
            DOMException.prototype = Object.create(Error.prototype);
            Object.defineProperty(DOMException.prototype, "constructor", { value: DOMException, writable: true, configurable: true });
            function state(self) {
                var s = store.get(self);
                if (!s) throw new TypeError("Illegal invocation");
                return s;
            }
            Object.defineProperty(DOMException.prototype, "name", { get: function () { return state(this).name; }, enumerable: true, configurable: true });
            Object.defineProperty(DOMException.prototype, "message", { get: function () { return state(this).message; }, enumerable: true, configurable: true });
            Object.defineProperty(DOMException.prototype, "code", { get: function () { return codes[state(this).name] || 0; }, enumerable: true, configurable: true });
            Object.defineProperty(DOMException.prototype, Symbol.toStringTag, { value: "DOMException", configurable: true });
            // Error.prototype.toString would read name and message through the prototype, where they have no state; this reads the exception's own.
            Object.defineProperty(DOMException.prototype, "toString", {
                value: function toString() { var s = state(this); return s.message ? s.name + ": " + s.message : s.name; },
                writable: true, configurable: true
            });
            for (var key in constants) {
                Object.defineProperty(DOMException, key, { value: constants[key], enumerable: true });
                Object.defineProperty(DOMException.prototype, key, { value: constants[key], enumerable: true });
            }
            return DOMException;
        })()
        """;

    /// <summary>AbortController and AbortSignal, NodeFilter, TreeWalker and NodeIterator.</summary>
    public const string Interfaces = """
        (function (global) {
            function define(name, value) {
                Object.defineProperty(global, name, { value: value, writable: true, configurable: true });
            }
            function tag(constructor, name) {
                Object.defineProperty(constructor.prototype, Symbol.toStringTag, { value: name, configurable: true });
            }

            // ---- AbortSignal and AbortController
            var TOKEN = {};
            var abortFns = new WeakMap();
            class AbortSignal extends EventTarget {
                #aborted = false;
                #reason = undefined;
                constructor(token) {
                    if (token !== TOKEN) throw new TypeError("Illegal constructor");
                    super();
                    abortFns.set(this, (reason) => {
                        if (this.#aborted) return;
                        this.#aborted = true;
                        this.#reason = reason;
                        this.dispatchEvent(new Event("abort"));
                    });
                }
                get aborted() { return this.#aborted; }
                get reason() { return this.#reason; }
                throwIfAborted() { if (this.#aborted) throw this.#reason; }
                static abort(reason) {
                    var signal = new AbortSignal(TOKEN);
                    abortFns.get(signal)(reason === undefined ? new DOMException("signal is aborted without reason", "AbortError") : reason);
                    return signal;
                }
                static timeout(milliseconds) {
                    var signal = new AbortSignal(TOKEN);
                    setTimeout(function () { abortFns.get(signal)(new DOMException("signal timed out", "TimeoutError")); }, milliseconds);
                    return signal;
                }
                static any(signals) {
                    var result = new AbortSignal(TOKEN);
                    for (var source of signals) {
                        if (source.aborted) { abortFns.get(result)(source.reason); return result; }
                    }
                    for (var source of signals) {
                        (function (s) { s.addEventListener("abort", function () { abortFns.get(result)(s.reason); }); })(source);
                    }
                    return result;
                }
            }
            tag(AbortSignal, "AbortSignal");
            class AbortController {
                #signal = new AbortSignal(TOKEN);
                get signal() { return this.#signal; }
                abort(reason) {
                    abortFns.get(this.#signal)(reason === undefined ? new DOMException("signal is aborted without reason", "AbortError") : reason);
                }
            }
            tag(AbortController, "AbortController");
            define("AbortSignal", AbortSignal);
            define("AbortController", AbortController);

            // ---- NodeFilter
            var NodeFilter = {
                FILTER_ACCEPT: 1, FILTER_REJECT: 2, FILTER_SKIP: 3,
                SHOW_ALL: 0xFFFFFFFF, SHOW_ELEMENT: 0x1, SHOW_ATTRIBUTE: 0x2, SHOW_TEXT: 0x4, SHOW_CDATA_SECTION: 0x8, SHOW_ENTITY_REFERENCE: 0x10,
                SHOW_ENTITY: 0x20, SHOW_PROCESSING_INSTRUCTION: 0x40, SHOW_COMMENT: 0x80, SHOW_DOCUMENT: 0x100, SHOW_DOCUMENT_TYPE: 0x200,
                SHOW_DOCUMENT_FRAGMENT: 0x400, SHOW_NOTATION: 0x800
            };
            Object.defineProperty(NodeFilter, Symbol.toStringTag, { value: "NodeFilter", configurable: true });
            define("NodeFilter", NodeFilter);

            // ---- TreeWalker and NodeIterator
            var ACCEPT = 1, REJECT = 2, SKIP = 3;
            var states = new WeakMap();

            function filterNode(state, node) {
                if (state.active) throw new DOMException("The filter is already running.", "InvalidStateError");
                if (!((state.whatToShow >>> 0) & (1 << (node.nodeType - 1)))) return SKIP;
                var filter = state.filter;
                if (filter === null) return ACCEPT;
                state.active = true;
                try {
                    return typeof filter === "function" ? filter.call(undefined, node) : filter.acceptNode(node);
                } finally {
                    state.active = false;
                }
            }

            function make(Type, root, whatToShow, filter, extra) {
                if (!root || typeof root.nodeType !== "number")
                    throw new TypeError("The root is not a Node.");
                var object = Object.create(Type.prototype);
                var state = { root: root, whatToShow: whatToShow === undefined ? 0xFFFFFFFF : whatToShow >>> 0, filter: filter === undefined ? null : filter, active: false };
                for (var k in extra) state[k] = extra[k];
                states.set(object, state);
                return object;
            }
            function stateOf(object, Type) {
                var state = states.get(object);
                if (!state) throw new TypeError("Illegal invocation");
                return state;
            }
            function readonly(Type, name, get) {
                Object.defineProperty(Type.prototype, name, { get: function () { return get(stateOf(this)); }, enumerable: true, configurable: true });
            }

            function TreeWalker() { throw new TypeError("Illegal constructor"); }
            readonly(TreeWalker, "root", function (s) { return s.root; });
            readonly(TreeWalker, "whatToShow", function (s) { return s.whatToShow; });
            readonly(TreeWalker, "filter", function (s) { return s.filter; });
            Object.defineProperty(TreeWalker.prototype, "currentNode", {
                get: function () { return stateOf(this).current; },
                set: function (value) {
                    if (!value || typeof value.nodeType !== "number") throw new TypeError("The value is not a Node.");
                    stateOf(this).current = value;
                },
                enumerable: true, configurable: true
            });

            function walkerParent(s) {
                var node = s.current;
                while (node !== null && node !== s.root) {
                    node = node.parentNode;
                    if (node !== null && filterNode(s, node) === ACCEPT) { s.current = node; return node; }
                }
                return null;
            }
            function walkerChild(s, first) {
                var node = first ? s.current.firstChild : s.current.lastChild;
                while (node !== null) {
                    var result = filterNode(s, node);
                    if (result === ACCEPT) { s.current = node; return node; }
                    if (result === SKIP) {
                        var child = first ? node.firstChild : node.lastChild;
                        if (child !== null) { node = child; continue; }
                    }
                    while (node !== null) {
                        var sibling = first ? node.nextSibling : node.previousSibling;
                        if (sibling !== null) { node = sibling; break; }
                        var parent = node.parentNode;
                        if (parent === null || parent === s.root || parent === s.current) return null;
                        node = parent;
                    }
                }
                return null;
            }
            function walkerSibling(s, next) {
                var node = s.current;
                if (node === s.root) return null;
                while (true) {
                    var sibling = next ? node.nextSibling : node.previousSibling;
                    while (sibling !== null) {
                        node = sibling;
                        var result = filterNode(s, node);
                        if (result === ACCEPT) { s.current = node; return node; }
                        sibling = next ? node.firstChild : node.lastChild;
                        if (result === REJECT || sibling === null) sibling = next ? node.nextSibling : node.previousSibling;
                    }
                    node = node.parentNode;
                    if (node === null || node === s.root) return null;
                    if (filterNode(s, node) === ACCEPT) return null;
                }
            }
            function walkerNext(s) {
                var node = s.current, result = ACCEPT;
                while (true) {
                    while (result !== REJECT && node.firstChild !== null) {
                        node = node.firstChild;
                        result = filterNode(s, node);
                        if (result === ACCEPT) { s.current = node; return node; }
                    }
                    var sibling = null, temporary = node;
                    while (temporary !== null) {
                        if (temporary === s.root) return null;
                        sibling = temporary.nextSibling;
                        if (sibling !== null) { node = sibling; break; }
                        temporary = temporary.parentNode;
                    }
                    if (sibling === null) return null;
                    result = filterNode(s, node);
                    if (result === ACCEPT) { s.current = node; return node; }
                }
            }
            function walkerPrevious(s) {
                var node = s.current;
                while (node !== s.root) {
                    var sibling = node.previousSibling;
                    while (sibling !== null) {
                        node = sibling;
                        var result = filterNode(s, node);
                        while (result !== REJECT && node.lastChild !== null) {
                            node = node.lastChild;
                            result = filterNode(s, node);
                        }
                        if (result === ACCEPT) { s.current = node; return node; }
                        sibling = node.previousSibling;
                    }
                    if (node === s.root || node.parentNode === null) return null;
                    node = node.parentNode;
                    if (filterNode(s, node) === ACCEPT) { s.current = node; return node; }
                }
                return null;
            }
            function method(Type, name, length, body) {
                Object.defineProperty(Type.prototype, name, {
                    value: { [name]: function () { return body(stateOf(this)); } }[name], writable: true, configurable: true, enumerable: true
                });
            }
            method(TreeWalker, "parentNode", 0, walkerParent);
            method(TreeWalker, "firstChild", 0, function (s) { return walkerChild(s, true); });
            method(TreeWalker, "lastChild", 0, function (s) { return walkerChild(s, false); });
            method(TreeWalker, "previousSibling", 0, function (s) { return walkerSibling(s, false); });
            method(TreeWalker, "nextSibling", 0, function (s) { return walkerSibling(s, true); });
            method(TreeWalker, "previousNode", 0, walkerPrevious);
            method(TreeWalker, "nextNode", 0, walkerNext);
            tag(TreeWalker, "TreeWalker");

            function NodeIterator() { throw new TypeError("Illegal constructor"); }
            readonly(NodeIterator, "root", function (s) { return s.root; });
            readonly(NodeIterator, "whatToShow", function (s) { return s.whatToShow; });
            readonly(NodeIterator, "filter", function (s) { return s.filter; });
            readonly(NodeIterator, "referenceNode", function (s) { return s.reference; });
            readonly(NodeIterator, "pointerBeforeReferenceNode", function (s) { return s.before; });
            function following(node, root) {
                if (node.firstChild !== null) return node.firstChild;
                for (var n = node; n !== null; n = n.parentNode) {
                    if (n === root) return null;
                    if (n.nextSibling !== null) return n.nextSibling;
                }
                return null;
            }
            function preceding(node, root) {
                if (node === root) return null;
                var sibling = node.previousSibling;
                if (sibling !== null) {
                    while (sibling.lastChild !== null) sibling = sibling.lastChild;
                    return sibling;
                }
                return node.parentNode;
            }
            function iterate(s, next) {
                var node = s.reference, before = s.before;
                while (true) {
                    if (next) {
                        if (!before) { node = following(node, s.root); if (node === null) return null; }
                        else before = false;
                    } else {
                        if (before) { node = preceding(node, s.root); if (node === null) return null; }
                        else before = true;
                    }
                    if (filterNode(s, node) === ACCEPT) break;
                }
                s.reference = node;
                s.before = before;
                return node;
            }
            method(NodeIterator, "nextNode", 0, function (s) { return iterate(s, true); });
            method(NodeIterator, "previousNode", 0, function (s) { return iterate(s, false); });
            method(NodeIterator, "detach", 0, function () { });
            tag(NodeIterator, "NodeIterator");
            define("TreeWalker", TreeWalker);
            define("NodeIterator", NodeIterator);

            var documentPrototype = Object.getPrototypeOf(global.document);
            Object.defineProperty(documentPrototype, "createTreeWalker", {
                value: function createTreeWalker(root, whatToShow, filter) {
                    if (arguments.length === 0) throw new TypeError("1 argument required, but only 0 present.");
                    return make(TreeWalker, root, whatToShow, filter, { current: root });
                },
                writable: true, configurable: true, enumerable: true
            });
            Object.defineProperty(documentPrototype, "createNodeIterator", {
                value: function createNodeIterator(root, whatToShow, filter) {
                    if (arguments.length === 0) throw new TypeError("1 argument required, but only 0 present.");
                    return make(NodeIterator, root, whatToShow, filter, { reference: root, before: true });
                },
                writable: true, configurable: true, enumerable: true
            });
        })(globalThis);
        """;
}
