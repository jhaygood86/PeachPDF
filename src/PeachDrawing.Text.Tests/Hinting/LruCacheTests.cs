using PeachDrawing.Text.Internal.Hinting;

namespace PeachDrawing.Text.Tests.Hinting
{
    /// <summary>The cache the hinting engine keeps its sizes and glyphs in: strict least-recently-used order, weights, single-flight creation, and use from many threads.</summary>
    public class LruCacheTests
    {
        [Fact]
        public void ItBehavesAsAStrictLeastRecentlyUsedCacheWhateverIsDoneToIt()
        {
            // the same random operations on the cache and on a plain list kept in order of use, entry by entry: what is found, and what leaves, and when
            for (int seed = 0; seed < 40; seed++)
            {
                var random = new Random(seed);
                var removed = new List<(int Key, int Value)>();
                var cache = new LruCache<int, int>(6, weight => weight, 30, (key, value) => removed.Add((key, value)));
                var model = new List<(int Key, int Value)>(); // most recent first
                var modelRemoved = new List<(int Key, int Value)>();
                long modelWeight = 0;

                void ModelSet(int key, int value)
                {
                    int at = model.FindIndex(e => e.Key == key);
                    if (at >= 0)
                    {
                        modelRemoved.Add(model[at]);
                        modelWeight -= model[at].Value;
                        model.RemoveAt(at);
                    }

                    model.Insert(0, (key, value));
                    modelWeight += value;
                    while (model.Count > 6 || (modelWeight > 30 && model.Count > 1))
                    {
                        modelRemoved.Add(model[^1]);
                        modelWeight -= model[^1].Value;
                        model.RemoveAt(model.Count - 1);
                    }
                }

                bool ModelGet(int key, out int value)
                {
                    int at = model.FindIndex(e => e.Key == key);
                    if (at < 0)
                    {
                        value = 0;
                        return false;
                    }

                    value = model[at].Value;
                    model.Insert(0, model[at]);
                    model.RemoveAt(at + 1);
                    return true;
                }

                for (int step = 0; step < 600; step++)
                {
                    int key = random.Next(12);
                    switch (random.Next(3))
                    {
                        case 0:
                            int value = 1 + random.Next(12);
                            cache.Set(key, value);
                            ModelSet(key, value);
                            break;
                        case 1:
                            bool found = cache.TryGet(key, out int actual);
                            Assert.Equal(ModelGet(key, out int expected), found);
                            if (found)
                                Assert.Equal(expected, actual);
                            break;
                        default:
                            int made = 1 + random.Next(12);
                            int result = cache.GetOrAdd(key, _ => made);
                            if (ModelGet(key, out int kept))
                                Assert.Equal(kept, result);
                            else
                            {
                                Assert.Equal(made, result);
                                ModelSet(key, made);
                            }

                            break;
                    }

                    Assert.Equal(modelRemoved, removed);
                }
            }
        }

        [Fact]
        public void AnEntryThatIsAlreadyTheMostRecentStaysWhereItIs()
        {
            var cache = new LruCache<int, string>(2);
            cache.Set(1, "a");
            cache.Set(2, "b");

            for (int i = 0; i < 3; i++)
                Assert.True(cache.TryGet(2, out _));

            cache.Set(3, "c"); // 1 is the oldest
            Assert.False(cache.TryGet(1, out _));
            Assert.True(cache.TryGet(2, out _));
            Assert.True(cache.TryGet(3, out _));
        }

        [Fact]
        public void TheWeightOfAValueIsWorkedOutOnceAndOutsideTheLock()
        {
            int calls = 0;
            LruCache<int, int>? cache = null;
            cache = new LruCache<int, int>(3, value =>
            {
                calls++;
                // another thread can use the cache meanwhile: the lock is not held while the weigher runs
                Assert.True(OnAnotherThread(() => cache!.TryGet(-1, out _) || true));
                return value;
            }, 100);

            for (int i = 0; i < 10; i++)
                cache.Set(i, 1); // evicts seven of them: an eviction does not weigh what leaves again

            Assert.Equal(10, calls);
        }

        [Fact]
        public void AnEntryThatLeavesTheCacheIsToldToWhoWantsToKnow()
        {
            var removed = new List<(int, string)>();
            var cache = new LruCache<int, string>(2, removed: (key, value) => removed.Add((key, value)));

            cache.Set(1, "a");
            cache.Set(2, "b");
            Assert.Empty(removed);

            cache.Set(2, "b2"); // replaced
            Assert.Equal([(2, "b")], removed);

            cache.Set(3, "c"); // 1 is evicted
            Assert.Equal([(2, "b"), (1, "a")], removed);

            Assert.True(cache.TryGet(3, out _)); // finding an entry does not remove it
            Assert.Equal(2, removed.Count);
        }

        [Fact]
        public void ManyThreadsAskingForTheSameNewKeyMakeItOnce()
        {
            const int threads = 8;
            var cache = new LruCache<int, object>(4);
            int made = 0;
            using var allStarted = new CountdownEvent(threads);

            object[] results = new object[threads];
            var workers = Enumerable.Range(0, threads).Select(t => new Thread(() =>
            {
                allStarted.Signal();
                allStarted.Wait();
                results[t] = cache.GetOrAdd(7, _ =>
                {
                    Interlocked.Increment(ref made);
                    Thread.Sleep(150); // long enough for the others to be waiting
                    return new object();
                });
            })).ToList();

            workers.ForEach(w => w.Start());
            workers.ForEach(w => w.Join());

            Assert.Equal(1, made);
            Assert.All(results, r => Assert.Same(results[0], r));
        }

        [Fact]
        public void ADifferentKeyIsNotHeldUpByAKeyThatIsBeingMade()
        {
            var cache = new LruCache<int, int>(4);
            using var slowStarted = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();

            int slowResult = 0;
            var slow = new Thread(() => slowResult = cache.GetOrAdd(1, _ =>
            {
                slowStarted.Set();
                release.Wait();
                return 10;
            }));
            slow.Start();

            slowStarted.Wait(TestContext.Current.CancellationToken);
            Assert.Equal(20, cache.GetOrAdd(2, _ => 20)); // does not wait for key 1
            Assert.True(cache.TryGet(2, out _));

            release.Set();
            slow.Join();
            Assert.Equal(10, slowResult);
        }

        [Fact]
        public void WhenTheThreadThatMakesAValueFailsTheOneThatWaitedMakesItItself()
        {
            var cache = new LruCache<int, string>(4);
            using var failing = new ManualResetEventSlim();
            using var waiting = new ManualResetEventSlim();

            Exception? thrown = null;
            var first = new Thread(() =>
            {
                try
                {
                    cache.GetOrAdd(1, _ =>
                    {
                        failing.Set();
                        waiting.Wait();
                        Thread.Sleep(100); // the other thread is in the queue by now
                        throw new InvalidOperationException("no");
                    });
                }
                catch (InvalidOperationException ex)
                {
                    thrown = ex;
                }
            });
            first.Start();

            failing.Wait(TestContext.Current.CancellationToken);
            string? secondResult = null;
            var second = new Thread(() =>
            {
                waiting.Set();
                secondResult = cache.GetOrAdd(1, _ => "made by the second");
            });
            second.Start();

            first.Join();
            second.Join();
            Assert.Equal("no", thrown?.Message);
            Assert.Equal("made by the second", secondResult);
            Assert.True(cache.TryGet(1, out var kept));
            Assert.Equal("made by the second", kept);
        }

        private static bool OnAnotherThread(Func<bool> action)
        {
            bool result = false;
            var thread = new Thread(() => result = action());
            thread.Start();
            return thread.Join(TimeSpan.FromSeconds(30)) && result;
        }

        [Fact]
        public void ManyThreadsSettingGettingAndMakingLeaveItConsistent()
        {
            // values are a function of their keys, so whatever a thread gets must be the function of what it asked for; the cache is far smaller than the keys
            var cache = new LruCache<int, string>(16, value => value.Length, 200);
            var errors = new List<string>();

            Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, t =>
            {
                var random = new Random(t);
                for (int i = 0; i < 20_000; i++)
                {
                    int key = random.Next(64);
                    string expected = "value-" + key;

                    switch (random.Next(3))
                    {
                        case 0:
                            cache.Set(key, expected);
                            break;
                        case 1:
                            if (cache.TryGet(key, out var found) && found != expected)
                                lock (errors) errors.Add($"TryGet({key}) gave {found}");
                            break;
                        default:
                            string made = cache.GetOrAdd(key, k => "value-" + k);
                            if (made != expected)
                                lock (errors) errors.Add($"GetOrAdd({key}) gave {made}");
                            break;
                    }
                }
            });

            Assert.Empty(errors);
        }
    }
}
