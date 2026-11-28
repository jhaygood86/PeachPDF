namespace PeachBrackets.Scripting.Internal;

/// <summary>
/// <c>Atomics.waitAsync</c> with its timeout on the virtual clock. The engine's own times out on the real one, so whether a page that waited 10 ms had its answer
/// by the time its timers ran depended on how fast the machine was. Run at startup.
/// </summary>
internal static class AtomicsScripts
{
    public const string Source = """
        (function (global) {
            "use strict";
            var original = global.Atomics && global.Atomics.waitAsync;
            if (typeof original !== "function") return;
            var waitAsync = function waitAsync(typedArray, index, value, timeout) {
                // The engine checks the arguments (and settles the cases that need no waiting) with no timeout of its own to race.
                var result = original.call(global.Atomics, typedArray, index, value, timeout === undefined ? undefined : Infinity);
                if (!result.async || timeout === undefined) return result;
                var milliseconds = Number(timeout);
                if (milliseconds !== milliseconds) milliseconds = Infinity;
                if (milliseconds === Infinity) return result;
                var promise = new Promise(function (resolve) {
                    result.value.then(resolve);
                    setTimeout(function () { resolve("timed-out"); }, Math.max(0, milliseconds));
                });
                return { async: true, value: promise };
            };
            Object.defineProperty(waitAsync, "length", { value: 4, configurable: true });
            Object.defineProperty(global.Atomics, "waitAsync", { value: waitAsync, writable: true, configurable: true, enumerable: false });
        })(globalThis);
        """;
}
