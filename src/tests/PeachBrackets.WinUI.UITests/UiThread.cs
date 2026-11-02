using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

// Every test puts its own content in the one window, so tests must not overlap.
[assembly: Xunit.CollectionBehavior(Xunit.CollectionBehavior.CollectionPerAssembly)]

namespace PeachBrackets.WinUI.UITests;

/// <summary>
/// One XAML application on its own thread for the whole test run, with a window to put controls in. Tests hand it work with
/// <see cref="RunAsync{T}(Func{Task{T}})"/>, which runs it on that thread, where WinUI objects may be used.
/// </summary>
internal static class UiThread
{
    private static readonly Lazy<Task<(DispatcherQueue Queue, Window Window)>> Host = new(Start);

    private static Task<(DispatcherQueue, Window)> Start()
    {
        var ready = new TaskCompletionSource<(DispatcherQueue, Window)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                WinRT.ComWrappersSupport.InitializeComWrappers();
                Application.Start(parameters =>
                {
                    // What the generated Main of a XAML app does: continuations after an await come back to this thread.
                    SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                    _ = new HostApp(ready);
                });
            }
            catch (Exception e)
            {
                ready.TrySetException(e);
            }
        })
        {
            IsBackground = true,
            Name = "WinUI test host"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task;
    }

    /// <summary>Runs <paramref name="work"/> on the XAML thread with the test window.</summary>
    public static async Task<T> RunAsync<T>(Func<Window, Task<T>> work)
    {
        var (queue, window) = await Host.Value;
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!queue.TryEnqueue(async () =>
        {
            try
            {
                done.SetResult(await work(window));
            }
            catch (Exception e)
            {
                done.SetException(e);
            }
        }))
        {
            throw new InvalidOperationException("The XAML thread is not accepting work.");
        }

        return await done.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }

    private sealed class HostApp(TaskCompletionSource<(DispatcherQueue, Window)> ready) : Application
    {
        private Window? _window;

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            _window = new Window { Title = "PeachBrackets.WinUI tests" };
            _window.Activate();
            ready.TrySetResult((DispatcherQueue.GetForCurrentThread(), _window));
        }
    }
}
