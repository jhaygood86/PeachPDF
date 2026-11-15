using Avalonia.Headless;
using Avalonia.Threading;
using PeachBrackets.Updates;
using PeachBrackets.Avalonia.Tests.TestSupport;

namespace PeachBrackets.Avalonia.Tests;

public class AnimationTests
{
    private const string Fade =
        "<html><head><style>@keyframes fade{from{opacity:0}to{opacity:1}}" +
        "#box{width:100px;height:100px;background:#c33;animation:fade 0.2s linear 1}</style></head>" +
        "<body style='margin:0'><div id='box'></div></body></html>";

    private static Task<bool> PumpAsync(Func<bool> condition, int milliseconds = 15000) => HeadlessSession.PumpUntilAsync(condition, milliseconds);

    [Fact]
    public async Task AnAnimationStepsTheDocumentFrameByFrameAndRaisesItsEvents()
    {
        await HeadlessSession.Run(async () =>
        {
            var events = new List<string>();
            var (window, view) = await HeadlessSession.ShowAsync(Fade, 300, 200, v => v.AnimationsEnabled = true);
            view.AnimationEvents += (_, e) => events.AddRange(e.Events.Select(x => x.ToString() ?? string.Empty));

            Assert.True(await PumpAsync(() => view.FrameCount > 3), "frames should be stepped while the animation runs");
            Assert.True(await PumpAsync(() => events.Count >= 1), "the animation's events should reach the host");

            // Once nothing runs the loop stops: no further frames are asked for.
            Assert.True(await PumpAsync(() => !view.IsAnimationLoopRunning), "the loop stops when the animation has ended");
            var settled = view.FrameCount;
            await PumpAsync(() => false, 200);
            Assert.Equal(settled, view.FrameCount);
            window.Close();
        });
    }

    [Fact]
    public async Task ADocumentThatDoesNotAnimateAsksForNoFrames()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Fade, 300, 200);
            await PumpAsync(() => false, 200);
            Assert.Equal(0, view.FrameCount);
            Assert.False(view.IsAnimationLoopRunning);
            window.Close();
        });
    }

    [Fact]
    public async Task LeavingTheTreeStopsTheLoop()
    {
        await HeadlessSession.Run(async () =>
        {
            const string Endless = "<html><head><style>@keyframes f{from{opacity:0}to{opacity:1}}#b{width:50px;height:50px;background:#333;animation:f 1s linear infinite}</style></head><body><div id='b'></div></body></html>";
            var (window, view) = await HeadlessSession.ShowAsync(Endless, 300, 200, v => v.AnimationsEnabled = true);
            Assert.True(await PumpAsync(() => view.FrameCount > 2));
            Assert.True(view.IsAnimationLoopRunning);

            window.Content = null;
            Dispatcher.UIThread.RunJobs();
            Assert.False(view.IsAnimationLoopRunning);
            var stopped = view.FrameCount;
            await PumpAsync(() => false, 150);
            Assert.Equal(stopped, view.FrameCount);
            window.Close();
        });
    }

    private const string Grow =
        "<html><head><style>@keyframes grow{from{width:20px}to{width:200px}}" +
        "#bar{height:40px;background:#c33;animation:grow 0.4s linear infinite alternate}" +
        "</style></head><body style='margin:0'><div id='bar'></div></body></html>";

    [Fact]
    public async Task AnAnimationThatChangesLayoutIsSteppedAndRepainted()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Grow, 300, 200, v => v.AnimationsEnabled = true);
            Assert.True(await PumpAsync(() => view.FrameCount > 6));

            // The bar's width changes from frame to frame, so the layout was done again and what it painted differs: the box reaches different widths.
            var widths = new HashSet<int>();
            var sampled = await PumpAsync(() =>
            {
                var bounds = view.Document!.GetElementBounds("bar");
                if (bounds is { } b)
                    widths.Add((int)(b.Width / 20));

                return widths.Count > 3;
            });
            Assert.True(sampled, "the animated width should take several values");
            Assert.True(view.FramesPainted > 1);
            window.Close();
        });
    }

    [Fact]
    public async Task APausedAnimationStopsTheLoopAndPlayingItStartsTheLoopAgain()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Grow, 300, 200, v => v.AnimationsEnabled = true);
            Assert.True(await PumpAsync(() => view.FrameCount > 3));
            var animation = Assert.Single(view.Document!.GetAnimations());

            animation.Pause();
            Assert.True(await PumpAsync(() => !view.IsAnimationLoopRunning), "nothing runs, so no frame is asked for");
            var paused = view.FrameCount;
            await PumpAsync(() => false, 150);
            Assert.Equal(paused, view.FrameCount);

            // The control API starts the clock from any thread, and the loop that steps it starts again.
            await Task.Run(animation.Play);
            Assert.True(await PumpAsync(() => view.IsAnimationLoopRunning && view.FrameCount > paused + 2), "the loop should run again");
            window.Close();
        });
    }

    private const string Endless =
        "<html><head><style>@keyframes f{from{opacity:0}to{opacity:1}}#b{width:50px;height:50px;background:#333;animation:f 1s linear infinite}</style></head><body><div id='b'></div></body></html>";

    private sealed class SlowStepper
    {
        public TaskCompletionSource Gate { get; } = new();

        public int Calls { get; private set; }

        public bool ThrowSynchronously { get; set; }

        public Func<AnimationFrame>? Result { get; set; }

        // The first step waits for the gate and ends as Result says (the document's own step when there is none); the later ones are the document's own.
        public ValueTask<AnimationFrame> Step(HtmlDocument document, TimeSpan time, PeachDrawing.Core.Canvas canvas)
        {
            Calls++;
            if (ThrowSynchronously)
                throw new InvalidOperationException("the step failed at once");

            if (Calls > 1)
                return document.AdvanceAnimationsAsync(time, canvas);

            return Wait(document, time, canvas);
        }

        private async ValueTask<AnimationFrame> Wait(HtmlDocument document, TimeSpan time, PeachDrawing.Core.Canvas canvas)
        {
            await Gate.Task;
            return Result is { } result ? result() : await document.AdvanceAnimationsAsync(time, canvas);
        }
    }

    [Fact]
    public async Task AStepThatTakesAWhileHoldsBackTheNextOneAndFinishesTheFrameWhenItIsDone()
    {
        await HeadlessSession.Run(async () =>
        {
            var stepper = new SlowStepper();
            var (window, view) = await HeadlessSession.ShowAsync(Endless, 300, 200, v =>
            {
                v.AnimationsEnabled = true;
                v.AnimationStepper = stepper.Step;
            });

            Assert.True(await PumpAsync(() => stepper.Calls >= 1));
            Assert.Equal(0, view.FrameCount);

            // A frame that comes while the step is in flight steps nothing: the layout it would paint from is not there yet.
            view.OnFrame(TimeSpan.FromSeconds(50));
            view.OnFrame(TimeSpan.FromSeconds(51));
            await PumpAsync(() => false, 100);
            Assert.Equal(1, stepper.Calls);
            Assert.Equal(0, view.FrameCount);

            stepper.Gate.SetResult();
            Assert.True(await PumpAsync(() => view.FrameCount >= 1), "the frame finishes when its step does");
            Assert.True(await PumpAsync(() => stepper.Calls > 1 && view.FrameCount > 2), "and the loop goes on with the next frames");
            window.Close();
        });
    }

    [Fact]
    public async Task AStepThatFailsStopsTheLoopAndReportsTheFailure()
    {
        await HeadlessSession.Run(async () =>
        {
            var stepper = new SlowStepper { Result = () => throw new InvalidOperationException("the step failed late") };
            Exception? failure = null;
            var (window, view) = await HeadlessSession.ShowAsync(Endless, 300, 200, v =>
            {
                v.AnimationsEnabled = true;
                v.AnimationStepper = stepper.Step;
                v.DocumentFailed += (_, e) => failure = e.Exception;
            });

            Assert.True(await PumpAsync(() => stepper.Calls >= 1));
            stepper.Gate.SetResult();
            Assert.True(await PumpAsync(() => failure is not null));
            Assert.Equal("the step failed late", failure!.Message);
            Assert.False(view.IsAnimationLoopRunning);
            window.Close();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AStepOfADocumentThatWasReplacedMeanwhileEndsWithoutTouchingTheNewOne(bool throws)
    {
        await HeadlessSession.Run(async () =>
        {
            var stepper = new SlowStepper
            {
                Result = throws
                    ? () => throw new ObjectDisposedException("the old document")
                    : () => new AnimationFrame(true, [], false),
            };
            var failures = 0;
            var (window, view) = await HeadlessSession.ShowAsync(Endless, 300, 200, v =>
            {
                v.AnimationsEnabled = true;
                v.AnimationStepper = stepper.Step;
                v.DocumentFailed += (_, _) => failures++;
            });

            Assert.True(await PumpAsync(() => stepper.Calls >= 1));
            var replaced = new TaskCompletionSource();
            view.DocumentLoaded += (_, _) => replaced.TrySetResult();
            view.Html = "<html><body><p id='plain'>No animation here.</p></body></html>";
            await replaced.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await PumpAsync(() => !view.IsReloading);

            stepper.Gate.SetResult();
            Assert.True(await PumpAsync(() => !view.IsAnimationLoopRunning), "the loop ends once the new document, which plays nothing, has had its step");
            await PumpAsync(() => false, 100);

            // The step that finished for the document that was let go of counted for nothing: only the new document's own step was a frame.
            Assert.Equal(0, failures);
            Assert.True(view.FrameCount <= 1, $"{view.FrameCount} frames were counted");
            Assert.NotNull(view.Document!.GetElement("plain"));
            window.Close();
        });
    }

    [Fact]
    public async Task AStepThatFailsAtOnceReachesTheCallerAndDoesNotDamageTheLoop()
    {
        await HeadlessSession.Run(async () =>
        {
            var stepper = new SlowStepper();
            var (window, view) = await HeadlessSession.ShowAsync(Endless, 300, 200, v =>
            {
                v.AnimationsEnabled = true;
                v.AnimationStepper = stepper.Step;
            });
            Assert.True(await PumpAsync(() => stepper.Calls >= 1));
            stepper.Gate.SetResult();
            Assert.True(await PumpAsync(() => view.FrameCount >= 1));

            // The loop runs on the UI thread and so does this: no frame is stepped between the lines.
            stepper.ThrowSynchronously = true;
            try
            {
                Assert.Throws<InvalidOperationException>(() => view.OnFrame(TimeSpan.FromSeconds(100)));
            }
            finally
            {
                stepper.ThrowSynchronously = false;
            }

            var before = view.FrameCount;
            Assert.True(await PumpAsync(() => view.FrameCount > before + 1));
            window.Close();
        });
    }

    [Fact]
    public async Task AFrameThatArrivesAfterTheViewLeftTheTreeStepsNothing()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Endless, 300, 200, v => v.AnimationsEnabled = true);
            Assert.True(await PumpAsync(() => view.FrameCount > 1));

            window.Content = null;
            Dispatcher.UIThread.RunJobs();
            Assert.False(view.IsAnimationLoopRunning);
            var frames = view.FrameCount;
            view.OnFrame(TimeSpan.FromSeconds(10));
            Assert.Equal(frames, view.FrameCount);
            window.Close();
        });
    }
}
