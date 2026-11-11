using System.Text;

namespace HitokotoTaskbar.Core;

/// <summary>Best-effort bounded logging: the current file plus two backups, at most ~1.5 MiB.</summary>
public sealed class AppLog
{
    public const long MaximumFileBytes = 512 * 1024;
    private const int MaximumEntryCharacters = 16 * 1024;
    private readonly object _gate = new();
    public string FilePath { get; }

    public AppLog(string? directory = null)
    {
        directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HitokotoTaskbar", "logs");
        FilePath = Path.Combine(directory, "app.log");
    }

    public void Write(string message, Exception? error = null)
    {
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var entry = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}";
                if (error is not null)
                    entry += $" | {error}";
                if (entry.Length > MaximumEntryCharacters)
                    entry = entry[..MaximumEntryCharacters] + " [truncated]";
                entry += Environment.NewLine;
                var bytes = Encoding.UTF8.GetBytes(entry);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length + bytes.Length > MaximumFileBytes)
                {
                    var first = FilePath + ".1";
                    var second = FilePath + ".2";
                    if (File.Exists(first)) File.Move(first, second, overwrite: true);
                    File.Move(FilePath, first, overwrite: true);
                }
                using var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                stream.Write(bytes);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // A locked/full/unwritable log directory must not take the desktop overlay down.
        }
    }
}
