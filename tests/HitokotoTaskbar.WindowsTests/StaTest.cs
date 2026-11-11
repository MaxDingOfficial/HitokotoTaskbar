using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace HitokotoTaskbar.WindowsTests;

internal static class StaTest
{
    /// <summary>One serialized STA, with an actual dispatcher, closed HWNDs, and a stopped loop per test.</summary>
    internal static async Task RunAsync(Func<WindowScope, Task> action)
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            // The test executable also embeds the application's PerMonitorV2 manifest.
            // Explicit thread context protects physical coordinates when run by an IDE host.
            var previousDpiContext = SetThreadDpiAwarenessContext(new nint(-4));
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            Exception? failure = null;
            dispatcher.BeginInvoke(new Action(async () =>
            {
                using var scope = new WindowScope();
                try { await action(scope); }
                catch (Exception exception) { failure = exception; }
                finally
                {
                    try { scope.CloseWindows(); }
                    catch (Exception exception) { failure ??= exception; }
                    dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            }));
            try { Dispatcher.Run(); }
            catch (Exception exception) { failure ??= exception; }
            finally
            {
                if (previousDpiContext != 0) SetThreadDpiAwarenessContext(previousDpiContext);
                if (failure is null) finished.TrySetResult();
                else finished.TrySetException(failure);
            }
        }) { IsBackground = true, Name = "Hitokoto WPF integration test" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.True(thread.Join(2000), "The WPF test dispatcher must terminate.");
    }

    internal static Task RunAsync(Action<WindowScope> action) => RunAsync(scope =>
    {
        action(scope);
        return Task.CompletedTask;
    });

    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
}

internal sealed class WindowScope : IDisposable
{
    private readonly HashSet<Window> _windows = [];
    internal T Own<T>(T window) where T : Window
    {
        _windows.Add(window);
        window.Closed += (_, _) => _windows.Remove(window);
        return window;
    }
    internal void CloseWindows()
    {
        foreach (var window in _windows.ToArray()) window.Close();
        _windows.Clear();
    }
    public void Dispose() => CloseWindows();
}
