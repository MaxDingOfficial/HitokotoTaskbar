using Microsoft.Win32;

namespace HitokotoTaskbar.Services;

public sealed class StartupService
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HitokotoTaskbar";
    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(ValueName) is string command && command == Command;
    }
    private static string Command => $"\"{Environment.ProcessPath ?? throw new InvalidOperationException("无法确定程序路径。请运行发布的 exe。") }\"";
    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, true);
        if (enabled)
        {
            if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("请先运行 HitokotoTaskbar.exe，再启用开机启动。");
            key.SetValue(ValueName, Command, RegistryValueKind.String);
        }
        else key.DeleteValue(ValueName, false);
    }
}
