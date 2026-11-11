using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using HitokotoTaskbar.Native;
using HitokotoTaskbar.Core;

namespace HitokotoTaskbar.UI;

public sealed record MenuAction(string Text, Action Execute, bool Checked = false);

/// <summary>A small no-activate menu. WPF ContextMenu normally takes keyboard focus;
/// this mouse-only surface deliberately does not. Settings remains keyboard accessible.</summary>
public sealed class ActionMenuWindow : Window
{
    private readonly DispatcherTimer _outsideTimer;
    private IDisposable? _style;
    private bool _previousDown;
    private readonly long _openedAt = Environment.TickCount64;
    public ActionMenuWindow(IEnumerable<MenuAction> actions)
    {
        Title = "Hitokoto Taskbar 菜单";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Width = 216;
        SizeToContent = SizeToContent.Height;
        var stack = new StackPanel { Margin = new Thickness(5) };
        foreach (var action in actions)
        {
            var button = new Button
            {
                Content = (action.Checked ? "✓  " : "　 ") + action.Text,
                Focusable = false,
                IsTabStop = false,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(Color.FromRgb(245, 234, 248)),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(10, 8, 10, 8),
                FontFamily = new FontFamily("Microsoft YaHei UI"),
                FontSize = 13
            };
            button.Template = (ControlTemplate)XamlReader.Parse("""
                <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                 xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                 TargetType="Button">
                  <Border x:Name="Surface" CornerRadius="4" Background="{TemplateBinding Background}"
                          Padding="{TemplateBinding Padding}">
                    <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                      VerticalAlignment="Center" />
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property="IsMouseOver" Value="True">
                      <Setter TargetName="Surface" Property="Background" Value="#54415E" />
                    </Trigger>
                    <Trigger Property="IsPressed" Value="True">
                      <Setter TargetName="Surface" Property="Background" Value="#665172" />
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate>
                """);
            button.Click += (_, _) => { Close(); action.Execute(); };
            stack.Children.Add(button);
        }
        Content = new Border
        {
            CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(84, 69, 96)),
            Background = new SolidColorBrush(Color.FromRgb(37, 29, 46)), Child = stack
        };
        SourceInitialized += (_, _) => _style = NativeWindow.AttachNoActivate(
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle));
        _outsideTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
        _outsideTimer.Tick += (_, _) =>
        {
            bool down = (GetAsyncKeyState(1) & 0x8000) != 0 || (GetAsyncKeyState(2) & 0x8000) != 0;
            GetCursorPos(out var point);
            GetWindowRect(new WindowInteropHelper(this).Handle, out var bounds);
            bool outside = point.X < bounds.Left || point.X >= bounds.Right || point.Y < bounds.Top || point.Y >= bounds.Bottom;
            if ((outside && down && !_previousDown) || (GetAsyncKeyState(27) & 0x8000) != 0 ||
                Environment.TickCount64 - _openedAt > 30000) Close();
            _previousDown = down;
        };
        Closed += (_, _) => { _outsideTimer.Stop(); _style?.Dispose(); };
    }
    public void ShowNearCursor()
    {
        GetCursorPos(out var cursor);
        var monitors = TaskbarProbe.EnumerateMonitors();
        var monitor = monitors.FirstOrDefault(m => cursor.X >= m.Bounds.Left && cursor.X < m.Bounds.Right &&
            cursor.Y >= m.Bounds.Top && cursor.Y < m.Bounds.Bottom) ?? monitors.First();
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        // Prime target monitor before WPF measure, so PMv2 chooses its DPI.
        NativeWindow.PlaceNoActivate(hwnd, new PixelRect(cursor.X, cursor.Y, cursor.X + 1, cursor.Y + 1));
        Measure(new Size(216, double.PositiveInfinity));
        var width = (int)Math.Ceiling(216 * monitor.Scale);
        var height = (int)Math.Ceiling(DesiredSize.Height * monitor.Scale);
        int left = Math.Clamp(cursor.X, monitor.WorkArea.Left, Math.Max(monitor.WorkArea.Left, monitor.WorkArea.Right - width));
        int top = Math.Clamp(cursor.Y - height, monitor.WorkArea.Top, Math.Max(monitor.WorkArea.Top, monitor.WorkArea.Bottom - height));
        Show();
        NativeWindow.PlaceNoActivate(hwnd, new PixelRect(left, top, left + width, top + height));
        _outsideTimer.Start();
    }
    [StructLayout(LayoutKind.Sequential)] private struct PointI { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RectI { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out PointI point);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out RectI rect);
}
