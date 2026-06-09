using System.Collections.Concurrent;
using System.IO;

namespace PickMoney.App.Services;

public class TradeLogService
{
    private readonly string _logDirectory;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public TradeLogService(string baseDirectory)
    {
        _logDirectory = Path.Combine(baseDirectory, "logs");
        Directory.CreateDirectory(_logDirectory);
    }

    public async Task WriteAsync(string accountName, string message)
    {
        var safeName = string.Join("_", accountName.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        var filePath = Path.Combine(_logDirectory, $"{safeName}-{DateTime.Now:yyyyMMdd}.log");
        var semaphore = _locks.GetOrAdd(filePath, _ => new SemaphoreSlim(1, 1));
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";

        await semaphore.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(filePath, line);
        }
        finally
        {
            semaphore.Release();
        }
    }
}
