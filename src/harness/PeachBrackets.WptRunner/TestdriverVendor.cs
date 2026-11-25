namespace PeachBrackets.WptRunner;

/// <summary>
/// What the page's <c>testdriver-vendor.js</c> is: the part of testdriver.js a browser's automation fills in. Here the actions are done in the page, by
/// dispatching the events a user's input would raise, so tests that drive a control with a click or keys run instead of failing on "not implemented".
/// The pointer and keyboard model is the minimum those tests need: no hit testing, no default actions beyond a text control's value.
/// </summary>
internal static class TestdriverVendor
{
    public const string Script = """
        (function () {
            var internal = window.test_driver_internal;
            if (!internal) return;
            internal.in_automation = true;

            function make(type, init) {
                var options = Object.assign({ bubbles: true, cancelable: true }, init || {});
                var name = /^key/.test(type) ? 'KeyboardEvent' : /^(mouse|click|dblclick|contextmenu)/.test(type) ? 'MouseEvent' : /^pointer/.test(type) ? 'PointerEvent' : 'Event';
                var Constructor = typeof window[name] === 'function' ? window[name] : Event;
                var event = new Constructor(type, options);
                for (var key in options) { if (!(key in event)) { try { event[key] = options[key]; } catch (e) {} } }
                return event;
            }
            function fire(target, type, init) { return target.dispatchEvent(make(type, init)); }
            function target(element) { return element || document.activeElement || document.body; }

            function type(element, key) {
                var el = target(element);
                var code = key.length === 1 ? 'Key' + key.toUpperCase() : key;
                fire(el, 'keydown', { key: key, code: code });
                if (key.length === 1 && fire(el, 'keypress', { key: key, code: code }) && 'value' in el && !el.readOnly && !el.disabled) {
                    el.value = (el.value || '') + key;
                    fire(el, 'input', { data: key, inputType: 'insertText' });
                }
                fire(el, 'keyup', { key: key, code: code });
            }

            internal.click = function (element) {
                var coords = { clientX: 0, clientY: 0, button: 0 };
                fire(element, 'pointerdown', coords); fire(element, 'mousedown', coords);
                fire(element, 'pointerup', coords); fire(element, 'mouseup', coords);
                fire(element, 'click', coords);
                return Promise.resolve();
            };
            internal.send_keys = function (element, keys) {
                for (var i = 0; i < keys.length; i++) type(element, keys[i]);
                return Promise.resolve();
            };
            internal.action_sequence = function (sources) {
                (sources || []).forEach(function (source) {
                    var el = null;
                    (source.actions || []).forEach(function (action) {
                        if (source.type === 'pointer') {
                            if (action.type === 'pointerMove' && action.origin && action.origin.nodeType) el = action.origin;
                            var init = { clientX: action.x || 0, clientY: action.y || 0, button: action.button || 0 };
                            if (action.type === 'pointerDown') { fire(target(el), 'pointerdown', init); fire(target(el), 'mousedown', init); }
                            else if (action.type === 'pointerUp') { fire(target(el), 'pointerup', init); fire(target(el), 'mouseup', init); fire(target(el), 'click', init); }
                            else if (action.type === 'pointerMove') { fire(target(el), 'pointermove', init); fire(target(el), 'mousemove', init); }
                        } else if (source.type === 'key') {
                            if (action.type === 'keyDown') type(null, action.value);
                        }
                    });
                });
                return Promise.resolve();
            };
            internal.bless = function (intent, action) { return Promise.resolve(action ? action() : undefined); };
            internal.set_test_context = function () {};
            internal.minimize_window = function () { return Promise.resolve(); };
            internal.set_window_rect = function () { return Promise.resolve(); };
            internal.delete_all_cookies = function () { return Promise.resolve(); };
            internal.freeze = function () { return Promise.resolve(); };
        })();
        """;
}
