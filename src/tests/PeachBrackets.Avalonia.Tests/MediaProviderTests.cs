using PeachBrackets.Media;
using PeachBrackets.Avalonia.Tests.TestSupport;

namespace PeachBrackets.Avalonia.Tests;

public class MediaProviderTests
{
    private const string Page = "<html><body style='margin:0'><video src='https://media.test/a.webm' width='64' height='48'></video></body></html>";

    private sealed class RecordingProvider : IMediaProvider
    {
        public List<Uri> Opened { get; } = [];

        public string CanPlayType(string mimeType) => "probably";

        public ValueTask<IMediaSession?> OpenAsync(MediaRequest request, CancellationToken cancellationToken = default)
        {
            lock (Opened)
                Opened.Add(request.Address);

            return new ValueTask<IMediaSession?>((IMediaSession?)null);
        }
    }

    [Fact]
    public async Task AVideoIsOpenedThroughTheProviderTheViewWasGiven()
    {
        await HeadlessSession.Run(async () =>
        {
            var provider = new RecordingProvider();
            var (window, view) = await HeadlessSession.ShowAsync(Page, 300, 200, v => v.MediaProvider = provider);

            Assert.Same(provider, view.MediaProvider);
            Assert.True(await HeadlessSession.PumpUntilAsync(() => { lock (provider.Opened) return provider.Opened.Count > 0; }, 15000), "the video should be opened through the provider");
            Assert.Equal(new Uri("https://media.test/a.webm"), provider.Opened[0]);
            window.Close();
        });
    }

    [Fact]
    public async Task OnWindows_TheDefaultProviderIsMediaFoundation_WhateverTheAppsTargetFramework()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Media Foundation is Windows only.");
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 300, 200);

            Assert.IsType<PeachBrackets.Media.MediaFoundation.MediaFoundationMediaProvider>(view.MediaProvider);
            window.Close();
        });
    }

    [Fact]
    public async Task ANullProviderMeansNoMedia_AndStaysNull()
    {
        await HeadlessSession.Run(async () =>
        {
            var (window, view) = await HeadlessSession.ShowAsync(Page, 300, 200, v => v.MediaProvider = null);

            Assert.Null(view.MediaProvider);
            window.Close();
        });
    }
}
