using Avalonia.Headless;
using Avalonia.Threading;
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
}
