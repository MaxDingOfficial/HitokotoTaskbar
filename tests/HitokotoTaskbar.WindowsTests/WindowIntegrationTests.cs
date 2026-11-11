using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using HitokotoTaskbar.Core;
using HitokotoTaskbar.Native;
using HitokotoTaskbar.UI;
using Xunit;

namespace HitokotoTaskbar.WindowsTests;

public sealed class WindowIntegrationTests
{
    [Fact]
    public Task OverlayHandleHasRequiredNoActivateStylesAndMouseActivateResponse() => StaTest.RunAsync(scope =>
    {
        var window = scope.Own(new OverlayWindow());
        window.InitializeHandle();
        var before = GetForegroundWindow();
        var state = NativeWindow.Inspect(window.Handle);
        Assert.True(state.Exists);
        AssertStyles(state.ExtendedStyle);
        // A direct message to OUR HWND verifies the real HwndSource hook. This is not
        // mouse injection, and it does not claim a real desktop click was exercised.
        var result = SendMessage(window.Handle, 0x0021 /* WM_MOUSEACTIVATE */, before,
            new nint((0x0201 /* WM_LBUTTONDOWN */ << 16) | 1 /* HTCLIENT */));
        Assert.Equal(new nint(3 /* MA_NOACTIVATE */), result);
        Assert.Equal(before, GetForegroundWindow());
        Assert.False(window.ShowInTaskbar);
        Assert.False(window.ShowActivated);
        Assert.Equal(WindowStyle.None, window.WindowStyle);
    });

    [Fact]
    public Task PhysicalPlacementRoundTripsOnCurrentMonitorWithoutActivation() => StaTest.RunAsync(scope =>
    {
        var monitor = TaskbarProbe.EnumerateMonitors().FirstOrDefault(m => m.IsPrimary);
        Assert.SkipWhen(monitor is null, "No interactive monitor is available.");
        var screen = monitor!;
        var window = scope.Own(new OverlayWindow());
        // Empty transparent content means this short native-placement check has no
        // visible glyphs and never creates a wide taskbar input surface.
        window.InitializeHandle();
        var before = GetForegroundWindow();
        var rect = new PixelRect(screen.Bounds.Left + (int)Math.Round(24 * screen.Scale),
            screen.WorkArea.Bottom + 4, screen.Bounds.Left + (int)Math.Round(124 * screen.Scale),
            Math.Min(screen.Bounds.Bottom, screen.WorkArea.Bottom + (int)Math.Round(24 * screen.Scale)));
        if (rect.Height <= 0)
            rect = new PixelRect(screen.Bounds.Left + 24, screen.Bounds.Top + 24, screen.Bounds.Left + 124, screen.Bounds.Top + 48);
        window.ShowAt(rect, screen.Scale);
        var state = NativeWindow.Inspect(window.Handle);
        Assert.Equal(rect, state.Bounds);
        Assert.True(state.IsVisible);
        Assert.Equal((uint)Math.Round(screen.Scale * 96), state.Dpi);
        Assert.Equal(before, state.ForegroundHandle == 0 ? nint.Zero : new nint(state.ForegroundHandle));
        AssertStyles(state.ExtendedStyle);
        window.Conceal();
        Assert.False(NativeWindow.Inspect(window.Handle).IsVisible);
    });

    [Fact]
    public Task OverlayNormalizesDisplayTextRetainsTooltipAndClampsEllipsisWidth() => StaTest.RunAsync(scope =>
    {
        var window = scope.Own(new OverlayWindow());
        const string original = "生活明朗\r\n万物可爱\t继续向前\u2028中文第一行\u2029";
        var quote = new HitokotoQuote { Text = string.Concat(Enumerable.Repeat(original, 10)), Source = "集成测试", Author = "作者", Uuid = "test" };
        var settings = new AppSettings { ShowQuoteIcon = false, MaxWidth = 100, FontSize = 13, Opacity = 0.8 };
        window.Apply(settings, quote);
        var text = Assert.IsType<TextBlock>(window.FindName("QuoteText"));
        var tooltip = Assert.IsType<TextBlock>(window.FindName("TooltipText"));
        Assert.Contains("生活明朗", text.Text);
        Assert.DoesNotContain('\r', text.Text);
        Assert.DoesNotContain('\n', text.Text);
        Assert.DoesNotContain('\t', text.Text);
        Assert.DoesNotContain('\u2028', text.Text);
        Assert.DoesNotContain('\u2029', text.Text);
        Assert.Equal(quote.TooltipText, tooltip.Text);
        Assert.Equal(TextTrimming.CharacterEllipsis, text.TextTrimming);
        Assert.Equal(TextWrapping.NoWrap, text.TextWrapping);
        Assert.Equal(0.8, text.Opacity);
        Assert.Equal((byte)0, Assert.IsType<SolidColorBrush>(window.Background).Color.A);
        Assert.True(window.AllowsTransparency);
        var bar = new PixelRect(3840, 2088, 7680, 2160);
        var monitor = new MonitorDescriptor("test", "Test 150%", false,
            new PixelRect(3840, 0, 7680, 2160), new PixelRect(3840, 0, 7680, 2088), 1.5);
        var snapshot = new TaskbarSnapshot(monitor, 0, bar, new PixelRect(3840, 2088, 5200, 2160), true, false, "test");
        var placement = window.MeasurePlacement(snapshot);
        Assert.Equal(150, placement.Width); // 100 DIP at 150%, not 100 physical pixels.
        Assert.Equal(3876, placement.Left); // monitor origin + 24 DIP * 1.5.
        Assert.InRange(placement.Top, bar.Top, bar.Bottom - placement.Height);
        Assert.True(placement.Right <= snapshot.SafeRegion.Right);
    });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public Task SixMenuActionsDispatchOnceAndCloseWithoutActivating(int index) => StaTest.RunAsync(scope =>
    {
        string[] labels = ["下一句", "复制当前语句", "打开一言官网", "设置", "开机自动启动", "退出程序"];
        var calls = new int[labels.Length];
        var actions = labels.Select((label, i) => new MenuAction(label, () => calls[i]++, i == 4)).ToArray();
        var menu = scope.Own(new ActionMenuWindow(actions));
        var handle = new WindowInteropHelper(menu).EnsureHandle();
        AssertStyles(NativeWindow.Inspect(handle).ExtendedStyle);
        var stack = Assert.IsType<StackPanel>(Assert.IsType<Border>(menu.Content).Child);
        var buttons = stack.Children.Cast<Button>().ToArray();
        Assert.Equal(6, buttons.Length);
        Assert.All(buttons, button => { Assert.False(button.Focusable); Assert.False(button.IsTabStop); });
        for (var i = 0; i < labels.Length; i++) Assert.Contains(labels[i], buttons[i].Content.ToString());
        var closed = false;
        menu.Closed += (_, _) => closed = true;
        var before = GetForegroundWindow();
        buttons[index].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.True(closed);
        Assert.False(NativeWindow.Inspect(handle).Exists);
        Assert.Equal(1, calls[index]);
        Assert.Equal(1, calls.Sum());
        Assert.Equal(before, GetForegroundWindow());
    });

    private static void AssertStyles(long styles)
    {
        Assert.True((styles & 0x00000080L /* WS_EX_TOOLWINDOW */) != 0);
        Assert.True((styles & 0x08000000L /* WS_EX_NOACTIVATE */) != 0);
        Assert.True((styles & 0x00080000L /* WS_EX_LAYERED */) != 0);
        Assert.Equal(0, styles & 0x00040000L /* WS_EX_APPWINDOW */);
        Assert.Equal(0, styles & 0x00000020L /* WS_EX_TRANSPARENT would disable text clicks */);
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
}
