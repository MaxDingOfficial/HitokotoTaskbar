using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using HitokotoTaskbar.Core;
using HitokotoTaskbar.Native;

namespace HitokotoTaskbar.UI;

public partial class OverlayWindow : Window
{
    private IDisposable? _style;
    private AppSettings _settings = new();
    private string _text = "正在获取一言…";
    public event Action? NextRequested;
    public event Action? MenuRequested;
    public nint Handle { get; private set; }
    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            Handle = new WindowInteropHelper(this).Handle;
            _style = NativeWindow.AttachNoActivate(HwndSource.FromHwnd(Handle));
        };
        Closed += (_, _) => _style?.Dispose();
        System.Windows.Controls.ToolTipService.SetInitialShowDelay(QuoteText, 600);
        System.Windows.Controls.ToolTipService.SetShowDuration(QuoteText, 30000);
    }
    public void InitializeHandle() => new WindowInteropHelper(this).EnsureHandle();
    public void Apply(AppSettings settings, HitokotoQuote? quote, string? status = null)
    {
        _settings = settings;
        _text = (quote?.Text ?? "暂时没有一言，点击重试")
            .Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ')
            .Replace('\u2028', ' ').Replace('\u2029', ' ');
        QuoteText.Text = (settings.ShowQuoteIcon ? "❝  " : "") + _text;
        QuoteText.FontFamily = new FontFamily(settings.FontFamily);
        QuoteText.FontSize = settings.FontSize;
        QuoteText.Foreground = (SolidColorBrush)new BrushConverter().ConvertFromString(settings.TextColor)!;
        QuoteText.Opacity = settings.Opacity;
        TooltipText.Text = (quote?.TooltipText ?? "联网后会自动获取语句。") +
            (string.IsNullOrWhiteSpace(status) ? "" : "\n\n" + status);
        // A brief fade-in cannot black out the complete layered window or alter hit testing.
        if (settings.EnableTransitions && IsVisible)
            QuoteText.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(settings.Opacity * 0.65, settings.Opacity, TimeSpan.FromMilliseconds(160)));
        else QuoteText.BeginAnimation(UIElement.OpacityProperty, null);
    }
    public PixelRect MeasurePlacement(TaskbarSnapshot snapshot)
    {
        var scale = snapshot.Monitor.Scale;
        if (!LayoutRules.TryPlace(snapshot.TaskbarRect, scale, _settings.HorizontalOffset,
            _settings.MaxWidth, 0, snapshot.SafeRegion.Left, snapshot.SafeRegion.Right, out var corridor))
            return default;
        var formatted = new FormattedText(QuoteText.Text, CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight, new Typeface(QuoteText.FontFamily, FontStyles.Normal,
                FontWeights.Normal, FontStretches.Normal), _settings.FontSize, QuoteText.Foreground, scale);
        return LayoutRules.PlaceText(corridor, scale, Math.Ceiling(formatted.WidthIncludingTrailingWhitespace + 4),
            Math.Ceiling(formatted.Height + 4), _settings.VerticalOffset);
    }
    public void ShowAt(PixelRect rect, double scale)
    {
        if (rect.Width < 24 * scale || rect.Height < 1) { Conceal(); return; }
        // Width/Height are DIP; only SetWindowPos receives absolute physical screen pixels.
        Width = rect.Width / scale;
        Height = rect.Height / scale;
        NativeWindow.PlaceNoActivate(Handle, rect);
        if (!IsVisible) Show();
        // Show may reapply cached WPF placement. Reassert physical placement after it.
        NativeWindow.PlaceNoActivate(Handle, rect);
    }
    public void Conceal()
    {
        if (IsVisible) Hide();
    }
    private void OnNext(object sender, MouseButtonEventArgs e) { e.Handled = true; NextRequested?.Invoke(); }
    private void OnMenu(object sender, MouseButtonEventArgs e) { e.Handled = true; MenuRequested?.Invoke(); }
}
