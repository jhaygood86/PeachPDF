namespace PeachBrackets.Scripting.Internal;

/// <summary>
/// <c>Observable</c>, <c>Subscriber</c> and <c>EventTarget.when</c> (the WICG Observable API) and the global <c>reportError</c>, in script: the push-based
/// counterpart of an async iterable, with the operators the proposal lists. Each subscription has its own <c>AbortSignal</c>, which is how an operator stops
/// the sources it reads when its own subscriber goes away.
/// </summary>
internal static class ObservableScripts
{
    public const string Source = """
        (function (global) {
            "use strict";
            var TOKEN = {};
            function define(name, value) { Object.defineProperty(global, name, { value: value, writable: true, configurable: true }); }
            function method(proto, name, fn) { Object.defineProperty(proto, name, { value: fn, writable: true, configurable: true, enumerable: true }); }
            function tag(constructor, name) { Object.defineProperty(constructor.prototype, Symbol.toStringTag, { value: name, configurable: true }); }
            function isCallable(f) { return typeof f === "function"; }

            if (typeof global.reportError !== "function") {
                define("reportError", function reportError(e) {
                    if (arguments.length === 0) throw new TypeError("Failed to execute 'reportError' on 'Window': 1 argument required, but only 0 present.");
                    var message = "Uncaught " + (e !== null && typeof e === "object" && "message" in e ? String(e.message) : String(e));
                    global.dispatchEvent(new ErrorEvent("error", { cancelable: true, error: e, message: message }));
                });
            }
            var report = function (e) { global.reportError(e); };

            var subscribers = new WeakMap();
            // A subscriber's end: its signal aborts with the reason, then its teardown callbacks run, last added first.
            function close(state, reason) {
                if (state.closed) return;
                state.closed = true;
                state.controller.abort(reason);
                var list = state.teardowns;
                state.teardowns = [];
                for (var i = list.length - 1; i >= 0; i--) {
                    try { list[i](); } catch (e) { report(e); }
                }
            }
            function Subscriber(token) {
                if (token !== TOKEN) throw new TypeError("Illegal constructor");
            }
            function stateOf(self) { var s = subscribers.get(self); if (!s) throw new TypeError("Illegal invocation"); return s; }
            method(Subscriber.prototype, "next", function next(value) {
                var s = stateOf(this);
                if (s.closed) return;
                if (s.observer.next) { try { s.observer.next.call(s.observer.target, value); } catch (e) { report(e); } }
            });
            method(Subscriber.prototype, "error", function error(e) {
                var s = stateOf(this);
                if (s.closed) { report(e); return; }
                close(s, e);
                try { if (s.observer.error) s.observer.error.call(s.observer.target, e); else report(e); } catch (x) { report(x); }
            });
            method(Subscriber.prototype, "complete", function complete() {
                var s = stateOf(this);
                if (s.closed) return;
                close(s, undefined);
                if (s.observer.complete) { try { s.observer.complete.call(s.observer.target); } catch (e) { report(e); } }
            });
            method(Subscriber.prototype, "addTeardown", function addTeardown(teardown) {
                var s = stateOf(this);
                if (arguments.length < 1 || !isCallable(teardown)) throw new TypeError("Failed to execute 'addTeardown' on 'Subscriber': The callback provided as parameter 1 is not a function.");
                if (s.closed) { try { teardown(); } catch (e) { report(e); } return; }
                s.teardowns.push(teardown);
            });
            Object.defineProperty(Subscriber.prototype, "active", { get: function () { return !stateOf(this).closed; }, enumerable: true, configurable: true });
            Object.defineProperty(Subscriber.prototype, "signal", { get: function () { return stateOf(this).controller.signal; }, enumerable: true, configurable: true });
            tag(Subscriber, "Subscriber");

            function toObserver(arg) {
                if (arg === undefined || arg === null) return {};
                if (isCallable(arg)) return { next: arg };
                if (typeof arg !== "object") throw new TypeError("Failed to execute 'subscribe' on 'Observable': The provided value is not of type '(SubscriptionObserver or function)'.");
                var out = { target: arg };
                ["next", "error", "complete"].forEach(function (name) {
                    var cb = arg[name];
                    if (cb !== undefined && cb !== null) {
                        if (!isCallable(cb)) throw new TypeError("Failed to execute 'subscribe' on 'Observable': The '" + name + "' callback is not a function.");
                        out[name] = cb;
                    }
                });
                return out;
            }

            var observableCallbacks = new WeakMap();
            function Observable(callback) {
                if (new.target === undefined) throw new TypeError("Failed to construct 'Observable': Please use the 'new' operator, this DOM object constructor cannot be called as a function.");
                if (arguments.length < 1 || !isCallable(callback)) throw new TypeError("Failed to construct 'Observable': The subscribe callback provided as parameter 1 is not a function.");
                observableCallbacks.set(this, callback);
            }
            function callbackOf(self) { var c = observableCallbacks.get(self); if (!c) throw new TypeError("Illegal invocation"); return c; }
            function subscribeInternal(observable, observer, signal) {
                var callback = callbackOf(observable);
                var subscriber = new Subscriber(TOKEN);
                Object.setPrototypeOf(subscriber, Subscriber.prototype);
                var state = { observer: observer, controller: new AbortController(), teardowns: [], closed: false };
                subscribers.set(subscriber, state);
                if (signal) {
                    if (signal.aborted) close(state, signal.reason);
                    else signal.addEventListener("abort", function () { close(state, signal.reason); }, { once: true });
                }
                try { callback.call(undefined, subscriber); }
                catch (e) { if (!state.closed) subscriber.error(e); else report(e); }
            }
            method(Observable.prototype, "subscribe", function subscribe(observerOrNext, options) {
                callbackOf(this);
                var observer = toObserver(observerOrNext);
                var signal = options === undefined || options === null ? undefined : options.signal;
                if (signal !== undefined && !(signal instanceof AbortSignal)) throw new TypeError("Failed to execute 'subscribe' on 'Observable': The 'signal' member is not an AbortSignal.");
                subscribeInternal(this, observer, signal);
            });
            tag(Observable, "Observable");

            function make(fn) { return new Observable(fn); }
            function needCallable(f, name) { if (!isCallable(f)) throw new TypeError("The callback provided as parameter 1 is not a function."); }
            function guard(sub, fn) { try { return fn(); } catch (e) { sub.error(e); return undefined; } }

            // ---- from
            function isThenable(v) { return v !== null && (typeof v === "object" || typeof v === "function") && typeof v.then === "function"; }
            method(Observable, "from", function from(value) {
                if (value instanceof Observable) return value;
                if (value === undefined || value === null) throw new TypeError("Failed to execute 'from' on 'Observable': The provided value is not convertible to an Observable.");
                var asyncMethod = value[Symbol.asyncIterator];
                if (asyncMethod !== undefined && asyncMethod !== null) {
                    if (!isCallable(asyncMethod)) throw new TypeError("Failed to execute 'from' on 'Observable': The async iterator method is not callable.");
                    return make(function (sub) {
                        var iterator = asyncMethod.call(value);
                        if (iterator === null || typeof iterator !== "object") throw new TypeError("The async iterator is not an object.");
                        var nextMethod = iterator.next;
                        sub.addTeardown(function () { var r = iterator["return"]; if (isCallable(r)) { try { Promise.resolve(r.call(iterator)).then(undefined, report); } catch (e) { report(e); } } });
                        (function step() {
                            if (!sub.active) return;
                            var p;
                            try { p = Promise.resolve(nextMethod.call(iterator)); } catch (e) { sub.error(e); return; }
                            p.then(function (r) {
                                if (r === null || typeof r !== "object") { sub.error(new TypeError("The iterator result is not an object.")); return; }
                                if (r.done) { sub.complete(); return; }
                                sub.next(r.value);
                                step();
                            }, function (e) { sub.error(e); });
                        })();
                    });
                }
                var syncMethod = value[Symbol.iterator];
                if (syncMethod !== undefined && syncMethod !== null) {
                    if (!isCallable(syncMethod)) throw new TypeError("Failed to execute 'from' on 'Observable': The iterator method is not callable.");
                    return make(function (sub) {
                        var iterator = syncMethod.call(value);
                        if (iterator === null || typeof iterator !== "object") throw new TypeError("The iterator is not an object.");
                        var nextMethod = iterator.next;
                        sub.addTeardown(function () { var r = iterator["return"]; if (isCallable(r)) r.call(iterator); });
                        while (sub.active) {
                            var r = nextMethod.call(iterator);
                            if (r === null || typeof r !== "object") throw new TypeError("The iterator result is not an object.");
                            if (r.done) { sub.complete(); return; }
                            sub.next(r.value);
                        }
                    });
                }
                if (isThenable(value)) {
                    return make(function (sub) {
                        Promise.resolve(value).then(function (v) { sub.next(v); sub.complete(); }, function (e) { sub.error(e); });
                    });
                }
                throw new TypeError("Failed to execute 'from' on 'Observable': The provided value is not convertible to an Observable.");
            });

            // ---- operators that return an Observable
            function relay(source, sub, handlers, signal) {
                source.subscribe({
                    next: handlers.next || function (v) { sub.next(v); },
                    error: handlers.error || function (e) { sub.error(e); },
                    complete: handlers.complete || function () { sub.complete(); }
                }, { signal: signal || sub.signal });
            }
            method(Observable.prototype, "map", function map(mapper) {
                callbackOf(this); needCallable(mapper);
                var source = this;
                return make(function (sub) {
                    var index = 0;
                    relay(source, sub, { next: function (v) { guard(sub, function () { sub.next(mapper(v, index++)); }); } });
                });
            });
            method(Observable.prototype, "filter", function filter(predicate) {
                callbackOf(this); needCallable(predicate);
                var source = this;
                return make(function (sub) {
                    var index = 0;
                    relay(source, sub, { next: function (v) { guard(sub, function () { if (predicate(v, index++)) sub.next(v); }); } });
                });
            });
            method(Observable.prototype, "take", function take(amount) {
                callbackOf(this);
                amount = Math.trunc(Number(amount)) || 0;
                if (amount < 0) throw new TypeError("Failed to execute 'take' on 'Observable': value is out of range.");
                var source = this;
                return make(function (sub) {
                    var remaining = amount;
                    if (remaining === 0) { sub.complete(); return; }
                    relay(source, sub, { next: function (v) { remaining--; sub.next(v); if (remaining === 0) sub.complete(); } });
                });
            });
            method(Observable.prototype, "drop", function drop(amount) {
                callbackOf(this);
                amount = Math.trunc(Number(amount)) || 0;
                if (amount < 0) throw new TypeError("Failed to execute 'drop' on 'Observable': value is out of range.");
                var source = this;
                return make(function (sub) {
                    var remaining = amount;
                    relay(source, sub, { next: function (v) { if (remaining > 0) remaining--; else sub.next(v); } });
                });
            });
            method(Observable.prototype, "flatMap", function flatMap(mapper) {
                callbackOf(this); needCallable(mapper);
                var source = this;
                return make(function (sub) {
                    var index = 0, queue = [], inner = false, outerDone = false;
                    function startNext() {
                        if (queue.length === 0) { inner = false; if (outerDone) sub.complete(); return; }
                        var value = queue.shift();
                        inner = true;
                        var observable;
                        try { observable = Observable.from(mapper(value, index++)); } catch (e) { sub.error(e); return; }
                        relay(observable, sub, { complete: startNext });
                    }
                    relay(source, sub, {
                        next: function (v) { queue.push(v); if (!inner) startNext(); },
                        complete: function () { outerDone = true; if (!inner) sub.complete(); }
                    });
                });
            });
            method(Observable.prototype, "switchMap", function switchMap(mapper) {
                callbackOf(this); needCallable(mapper);
                var source = this;
                return make(function (sub) {
                    var index = 0, current = null, outerDone = false, innerActive = false;
                    relay(source, sub, {
                        next: function (v) {
                            if (current) current.abort();
                            current = new AbortController();
                            var observable;
                            try { observable = Observable.from(mapper(v, index++)); } catch (e) { sub.error(e); return; }
                            innerActive = true;
                            var mine = current;
                            relay(observable, sub, { complete: function () { if (current === mine) { innerActive = false; if (outerDone) sub.complete(); } } }, AbortSignal.any([sub.signal, mine.signal]));
                        },
                        complete: function () { outerDone = true; if (!innerActive) sub.complete(); }
                    });
                });
            });
            method(Observable.prototype, "inspect", function inspect(arg) {
                callbackOf(this);
                var hooks;
                if (arg === undefined || arg === null) hooks = {};
                else if (isCallable(arg)) hooks = { next: arg };
                else hooks = { next: arg.next, error: arg.error, complete: arg.complete, subscribe: arg.subscribe, abort: arg.abort };
                Object.keys(hooks).forEach(function (k) { if (hooks[k] !== undefined && hooks[k] !== null && !isCallable(hooks[k])) throw new TypeError("The '" + k + "' callback is not a function."); });
                var source = this;
                return make(function (sub) {
                    if (hooks.subscribe) { try { hooks.subscribe.call(arg); } catch (e) { sub.error(e); return; } }
                    if (hooks.abort) sub.signal.addEventListener("abort", function () { try { hooks.abort.call(arg, sub.signal.reason); } catch (e) { report(e); } }, { once: true });
                    relay(source, sub, {
                        next: function (v) { try { if (hooks.next) hooks.next.call(arg, v); } catch (e) { sub.error(e); return; } sub.next(v); },
                        error: function (e) { try { if (hooks.error) hooks.error.call(arg, e); } catch (x) { sub.error(x); return; } sub.error(e); },
                        complete: function () { try { if (hooks.complete) hooks.complete.call(arg); } catch (x) { sub.error(x); return; } sub.complete(); }
                    });
                });
            });
            method(Observable.prototype, "catch", function (callback) {
                callbackOf(this); needCallable(callback);
                var source = this;
                return make(function (sub) {
                    relay(source, sub, {
                        error: function (e) {
                            var observable;
                            try { observable = Observable.from(callback(e)); } catch (x) { sub.error(x); return; }
                            relay(observable, sub, {});
                        }
                    });
                });
            });
            method(Observable.prototype, "finally", function (callback) {
                callbackOf(this); needCallable(callback);
                var source = this;
                return make(function (sub) {
                    sub.addTeardown(callback);
                    relay(source, sub, {});
                });
            });
            method(Observable.prototype, "takeUntil", function takeUntil(notifier) {
                callbackOf(this);
                var until = Observable.from(notifier), source = this;
                return make(function (sub) {
                    until.subscribe({ next: function () { sub.complete(); }, error: function (e) { sub.error(e); } }, { signal: sub.signal });
                    if (!sub.active) return;
                    relay(source, sub, {});
                });
            });

            // ---- operators that return a Promise
            function promiseOperator(source, options, body) {
                return new Promise(function (resolve, reject) {
                    callbackOf(source);
                    var signal = options === undefined || options === null ? undefined : options.signal;
                    if (signal !== undefined && !(signal instanceof AbortSignal)) throw new TypeError("The 'signal' member is not an AbortSignal.");
                    if (signal && signal.aborted) { reject(signal.reason); return; }
                    var controller = new AbortController();
                    if (signal) signal.addEventListener("abort", function () { reject(signal.reason); controller.abort(signal.reason); }, { once: true });
                    var finish = function (fn, v) { fn(v); controller.abort(); };
                    body(function (v) { finish(resolve, v); }, function (e) { finish(reject, e); }, signal ? AbortSignal.any([signal, controller.signal]) : controller.signal);
                });
            }
            function lift(fn) { return function () { try { return fn.apply(this, arguments); } catch (e) { return Promise.reject(e); } }; }
            method(Observable.prototype, "toArray", lift(function toArray(options) {
                var source = this;
                return promiseOperator(source, options, function (resolve, reject, signal) {
                    var values = [];
                    source.subscribe({ next: function (v) { values.push(v); }, error: reject, complete: function () { resolve(values); } }, { signal: signal });
                });
            }));
            method(Observable.prototype, "forEach", lift(function forEach(callback, options) {
                var source = this;
                needCallable(callback);
                return promiseOperator(source, options, function (resolve, reject, signal) {
                    var index = 0;
                    source.subscribe({ next: function (v) { try { callback(v, index++); } catch (e) { reject(e); } }, error: reject, complete: function () { resolve(undefined); } }, { signal: signal });
                });
            }));
            function predicateOperator(name, onValue, onComplete) {
                method(Observable.prototype, name, lift(function (predicate, options) {
                    var source = this;
                    needCallable(predicate);
                    return promiseOperator(source, options, function (resolve, reject, signal) {
                        var index = 0;
                        source.subscribe({
                            next: function (v) { try { onValue(predicate(v, index++), v, resolve); } catch (e) { reject(e); } },
                            error: reject,
                            complete: function () { onComplete(resolve); }
                        }, { signal: signal });
                    });
                }));
            }
            predicateOperator("every", function (r, v, resolve) { if (!r) resolve(false); }, function (resolve) { resolve(true); });
            predicateOperator("some", function (r, v, resolve) { if (r) resolve(true); }, function (resolve) { resolve(false); });
            predicateOperator("find", function (r, v, resolve) { if (r) resolve(v); }, function (resolve) { resolve(undefined); });
            method(Observable.prototype, "first", lift(function first(options) {
                var source = this;
                return promiseOperator(source, options, function (resolve, reject, signal) {
                    source.subscribe({ next: resolve, error: reject, complete: function () { reject(new RangeError("No values in Observable")); } }, { signal: signal });
                });
            }));
            method(Observable.prototype, "last", lift(function last(options) {
                var source = this;
                return promiseOperator(source, options, function (resolve, reject, signal) {
                    var has = false, value;
                    source.subscribe({ next: function (v) { has = true; value = v; }, error: reject, complete: function () { if (has) resolve(value); else reject(new RangeError("No values in Observable")); } }, { signal: signal });
                });
            }));
            method(Observable.prototype, "reduce", lift(function reduce(reducer, initialValue, options) {
                var source = this, hasInitial = arguments.length >= 2;
                needCallable(reducer);
                return promiseOperator(source, options, function (resolve, reject, signal) {
                    var accumulator = initialValue, has = hasInitial, index = 0;
                    source.subscribe({
                        next: function (v) {
                            if (!has) { accumulator = v; has = true; index++; return; }
                            try { accumulator = reducer(accumulator, v, index++); } catch (e) { reject(e); }
                        },
                        error: reject,
                        complete: function () { if (has) resolve(accumulator); else reject(new TypeError("Reduce of an empty Observable with no initial value")); }
                    }, { signal: signal });
                });
            }));

            // ---- EventTarget.when
            method(EventTarget.prototype, "when", function when(type, options) {
                var target = this;
                if (arguments.length < 1) throw new TypeError("Failed to execute 'when' on 'EventTarget': 1 argument required, but only 0 present.");
                type = String(type);
                return make(function (sub) {
                    var init = { capture: !!(options && options.capture), signal: sub.signal };
                    if (options && options.passive !== undefined) init.passive = !!options.passive;
                    target.addEventListener(type, function (event) { sub.next(event); }, init);
                });
            });

            define("Observable", Observable);
            define("Subscriber", Subscriber);
        })(globalThis);
        """;
}
