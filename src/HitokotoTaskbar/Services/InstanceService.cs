using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace HitokotoTaskbar.Services;

/// <summary>Per-user mutex and same-user-only pipe; never exposes a remote control endpoint.</summary>
public sealed class InstanceService : IDisposable
{
    private readonly Mutex _mutex;
    private readonly bool _ownsMutex;
    private readonly CancellationTokenSource _stop = new();
    private Task? _server;
    private static readonly string Name = CreateName();
    private static string CreateName()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? Environment.UserName;
        return "HitokotoTaskbar." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid)))[..16];
    }
    public bool IsFirst => _ownsMutex;
    public InstanceService()
    {
        _mutex = new Mutex(true, @"Local\" + Name, out _ownsMutex);
    }
    public void Listen(Action<string> command, Action<Exception> error)
    {
        _server = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(Name, PipeDirection.In, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(_stop.Token);
                    using var reader = new StreamReader(server, Encoding.UTF8, false, 128, true);
                    using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                    readTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                    // Bound both bytes and time: do not use unbounded ReadLine on an IPC client.
                    var buffer = new char[32];
                    int count = 0;
                    while (count < buffer.Length)
                    {
                        int read = await reader.ReadAsync(buffer.AsMemory(count, 1), readTimeout.Token);
                        if (read == 0 || buffer[count] == '\n') break;
                        count += read;
                    }
                    var value = new string(buffer, 0, count).Trim();
                    if (value is "settings" or "exit" or "refresh") command(value);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    error(ex);
                    try { await Task.Delay(1000, _stop.Token); } catch (OperationCanceledException) { }
                }
            }
        });
    }
    public static async Task<bool> SendAsync(string command)
    {
        try
        {
            await using var client = new NamedPipeClientStream(".", Name, PipeDirection.Out, PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await client.ConnectAsync(timeout.Token);
            await client.WriteAsync(Encoding.UTF8.GetBytes(command + "\n"), timeout.Token);
            await client.FlushAsync(timeout.Token);
            return true;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or TimeoutException) { return false; }
    }
    public async Task StopAsync()
    {
        _stop.Cancel();
        if (_server is not null) await _server;
    }
    public void Dispose()
    {
        _stop.Cancel();
        if (_ownsMutex) _mutex.ReleaseMutex();
        _mutex.Dispose();
        _stop.Dispose();
    }
}
