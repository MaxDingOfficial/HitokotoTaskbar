using HitokotoTaskbar.Core;
using HitokotoTaskbar.Native;

namespace HitokotoTaskbar.UI;

/// <summary>One quote and appearance, with an independent overlay for each usable taskbar.</summary>
public sealed class TaskbarOverlays(Action next, Action menu) : IDisposable
{
    private readonly Dictionary<string, OverlayWindow> _windows = new(StringComparer.OrdinalIgnoreCase);
    private AppSettings _settings = new();
    private HitokotoQuote? _quote;
    private string? _status;

    public void Apply(AppSettings settings, HitokotoQuote? quote, string? status = null)
    {
        (_settings, _quote, _status) = (settings, quote, status);
        foreach (var window in _windows.Values) window.Apply(settings, quote, status);
    }

    public void Place(IReadOnlyList<TaskbarSnapshot> snapshots, Func<PixelRect, bool>? isFullscreen = null)
    {
        isFullscreen ??= FullscreenDetector.IsFullscreenOnMonitor;
        var active = snapshots.Select(s => s.Monitor.DeviceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var device in _windows.Keys.Where(device => !active.Contains(device)).ToArray())
        {
            _windows[device].Close();
            _windows.Remove(device);
        }
        foreach (var snapshot in snapshots)
        {
            var device = snapshot.Monitor.DeviceName;
            _windows.TryGetValue(device, out var window);
            // Recheck fullscreen immediately before placement: another monitor may have
            // required an asynchronous scan since this snapshot was collected.
            if (!snapshot.IsVisible || snapshot.IsFullscreen || isFullscreen(snapshot.Monitor.Bounds))
            {
                window?.Conceal();
                continue;
            }
            if (window is null)
            {
                window = new OverlayWindow();
                _windows.Add(device, window);
                window.Closed += (_, _) => _windows.Remove(device);
                window.InitializeHandle();
                window.NextRequested += next;
                window.MenuRequested += menu;
                window.Apply(_settings, _quote, _status);
            }
            window.ShowAt(window.MeasurePlacement(snapshot), snapshot.Monitor.Scale);
        }
    }

    public bool Conceal(string device)
    {
        if (!_windows.TryGetValue(device, out var window) || !window.IsVisible) return false;
        window.Conceal();
        return true;
    }

    public void ConcealAll()
    {
        foreach (var window in _windows.Values) window.Conceal();
    }

    public IReadOnlyDictionary<string, WindowDiagnostic> Inspect() =>
        _windows.ToDictionary(entry => entry.Key, entry => NativeWindow.Inspect(entry.Value.Handle), StringComparer.OrdinalIgnoreCase);

    public void Dispose()
    {
        foreach (var window in _windows.Values.ToArray()) window.Close();
        _windows.Clear();
    }
}
