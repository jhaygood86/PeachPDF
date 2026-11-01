using System;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// Measures what a piece of work allocates <i>in steady state</i>, which is what an
    /// "allocates nothing per call" test actually means. Prefer this over a bare
    /// <see cref="GC.GetAllocatedBytesForCurrentThread"/> delta around a single loop.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bare version is not wrong so much as unstable, and it failed in CI on a green branch: one
    /// measured loop preceded by a three-call warm-up straddles tiered compilation's promotion point,
    /// so what the loop measures depends on the machine. .NET promotes a method from tier-0 at 30
    /// calls, and only after a background call-counting delay that restarts while other methods are
    /// still being called for the first time — which, in a suite running test collections in parallel,
    /// is continuously. Three warm-up calls therefore leave the method at tier-0, and promotion lands
    /// somewhere inside the loop that is being counted.
    /// </para>
    /// <para>
    /// Two things fix it together. The warm-up runs past the promotion threshold rather than short of
    /// it, so the common case is already settled before anything is counted; and the measurement is the
    /// <b>smallest</b> of several batches rather than one batch taken on trust. The minimum is the
    /// right estimator precisely because of what each kind of allocation does to it: a genuine
    /// per-iteration allocation — the thing every caller of this is guarding against — appears in
    /// <i>every</i> batch and so survives the minimum untouched, while a one-off transient lands in one
    /// batch and is discarded. It cannot hide a regression; it can only discard a coincidence.
    /// </para>
    /// <para>
    /// Per <i>thread</i>, not process-wide: the suite runs collections in parallel, so
    /// <c>GC.GetTotalAllocatedBytes</c> would count whatever every other test is allocating at the same
    /// time. Every caller's body must therefore be synchronous and never await, or it can resume on a
    /// different thread from the one the counter was read on.
    /// </para>
    /// </remarks>
    internal static class AllocationProbe
    {
        /// <summary>
        /// Past tiered compilation's default 30-call promotion threshold with room to spare, so
        /// <paramref name="body"/> and everything it reaches is running tier-1 code by the time the
        /// first batch is counted.
        /// </summary>
        private const int WarmupCalls = 64;

        /// <summary>
        /// The bytes one batch of <paramref name="iterations"/> calls to <paramref name="body"/>
        /// allocates on this thread, taken as the smallest of <paramref name="batches"/> such batches.
        /// </summary>
        /// <param name="body">
        /// the work to measure. Must be synchronous. Build it once, before calling: constructing the
        /// delegate allocates, and invoking an already-constructed one does not.
        /// </param>
        /// <param name="iterations">how many times to run <paramref name="body"/> per batch</param>
        /// <param name="batches">
        /// how many batches to take the minimum of. More than one is the whole point; the default is
        /// enough for a one-off transient to miss at least one batch.
        /// </param>
        internal static long Bytes(Action body, int iterations, int batches = 5)
        {
            for (var i = 0; i < WarmupCalls; i++) body();

            var fewest = long.MaxValue;

            for (var batch = 0; batch < batches; batch++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                for (var i = 0; i < iterations; i++) body();
                fewest = Math.Min(fewest, GC.GetAllocatedBytesForCurrentThread() - before);
            }

            return fewest;
        }
    }
}
