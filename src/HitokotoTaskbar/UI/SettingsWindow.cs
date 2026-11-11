using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using HitokotoTaskbar.Core;
using HitokotoTaskbar.Native;

namespace HitokotoTaskbar.UI;

public sealed class SettingsWindow : Window
{
    private const string AllDisplays = "*";
    private static readonly (char Code, string Label)[] Categories =
    [
        ('a', "动画"), ('b', "漫画"), ('c', "游戏"), ('d', "文学"),
        ('e', "原创"), ('f', "网络"), ('g', "其他"), ('h', "影视"),
        ('i', "诗词"), ('j', "网易云"), ('k', "哲学"), ('l', "抖机灵")
    ];
    private readonly Dictionary<string, TextBox> _text = new();
    private readonly Dictionary<string, CheckBox> _checks = new();
    private readonly List<(char Code, CheckBox Check)> _categoryChecks = new();
    private readonly ComboBox _monitor;
    private readonly TextBlock _error;
    private readonly TextBlock _status;
    private readonly AppSettings _original;
    private readonly Func<AppSettings, string?> _save;
    public SettingsWindow(AppSettings settings, string status, Func<AppSettings, string?> save)
    {
        settings = settings.Validated();
        _original = settings;
        _save = save;
        Title = $"Hitokoto Taskbar {App.VersionName} · 设置";
        Width = 580;
        Height = 760;
        MinWidth = 460;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowActivated = false;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        FontSize = 13;
        Background = new SolidColorBrush(Color.FromRgb(249, 247, 251));
        var root = new DockPanel { Margin = new Thickness(24) };
        Content = root;
        var footer = new StackPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        _error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
        footer.Children.Add(_error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "取消", Padding = new Thickness(24, 8, 24, 8), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        cancel.Click += (_, _) => Close();
        var apply = new Button { Content = "保存设置", Padding = new Thickness(24, 8, 24, 8), IsDefault = true };
        apply.Click += (_, _) => Save();
        buttons.Children.Add(cancel);
        buttons.Children.Add(apply);
        footer.Children.Add(buttons);
        var stack = new StackPanel { Margin = new Thickness(0, 0, 12, 8) };
        root.Children.Add(new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        stack.Children.Add(new TextBlock { Text = "留一句话，在任务栏", FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        _status = new TextBlock { Text = status, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 18) };
        stack.Children.Add(_status);
        Section(stack, "内容与刷新");
        AddSentenceFilters(stack, settings);
        Field(stack, "RefreshIntervalMinutes", "自动刷新间隔（分钟，1–1440）", settings.RefreshIntervalMinutes);
        Hint(stack, "手动刷新至少间隔 15 秒；网络失败时继续显示缓存。");
        var advanced = new StackPanel { Margin = new Thickness(14, 2, 0, 0) };
        Field(advanced, "ApiUrl", "API 地址（HTTPS）", settings.ApiUrl);
        Hint(advanced, "分类和字数以上方设置为准。使用官方接口时，无需修改这里。");
        stack.Children.Add(new Expander { Header = "高级：API 地址", Content = advanced });
        Section(stack, "显示位置");
        stack.Children.Add(new TextBlock { Text = "显示器", Margin = new Thickness(0, 4, 0, 5) });
        var options = new List<MonitorOption> { new("", "跟随主显示器"), new(AllDisplays, "所有显示器（同时显示）") };
        foreach (var m in TaskbarProbe.EnumerateMonitors())
            options.Add(new(m.DeviceName, $"{m.DeviceName} · {m.Bounds.Width} × {m.Bounds.Height} · {m.Scale * 100:0}%" + (m.IsPrimary ? " · 主显示器" : "")));
        if (!string.IsNullOrEmpty(settings.MonitorDeviceName) && options.All(x => x.Device != settings.MonitorDeviceName))
            options.Add(new(settings.MonitorDeviceName, settings.MonitorDeviceName + "（未连接，暂用主显示器）"));
        _monitor = new ComboBox { ItemsSource = options, DisplayMemberPath = nameof(MonitorOption.Label), SelectedValuePath = nameof(MonitorOption.Device),
            SelectedValue = settings.ShowOnAllDisplays ? AllDisplays : settings.MonitorDeviceName, Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 10) };
        stack.Children.Add(_monitor);
        Field(stack, "HorizontalOffset", "距任务栏左边缘（DIP，0–2000）", settings.HorizontalOffset);
        Field(stack, "VerticalOffset", "垂直偏移（DIP，-100–100）", settings.VerticalOffset);
        Field(stack, "MaxWidth", "最大文字宽度（DIP，80–2000）", settings.MaxWidth);
        Hint(stack, "实际宽度会受任务栏空白区域限制。开启任务栏自动隐藏时，一言也保持隐藏。");
        Section(stack, "文字样式");
        Field(stack, "FontFamily", "字体名称", settings.FontFamily);
        Field(stack, "FontSize", "字号（DIP，8–48）", settings.FontSize);
        Field(stack, "TextColor", "文字颜色（#RRGGBB，如 #F5EAF8）", settings.TextColor);
        Field(stack, "Opacity", "文字不透明度（0.1–1）", settings.Opacity);
        Check(stack, "ShowQuoteIcon", "显示引号图标", settings.ShowQuoteIcon);
        Check(stack, "EnableTransitions", "启用短暂淡入过渡", settings.EnableTransitions);
        Section(stack, "后台运行");
        Check(stack, "ShowTrayIcon", "显示系统托盘图标", settings.ShowTrayIcon);
        Check(stack, "AutoStart", "登录 Windows 后自动启动（仅当前用户）", settings.AutoStart);
        Check(stack, "AutomaticallyCheckForUpdates", "自动检查更新，发现新版时打开 GitHub 下载页", settings.AutomaticallyCheckForUpdates);
        Hint(stack, "关闭托盘后，仍可右键任务栏文字管理程序；文字隐藏时，重新运行 exe 会打开设置。\n配置保存到 %LOCALAPPDATA%\\HitokotoTaskbar\\settings.json。");
    }
    public void UpdateStatus(string status) => _status.Text = status;
    private void AddSentenceFilters(Panel panel, AppSettings settings)
    {
        panel.Children.Add(new TextBlock { Text = "句子分类", Margin = new Thickness(0, 4, 0, 5) });
        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 5) };
        foreach (var (code, label) in Categories)
        {
            var check = new CheckBox { Content = label, IsChecked = settings.Categories.Contains(code), Margin = new Thickness(0, 3, 8, 5) };
            _categoryChecks.Add((code, check));
            grid.Children.Add(check);
        }
        panel.Children.Add(grid);
        Hint(panel, "可同时勾选多个分类；一个都不勾选时，不限分类。");
        var lengths = new Grid();
        lengths.ColumnDefinitions.Add(new ColumnDefinition());
        lengths.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        lengths.ColumnDefinitions.Add(new ColumnDefinition());
        var minimum = new StackPanel();
        var maximum = new StackPanel();
        Grid.SetColumn(maximum, 2);
        Field(minimum, nameof(AppSettings.MinimumSentenceLength), "最短字数", settings.MinimumSentenceLength);
        Field(maximum, nameof(AppSettings.MaximumSentenceLength), "最长字数", settings.MaximumSentenceLength);
        lengths.Children.Add(minimum);
        lengths.Children.Add(maximum);
        panel.Children.Add(lengths);
        Hint(panel, "填整数；最短可填 0（不限），最长至少为 1。新范围从下次刷新生效。");
    }
    private static void Section(Panel panel, string label) => panel.Children.Add(new TextBlock
        { Text = label, FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 10) });
    private static void Hint(Panel panel, string label) => panel.Children.Add(new TextBlock
        { Text = label, Foreground = Brushes.DimGray, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
    private void Field(Panel panel, string key, string label, object? value)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 4, 0, 5) });
        var text = new TextBox { Text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "", Padding = new Thickness(7), Margin = new Thickness(0, 0, 0, 7) };
        System.Windows.Automation.AutomationProperties.SetName(text, label);
        _text[key] = text;
        panel.Children.Add(text);
    }
    private void Check(Panel panel, string key, string label, bool value)
    {
        var check = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 4, 0, 8) };
        _checks[key] = check;
        panel.Children.Add(check);
    }
    private string Text(string key) => _text[key].Text.Trim();
    private double Number(string key, double min, double max, string name)
    {
        if (!double.TryParse(Text(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
            !double.IsFinite(value) || value < min || value > max)
            throw new ArgumentException($"{name}应为 {min} 到 {max} 之间的数字，小数点使用英文句点。");
        return value;
    }
    private int SentenceLength(string key, int minimum, string label)
    {
        if (!int.TryParse(Text(key), NumberStyles.None, CultureInfo.InvariantCulture, out var length) ||
            length < minimum || length > AppSettings.SentenceLengthLimit)
            throw new ArgumentException($"{label}应为 {minimum} 到 {AppSettings.SentenceLengthLimit} 之间的整数。");
        return length;
    }
    private void Save()
    {
        try
        {
            var s = _original.Copy();
            s.ApiUrl = Text("ApiUrl");
            if (!Uri.TryCreate(s.ApiUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo))
                throw new ArgumentException("请使用有效的 HTTPS API 地址，不能在地址中包含账号密码。");
            s.Categories = new string(_categoryChecks.Where(x => x.Check.IsChecked == true).Select(x => x.Code).ToArray());
            s.MinimumSentenceLength = SentenceLength(nameof(AppSettings.MinimumSentenceLength), 0, "最短字数");
            s.MaximumSentenceLength = SentenceLength(nameof(AppSettings.MaximumSentenceLength), 1, "最长字数");
            if (s.MinimumSentenceLength > s.MaximumSentenceLength)
                throw new ArgumentException("最短字数不能大于最长字数。");
            s.RefreshIntervalMinutes = Number("RefreshIntervalMinutes", 1, 1440, "刷新间隔");
            s.HorizontalOffset = Number("HorizontalOffset", 0, 2000, "水平偏移");
            s.VerticalOffset = Number("VerticalOffset", -100, 100, "垂直偏移");
            s.MaxWidth = Number("MaxWidth", 80, 2000, "最大宽度");
            s.FontSize = Number("FontSize", 8, 48, "字号");
            s.Opacity = Number("Opacity", 0.1, 1, "不透明度");
            s.FontFamily = Text("FontFamily");
            if (string.IsNullOrWhiteSpace(s.FontFamily)) throw new ArgumentException("字体名称不能为空。");
            s.TextColor = Text("TextColor");
            if (s.TextColor.Length != 7 || s.TextColor[0] != '#' || !s.TextColor[1..].All(Uri.IsHexDigit))
                throw new ArgumentException("文字颜色需要使用 #RRGGBB，例如 #F5EAF8。");
            var monitorChoice = _monitor.SelectedValue as string ?? "";
            s.ShowOnAllDisplays = monitorChoice == AllDisplays;
            if (!s.ShowOnAllDisplays) s.MonitorDeviceName = monitorChoice;
            s.ShowQuoteIcon = _checks["ShowQuoteIcon"].IsChecked == true;
            s.EnableTransitions = _checks["EnableTransitions"].IsChecked == true;
            s.ShowTrayIcon = _checks["ShowTrayIcon"].IsChecked == true;
            s.AutoStart = _checks["AutoStart"].IsChecked == true;
            s.AutomaticallyCheckForUpdates = _checks["AutomaticallyCheckForUpdates"].IsChecked == true;
            var error = _save(s);
            if (error is not null) { _error.Text = error; return; }
            Close();
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException) { _error.Text = ex.Message; }
    }
    private sealed record MonitorOption(string Device, string Label);
}
