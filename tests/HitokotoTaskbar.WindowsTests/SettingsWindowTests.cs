using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using HitokotoTaskbar.Core;
using HitokotoTaskbar.UI;
using Xunit;

namespace HitokotoTaskbar.WindowsTests;

public sealed class SettingsWindowTests
{
    [Fact]
    public Task CanChangeChineseCategorySelectionAndLengthWithoutEditingTheUrl() => StaTest.RunAsync(scope =>
    {
        var original = new AppSettings { Categories = "ai", ApiUrl = "https://v1.hitokoto.cn/?max_length=24" };
        AppSettings? saved = null;
        var window = scope.Own(new SettingsWindow(original, "测试", settings => { saved = settings; return null; }));
        var checks = Assert.Single(Descendants<UniformGrid>(window)).Children.OfType<CheckBox>().ToArray();
        Assert.Equal(12, checks.Length);
        Assert.True(FindCategory("动画").IsChecked);
        Assert.True(FindCategory("诗词").IsChecked);
        Assert.False(FindCategory("文学").IsChecked);
        FindCategory("动画").IsChecked = false;
        FindCategory("文学").IsChecked = true;

        var shortest = FindField("最短字数");
        var longest = FindField("最长字数");
        Assert.Equal("0", shortest.Text);
        Assert.Equal("24", longest.Text);
        shortest.Text = "18";
        longest.Text = "8";
        var save = Assert.Single(Descendants<Button>(window), button => Equals(button.Content, "保存设置"));
        save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.Null(saved);
        Assert.Contains(Descendants<TextBlock>(window), text => text.Text == "最短字数不能大于最长字数。");

        shortest.Text = "8";
        longest.Text = "18";
        Assert.Single(Descendants<ComboBox>(window)).SelectedValue = "*";
        save.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Assert.NotNull(saved);
        Assert.Equal(original.ApiUrl, saved.ApiUrl);
        Assert.Equal("di", saved.Categories);
        Assert.True(saved.ShowOnAllDisplays);
        Assert.Equal("?encode=json&min_length=8&max_length=18&c=d&c=i", HitokotoService.BuildRequestUri(saved).Query);

        CheckBox FindCategory(string name) => Assert.Single(checks, check => Equals(check.Content, name));
        TextBox FindField(string name) => Assert.Single(Descendants<TextBox>(window), text => AutomationProperties.GetName(text) == name);
    });

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is T matching) yield return matching;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
}
