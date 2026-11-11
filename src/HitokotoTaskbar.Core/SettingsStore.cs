using System.Text.Json;

namespace HitokotoTaskbar.Core;

public sealed class SettingsStore
{
    private const int MaximumSettingsBytes = 1024 * 1024;
    private readonly object _gate = new();
    private readonly AppLog _log;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public string FilePath { get; }

    public SettingsStore(string? path = null, AppLog? log = null)
    {
        FilePath = Path.GetFullPath(path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HitokotoTaskbar", "settings.json"));
        _log = log ?? new AppLog();
    }

    public AppSettings Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(FilePath)) return new AppSettings().Validated();
                if (new FileInfo(FilePath).Length > MaximumSettingsBytes)
                    throw new InvalidDataException("配置文件超过 1 MiB。 ");
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllBytes(FilePath), JsonOptions)
                    ?? throw new JsonException("配置内容不能为 null。");
                return settings.Validated();
            }
            catch (Exception error) when (error is JsonException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                _log.Write("读取配置失败，已恢复默认设置。", error);
                if (error is JsonException or InvalidDataException)
                {
                    try
                    {
                        // One bounded backup avoids creating an unbounded corruption archive.
                        File.Copy(FilePath, FilePath + ".corrupt", overwrite: true);
                        Save(new AppSettings());
                    }
                    catch (Exception backupError) when (backupError is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                    {
                        _log.Write("无法备份损坏配置。", backupError);
                    }
                }
                return new AppSettings().Validated();
            }
        }
    }

    /// <summary>Atomic replacement on the same volume. Exceptions are reported to the caller.</summary>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        lock (_gate)
        {
            var directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, ".settings-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                var data = JsonSerializer.SerializeToUtf8Bytes(settings.Validated(), JsonOptions);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(data);
                    stream.Flush(flushToDisk: true);
                }
                File.Move(temporary, FilePath, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
